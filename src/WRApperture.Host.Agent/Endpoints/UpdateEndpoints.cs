using WRApperture.Host.Agent.Updates;
using WRApperture.Shared;
using WRApperture.Shared.Localization;
using WRApperture.Shared.Updates;

namespace WRApperture.Host.Agent.Endpoints;

/// <summary>
/// GET/POST /api/update (token protected). The request carries no data: what gets installed comes only
/// from the latest GitHub release, verified against its SHA256SUMS.txt.
/// </summary>
internal static class UpdateEndpoints
{
    public static void MapUpdateApi(this WebApplication app)
    {
        app.MapGet(ApiContract.Update, async (HostUpdater updater, CancellationToken ct) => Results.Ok(await updater.GetStatusAsync(ct)));

        app.MapPost(ApiContract.Update, async (HostUpdater updater, ILogger<HostUpdater> logger, CancellationToken ct) =>
        {
            try
            {
                var (outcome, latest) = await updater.UpdateNowAsync(ct);
                var current = ProductVersion.Format(updater.Current);
                var latestText = latest is null ? null : ProductVersion.Format(latest);
                return outcome == UpdateRequestOutcome.Installing
                    ? Results.Json(new UpdateRequestResultDto(UpdateOutcomes.Installing, current, latestText), statusCode: StatusCodes.Status202Accepted)
                    : Results.Ok(new UpdateRequestResultDto(UpdateOutcomes.UpToDate, current, latestText));
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Update requested by a client, but the check failed: {Error}", ex.Message);
                return Results.Json(new ApiErrorDto(ApiContract.Errors.UpdateCheckFailed, Strings.T("agent.errors.updateCheckFailed")),
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}
