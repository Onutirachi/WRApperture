using System.Reflection;
using Microsoft.Net.Http.Headers;
using WRApperture.Host.Agent.Certificates;
using WRApperture.Host.Agent.Icons;
using WRApperture.Host.Core;
using WRApperture.Host.Core.HostInfo;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Endpoints;

internal static class ApiEndpoints
{
    private static readonly string AgentVersion =
        typeof(ApiEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static void MapAgentApi(this WebApplication app)
    {
        app.MapGet(ApiContract.Health, () => Results.Text("ok"));

        app.MapGet(ApiContract.Host, (IHostInfoProvider hostInfo, AgentIdentity identity) =>
        {
            var info = hostInfo.Get();
            return Results.Ok(new HostInfoDto(info.HostName, info.RdpPort, AgentVersion, identity.Fingerprint,
                [AgentFeatures.DesktopSession, AgentFeatures.SelfUpdate]));
        });

        // Only alias and name leave the host: executable paths stay private.
        app.MapGet(ApiContract.Apps, (IAppPublisher publisher) =>
            Results.Ok(publisher.List()
                .Where(a => a.ShowInList)
                .Select(a => new AppSummaryDto(a.Alias, a.DisplayName, ApiContract.Icon(a.Alias)))));

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
