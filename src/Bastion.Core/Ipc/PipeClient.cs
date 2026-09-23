using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bastion.Core.Ipc;

/// <summary>
/// Thin client for the service pipe. One request/response per connection.
/// Framing is a single newline-terminated UTF-8 JSON line each way.
/// </summary>
public static class PipeClient
{
    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Sends a request to the service and returns its response, or an Error
    /// response if the service is unreachable within <paramref name="timeoutMs"/>.
    /// Never throws for connection problems — the caller decides how to fail safe.
    /// </summary>
    public static async Task<ServiceResponse> SendAsync(ServiceRequest request, int timeoutMs = 4000, CancellationToken ct = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeInfo.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);

            using var writer = new StreamWriter(client, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = false };
            var payload = JsonSerializer.Serialize(request, Json);
            await writer.WriteLineAsync(payload).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            using var reader = new StreamReader(client, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line))
                return ServiceResponse.Fail(ResponseStatus.Error, "Empty response from protection service.");

            return JsonSerializer.Deserialize<ServiceResponse>(line, Json)
                   ?? ServiceResponse.Fail(ResponseStatus.Error, "Unparseable response.");
        }
        catch (TimeoutException)
        {
            return ServiceResponse.Fail(ResponseStatus.Error, "The protection service is not responding.");
        }
        catch (Exception ex)
        {
            return ServiceResponse.Fail(ResponseStatus.Error, ex.Message);
        }
    }

    public static ServiceResponse Send(ServiceRequest request, int timeoutMs = 4000)
        => SendAsync(request, timeoutMs).GetAwaiter().GetResult();

    /// <summary>Quick reachability probe.</summary>
    public static async Task<bool> IsServiceReachableAsync(int timeoutMs = 1500)
    {
        var resp = await SendAsync(new ServiceRequest { Type = RequestType.Ping }, timeoutMs).ConfigureAwait(false);
        return resp.Status == ResponseStatus.Ok;
    }
}
