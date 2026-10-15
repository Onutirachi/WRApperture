using System.Net;
using WRApperture.Host.Agent.Pairing;
using WRApperture.Shared;
using WRApperture.Shared.Localization;

namespace WRApperture.Host.Agent.Endpoints;

/// <summary>Pairing without a token (outside /api): requests from the network, approval from this PC only.</summary>
internal static class PairingEndpoints
{
    public static void MapPairingApi(this WebApplication app)
    {
        app.MapPost(ApiContract.PairingRequests, (PairingRequestDto request, HttpContext context, PairingService pairing) =>
        {
            try
            {
                var ticket = pairing.Create(request, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                return Results.Accepted(ApiContract.PairingRequest(ticket.Id), ticket);
            }
            catch (PairingException ex)
            {
                return Results.Json(new ApiErrorDto(ex.Error, ex.Message), statusCode: ex.Status);
            }
        });

        app.MapGet(ApiContract.PairingRequests + "/{id}", (string id, PairingService pairing) =>
            pairing.Status(id) is { } status ? Results.Ok(status) : NotFound());

        app.MapGet("/pair/approval/{id}", (string id, HttpContext context, PairingService pairing) =>
            IsLoopback(context) && pairing.Details(id, context.Request.Headers[ApiContract.ApprovalHeader]) is { } details
                ? Results.Ok(details)
                : NotFound());

        app.MapPost("/pair/approval/{id}", (string id, PairingDecisionDto decision, HttpContext context, PairingService pairing) =>
            IsLoopback(context) && pairing.Decide(id, context.Request.Headers[ApiContract.ApprovalHeader], decision.Approve)
                ? Results.NoContent()
                : NotFound());
    }

    private static bool IsLoopback(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);

    private static IResult NotFound() =>
        Results.NotFound(new ApiErrorDto(ApiContract.Errors.NotFound, Strings.T("agent.pairing.notFound")));
}
