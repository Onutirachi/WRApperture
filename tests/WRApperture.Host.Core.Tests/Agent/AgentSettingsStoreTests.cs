using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.Agent;

public class AgentSettingsStoreTests
{
    private readonly FakeRegistryAccess _registry = new();
    private readonly FakePrivilegeChecker _privileges = new();
    private readonly AgentSettingsStore _store;

    public AgentSettingsStoreTests() =>
        _store = new AgentSettingsStore(_registry, _privileges, NullLogger<AgentSettingsStore>.Instance);

    [Fact]
    public void Defaults_when_nothing_is_configured()
    {
        var settings = _store.Load();

        Assert.Equal(AgentSettings.DefaultPort, settings.Port);
        Assert.False(settings.IsPaired);
        Assert.Null(settings.CertificateThumbprint);
        Assert.False(_store.VerifyToken("anything"));
    }

    [Fact]
    public void Regenerated_token_verifies_and_only_hash_is_stored()
    {
        var token = _store.RegenerateToken();

        Assert.True(token.Length >= 43);
        Assert.True(_store.VerifyToken(token));
        Assert.True(_store.Load().IsPaired);
        foreach (var name in _registry.GetValueNames(RegistryPaths.AgentSettings))
        {
            Assert.NotEqual(token, _registry.GetValue(RegistryPaths.AgentSettings, name) as string);
        }
    }

    [Fact]
    public void Regenerating_invalidates_previous_token()
    {
        var first = _store.RegenerateToken();
        var second = _store.RegenerateToken();

        Assert.NotEqual(first, second);
        Assert.False(_store.VerifyToken(first));
        Assert.True(_store.VerifyToken(second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public void Rejects_missing_or_wrong_tokens(string? candidate)
    {
        _store.RegenerateToken();

        Assert.False(_store.VerifyToken(candidate));
    }

    [Fact]
    public void Salt_differs_between_generations()
    {
        _store.RegenerateToken();
        var salt1 = (byte[])_registry.GetValue(RegistryPaths.AgentSettings, "TokenSalt")!;
        _store.RegenerateToken();
        var salt2 = (byte[])_registry.GetValue(RegistryPaths.AgentSettings, "TokenSalt")!;

        Assert.NotEqual(salt1, salt2);
    }

    [Fact]
    public void Corrupted_credentials_fail_closed()
    {
        _registry.Seed(RegistryPaths.AgentSettings, "TokenSalt", new byte[3], RegistryValueKind.Binary);
        _registry.Seed(RegistryPaths.AgentSettings, "TokenHash", new byte[32], RegistryValueKind.Binary);

        Assert.False(_store.Load().IsPaired);
        Assert.False(_store.VerifyToken("x"));
    }

    [Fact]
    public void Port_is_validated_and_persisted()
    {
        _store.SetPort(6000);
        Assert.Equal(6000, _store.Load().Port);

        Assert.Throws<AppValidationException>(() => _store.SetPort(80));
        Assert.Throws<AppValidationException>(() => _store.SetPort(70000));
    }

    [Fact]
    public void Invalid_stored_port_falls_back_to_default()
    {
        _registry.Seed(RegistryPaths.AgentSettings, "Port", 1, RegistryValueKind.DWord);

        Assert.Equal(AgentSettings.DefaultPort, _store.Load().Port);
    }

    [Fact]
    public void Writes_require_admin_but_reads_do_not()
    {
        var token = _store.RegenerateToken();
        _privileges.IsAdmin = false;

        Assert.Throws<AdminRequiredException>(() => _store.RegenerateToken());
        Assert.Throws<AdminRequiredException>(() => _store.SetPort(6000));
        Assert.Throws<AdminRequiredException>(() => _store.SetCertificateThumbprint("AB"));
        Assert.True(_store.VerifyToken(token));
        Assert.True(_store.Load().IsPaired);
    }

    [Fact]
    public void Certificate_thumbprint_is_persisted()
    {
        _store.SetCertificateThumbprint("ABCDEF");

        Assert.Equal("ABCDEF", _store.Load().CertificateThumbprint);
    }
}

public class HostInfoProviderTests
{
    [Fact]
    public void Reads_rdp_port_with_default()
    {
        var registry = new FakeRegistryAccess();
        var provider = new HostInfoProvider(registry);

        Assert.Equal(3389, provider.Get().RdpPort);
        Assert.Equal(Environment.MachineName, provider.Get().HostName);

        registry.Seed(RegistryPaths.RdpTcp, "PortNumber", 3390, RegistryValueKind.DWord);
        Assert.Equal(3390, provider.Get().RdpPort);
    }
}
