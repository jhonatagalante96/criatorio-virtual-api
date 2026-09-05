using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Api;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
_ = BackendLayers.Types;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var connectionString = builder.Configuration.GetConnectionString("CriatorioVirtual");
PostgreSqlConnectionStringValidator.Validate(connectionString);
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddInfrastructurePersistence(connectionString);
}

var app = builder.Build();

app.UseCorrelationId();
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(context =>
    HttpProblemResults.Write(context, StatusCodes.Status500InternalServerError)));
app.UseStatusCodePages(context => HttpProblemResults.Write(context.HttpContext, context.HttpContext.Response.StatusCode));

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
