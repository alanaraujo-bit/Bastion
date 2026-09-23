using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Bastion.Core.Ipc;
using Bastion.Service.Engine;
using Bastion.Service.Logging;

namespace Bastion.Service.Server;

/// <summary>
/// Accepts client connections on the service pipe and dispatches each request
/// to the <see cref="ProtectionEngine"/>. The pipe ACL grants connect/read/write
/// to authenticated users (the user-context gatekeeper and app must reach it)
/// while the engine itself enforces all authorization decisions.
/// </summary>
public sealed class PipeServer
{
    private readonly ProtectionEngine _engine;
    private readonly int _maxConcurrent = 8;

    public PipeServer(ProtectionEngine engine) => _engine = engine;

    public async Task RunAsync(CancellationToken ct)
    {
        var workers = new List<Task>();
        for (int i = 0; i < _maxConcurrent; i++)
            workers.Add(Task.Run(() => AcceptLoop(ct), ct));
        ServiceLog.Info($"Pipe server listening on \\\\.\\pipe\\{PipeInfo.PipeName} ({_maxConcurrent} workers).");
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await HandleConnection(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                ServiceLog.Error("Pipe accept loop error", ex);
                await Task.Delay(250, ct).ContinueWith(_ => { }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                try { if (server?.IsConnected == true) server.Disconnect(); } catch { }
                server?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        security.AddAccessRule(new PipeAccessRule(authUsers,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(admins, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeInfo.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleConnection(NamedPipeServerStream server, CancellationToken ct)
    {
        using var reader = new StreamReader(server, new System.Text.UTF8Encoding(false), false, leaveOpen: true);
        using var writer = new StreamWriter(server, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = false };

        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(line)) return;

        ServiceResponse response;
        ServiceRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<ServiceRequest>(line, PipeClient.Json);
            response = request is null
                ? ServiceResponse.Fail(ResponseStatus.BadRequest, "Malformed request.")
                : _engine.Handle(request);
        }
        catch (Exception ex)
        {
            ServiceLog.Error("Request handling threw", ex);
            response = ServiceResponse.Fail(ResponseStatus.Error, "Internal error.");
        }
        finally
        {
            // Scrub any secrets that arrived on the wire as soon as we're done.
            if (request is not null)
            {
                request.Password = null;
                request.NewPassword = null;
                request.RecoveryAnswer = null;
            }
        }

        var json = JsonSerializer.Serialize(response, PipeClient.Json);
        await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
    }
}
