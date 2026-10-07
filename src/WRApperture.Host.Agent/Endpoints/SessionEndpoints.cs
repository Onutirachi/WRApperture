using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Endpoints;

internal static class SessionEndpoints
{
    public static void MapSessionApi(this WebApplication app)
    {
        app.MapGet(ApiContract.Session, (string? user, ISessionProvider sessions) =>
            Results.Ok(Status(SessionPolicy.Blocking(sessions.List(), user))));

        app.MapPost(ApiContract.SessionRelease, async (SessionReleaseRequestDto request, ISessionProvider sessions,
            ILogger<SessionReleaseRequestDto> logger, CancellationToken ct) =>
        {
            var blocking = SessionPolicy.Blocking(sessions.List(), request.User);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(ApiContract.SessionReleaseTimeoutSeconds - 5));

            var released = 0;
            foreach (var session in blocking)
            {
                logger.LogWarning("Logging off desktop session {Session} of {Domain}\\{User} ({State}, console: {Console}) so a RemoteApp can start",
                    session.Id, session.Domain, session.User, session.State, session.IsConsole);
                try
                {
                    await Task.Run(() => sessions.Logoff(session.Id), timeout.Token);
                    released++;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException)
                {
                    logger.LogError(ex, "Logoff of session {Session} failed", session.Id);
                }
            }

            var stillBusy = SessionPolicy.Blocking(sessions.List(), request.User).Count > 0;
            return Results.Ok(new SessionReleaseResultDto(released, stillBusy));
        });
    }

    private static SessionStatusDto Status(IReadOnlyList<HostSession> blocking) =>
        new(blocking.Count > 0, blocking.Select(s => new BusySessionDto(
            string.IsNullOrEmpty(s.Domain) ? s.User : $@"{s.Domain}\{s.User}",
            s.State == SessionConnectState.Disconnected ? "desconectada" : "em uso",
            s.IsConsole)).ToList());
}
