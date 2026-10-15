using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Core.Agent;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Pairing;

internal sealed class PairingOptions
{
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The host app, started with "--pairing &lt;id&gt; &lt;secret&gt;" on the screen of whoever uses the PC.</summary>
    public string ApproverPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "WRApperture.Host.exe");
}

/// <summary>A request the agent refuses; <see cref="Error"/> is an <see cref="ApiContract.Errors"/> code.</summary>
internal sealed class PairingException(string error, int status, string message) : Exception(message)
{
    public string Error { get; } = error;
    public int Status { get; } = status;
}

/// <summary>
/// Pairing by approval: a client asks, the person using the host sees who asks (and the verification code) in a
/// window the agent opens on their screen, and allows or denies. Allowed clients get a token of their own,
/// delivered once to the asking client.
/// </summary>
internal sealed class PairingService(
    IAgentSettingsStore settings,
    ISessionProvider sessions,
    AgentIdentity identity,
    TimeProvider time,
    PairingOptions options,
    ILogger<PairingService> logger)
{
    public const int MaxPending = 3;
    public const int MaxPerAddress = 5;
    public const int MaxNameLength = 40;
    public static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, Request> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _history = new(StringComparer.Ordinal);

    public PairingTicketDto Create(PairingRequestDto dto, string address)
    {
        var name = Clean(dto.ClientName);
        var user = Clean(dto.ClientUser);
        if (name.Length == 0 || !IsNonce(dto.Nonce))
        {
            throw new PairingException(ApiContract.Errors.BadRequest, StatusCodes.Status400BadRequest, Strings.T("agent.pairing.invalidRequest"));
        }

        var approver = sessions.List()
            .Where(s => s is { State: SessionConnectState.Active, Kind: SessionKind.Desktop })
            .OrderByDescending(s => s.IsConsole)
            .FirstOrDefault()
            ?? throw new PairingException(ApiContract.Errors.NoApprover, StatusCodes.Status409Conflict, Strings.T("agent.pairing.noApprover"));

        Request request;
        lock (_gate)
        {
            var now = time.GetUtcNow();
            Forget(now);
            var recent = _history.TryGetValue(address, out var times) ? times : _history[address] = new Queue<DateTimeOffset>();
            if (_requests.Values.Count(r => r.IsPending(now)) >= MaxPending ||
                _requests.Values.Any(r => r.Address == address && r.IsPending(now)) ||
                recent.Count >= MaxPerAddress)
            {
                logger.LogWarning("Pairing request from {Address} refused: too many requests", address);
                throw new PairingException(ApiContract.Errors.PairingLimited, StatusCodes.Status429TooManyRequests, Strings.T("agent.pairing.limited"));
            }

            recent.Enqueue(now);
            request = new Request(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                name, user.Length == 0 ? null : user, address,
                PairingCode.Compute(identity.Fingerprint, dto.Nonce),
                now + options.Lifetime);
            _requests[request.Id] = request;
        }

        try
        {
            sessions.StartProcess(approver.Id, options.ApproverPath, $"--pairing {request.Id} {request.Secret}");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "Could not open the pairing window in session {Session}", approver.Id);
            lock (_gate)
            {
                _requests.Remove(request.Id);
            }

            throw new PairingException(ApiContract.Errors.NoApprover, StatusCodes.Status409Conflict, Strings.T("agent.pairing.noWindow"));
        }

        logger.LogInformation("Pairing request {Id} from {Name} ({User}, {Address}) shown to {Approver}",
            request.Id, request.Name, request.User, address, approver.User);
        return new PairingTicketDto(request.Id, (int)options.Lifetime.TotalSeconds);
    }

    /// <summary>Null for unknown ids. The token goes out only in the first "approved" answer.</summary>
    public PairingStatusDto? Status(string id)
    {
        lock (_gate)
        {
            if (!_requests.TryGetValue(id, out var request))
            {
                return null;
            }

            var state = request.StateAt(time.GetUtcNow());
            var token = request.Token;
            request.Token = null;
            return new PairingStatusDto(state, token);
        }
    }

    /// <summary>For the approval window; null when the id or secret is wrong, or the request is no longer pending.</summary>
    public PairingDetailsDto? Details(string id, string? secret)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            return Find(id, secret) is { } request && request.IsPending(now)
                ? new PairingDetailsDto(request.Name, request.User, request.Address, request.Code,
                    Math.Max(0, (int)(request.ExpiresUtc - now).TotalSeconds))
                : null;
        }
    }

    /// <summary>False when the id or secret is wrong, or the request is no longer pending.</summary>
    public bool Decide(string id, string? secret, bool approve)
    {
        lock (_gate)
        {
            if (Find(id, secret) is not { } request || !request.IsPending(time.GetUtcNow()))
            {
                return false;
            }

            if (approve)
            {
                var (client, token) = settings.AddClient(request.Name, request.User);
                request.Token = token;
                request.State = PairingStates.Approved;
                logger.LogInformation("Pairing request {Id} approved: client {Client} ({Name})", id, client.Id, client.Name);
            }
            else
            {
                request.State = PairingStates.Denied;
                logger.LogInformation("Pairing request {Id} from {Name} denied", id, request.Name);
            }

            return true;
        }
    }

    private Request? Find(string id, string? secret) =>
        _requests.TryGetValue(id, out var request) && secret is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(request.Secret))
            ? request
            : null;

    /// <summary>Drops requests long past their expiry and address history outside the window.</summary>
    private void Forget(DateTimeOffset now)
    {
        foreach (var id in _requests.Where(r => now - r.Value.ExpiresUtc > options.Lifetime).Select(r => r.Key).ToList())
        {
            _requests.Remove(id);
        }

        foreach (var (address, times) in _history.ToList())
        {
            while (times.TryPeek(out var oldest) && now - oldest > AddressWindow)
            {
                times.Dequeue();
            }

            if (times.Count == 0)
            {
                _history.Remove(address);
            }
        }
    }

    /// <summary>Names come from the network: no control characters, bounded length.</summary>
    private static string Clean(string? text)
    {
        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray()).Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength] : clean;
    }

    private static bool IsNonce(string? nonce)
    {
        var buffer = new byte[32];
        return nonce is not null && Convert.TryFromBase64String(nonce, buffer, out var length) && length == 16;
    }

    private sealed class Request(string id, string secret, string name, string? user, string address, string code, DateTimeOffset expiresUtc)
    {
        public string Id { get; } = id;
        public string Secret { get; } = secret;
        public string Name { get; } = name;
        public string? User { get; } = user;
        public string Address { get; } = address;
        public string Code { get; } = code;
        public DateTimeOffset ExpiresUtc { get; } = expiresUtc;
        public string State { get; set; } = PairingStates.Pending;
        public string? Token { get; set; }

        public bool IsPending(DateTimeOffset now) => StateAt(now) == PairingStates.Pending;

        public string StateAt(DateTimeOffset now) =>
            State == PairingStates.Pending && now >= ExpiresUtc ? PairingStates.Expired : State;
    }
}
