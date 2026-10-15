using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Errors;
using WRApperture.Host.Core.Models;
using WRApperture.Host.Core.Publishing;
using WRApperture.Host.Core.Readiness;
using WRApperture.Host.Core.Tests.Fakes;

namespace WRApperture.Host.Core.Tests.Readiness;

public class HostPreparationTests
{
    private readonly FakeRegistryAccess _registry = new();
    private readonly FakeFirewallRules _firewall = new();
    private const string Alice = "S-1-5-21-1-2-3-1001";
    private const string Bob = "S-1-5-21-1-2-3-1002";

    private readonly FakePrivilegeChecker _privileges = new();
    private readonly FakeUserRegistryAccess _users = new() { Loaded = { Alice } };
    private readonly FakeNetworkProfiles _networks = new();
    private readonly FakeLocalAccounts _accounts = new();
    private readonly FakeServiceControl _services = new();
    private readonly FakePowerSettings _power = new();
    private readonly LanguageBarSettings _langBar;
    private readonly HostPreparation _preparation;

    public HostPreparationTests()
    {
        // Fresh Windows Pro: RDP off, NLA on, allow-list value absent, firewall rules off.
        _registry.Seed(RegistryPaths.CurrentVersion, "EditionID", "Professional");
        _registry.Seed(RegistryPaths.TerminalServer, "fDenyTSConnections", 1, RegistryValueKind.DWord);
        _registry.Seed(RegistryPaths.RdpTcp, "UserAuthentication", 1, RegistryValueKind.DWord);
        _preparation = new HostPreparation(_registry, _firewall, _privileges, _users, _networks, _accounts, _services, _power,
            NullLogger<HostPreparation>.Instance);
        _langBar = new LanguageBarSettings(_users);
    }

    [Fact]
    public void Language_bar_is_pending_until_hidden_for_every_loaded_profile()
    {
        _users.Loaded.Add(Bob);
        _langBar.Hide(Bob);

        var check = _preparation.Check().Single(c => c.Id == ReadinessCheckId.LanguageBarHidden);

        Assert.Equal(ReadinessState.Pending, check.State);
        Assert.Equal("1/2 profiles", check.CurrentValue);
    }

    [Fact]
    public void Language_bar_is_ok_without_loaded_profiles()
    {
        _users.Loaded.Clear();

        Assert.Equal(ReadinessState.Ok, StateOf(_preparation.Check(), ReadinessCheckId.LanguageBarHidden));
    }

    [Fact]
    public void Prepare_hides_language_bar_and_revert_restores_each_profile()
    {
        _users.Loaded.Add(Bob);
        _users.SetValue(Bob, LanguageBarSettings.Path, LanguageBarSettings.Name, 4, RegistryValueKind.DWord);

        _preparation.Prepare();
        Assert.True(_langBar.IsHidden(Alice));
        Assert.True(_langBar.IsHidden(Bob));

        _preparation.Revert();
        Assert.Null(_users.GetValue(Alice, LanguageBarSettings.Path, LanguageBarSettings.Name)); // did not exist before
        Assert.Equal(4, _users.GetValue(Bob, LanguageBarSettings.Path, LanguageBarSettings.Name));
    }

    [Fact]
    public void Revert_skips_profiles_that_are_no_longer_loaded()
    {
        _users.Loaded.Add(Bob);
        _preparation.Prepare();
        _users.Loaded.Remove(Bob);

        _preparation.Revert();

        Assert.True(_langBar.IsHidden(Bob));
        Assert.False(_registry.KeyExists(RegistryPaths.PreparationSnapshot));
    }

    [Fact]
    public void Language_bar_settings_hide_and_show()
    {
        _langBar.Hide(Alice);
        Assert.True(_langBar.IsHidden(Alice));

        _langBar.Show(Alice);
        Assert.False(_langBar.IsHidden(Alice));
    }

    private ReadinessState StateOf(IReadOnlyList<ReadinessCheck> checks, ReadinessCheckId id) =>
        checks.Single(c => c.Id == id).State;

