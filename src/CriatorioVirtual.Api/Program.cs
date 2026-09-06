using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Api;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
_ = BackendLayers.Types;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Criatório Virtual API",
        Version = "v1"
    });
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddHealthChecks();

var connectionString = builder.Configuration.GetConnectionString("CriatorioVirtual");
PostgreSqlConnectionStringValidator.Validate(connectionString);
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var dataProtectionCertificate = DataProtectionCertificateLoader.Load(builder.Configuration);
    builder.Services.AddInfrastructurePersistence(connectionString, dataProtectionCertificate);
}
else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection();
    builder.Services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
}
else
{
    throw new InvalidOperationException(
        "ConnectionStrings:CriatorioVirtual is required outside Development and Testing environments so Data Protection keys remain durable.");
}

builder.Services.AddAuthenticationEmailDelivery(builder.Configuration, builder.Environment);
builder.Services.AddHttpSecurity(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Criatório Virtual API v1");
    });
}

app.UseCorrelationId();
app.UseForwardedHeaders();
app.UseExceptionHandler(exceptionApp => exceptionApp.Run(context =>
    HttpProblemResults.Write(context, StatusCodes.Status500InternalServerError)));
app.UseStatusCodePages(context => HttpProblemResults.Write(context.HttpContext, context.HttpContext.Response.StatusCode));
app.UseCors("trusted-client");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRequiredAntiforgeryProtection();

app.MapControllers();
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
