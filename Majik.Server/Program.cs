using Majik.Server.Composition;
using Majik.Server.Matches;
using Majik.Server.Profiles;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMajikEngine();
builder.Services.AddMajikAuth(builder.Configuration);
builder.Services.AddMajikCors(builder.Configuration);
builder.Services.AddMajikMongo(builder.Configuration);
builder.Services.AddMajikMatches(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (CorsRegistration.HasAnyOrigins(builder.Configuration))
{
    app.UseCors(CorsRegistration.PolicyName);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck")
   .WithTags("Meta")
   .AllowAnonymous()
   .Produces(StatusCodes.Status200OK);

app.MapGet("/whoami", (System.Security.Claims.ClaimsPrincipal user) =>
    Results.Ok(new
    {
        sub = user.FindFirst("sub")?.Value,
        name = user.Identity?.Name,
        authenticated = user.Identity?.IsAuthenticated ?? false,
    }))
   .WithName("WhoAmI")
   .WithTags("Meta")
   .RequireAuthorization(AuthRegistration.AsPlayerPolicy)
   .Produces(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status401Unauthorized);

app.MapProfileEndpoints();
app.MapMatchEndpoints();
app.MapHub<MatchHub>("/hubs/match");

app.Run();

public partial class Program { }
