using Majik.Server.Composition;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMajikEngine();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck");

app.Run();

/// <summary>Marker class so test hosts and OpenAPI emitters can refer to
/// the server entry assembly by type.</summary>
public partial class Program { }
