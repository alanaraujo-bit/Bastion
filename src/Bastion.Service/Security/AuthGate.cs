using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Bastion.Service.Security;

/// <summary>
/// Mints and validates short-lived admin tokens (used to authorize a burst of
/// configuration changes after a single re-authentication) and enforces a
/// failed-attempt cooldown to blunt brute force against the unlock prompt.
/// </summary>
public sealed class AuthGate
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly TimeSpan _tokenLifetime = TimeSpan.FromMinutes(3);

    // failed-attempt tracking, keyed by image name (per-app) plus a global bucket.
    private readonly ConcurrentDictionary<string, (int count, DateTimeOffset until)> _fails = new();

    public string MintToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = DateTimeOffset.UtcNow + _tokenLifetime;
        return token;
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (!_tokens.TryGetValue(token, out var expiry)) return false;
        if (DateTimeOffset.UtcNow > expiry) { _tokens.TryRemove(token, out _); return false; }
        return true;
    }

    public void RevokeToken(string? token)
    {
        if (!string.IsNullOrEmpty(token)) _tokens.TryRemove(token, out _);
    }

    /// <summary>Returns remaining cooldown seconds, or 0 if attempts are permitted.</summary>
    public int CooldownRemaining(string bucket)
    {
        if (_fails.TryGetValue(bucket, out var s) && DateTimeOffset.UtcNow < s.until)
            return (int)Math.Ceiling((s.until - DateTimeOffset.UtcNow).TotalSeconds);
        return 0;
    }

    public void RegisterFailure(string bucket, int maxAttempts, int lockoutSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        _fails.AddOrUpdate(bucket,
            _ => (1, now),
            (_, prev) =>
            {
                var count = prev.until < now && prev.count >= maxAttempts ? 1 : prev.count + 1;
                var until = count >= maxAttempts ? now.AddSeconds(lockoutSeconds) : prev.until;
                return (count, until);
            });
    }

    public void RegisterSuccess(string bucket) => _fails.TryRemove(bucket, out _);

    public void SweepExpiredTokens()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _tokens.ToArray())
            if (now > kv.Value) _tokens.TryRemove(kv.Key, out _);
    }
}
