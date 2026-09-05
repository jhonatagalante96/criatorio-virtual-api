using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
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
else
{
    builder.Services.AddDataProtection();
    builder.Services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
}

builder.Services.AddHttpSecurity(builder.Configuration);

var app = builder.Build();

app.UseCorrelationId();
app.UseForwardedHeaders();
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(context =>
    HttpProblemResults.Write(context, StatusCodes.Status500InternalServerError)));
app.UseStatusCodePages(context => HttpProblemResults.Write(context.HttpContext, context.HttpContext.Response.StatusCode));
app.UseCors("trusted-client");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready");
app.MapGet("/antiforgery/token", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers[HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName] = tokens.RequestToken;
    return Results.NoContent();
});

app.Run();

public partial class Program;
