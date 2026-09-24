# Bastion — Application Lock for Windows

Bastion requires your authentication before a chosen Windows application can
open. It intercepts **every** launch vector — desktop, Start menu, taskbar,
shortcuts, file associations, protocols, direct execution, and child processes —
using a Windows-enforced mechanism, not a password on shortcuts.

Priorities, in order: **security → reliability → speed → experience → design.**

---

## What it is (and isn't)

Bastion is a real user-mode application lock. It protects against everyday access
by other users of a PC. It is **honest about its limits**: an administrator of
the machine can always remove it (only a signed kernel driver could change that),
protection is keyed on executable *name*, and if the protection service is down,
protected apps stay locked (it fails closed). See
[How-it-works.txt](How-it-works.txt) and [ARCHITECTURE.md](ARCHITECTURE.md).

---

## Solution layout

| Project | Type | Runs as | Responsibility |
|---|---|---|---|
| `Bastion.Core` | class lib | — | Models, Argon2id crypto, config store, IPC contract |
| `Bastion.Service` | Windows Service | LocalSystem | IFEO hooks, launch authority, grants, re-lock policy |
| `Bastion.Gatekeeper` | WPF exe | user | The fast auth prompt the OS launches instead of a protected app |
| `Bastion.Ui` | WPF lib | — | Shared theme/design system, Windows Hello, icon + app scanning |
| `Bastion.App` | WPF exe | user | Console UI: onboarding, home, management, settings, tray |

The interface, the protection mechanism, authentication, configuration, secure
storage, startup, and logging are cleanly separated. Closing the console window
does **not** stop protection — the service is independent.

---

## Build

Requires the .NET 8 SDK (LTS) on Windows 10/11 x64.

```bash
dotnet build Bastion.sln -c Release
dotnet test tests/Bastion.Tests/Bastion.Tests.csproj
```

## Package the installer

```powershell
# 1) Publish the three executables (framework-dependent, win-x64)
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
# 2) Compile the installer with Inno Setup 6 (ISCC.exe on PATH)
iscc installer\Bastion.iss
# Output: dist\BastionSetup-1.0.0.exe
```

The installer places files in `Program Files\Bastion`, ACL-protects
`ProgramData\Bastion`, registers and starts the `BastionProtection` service
(auto-start, restart-on-failure), and adds a Start Menu entry. Uninstall stops
the service, removes **all** IFEO hooks (so protected apps open normally again),
and deletes the service and files.

> No prerequisites: the .NET 8 runtime is bundled inside the installer.

---

## Master password & recovery

- The master password is never stored — only an **Argon2id** verifier (salt +
  hash + parameters). Verification is constant-time.
- Optional recovery: a security-question answer (also Argon2id-hashed) lets you
  set a new password locally. Nothing leaves the device.
- **Windows Hello** is an opt-in convenience: after you prove the password once,
  it is sealed per-user with DPAPI and only released after a successful Hello
  check, so the service still verifies the real credential.

---

## Testing done

- `Bastion.Tests` — crypto round-trips, config persistence & corruption
  fallback, IPC serialization, protectable-app denylist, auth-gate cooldown
  (21 tests, all passing).
- The gatekeeper prompt and all console pages were rendered and visually
  reviewed in light and dark themes, and in English and Brazilian Portuguese.
- **Full end-to-end interception was verified on Windows 11 with the installed
  SYSTEM service**: protect an app (password-gated) → launch is redirected to the
  gatekeeper and the app is blocked → correct password runs the SYSTEM
  pass-through and the app opens → closing it re-locks automatically → a wrong
  password is denied → uninstall removes every IFEO hook. See ARCHITECTURE.md →
  "Verification status" (includes the Windows 11 Store-app caveat).
- The installer compiles with Inno Setup 6 to `dist/BastionSetup-1.0.0.exe` and
  was installed/uninstalled cleanly during verification.

## Availability & i18n status

- **Languages:** English and Brazilian Portuguese, switchable live in Settings and
  applied to the gatekeeper. Add a language by extending
  `src/Bastion.Ui/Localization/Strings.cs`.
- **Windows Hello:** implemented; auto-disabled and clearly labelled on devices
  without Hello.
- **Store/MSIX apps** (e.g. Windows 11 Notepad/Paint/Calculator) can't be
  reliably intercepted; the "Protect an app" flow warns when you pick one.
