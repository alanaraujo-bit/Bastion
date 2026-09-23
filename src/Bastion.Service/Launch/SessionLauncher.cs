using System.ComponentModel;
using System.Runtime.InteropServices;
using static Bastion.Service.Interop.NativeMethods;

namespace Bastion.Service.Launch;

/// <summary>
/// Result of a pass-through launch. <see cref="ProcessHandle"/> is left OPEN so
/// the grant manager can wait on it for a precise, event-driven re-lock when the
/// application exits. The grant manager owns closing it.
/// </summary>
public sealed record LaunchResult(int ProcessId, IntPtr ProcessHandle);

/// <summary>
/// Launches an executable inside a specific interactive session from the
/// SYSTEM service, so the window appears on the user's desktop with the user's
/// identity and environment.
/// </summary>
public sealed class SessionLauncher
{
    public LaunchResult LaunchAsUser(uint sessionId, string exePath, string? arguments, string? workingDir)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("The protected application could not be found.", exePath);

        IntPtr userToken = IntPtr.Zero, primaryToken = IntPtr.Zero, envBlock = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken(sessionId, out userToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not obtain the user session token.");

            const uint desired = TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY |
                                 TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID;
            if (!DuplicateTokenEx(userToken, desired, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, TOKEN_TYPE.TokenPrimary, out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not duplicate the user token.");

            if (!CreateEnvironmentBlock(out envBlock, primaryToken, false))
                envBlock = IntPtr.Zero; // non-fatal; fall back to no custom env

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = SW_SHOW,
            };

            var cmdLine = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{exePath}\""
                : $"\"{exePath}\" {arguments}";

            uint flags = (uint)CreationFlags.CREATE_UNICODE_ENVIRONMENT;

            bool ok = CreateProcessAsUser(
                primaryToken,
                exePath,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                envBlock,
                string.IsNullOrWhiteSpace(workingDir) ? Path.GetDirectoryName(exePath) : workingDir,
                ref si,
                out var pi);

            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The system refused to start the application.");

            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
            // Deliberately keep pi.hProcess open; ownership transfers to the caller
            // (grant manager) which waits on it for event-driven re-lock.
            return new LaunchResult(pi.dwProcessId, pi.hProcess);
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }
}
