using Rag.Application.DependencyInjection;
using Rag.Infrastructure.DependencyInjection;
using Rag.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddRagApplication();
builder.Services.AddRagInfrastructure(builder.Configuration);

var app = builder.Build();

// Initialize database on startup
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
