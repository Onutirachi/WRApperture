using WRApperture.Host.Core.Agent;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Security;

/// <summary>Requires a valid pairing token on every /api route. The token itself is never logged.</summary>
internal sealed class TokenAuthMiddleware(
    RequestDelegate next,
    IAgentSettingsStore settings,
    FailedAuthLimiter limiter,
    ILogger<TokenAuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (limiter.IsBlocked(source))
        {
            logger.LogWarning("Blocked request from {Source}: too many failed attempts", source);
            await Reject(context, StatusCodes.Status429TooManyRequests, ApiContract.Errors.TooManyAttempts,
                Strings.T("agent.errors.tooManyAttempts"));
            return;
        }

        var token = context.Request.Headers[ApiContract.TokenHeader].ToString();
        if (!settings.VerifyToken(token))
        {
            limiter.RecordFailure(source);
            if (!settings.Load().IsPaired)
            {
                logger.LogWarning("Rejected request from {Source}: agent is not paired; run 'wrahost agent token'", source);
            }
            else
            {
                logger.LogWarning("Rejected request from {Source}: invalid or missing token", source);
            }

            await Reject(context, StatusCodes.Status401Unauthorized, ApiContract.Errors.Unauthorized,
                Strings.T("agent.errors.unauthorized"));
            return;
        }

        limiter.RecordSuccess(source);
        await next(context);
    }

    private static Task Reject(HttpContext context, int status, string error, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiErrorDto(error, message));
    }
}
