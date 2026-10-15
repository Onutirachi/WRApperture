using WRApperture.Shared.Localization;

namespace WRApperture.Client.Core.Agent;

/// <summary>Base for agent errors. Messages are user-facing (translated) and suggest an action.</summary>
public class AgentException(string message, Exception? inner = null) : Exception(message, inner);

/// <param name="message">Null: the generic "invalid or revoked token" message.</param>
public sealed class InvalidTokenException(string? message = null, Exception? inner = null)
    : AgentException(message ?? Strings.T("client.errors.invalidToken"), inner)
{
    /// <summary>
    /// The host itself refused the token (HTTP 401): this computer was removed from its devices, or its token was
    /// regenerated. False when the problem is local, e.g. a saved token this Windows user cannot decrypt.
    /// </summary>
    public bool RefusedByHost { get; init; }
}

public sealed class TooManyAttemptsException()
    : AgentException(Strings.T("client.errors.tooManyAttempts"));

public sealed class AgentUnreachableException(string address, int port, Exception? inner = null)
    : AgentException(Strings.T("client.errors.unreachable", address, port), inner);

public sealed class CertificateChangedException(string observedFingerprint)
    : AgentException(Strings.T("client.errors.certificateChanged"))
{
    public string ObservedFingerprint { get; } = observedFingerprint;
}

public sealed class PairingMismatchException()
    : AgentException(Strings.T("client.errors.pairingMismatch"));

/// <summary>The host did not pair: denied, expired, nobody there to allow it, too many requests...</summary>
public sealed class PairingRefusedException(string message)
    : AgentException(message);
