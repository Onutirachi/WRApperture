using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Endpoints;

internal static class SessionEndpoints
{
    public static void MapSessionApi(this WebApplication app)
    {
        app.MapGet(ApiContract.Session, (string? user, ISessionProvider sessions) =>
            Results.Ok(Status(SessionPolicy.Plan(sessions.List(), user))));

        app.MapPost(ApiContract.SessionRelease, async (SessionReleaseRequestDto request, ISessionProvider sessions,
            ILogger<SessionReleaseRequestDto> logger, CancellationToken ct) =>
        {
            var plan = SessionPolicy.Plan(sessions.List(), request.User);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(ApiContract.SessionReleaseTimeoutSeconds - 5));

            var released = 0;
            // Disconnect other users first: a pending logoff must not be interrupted by Windows' "allow?" prompt.
            foreach (var session in plan.Disconnect)
            {
                logger.LogWarning("Disconnecting active session {Session} of {Domain}\\{User} (programs stay open) so a RemoteApp can start",
                    session.Id, session.Domain, session.User);
                released += await RunAsync(() => sessions.Disconnect(session.Id), session, logger, timeout.Token);
            }

            foreach (var session in plan.Logoff)
            {
                logger.LogWarning("Logging off desktop session {Session} of {Domain}\\{User} ({State}, console: {Console}) so a RemoteApp can start",
                    session.Id, session.Domain, session.User, session.State, session.IsConsole);
                released += await RunAsync(() => sessions.Logoff(session.Id), session, logger, timeout.Token);
            }

            var stillBusy = !SessionPolicy.Plan(sessions.List(), request.User).IsEmpty;
            return Results.Ok(new SessionReleaseResultDto(released, stillBusy));
        });
    }

    private static async Task<int> RunAsync(Action action, HostSession session, ILogger logger, CancellationToken ct)
    {
        try
        {
            await Task.Run(action, ct);
            return 1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            logger.LogError(ex, "Releasing session {Session} failed", session.Id);
            return 0;
        }
    }

    private static SessionStatusDto Status(ReleasePlan plan)
    {
        var items = plan.Logoff.Select(s => Dto(s, SessionActions.Logoff))
            .Concat(plan.Disconnect.Select(s => Dto(s, SessionActions.Disconnect)))
            .ToList();
        return new SessionStatusDto(items.Count > 0, items);
    }

    private static BusySessionDto Dto(HostSession s, string action) => new(
        string.IsNullOrEmpty(s.Domain) ? s.User : $@"{s.Domain}\{s.User}",
        s.State == SessionConnectState.Disconnected ? "desconectada" : "em uso",
        s.IsConsole,
        action);
}
