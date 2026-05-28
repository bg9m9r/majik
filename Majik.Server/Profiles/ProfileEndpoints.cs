using System.Security.Claims;
using Majik.Server.Composition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;

namespace Majik.Server.Profiles;

/// <summary>Maps <c>/me</c> endpoints for reading and writing the
/// caller's <see cref="UserProfile"/>. Auth: <see cref="AuthRegistration.AsPlayerPolicy"/>.
/// When Mongo isn't configured, every call short-circuits with
/// <c>503 { error: "mongo-not-configured" }</c>.</summary>
public static class ProfileEndpoints
{
    public sealed record PutHandleRequest(string Handle);

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/me")
            .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
            .WithTags("Profile");

        // GET /me — read-only lookup; not rate-limited (lightweight, no abuse vector).
        group.MapGet("/", GetMe)
             .WithName("GetMyProfile")
             .Produces<ProfileDto>(StatusCodes.Status200OK)
             .Produces<ProfileError>(StatusCodes.Status404NotFound)
             .Produces(StatusCodes.Status401Unauthorized)
             .Produces<ProfileError>(StatusCodes.Status503ServiceUnavailable);

        // PUT /me — handle mutation; "expensive" policy (60 req/min).
        group.MapPut("/", PutMe)
             .WithName("UpdateMyHandle")
             .RequireRateLimiting(RateLimitPolicies.Expensive)
             .Produces<ProfileDto>(StatusCodes.Status200OK)
             .Produces<ProfileError>(StatusCodes.Status400BadRequest)
             .Produces<ProfileError>(StatusCodes.Status409Conflict)
             .Produces(StatusCodes.Status401Unauthorized)
             .Produces<ProfileError>(StatusCodes.Status503ServiceUnavailable);

        return routes;
    }

    private static async Task<IResult> GetMe(
        ClaimsPrincipal user,
        [FromServices] UserProfileRepository? repo,
        CancellationToken ct)
    {
        if (repo == null) return MongoUnavailable();
        var sub = user.FindFirst("sub")?.Value;
        if (sub == null) return Results.Unauthorized();

        var profile = await repo.GetBySubAsync(sub, ct);
        if (profile == null)
        {
            return Results.NotFound(new ProfileError("no-profile"));
        }
        return Results.Ok(ToDto(profile));
    }

    private static async Task<IResult> PutMe(
        PutHandleRequest? body,
        ClaimsPrincipal user,
        [FromServices] UserProfileRepository? repo,
        CancellationToken ct)
    {
        if (repo == null) return MongoUnavailable();
        var sub = user.FindFirst("sub")?.Value;
        if (sub == null) return Results.Unauthorized();

        // Defensive: a totally empty body or one with no/blank handle used
        // to dereference body.Handle and NRE up the pipeline. Return 400
        // with a typed error code instead.
        if (body is null || string.IsNullOrWhiteSpace(body.Handle))
            return Results.BadRequest(new ProfileError(
                "invalid-handle", "3-20 chars, letters digits _ -"));

        var validation = HandleValidator.Validate(body.Handle);
        switch (validation.Outcome)
        {
            case HandleValidationOutcome.InvalidFormat:
                return Results.BadRequest(new ProfileError(
                    "invalid-handle",
                    "3-20 chars, letters digits _ -"));
            case HandleValidationOutcome.Reserved:
                return Results.Conflict(new ProfileError("handle-taken"));
        }

        var taken = await repo.IsHandleTakenAsync(validation.Normalized, excludeSub: sub, ct);
        if (taken)
        {
            return Results.Conflict(new ProfileError("handle-taken"));
        }

        var now = DateTime.UtcNow;
        var profile = new UserProfile
        {
            Sub = sub,
            Handle = validation.Normalized,
            HandleDisplay = validation.Display,
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            var saved = await repo.UpsertAsync(profile, ct);
            return Results.Ok(ToDto(saved));
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Results.Conflict(new ProfileError("handle-taken"));
        }
    }

    private static IResult MongoUnavailable() =>
        Results.Json(
            new ProfileError("mongo-not-configured"),
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static ProfileDto ToDto(UserProfile p) =>
        new(p.Sub, p.HandleDisplay, p.CreatedAt, p.UpdatedAt);
}
