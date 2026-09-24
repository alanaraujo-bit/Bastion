# Bastion — Architecture & Security Model

This document describes how Bastion actually works, and is deliberately honest
about the boundaries of what a user-mode application lock can guarantee on
Windows.

## Components and trust boundaries

```
   ┌──────────────────────┐        named pipe (local, ACL'd)        ┌───────────────────────────┐
   │  Bastion.App (user)  │  ───────────────────────────────────▶  │  BastionProtection service │
   │  Bastion.Gatekeeper  │  ◀───────────────────────────────────  │  (LocalSystem)             │
   └──────────────────────┘         requests / responses            └────────────┬──────────────┘
        user session                                                             │ owns
                                                                                  ▼
                                                        HKLM IFEO keys  +  ProgramData\Bastion (ACL'd)
```

- **User-context processes** (the console app and the gatekeeper prompt) can
  *read* configuration and *request* actions, but hold no authority. They run
  `asInvoker` and never elevate.
- **The SYSTEM service** is the single authority. It owns the `HKLM` IFEO keys
  (which standard users cannot write) and the `ProgramData\Bastion` config
  (ACL: SYSTEM/Administrators full, Users read). Every launch decision and every
  security-weakening change is enforced here, so bypassing the UI gains nothing.

## Interception: Image File Execution Options (IFEO)

For each protected executable *name* (e.g. `msedge.exe`) the service sets:

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\
    <image.exe>\Debugger = "C:\Program Files\Bastion\Bastion.Gatekeeper.exe"