    [Fact]
    public void Check_reports_every_item_with_pending_ones()
    {
        var checks = _preparation.Check();

        // Every item but the Microsoft account one, which only exists for Microsoft accounts.
        Assert.Equal(Enum.GetValues<ReadinessCheckId>().Except([ReadinessCheckId.MicrosoftAccount]).Order(), checks.Select(c => c.Id).Order());
        Assert.Equal(ReadinessState.Ok, StateOf(checks, ReadinessCheckId.Edition));
        Assert.Equal(ReadinessState.Pending, StateOf(checks, ReadinessCheckId.RdpEnabled));
        Assert.Equal(ReadinessState.Ok, StateOf(checks, ReadinessCheckId.Nla));
        Assert.Equal(ReadinessState.Ok, StateOf(checks, ReadinessCheckId.AllowListEnforced));
        Assert.Equal(ReadinessState.Pending, StateOf(checks, ReadinessCheckId.FirewallRdp));
        Assert.All(checks, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
    }

    [Fact]
    public void Check_does_not_require_admin_nor_write()
    {
        _privileges.IsAdmin = false;

        _preparation.Check();

        Assert.Equal(0, _registry.WriteCount);
    }

    [Fact]
    public void Check_flags_allow_list_disabled_as_pending()
    {
        _registry.Seed(RegistryPaths.AllowList, "fDisabledAllowList", 1, RegistryValueKind.DWord);

        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.AllowListEnforced));
    }

    [Fact]
    public void Check_flags_nla_off_as_pending()
    {
        _registry.Seed(RegistryPaths.RdpTcp, "UserAuthentication", 0, RegistryValueKind.DWord);

        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.Nla));
    }

    [Fact]
    public void Firewall_without_rdp_rules_is_pending()
    {
        _firewall.Rules.Clear();

        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.FirewallRdp));
    }

    [Fact]
    public void Prepare_fixes_everything()
    {
        _registry.Seed(RegistryPaths.AllowList, "fDisabledAllowList", 1, RegistryValueKind.DWord);
        _registry.Seed(RegistryPaths.RdpTcp, "UserAuthentication", 0, RegistryValueKind.DWord);

        var checks = _preparation.Prepare();

        Assert.All(checks, c => Assert.Equal(ReadinessState.Ok, c.State));
        Assert.Equal(0, _registry.GetValue(RegistryPaths.TerminalServer, "fDenyTSConnections"));
        Assert.Equal(1, _registry.GetValue(RegistryPaths.RdpTcp, "UserAuthentication"));
        Assert.Equal(0, _registry.GetValue(RegistryPaths.AllowList, "fDisabledAllowList"));
        Assert.All(_firewall.Rules.Values, Assert.True);
    }

    [Fact]
    public void Prepare_is_idempotent()
    {
        _preparation.Prepare();
        var writes = _registry.WriteCount;

        var checks = _preparation.Prepare();

        Assert.Equal(writes, _registry.WriteCount);
        Assert.All(checks, c => Assert.Equal(ReadinessState.Ok, c.State));
    }

    [Fact]
    public void Prepare_requires_admin()
    {
        _privileges.IsAdmin = false;

        Assert.Throws<AdminRequiredException>(() => _preparation.Prepare());
        Assert.Throws<AdminRequiredException>(() => _preparation.Revert());
        Assert.Equal(0, _registry.WriteCount);
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("CoreSingleLanguage")]
    public void Home_edition_is_not_supported(string edition)
    {
        _registry.Seed(RegistryPaths.CurrentVersion, "EditionID", edition);

        Assert.Equal(ReadinessState.NotSupported, StateOf(_preparation.Check(), ReadinessCheckId.Edition));
        Assert.Throws<HostNotSupportedException>(() => _preparation.Prepare());
        Assert.Equal(0, _registry.WriteCount);
        Assert.All(_firewall.Rules.Values, Assert.False);
    }

    [Theory]
    [InlineData("Enterprise")]
    [InlineData("Education")]
    [InlineData("ProfessionalWorkstation")]
    [InlineData("ServerStandard")]
    public void Business_editions_are_supported(string edition)
    {
        _registry.Seed(RegistryPaths.CurrentVersion, "EditionID", edition);

        Assert.Equal(ReadinessState.Ok, StateOf(_preparation.Check(), ReadinessCheckId.Edition));
    }

    [Fact]
    public void Revert_restores_only_what_prepare_changed()
    {
        _preparation.Prepare();

        var checks = _preparation.Revert();

        Assert.Equal(1, _registry.GetValue(RegistryPaths.TerminalServer, "fDenyTSConnections"));
        Assert.Equal(1, _registry.GetValue(RegistryPaths.RdpTcp, "UserAuthentication")); // untouched
        Assert.Null(_registry.GetValue(RegistryPaths.AllowList, "fDisabledAllowList"));  // did not exist before
        Assert.All(_firewall.Rules.Values, Assert.False);
        Assert.Equal(ReadinessState.Pending, StateOf(checks, ReadinessCheckId.RdpEnabled));
        Assert.False(_registry.KeyExists(RegistryPaths.PreparationSnapshot));
    }

    [Fact]
    public void Revert_keeps_firewall_rules_that_were_already_enabled()
    {
        _firewall.Rules["RDP UserMode TCP"] = true;
        _preparation.Prepare();

        _preparation.Revert();

        Assert.True(_firewall.Rules["RDP UserMode TCP"]);
        Assert.False(_firewall.Rules["RDP UserMode UDP"]);
    }

    [Fact]
    public void Snapshot_keeps_the_original_value_across_repeated_prepares()
    {
        _preparation.Prepare();
        // Someone turns RDP off again by hand; a second prepare must not overwrite the original snapshot.
        _registry.Seed(RegistryPaths.TerminalServer, "fDenyTSConnections", 1, RegistryValueKind.DWord);
        _preparation.Prepare();
        _registry.Seed(RegistryPaths.TerminalServer, "fDenyTSConnections", 0, RegistryValueKind.DWord);

        _preparation.Revert();

        Assert.Equal(1, _registry.GetValue(RegistryPaths.TerminalServer, "fDenyTSConnections"));
    }

    [Fact]
    public void Revert_without_snapshot_changes_nothing()
    {
        var checks = _preparation.Revert();

        Assert.Equal(0, _registry.WriteCount);
        Assert.Equal(ReadinessState.Pending, StateOf(checks, ReadinessCheckId.RdpEnabled));
    }

    [Fact]
    public void Public_network_is_pending_and_fix_makes_it_private_until_revert()
    {
        _networks.Networks[0] = _networks.Networks[0] with { Category = NetworkCategory.Public };
        var check = _preparation.Check().Single(c => c.Id == ReadinessCheckId.NetworkProfile);
        Assert.Equal(ReadinessState.Pending, check.State);
        Assert.Equal("Public: Casa", check.CurrentValue);
        Assert.Equal(ReadinessFix.Automatic, check.Fix);

        Assert.Equal(ReadinessState.Ok, StateOf(_preparation.Fix(ReadinessCheckId.NetworkProfile), ReadinessCheckId.NetworkProfile));
        Assert.Equal(NetworkCategory.Private, _networks.Networks[0].Category);

        _preparation.Revert();
        Assert.Equal(NetworkCategory.Public, _networks.Networks[0].Category);
    }

    [Fact]
    public void Prepare_never_changes_network_groups_nor_sign_in()
    {
        _networks.Networks[0] = _networks.Networks[0] with { Category = NetworkCategory.Public };
        _accounts.Console = _accounts.Console! with { IsAdministrator = false };
        _registry.Seed(RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion", 2, RegistryValueKind.DWord);

        _preparation.Prepare();

        Assert.Equal(NetworkCategory.Public, _networks.Networks[0].Category);
        Assert.Empty(_accounts.RemoteDesktopUsers);
        Assert.Equal(2, _registry.GetValue(RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion"));
    }

    [Fact]
    public void Blank_password_is_pending_and_set_account_password_solves_it()
    {
        _accounts.Password = PasswordStatus.Blank;
        var check = _preparation.Check().Single(c => c.Id == ReadinessCheckId.AccountPassword);
        Assert.Equal(ReadinessState.Pending, check.State);
        Assert.Equal(ReadinessFix.SetPassword, check.Fix);
        Assert.Contains("carlos", check.Description);

        var checks = _preparation.SetAccountPassword("s3nha!");

        Assert.Equal("s3nha!", _accounts.NewPassword);
        Assert.Equal(ReadinessState.Ok, StateOf(checks, ReadinessCheckId.AccountPassword));
    }

    [Fact]
    public void Set_account_password_requires_admin_and_a_password()
    {
        Assert.Throws<AppValidationException>(() => _preparation.SetAccountPassword(""));

        _privileges.IsAdmin = false;
        Assert.Throws<AdminRequiredException>(() => _preparation.SetAccountPassword("x"));
        Assert.Null(_accounts.NewPassword);
    }

    [Fact]
    public void Microsoft_account_gets_advice_with_its_email_and_no_password_check()
    {
        _accounts.Console = _accounts.Console! with { MicrosoftAccount = "carlos@outlook.com" };

        var checks = _preparation.Check();

        Assert.DoesNotContain(checks, c => c.Id == ReadinessCheckId.AccountPassword);
        var check = checks.Single(c => c.Id == ReadinessCheckId.MicrosoftAccount);
        Assert.Equal(ReadinessState.Advice, check.State);
        Assert.Equal("carlos@outlook.com", check.CurrentValue);
        Assert.Throws<InvalidOperationException>(() => _preparation.SetAccountPassword("x"));
    }

    [Fact]
    public void Windows_hello_only_blocks_microsoft_accounts_until_fixed_and_revert_restores_it()
    {
        _accounts.Console = _accounts.Console! with { MicrosoftAccount = "carlos@outlook.com" };
        _registry.Seed(RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion", 2, RegistryValueKind.DWord);
        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.MicrosoftAccount));

        _preparation.Fix(ReadinessCheckId.MicrosoftAccount);
        Assert.Equal(0, _registry.GetValue(RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion"));
        Assert.Equal(ReadinessState.Advice, StateOf(_preparation.Check(), ReadinessCheckId.MicrosoftAccount));

        _preparation.Revert();
        Assert.Equal(2, _registry.GetValue(RegistryPaths.PasswordLess, "DevicePasswordLessBuildVersion"));
    }

    [Fact]
    public void Standard_user_is_added_to_remote_desktop_users_and_removed_on_revert()
    {
        _accounts.Console = _accounts.Console! with { IsAdministrator = false };
        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.RemoteDesktopUsers));

        _preparation.Fix(ReadinessCheckId.RemoteDesktopUsers);
        Assert.Contains(FakeLocalAccounts.Sid, _accounts.RemoteDesktopUsers);

        _preparation.Revert();
        Assert.Empty(_accounts.RemoteDesktopUsers);
    }

    [Fact]
    public void Administrator_already_signs_in_remotely_and_fix_changes_nothing()
    {
        _preparation.Fix(ReadinessCheckId.RemoteDesktopUsers);

        Assert.Empty(_accounts.RemoteDesktopUsers);
        Assert.False(_registry.KeyExists(RegistryPaths.PreparationSnapshot));
    }

    [Fact]
    public void Nobody_signed_in_means_no_account_checks()
    {
        _accounts.Console = null;

        var ids = _preparation.Check().Select(c => c.Id).ToList();

        Assert.DoesNotContain(ReadinessCheckId.AccountPassword, ids);
        Assert.DoesNotContain(ReadinessCheckId.RemoteDesktopUsers, ids);
    }

    [Fact]
    public void Policy_blocking_remote_desktop_is_fixed_by_prepare_and_restored_by_revert()
    {
        _registry.Seed(RegistryPaths.TerminalServicesPolicy, "fDenyTSConnections", 1, RegistryValueKind.DWord);
        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.RdpPolicy));

        _preparation.Prepare();
        Assert.Equal(0, _registry.GetValue(RegistryPaths.TerminalServicesPolicy, "fDenyTSConnections"));

        _preparation.Revert();
        Assert.Equal(1, _registry.GetValue(RegistryPaths.TerminalServicesPolicy, "fDenyTSConnections"));
    }

    [Fact]
    public void Disabled_remote_desktop_service_is_enabled_and_started()
    {
        _registry.Seed(RegistryPaths.TermService, "Start", 4, RegistryValueKind.DWord);
        var check = _preparation.Check().Single(c => c.Id == ReadinessCheckId.RdpService);
        Assert.Equal(ReadinessState.Pending, check.State);
        Assert.Equal("disabled", check.CurrentValue);

        _preparation.Prepare();

        Assert.Equal(3, _registry.GetValue(RegistryPaths.TermService, "Start"));
        Assert.Contains(HostPreparation.RdpServiceName, _services.Running);
    }

    [Fact]
    public void Stopped_service_only_matters_once_remote_desktop_is_on()
    {
        Assert.Equal(ReadinessState.Ok, StateOf(_preparation.Check(), ReadinessCheckId.RdpService)); // RDP off

        _registry.Seed(RegistryPaths.TerminalServer, "fDenyTSConnections", 0, RegistryValueKind.DWord);
        Assert.Equal(ReadinessState.Pending, StateOf(_preparation.Check(), ReadinessCheckId.RdpService));

        Assert.Equal(ReadinessState.Ok, StateOf(_preparation.Fix(ReadinessCheckId.RdpService), ReadinessCheckId.RdpService));
    }

    [Fact]
    public void Sleeping_pc_is_advice_with_the_power_settings_page()
    {
        _power.Sleep = TimeSpan.FromMinutes(30);

        var check = _preparation.Check().Single(c => c.Id == ReadinessCheckId.Sleep);

        Assert.Equal(ReadinessState.Advice, check.State);
        Assert.Equal(ReadinessFix.OpenSettings, check.Fix);
        Assert.Equal("ms-settings:powersleep", check.SettingsUri);
        Assert.Contains("30", check.CurrentValue);
    }

    [Theory]
    [InlineData(ReadinessCheckId.Edition)]
    [InlineData(ReadinessCheckId.Sleep)]
    [InlineData(ReadinessCheckId.AccountPassword)]
    public void Items_without_automatic_fix_refuse_fix(ReadinessCheckId id)
    {
        Assert.Throws<InvalidOperationException>(() => _preparation.Fix(id));
    }

    [Fact]
    public void Fix_requires_admin()
    {
        _privileges.IsAdmin = false;

        Assert.Throws<AdminRequiredException>(() => _preparation.Fix(ReadinessCheckId.NetworkProfile));
    }
}
