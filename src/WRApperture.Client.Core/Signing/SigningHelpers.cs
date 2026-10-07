using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WRApperture.Client.Core.Signing;

/// <summary>Edits the comma-separated SHA-1 list of the "trusted .rdp publishers" policy.</summary>
public static class ThumbprintList
{
    public static IReadOnlyList<string> Parse(string? value) =>
        (value ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Add(string? value, string thumbprint)
    {
        var list = Parse(value).ToList();
        var normalized = Normalize(thumbprint);
        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(normalized);
        }

        return string.Join(",", list);
    }

    /// <summary>Returns null when nothing is left.</summary>
    public static string? Remove(string? value, IEnumerable<string> thumbprints)
    {
        var remove = thumbprints.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = Parse(value).Where(t => !remove.Contains(t)).ToList();
        return left.Count == 0 ? null : string.Join(",", left);
    }

    public static bool Contains(string? value, string thumbprint) =>
        Parse(value).Contains(Normalize(thumbprint), StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string thumbprint) => thumbprint.Replace(" ", string.Empty).ToUpperInvariant();
}

/// <summary>DACL of the machine signing key: full control for SYSTEM/Administrators, read (use) for configured users.</summary>
public static class SigningKeySecurity
{
    private const int GenericRead = unchecked((int)0x80000000);

    public static string Initial(string userSid) => WithUser("D:P(A;;GA;;;SY)(A;;GA;;;BA)", userSid);

    /// <summary>Adds a read ACE for <paramref name="userSid"/> unless one already exists.</summary>
    public static string WithUser(string sddl, string userSid)
    {
        var sid = new SecurityIdentifier(userSid);
        var descriptor = new RawSecurityDescriptor(sddl);
        var acl = descriptor.DiscretionaryAcl ?? new RawAcl(GenericAcl.AclRevision, 1);
        if (acl.OfType<CommonAce>().Any(a => a.SecurityIdentifier == sid && a.AceQualifier == AceQualifier.AccessAllowed))
        {
            return descriptor.GetSddlForm(AccessControlSections.Access);
        }

        acl.InsertAce(acl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, GenericRead, sid, false, null));
        descriptor.DiscretionaryAcl = acl;
        return descriptor.GetSddlForm(AccessControlSections.Access);
    }

    public static byte[] ToBinary(string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        return bytes;
    }

    public static string FromBinary(byte[] bytes) =>
        new RawSecurityDescriptor(bytes, 0).GetSddlForm(AccessControlSections.Access);
}

public interface IProcessRunner
{
    /// <summary>Runs to completion (or timeout) and returns the exit code; output is captured.</summary>
    int Run(string fileName, IReadOnlyList<string> arguments, out string output);
}

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public int Run(string fileName, IReadOnlyList<string> arguments, out string output)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill();
            output = "timeout";
            return -1;
        }

        output = stdout.Result + stderr.Result;
        return process.ExitCode;
    }
}
