using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WRApperture.Shared;

/// <param name="ClientName">The client computer's name, shown on the host's screen.</param>
/// <param name="ClientUser">The Windows user on the client, shown on the host's screen.</param>
/// <param name="Nonce">Random, chosen by the client (Base64, 16 bytes): goes into the verification code.</param>
public sealed record PairingRequestDto(string ClientName, string? ClientUser, string Nonce);

public sealed record PairingTicketDto(string Id, int ExpiresInSeconds);

/// <param name="State">One of <see cref="PairingStates"/>.</param>
/// <param name="Token">The new client token: only in the first "approved" answer.</param>
public sealed record PairingStatusDto(string State, string? Token = null);

public static class PairingStates
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Expired = "expired";
}

/// <summary>What the approval window shows.</summary>
public sealed record PairingDetailsDto(string ClientName, string? ClientUser, string Address, string Code, int ExpiresInSeconds);

public sealed record PairingDecisionDto(bool Approve);

/// <summary>
/// The 6-digit code both screens show during pairing. Each side computes it from the certificate fingerprint
/// it sees on the TLS connection: someone relaying the connection presents another certificate, so the codes differ.
/// The client's nonce, sent after the TLS handshake, keeps a relay from crafting a certificate that matches.
/// </summary>
public static class PairingCode
{
    private const string Context = "WRApperture-pairing";

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>"482 913".</summary>
    public static string Compute(string fingerprint, string nonce)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Context}|{fingerprint.ToUpperInvariant()}|{nonce}"));
        var value = (BitConverter.ToUInt32(hash, 0) & 0x7FFFFFFF) % 1_000_000;
        var digits = value.ToString("D6", CultureInfo.InvariantCulture);
        return $"{digits[..3]} {digits[3..]}";
    }
}

/// <param name="HostName">The host computer's name.</param>
/// <param name="Features">What the agent supports (<see cref="AgentFeatures"/>).</param>
public sealed record DiscoveryReplyDto(string HostName, int AgentPort, string Fingerprint, string Version, IReadOnlyList<string> Features);

/// <summary>
/// UDP discovery on <see cref="ApiContract.DiscoveryPort"/>: the client broadcasts <see cref="BuildRequest"/>,
/// each agent answers with a <see cref="DiscoveryReplyDto"/> in JSON. The request is padded to
/// <see cref="RequestSize"/> bytes and replies are capped at that size, so an answer is never larger than the
/// question (no use for traffic amplification).
/// </summary>
public static class Discovery
{
    public const int RequestSize = 512;
    private const string Magic = "WRAPPERTURE-DISCOVER/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] BuildRequest()
    {
        var request = new byte[RequestSize];
        Array.Fill(request, (byte)' ');
        Encoding.ASCII.GetBytes(Magic).CopyTo(request, 0);
        return request;
    }

    public static bool IsRequest(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= RequestSize && datagram.StartsWith(Encoding.ASCII.GetBytes(Magic));

    /// <summary>Null when the reply would not fit in <see cref="RequestSize"/> bytes.</summary>
    public static byte[]? EncodeReply(DiscoveryReplyDto reply)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, Json);
        return bytes.Length <= RequestSize ? bytes : null;
    }

    public static DiscoveryReplyDto? DecodeReply(ReadOnlySpan<byte> datagram)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<DiscoveryReplyDto>(datagram, Json);
            return reply is { HostName.Length: > 0, Fingerprint.Length: > 0, AgentPort: > 0 and <= 65535 } ? reply : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
