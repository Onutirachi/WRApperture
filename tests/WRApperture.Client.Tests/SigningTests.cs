using System.Security.AccessControl;
using System.Security.Principal;
using WRApperture.Client.Core.Launch;
using WRApperture.Client.Core.Signing;

namespace WRApperture.Client.Tests;

public class ThumbprintListTests
{
    [Fact]
    public void Add_preserves_existing_entries_and_dedupes()
    {
        var value = ThumbprintList.Add("aa11, BB22", "cc 33");

        Assert.Equal("AA11,BB22,CC33", value);
        Assert.Equal("AA11,BB22,CC33", ThumbprintList.Add(value, "cc33"));
    }

    [Fact]
    public void Add_to_empty_policy()
    {
        Assert.Equal("ABC", ThumbprintList.Add(null, "abc"));
    }

    [Fact]
    public void Remove_keeps_other_publishers_and_returns_null_when_empty()
    {
        Assert.Equal("AA11", ThumbprintList.Remove("AA11,BB22", ["bb22"]));
        Assert.Null(ThumbprintList.Remove("BB22", ["BB22"]));
        Assert.Null(ThumbprintList.Remove(null, ["BB22"]));
    }

    [Fact]
    public void Contains_is_case_and_space_insensitive()
    {
        Assert.True(ThumbprintList.Contains("aa11;bb22", "BB 22"));
        Assert.False(ThumbprintList.Contains("aa11", "bb22"));
    }
}

public class SigningKeySecurityTests
{
    private const string Alice = "S-1-5-21-1-2-3-1001";
    private const string Bob = "S-1-5-21-1-2-3-1002";

    private static List<CommonAce> Aces(string sddl) =>
        new RawSecurityDescriptor(sddl).DiscretionaryAcl!.OfType<CommonAce>().ToList();

    [Fact]
    public void Initial_grants_full_control_to_system_and_admins_and_read_to_user()
    {
        var aces = Aces(SigningKeySecurity.Initial(Alice));

        Assert.Contains(aces, a => a.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid));
        Assert.Contains(aces, a => a.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
        var user = Assert.Single(aces, a => a.SecurityIdentifier.Value == Alice);
        Assert.Equal(unchecked((int)0x80000000), user.AccessMask);
        Assert.DoesNotContain(aces, a => a.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinUsersSid));
    }

    [Fact]
    public void WithUser_adds_second_user_once()
    {
        var sddl = SigningKeySecurity.WithUser(SigningKeySecurity.Initial(Alice), Bob);
        var again = SigningKeySecurity.WithUser(sddl, Bob);

        Assert.Equal(sddl, again);
        Assert.Single(Aces(sddl), a => a.SecurityIdentifier.Value == Bob);
        Assert.Single(Aces(sddl), a => a.SecurityIdentifier.Value == Alice);
    }

    [Fact]
    public void Binary_round_trip()
    {
        var sddl = SigningKeySecurity.Initial(Alice);

        Assert.Equal(sddl, SigningKeySecurity.FromBinary(SigningKeySecurity.ToBinary(sddl)));
    }

    [Fact]
    public void Rejects_invalid_sid()
    {
        Assert.ThrowsAny<ArgumentException>(() => SigningKeySecurity.Initial("not-a-sid"));
    }
}

public class RdpSigningTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeRunner _runner = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Signs_quietly_passing_the_sha1_thumbprint_when_configured()
    {
        var signing = new RdpSigning(_runner, () => new SigningStatus(true, "ABCD", "1234"), null);

        Assert.True(signing.TrySign(@"C:\x\app.rdp"));

        Assert.EndsWith("rdpsign.exe", _runner.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["/sha256", "1234", "/q", @"C:\x\app.rdp"], _runner.Arguments);
    }

    [Fact]
    public void Does_nothing_when_not_configured()
    {
        var signing = new RdpSigning(_runner, () => SigningStatus.NotConfigured, null);

        Assert.False(signing.TrySign(@"C:\x\app.rdp"));
        Assert.Null(_runner.FileName);
    }

    [Fact]
    public void Reports_failure_without_throwing()
    {
        _runner.ExitCode = 1;
        var signing = new RdpSigning(_runner, () => new SigningStatus(true, "ABCD", "1234"), null);

        Assert.False(signing.TrySign(@"C:\x\app.rdp"));
    }

    [Fact]
    public void Launcher_signs_written_files_and_still_launches_when_signing_fails()
    {
        _runner.ExitCode = 1;
        var signing = new RdpSigning(_runner, () => new SigningStatus(true, "ABCD", "1234"), null);
        var starter = new CountingStarter();
        var launcher = new RemoteAppLauncher(_dir.Paths, starter, signing);
        var host = Samples.Host();

        launcher.Launch(host, host.Apps[0]);

        Assert.Equal(launcher.RdpFilePath(host, host.Apps[0]), _runner.Arguments![^1]);
        Assert.Equal(1, starter.Count);
    }

    [Fact]
    public void Launcher_resigns_existing_files()
    {
        var launcher = new RemoteAppLauncher(_dir.Paths, new CountingStarter(), new RdpSigning(_runner, () => SigningStatus.NotConfigured, null));
        var host = Samples.Host();
        launcher.WriteRdpFile(host, host.Apps[0]);
        launcher.WriteRdpFile(host, host.Apps[1]);
        var configured = new RemoteAppLauncher(_dir.Paths, new CountingStarter(), new RdpSigning(_runner, () => new SigningStatus(true, "AB", "12"), null));

        Assert.Equal(2, configured.SignExistingFiles());
    }

    [Fact]
    public void Status_reading_never_throws_on_this_machine()
    {
        var status = new RdpSigning(_runner).GetStatus();

        Assert.NotNull(status);
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public int ExitCode { get; set; }
        public string? FileName { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }

        public int Run(string fileName, IReadOnlyList<string> arguments, out string output)
        {
            FileName = fileName;
            Arguments = arguments;
            output = ExitCode == 0 ? string.Empty : "error";
            return ExitCode;
        }
    }

    private sealed class CountingStarter : IProcessStarter
    {
        public int Count { get; private set; }

        public void Start(string fileName, string arguments) => Count++;
    }
}
