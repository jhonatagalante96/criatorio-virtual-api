using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Reports;

public sealed class BirdsReportEndpointTests
{
    [Fact]
    public async Task GetPdf_ReportsClassificationsFiltersAndKeepsTenantsIsolated()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "birds-report-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient, "Report owner farm", "report-owner@example.com");
        await SelectFarmAsync(ownerClient, ownerFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);

        var fatherId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Matrix Father",
            BirdSex.Male,
            "100001",
            new DateOnly(2020, 1, 1));
        var motherId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Matrix Mother",
            BirdSex.Female,
            "100002",
            new DateOnly(2020, 2, 2));
        var daughterId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Daughter Becomes Matrix",
            BirdSex.Female,
            null,
            null,
            fatherId,
            motherId);
        await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Direct Offspring",
            BirdSex.Unknown,
            null,
            null,
            fatherId,
            motherId);
        await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Grandchild",
            BirdSex.Unknown,
            "100003",
            new DateOnly(2025, 3, 3),
            motherBirdId: daughterId);

        var reproductionMaleId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Reproduction Male",
            BirdSex.Male,
            "100004",
            new DateOnly(2020, 4, 4));
        var reproductionFemaleId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Reproduction Female",
            BirdSex.Female,
            "100005",
            new DateOnly(2020, 5, 5));
        await CreateReproductionAsync(ownerClient, reproductionMaleId, reproductionFemaleId);

        await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Only Female Offspring",
            BirdSex.Female,
            "100006",
            new DateOnly(2024, 6, 6));
        await CreateBirdAsync(ownerClient, speciesId, "Only Unknown Offspring", BirdSex.Unknown, null, null);
        var archivedId = await CreateBirdAsync(
            ownerClient,
            speciesId,
            "Archived Offspring",
            BirdSex.Female,
            "100007",
            new DateOnly(2023, 7, 7));
        await ArchiveBirdAsync(factory, archivedId);

        using var unauthenticatedClient = CreateClient(factory);
        using var unauthenticated = await unauthenticatedClient.GetAsync("/api/reports/birds/pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var invalidParameters = await ownerClient.GetAsync(
            "/api/reports/birds/pdf?status=NotAStatus&sex=1&speciesId=00000000-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.BadRequest, invalidParameters.StatusCode);

        using var fullResponse = await ownerClient.GetAsync("/api/reports/birds/pdf");
        Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
        Assert.Equal("application/pdf", fullResponse.Content.Headers.ContentType?.MediaType);
        var fullPdf = await fullResponse.Content.ReadAsStringAsync();
        Assert.StartsWith("%PDF-1.4", fullPdf, StringComparison.Ordinal);
        AssertPdfContains(fullPdf, "Registered birds report", "Breeding farm: Report owner farm");
        AssertPdfContains(
            fullPdf,
            "Matrices - Male",
            "Matrices - Female",
            "Matrices - Unidentified",
            "Offspring - Male",
            "Offspring - Female",
            "Offspring - Unidentified",
            "Total birds: 10",
            "Group total: 2",
            "Group total: 3",
            "Group total: 0",
            "Group total: 2",
            "Not informed",
            "Matrix Father",
            "Matrix Mother",
            "Daughter Becomes Matrix",
            "Reproduction Male",
            "Reproduction Female",
            "Direct Offspring",
            "Only Female Offspring",
            "Only Unknown Offspring",
            "Archived Offspring");
        Assert.True(
            fullPdf.IndexOf(ToPdfHex("Matrices - Female"), StringComparison.Ordinal) <
            fullPdf.IndexOf(ToPdfHex("Daughter Becomes Matrix"), StringComparison.Ordinal));
        Assert.True(
            fullPdf.IndexOf(ToPdfHex("Offspring - Female"), StringComparison.Ordinal) <
            fullPdf.IndexOf(ToPdfHex("Only Female Offspring"), StringComparison.Ordinal));

        using var sexResponse = await ownerClient.GetAsync("/api/reports/birds/pdf?sex=Male");
        Assert.Equal(HttpStatusCode.OK, sexResponse.StatusCode);
        var malePdf = await sexResponse.Content.ReadAsStringAsync();
        AssertPdfContains(malePdf, "Total birds: 2", "Matrix Father", "Reproduction Male");
        AssertPdfDoesNotContain(malePdf, "Matrix Mother", "Only Female Offspring", "Direct Offspring");

        using var statusResponse = await ownerClient.GetAsync("/api/reports/birds/pdf?status=Archived");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var archivedPdf = await statusResponse.Content.ReadAsStringAsync();
        AssertPdfContains(archivedPdf, "Total birds: 1", "Archived Offspring", "Status: Archived");
        AssertPdfDoesNotContain(archivedPdf, "Matrix Father", "Only Female Offspring");

        using var speciesResponse = await ownerClient.GetAsync($"/api/reports/birds/pdf?speciesId={speciesId}");
        Assert.Equal(HttpStatusCode.OK, speciesResponse.StatusCode);
        var speciesPdf = await speciesResponse.Content.ReadAsStringAsync();
        AssertPdfContains(speciesPdf, "Total birds: 10");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            Assert.Equal(0, await dbContext.BirdDocuments.CountAsync());
        }

        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, otherClient, "birds-report-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient, "Report other farm", "report-other@example.com");
        await SelectFarmAsync(otherClient, otherFarmId);
        await CreateBirdAsync(otherClient, speciesId, "Other Tenant Bird", BirdSex.Male, "200001", new DateOnly(2022, 8, 8));

        using var otherResponse = await otherClient.GetAsync("/api/reports/birds/pdf");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        var otherPdf = await otherResponse.Content.ReadAsStringAsync();
        AssertPdfContains(otherPdf, "Total birds: 1", "Other Tenant Bird");
        AssertPdfDoesNotContain(otherPdf, "Matrix Father", "Report owner farm", "Archived Offspring");
    }

    private static void AssertPdfContains(string pdf, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.Contains(ToPdfHex(value), pdf, StringComparison.Ordinal);
        }
    }

    private static void AssertPdfDoesNotContain(string pdf, params string[] values)
    {
        foreach (var value in values)
        {
            Assert.DoesNotContain(ToPdfHex(value), pdf, StringComparison.Ordinal);
        }
    }

    private static string ToPdfHex(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var ascii = normalized
            .Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Where(character => character <= 127)
            .ToArray();
        return Convert.ToHexString(Encoding.ASCII.GetBytes(ascii));
    }

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        BirdSex sex,
        string? ringNumber,
        DateOnly? birthDate,
        Guid? fatherBirdId = null,
        Guid? motherBirdId = null)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                sex = sex.ToString(),
                speciesId,
                birthDate = birthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ringNumber,
                fatherBirdId,
                externalFatherName = (string?)null,
                externalFatherSex = (string?)null,
                motherBirdId,
                externalMotherName = (string?)null,
                externalMotherSex = (string?)null,
                notes = (string?)null
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("birdId").GetGuid();
    }

    private static async Task CreateReproductionAsync(HttpClient client, Guid maleBirdId, Guid femaleBirdId)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = "2026-09-01"
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task ArchiveBirdAsync(WebApplicationFactory<Program> factory, Guid birdId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        var now = DateTimeOffset.UtcNow;
        bird.ChangeStatus(
            BirdStatus.Archived,
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime),
            now);
        await dbContext.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services => services.AddInfrastructurePersistence(connectionString, certificate));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        using var registration = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/register",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                email,
                password = "StrongPassword!123",
                confirmPassword = "StrongPassword!123"
            }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            await dbContext.SaveChangesAsync();
        }

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client, string name, string contactEmail)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName = "Report Owner",
                contactEmail
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("breedingFarmId").GetGuid();
    }

    private static async Task SelectFarmAsync(HttpClient client, Guid farmId)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            "/api/breeding-farms/selection",
            await GetAntiforgeryTokenAsync(client),
            new { breedingFarmId = farmId }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> GetSpeciesIdAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.Species
            .Where(species => species.ScientificName == "Turdus rufiventris")
            .Select(species => species.Id)
            .SingleAsync();
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName).Single();
    }

    private static HttpRequestMessage CreateBrowserRequest(
        HttpMethod method,
        string path,
        string antiforgeryToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        return request;
    }
}
