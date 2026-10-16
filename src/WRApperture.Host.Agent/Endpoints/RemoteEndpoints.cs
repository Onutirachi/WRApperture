using WRApperture.Host.Agent.Remote;
using WRApperture.Host.Core;
using WRApperture.Host.Core.Abstractions;
using WRApperture.Host.Core.Sessions;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Endpoints;

/// <summary>Closing apps and ending RemoteApp sessions from a client (never touches desktop sessions).</summary>
internal static class RemoteEndpoints
{
    public static void MapRemoteApi(this WebApplication app)
    {
        app.MapPost(ApiContract.Apps + "/{alias}/close", (string alias, IAppPublisher publisher, RunningApps running, ISessionProvider sessions,
            ILogger<RunningApps> logger) =>
        {
            if (publisher.Get(alias) is not { } published)
            {
                return NotFound(alias);
            }

            // Like the window's X: taskkill without /F, run in the app's own session (it cannot reach other sessions' windows).
            var processes = running.Of(published);
            var taskkill = Path.Combine(Environment.SystemDirectory, "taskkill.exe");
            foreach (var session in processes.GroupBy(p => p.SessionId))
            {
                var arguments = string.Join(' ', session.Select(p => $"/PID {p.Id}"));
                try
                {
                    sessions.StartHidden(session.Key, taskkill, arguments);
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    logger.LogError(ex, "Could not ask {Alias} to close in session {Session}", alias, session.Key);
                }
            }

            logger.LogInformation("Asked {Alias} to close ({Count} processes)", alias, processes.Count);
            return Results.Ok(new AppStopResultDto(processes.Count));
        });

        app.MapPost(ApiContract.Apps + "/{alias}/kill", (string alias, IAppPublisher publisher, RunningApps running, IProcessList processList,
            ILogger<RunningApps> logger) =>
        {
            if (publisher.Get(alias) is not { } published)
            {
                return NotFound(alias);
            }

            var name = Path.GetFileNameWithoutExtension(published.ExecutablePath);
            var ended = running.Of(published).Count(p => processList.Stop(p.Id, name));
            logger.LogWarning("Ended {Alias} ({Count} processes) at a client's request", alias, ended);
            return Results.Ok(new AppStopResultDto(ended));
        });

        app.MapPost(ApiContract.SessionEnd, async (SessionEndRequestDto request, ISessionProvider sessions, ILogger<SessionEndRequestDto> logger,
            CancellationToken ct) =>
        {
            var targets = sessions.List()
                .Where(s => s.Kind == SessionKind.RemoteApp && (request.User is null || RdpUserMatcher.Matches(request.User, s)))
                .ToList();
            var ended = 0;
            foreach (var session in targets)
            {
                logger.LogWarning("Logging off RemoteApp session {Session} of {Domain}\\{User} at a client's request", session.Id, session.Domain, session.User);
                try
                {
                    await Task.Run(() => sessions.Logoff(session.Id), ct);
                    ended++;
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    logger.LogError(ex, "Logging off session {Session} failed", session.Id);
                }
            }

            return Results.Ok(new SessionEndResultDto(ended));
        });
    }

    private static IResult NotFound(string alias) =>
        Results.NotFound(new ApiErrorDto(ApiContract.Errors.NotFound, Strings.T("agent.errors.appNotFound", alias)));
}
