using Majik.Server.Composition;
using Majik.Server.Endpoints;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMajikEngine();
builder.Services.AddMajikAuth(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck")
   .AllowAnonymous();

app.MapGet("/whoami", (System.Security.Claims.ClaimsPrincipal user) =>
    Results.Ok(new
    {
        sub = user.FindFirst("sub")?.Value,
        name = user.Identity?.Name,
        authenticated = user.Identity?.IsAuthenticated ?? false,
    }))
   .WithName("WhoAmI")
   .RequireAuthorization(AuthRegistration.AsPlayerPolicy);

app.MapGameEndpoints();

app.Run();

public partial class Program { }
