using Microsoft.Net.Http.Headers;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Changes;
using WRApperture.Host.Agent.Icons;
using WRApperture.Host.Agent.Readiness;
using WRApperture.Host.Agent.Remote;
using WRApperture.Host.Core;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Endpoints;

internal static class ApiEndpoints
{
    public static void MapAgentApi(this WebApplication app)
    {
        app.MapGet(ApiContract.Health, () => Results.Text("ok"));

        app.MapGet(ApiContract.Host, (IHostInfoProvider hostInfo, AgentIdentity identity, HostReadiness readiness, HostVpns vpns, ManagerApp manager) =>
        {
            var info = hostInfo.Get();
            var summary = readiness.Get();
            return Results.Ok(new HostInfoDto(info.HostName, info.RdpPort, AgentInfo.Version, identity.Fingerprint,
                AgentInfo.Features, summary.Issues, summary.SuggestedUser, summary.MicrosoftAccount, vpns.Get(), manager.Alias()));
        });

        // Long poll: one request waits per client; it ends early when the client goes away.
        app.MapGet(ApiContract.Changes, async (string? since, HostChanges changes, HttpContext context) =>
        {
            try
            {
                return Results.Ok(new ChangesDto(await changes.WaitAsync(since, context.RequestAborted)));
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return Results.Empty;
            }
        });

        // Only alias and name leave the host: executable paths stay private.
        app.MapGet(ApiContract.Apps, (IAppPublisher publisher, RunningApps running) =>
        {
            var apps = publisher.List().Where(a => a.ShowInList).ToList();
            var open = running.Find(apps);
            return Results.Ok(apps.Select(a => new AppSummaryDto(a.Alias, a.DisplayName, ApiContract.Icon(a.Alias), open.ContainsKey(a.Alias))));
        });

        app.MapGet(ApiContract.Apps + "/{alias}/icon", (string alias, int? size, IAppPublisher publisher, IconCache icons, HttpContext context) =>
        {
            var pixels = size ?? ApiContract.DefaultIconSize;
            if (pixels is < ApiContract.MinIconSize or > ApiContract.MaxIconSize)
            {
                return Results.BadRequest(new ApiErrorDto(ApiContract.Errors.BadRequest,
                    Strings.T("agent.errors.iconSize", ApiContract.MinIconSize, ApiContract.MaxIconSize)));
            }

            var app = publisher.Get(alias);
            if (app is null || !app.ShowInList)
            {
                return Results.NotFound(new ApiErrorDto(ApiContract.Errors.NotFound, Strings.T("agent.errors.appNotFound", alias)));
            }

            var icon = icons.Get(app, pixels);
            context.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Bytes(icon.Png, "image/png", entityTag: new EntityTagHeaderValue(icon.ETag));
        });
    }
}
