using System.Text.Json;
using Bastion.Core.Ipc;
using Bastion.Core.Models;
using Bastion.Core.Security;
using Bastion.Core.Storage;
using Bastion.Service.Ifeo;
using Bastion.Service.Security;
using Xunit;

namespace Bastion.Tests;

public class PasswordServiceTests
{
    [Fact]
    public void Master_roundtrip_verifies_correct_and_rejects_wrong()
    {
        var cred = PasswordService.CreateMaster("Correct horse battery staple!");
        Assert.True(cred.IsConfigured);
        Assert.True(PasswordService.VerifyMaster(cred, "Correct horse battery staple!"));
        Assert.False(PasswordService.VerifyMaster(cred, "wrong"));
        Assert.False(PasswordService.VerifyMaster(cred, ""));
    }

    [Fact]
    public void Two_hashes_of_same_password_use_distinct_salts()
    {
        var a = PasswordService.CreateMaster("same-password");
        var b = PasswordService.CreateMaster("same-password");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Recovery_answer_is_normalized_case_and_whitespace_insensitive()
    {
        var (salt, hash) = PasswordService.CreateRecoveryAnswer("  My First   Car ");
        var rec = new RecoverySettings { Enabled = true, AnswerSalt = salt, AnswerHash = hash };
        Assert.True(PasswordService.VerifyRecoveryAnswer(rec, "my first car"));
        Assert.True(PasswordService.VerifyRecoveryAnswer(rec, "MY FIRST CAR"));
        Assert.False(PasswordService.VerifyRecoveryAnswer(rec, "my second car"));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("password", 1)]
    [InlineData("Password1", 3)]
    [InlineData("P@ssw0rd-longer!", 4)]
    public void Strength_estimate_is_monotone_ish(string pw, int atLeast)
        => Assert.True(PasswordService.EstimateStrength(pw) >= atLeast);
}

public class ConfigStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "bastion-test-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Save_then_load_roundtrips_apps_and_policy()
    {
        var path = TempPath();
        try
        {
            var store = new ConfigStore(path);
            var cfg = new BastionConfig();
            cfg.Master = PasswordService.CreateMaster("hunter2!");
            cfg.Apps.Add(new ProtectedApp { ImageName = "msedge.exe", DisplayName = "Microsoft Edge", TargetPath = @"C:\edge.exe" });
            cfg.DefaultPolicy.Mode = UnlockMode.ForMinutes;
            cfg.DefaultPolicy.Minutes = 15;
            store.Save(cfg);

            var loaded = store.Load();
            Assert.Single(loaded.Apps);
            Assert.Equal("msedge.exe", loaded.Apps[0].ImageName);
            Assert.Equal(UnlockMode.ForMinutes, loaded.DefaultPolicy.Mode);
            Assert.Equal(15, loaded.DefaultPolicy.Minutes);
            Assert.True(PasswordService.VerifyMaster(loaded.Master, "hunter2!"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Update_mutates_and_persists()
    {
        var path = TempPath();
        try
        {
            var store = new ConfigStore(path);
            store.Save(new BastionConfig());
            store.Update(c => c.GlobalProtectionEnabled = false);
            Assert.False(store.Load().GlobalProtectionEnabled);
        }
        finally { File.Delete(path); }
    }
}

public class IpcSerializationTests
{
    [Fact]
    public void Request_with_enums_and_app_roundtrips()
    {
        var req = new ServiceRequest
        {
            Type = RequestType.AddApp,
            App = new ProtectedApp { ImageName = "notepad.exe", Policy = new UnlockPolicy { Mode = UnlockMode.WholeSession } },
            SessionId = 3,
        };
        var json = JsonSerializer.Serialize(req, PipeClient.Json);
        var back = JsonSerializer.Deserialize<ServiceRequest>(json, PipeClient.Json)!;
        Assert.Equal(RequestType.AddApp, back.Type);
        Assert.Equal("notepad.exe", back.App!.ImageName);
        Assert.Equal(UnlockMode.WholeSession, back.App.Policy.Mode);
        // Enums must serialize as strings for forward-compat / readability.
        Assert.Contains("\"AddApp\"", json);
    }

    [Fact]
    public void Response_snapshot_roundtrips()
    {
        var resp = new ServiceResponse
        {
            Status = ResponseStatus.Ok,
            Snapshot = new StatusSnapshot { MasterConfigured = true, Apps = { new ProtectedApp { ImageName = "x.exe" } } },
        };
        var json = JsonSerializer.Serialize(resp, PipeClient.Json);
        var back = JsonSerializer.Deserialize<ServiceResponse>(json, PipeClient.Json)!;
        Assert.Equal(ResponseStatus.Ok, back.Status);
        Assert.True(back.Snapshot!.MasterConfigured);
        Assert.Single(back.Snapshot.Apps);
    }
}

public class IfeoPolicyTests
{
    [Theory]
    [InlineData("explorer.exe", false)]
    [InlineData("lsass.exe", false)]
    [InlineData("bastion.gatekeeper.exe", false)]
    [InlineData("notepad.exe", true)]
    [InlineData("msedge.exe", true)]
    [InlineData("game", false)]         // no .exe
    public void Denylist_and_extension_rules(string image, bool protectable)
        => Assert.Equal(protectable, IfeoManager.IsProtectable(image));

    [Fact]
    public void Denylisted_gives_reason_and_allowed_gives_null()
    {
        Assert.NotNull(IfeoManager.DenylistReason("winlogon.exe"));
        Assert.Null(IfeoManager.DenylistReason("chrome.exe"));
    }
}

public class AuthGateTests
{
    [Fact]
    public void Token_mint_validate_revoke()
    {
        var gate = new AuthGate();
        var t = gate.MintToken();
        Assert.True(gate.ValidateToken(t));
        gate.RevokeToken(t);
        Assert.False(gate.ValidateToken(t));
        Assert.False(gate.ValidateToken("bogus"));
    }

    [Fact]
    public void Cooldown_engages_after_max_failures()
    {
        var gate = new AuthGate();
        Assert.Equal(0, gate.CooldownRemaining("b"));
        for (int i = 0; i < 5; i++) gate.RegisterFailure("b", maxAttempts: 5, lockoutSeconds: 30);
        Assert.True(gate.CooldownRemaining("b") > 0);
        gate.RegisterSuccess("b");
        Assert.Equal(0, gate.CooldownRemaining("b"));
    }
}