```

The Windows loader honours this for **every** launch path — double-click,
Start menu, taskbar, shortcut, file association, protocol handler, `CreateProcess`
from any parent, direct command line — *before the target's code runs*. The
loader invokes `Bastion.Gatekeeper.exe "<full path to target>" <original args>`.
The value is written under both the 64-bit and WOW64 registry views for 32- and
64-bit targets.

This is the crucial difference from "a password on shortcuts": the redirection is
performed by the OS itself and cannot be sidestepped by choosing a different
launch method.

A denylist (in `Bastion.Core.ProtectableRules`) refuses critical OS images
(`explorer.exe`, `lsass.exe`, `winlogon.exe`, …) and Bastion's own binaries, so a
user cannot destabilise logon/shell or create launch recursion.

## The authorized pass-through

When the gatekeeper prompt succeeds, the target must be started **without**
re-triggering IFEO (which would loop). The service:

1. Removes the image's `Debugger` hook.
2. Launches the target **into the user's session** with `WTSQueryUserToken` →
   `DuplicateTokenEx` (primary) → `CreateEnvironmentBlock` →
   `CreateProcessAsUser` (`lpDesktop = winsta0\default`). Session-0 isolation
   means a service can't just `CreateProcess` onto the desktop; this is the
   canonical path.
3. Keeps the process handle and waits on it (event-driven) so it can re-lock
   precisely when the app exits.

**Why the hook stays off during an active unlock:** multi-process apps (browsers)
spawn many identically-named children. Rather than intercept each one, an active
grant means the hook is *removed* for the duration and *restored* when the grant
ends. This makes browsers usable and avoids per-child registry churn. It also
means: while an app is unlocked, it and its children run freely (expected).

## Unlock grants & re-locking

A grant is tracked per `(image, session)`:

| Mode | Ends when |
|---|---|
| Ask every time / Until it closes | the launched process tree exits |
| Stay unlocked N minutes | N minutes elapse *and* the process has exited |
| Until sign-out | the session ends or you re-lock |

Re-lock on **workstation lock** (Win+L) and **resume from sleep** is handled via
the service's `OnSessionChange`/`OnPowerEvent`. It is **non-destructive**: it does
not kill running apps (that would lose your work); instead it revokes any
time/session extension so the app re-locks as soon as it closes, and immediately
re-locks apps that aren't currently running.

## Authentication & storage

- **Argon2id** verifier for the master password (64 MiB / 3 iters / p=2, per-cred
  parameters), constant-time comparison. The password itself is never stored.
- Password material sent over the **local, ACL-restricted** named pipe is scrubbed
  by the service immediately after use.
- **Failed-attempt cooldown** per app/bucket blunts brute force.
- **Config-change gating:** removing an app, pausing protection, changing the
  password, editing security settings, or clearing history all require
  re-authentication (a short-lived admin token minted after one password check).
- **Recovery** (optional): Argon2id-hashed security answer, local only.
- **Windows Hello:** bound to the real credential — the verified password is
  sealed with DPAPI (CurrentUser) and only released after a Hello check, so the
  service always verifies the genuine credential rather than trusting a "Hello
  passed" claim.

## Resilience / fail-safe behaviour

- The service **reconciles** IFEO state with config on every start, so protection
  survives reboots and self-heals stale hooks.
- Config writes are atomic with a rolling backup; a corrupt primary falls back to
  the backup, and an unreadable config never silently unprotects apps.
- If the service is unreachable, the gatekeeper **fails closed** — the protected
  app does not open. The service installs with auto-start and restart-on-failure.
- App moved/uninstalled, missing permissions, and similar conditions surface as
  plain-language messages, never stack traces.

## Honest limitations (by design, not hidden)

1. **An administrator can remove Bastion.** They own the machine and the HKLM
   keys. User-mode software cannot prevent this; only a signed kernel driver
   could approach it, at a very different engineering and trust cost.
2. **Name-based matching.** Two distinct programs with the same executable file
   name are treated as the same protected app. (Per-path `FilterFullPath` IFEO
   subkeys are a possible future refinement.)
3. **A sub-millisecond pass-through window** exists while the hook is toggled for
   an *authorized* launch. Launches are serialized to keep it minimal.
4. **Fail-closed trade-off.** A stopped service blocks protected apps until it
   restarts. This is the correct security posture and is disclosed to the user.

## Verification status

- Automated tests (`Bastion.Tests`, 21) cover crypto, config persistence and
  corruption fallback, IPC serialization, the protectable denylist, and the
  auth-gate cooldown.
- The gatekeeper prompt and every console page were rendered and reviewed in
  light and dark themes, and in both English and Brazilian Portuguese.
- **Full end-to-end interception was verified on Windows 11 with the real
  installed SYSTEM service:**
  1. A classic Win32 app (`charmap.exe`) was placed under protection through the
     service pipe — which required the master password (bypass-resistant).
  2. The service armed the IFEO `Debugger` hook for it.
  3. Launching the app was redirected by Windows to the gatekeeper; the app did
     **not** run.
  4. The correct password triggered the SYSTEM `CreateProcessAsUser` pass-through
     and the app opened from its real path.
  5. Closing the app ended the grant and the IFEO hook was **automatically
     restored** (event-driven re-lock).
  6. With no active grant, a wrong password was **denied** and the app did not run.
  7. Uninstall stopped the service and removed **all** Bastion IFEO hooks, so the
     apps launch normally again.
- Note on test targets: Windows 11 redirects `notepad.exe`, `mspaint.exe`,
  `calc.exe` etc. to Store (MSIX) apps whose real image lives under
  `WindowsApps`, so those names are poor interception targets. Classic Win32
  executables (browsers, most installed desktop apps, `charmap.exe`) intercept as
  designed. Protecting MSIX/Store apps would require an AppX-aware mechanism and
  is a known limitation.

## Packaging notes

The executables are published self-contained (`tools\publish.ps1`), so the
.NET 8 runtime ships inside the installer and the target machine needs no
prerequisites. After creating the service, the installer waits for it to reach
RUNNING and tells the user if it did not. The project is code-signing ready: sign `dist\app\*.exe` and the final setup `.exe`
with your Authenticode certificate before distribution.
