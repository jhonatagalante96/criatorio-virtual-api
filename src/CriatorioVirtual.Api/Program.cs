using CriatorioVirtual.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
_ = BackendLayers.Types;

var connectionString = builder.Configuration.GetConnectionString("CriatorioVirtual");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddInfrastructurePersistence(connectionString);
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
