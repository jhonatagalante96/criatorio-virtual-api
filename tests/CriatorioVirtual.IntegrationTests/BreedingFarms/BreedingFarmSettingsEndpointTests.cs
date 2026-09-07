using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmSettingsEndpointTests
{
    [Fact]
    public async Task OwnerCanReadAndUpdateSettingsWithNormalizedAddressAndTimestamp()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");

        var farmId = await CreateFarmAsync(client, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-SETTINGS-001",
            address = new
            {
                street = "Rua A",
                number = "10",
                city = "São Paulo",
                state = "sp",
                postalCode = "12345-678"
            }
        });

        using var initial = await client.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using var initialDocument = JsonDocument.Parse(await initial.Content.ReadAsStreamAsync());
        var initialUpdatedAt = initialDocument.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset();
        Assert.Equal("SP", initialDocument.RootElement.GetProperty("address").GetProperty("state").GetString());
        Assert.Equal("12345678", initialDocument.RootElement.GetProperty("address").GetProperty("postalCode").GetString());

        using var update = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/breeding-farms/{farmId}/settings",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Sítio Aurora Atualizado",
                responsibleName = "Nova Responsável",
                contactEmail = "contact@example.com",
                contactPhone = "+55 11 98888-0000",
                officialRegistrationNumber = "REG-SETTINGS-001",
                address = new
                {
                    street = "Avenida B",
                    number = "20",
                    complement = "Casa 2",
                    neighborhood = "Centro",
                    city = "Rio de Janeiro",
                    state = "rj",
                    postalCode = "98765 432"
                }
            }));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var updateDocument = JsonDocument.Parse(await update.Content.ReadAsStreamAsync());
        Assert.Equal("Sítio Aurora Atualizado", updateDocument.RootElement.GetProperty("name").GetString());
        Assert.Equal("Nova Responsável", updateDocument.RootElement.GetProperty("responsibleName").GetString());
        Assert.Equal("RJ", updateDocument.RootElement.GetProperty("address").GetProperty("state").GetString());
        Assert.Equal("98765432", updateDocument.RootElement.GetProperty("address").GetProperty("postalCode").GetString());
        Assert.True(updateDocument.RootElement.GetProperty("updatedAtUtc").GetDateTimeOffset() > initialUpdatedAt);

        using var persisted = await client.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.OK, persisted.StatusCode);
        var persistedBody = await persisted.Content.ReadAsStringAsync();
        Assert.Contains("Sítio Aurora Atualizado", persistedBody, StringComparison.Ordinal);
        Assert.Contains("98765432", persistedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsRejectInvalidContactAndAddressFormatsWithoutChangingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");
        var farmId = await CreateFarmAsync(client, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-SETTINGS-002"
        });

        using var invalid = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/breeding-farms/{farmId}/settings",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Sítio Alterado",
                responsibleName = "Owner Principal",
                contactEmail = "not-an-email",
                address = new { state = "São Paulo", postalCode = "1234" }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("ContactEmail", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Address.State", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Address.PostalCode", invalidBody, StringComparison.OrdinalIgnoreCase);

        using var unchanged = await client.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        var unchangedBody = await unchanged.Content.ReadAsStringAsync();
        Assert.Contains("Sítio Aurora", unchangedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Sítio Alterado", unchangedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonOwnerCannotReadOrUpdateAnotherFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "owner@example.com");
        await RegisterAndAuthenticateAsync(factory, otherClient, "other@example.com");
        var farmId = await CreateFarmAsync(ownerClient, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-SETTINGS-003"
        });

        using var read = await otherClient.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        using var update = await otherClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/breeding-farms/{farmId}/settings",
            await GetAntiforgeryTokenAsync(otherClient),
            new
            {
                name = "Tentativa de invasão",
                responsibleName = "Other",
                contactEmail = "other@example.com"
            }));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        using var unchanged = await ownerClient.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Contains("Sítio Aurora", await unchanged.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonOwnerRolesCanBePersistedButCannotReadOrUpdateFarmSettings()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var managerClient = CreateClient(factory);
        using var employeeClient = CreateClient(factory);
        using var viewerClient = CreateClient(factory);
        var ownerUserId = await RegisterAndAuthenticateAsync(factory, ownerClient, "owner@example.com");
        var managerUserId = await RegisterAndAuthenticateAsync(factory, managerClient, "manager@example.com");
        var employeeUserId = await RegisterAndAuthenticateAsync(factory, employeeClient, "employee@example.com");
        var viewerUserId = await RegisterAndAuthenticateAsync(factory, viewerClient, "viewer@example.com");
        var farmId = await CreateFarmAsync(ownerClient, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-AUTHORIZATION-001"
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            dbContext.BreedingFarmUsers.AddRange(
                new BreedingFarmUser(farmId, managerUserId, BreedingFarmRole.Manager, DateTimeOffset.UtcNow),
                new BreedingFarmUser(farmId, employeeUserId, BreedingFarmRole.Employee, DateTimeOffset.UtcNow),
                new BreedingFarmUser(farmId, viewerUserId, BreedingFarmRole.Viewer, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var persistedRoles = await dbContext.BreedingFarmUsers
                .Where(candidate => candidate.BreedingFarmId == farmId)
                .ToDictionaryAsync(candidate => candidate.UserId, candidate => candidate.Role);

            Assert.Equal(BreedingFarmRole.Owner, persistedRoles[ownerUserId]);
            Assert.Equal(BreedingFarmRole.Manager, persistedRoles[managerUserId]);
            Assert.Equal(BreedingFarmRole.Employee, persistedRoles[employeeUserId]);
            Assert.Equal(BreedingFarmRole.Viewer, persistedRoles[viewerUserId]);
        }

        foreach (var client in new[] { managerClient, employeeClient, viewerClient })
        {
            using var read = await client.GetAsync($"/api/breeding-farms/{farmId}/settings");
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

            using var update = await client.SendAsync(CreateBrowserRequest(
                HttpMethod.Put,
                $"/api/breeding-farms/{farmId}/settings",
                await GetAntiforgeryTokenAsync(client),
                new
                {
                    name = "Tentativa sem permissão",
                    responsibleName = "Unauthorized",
                    contactEmail = "unauthorized@example.com"
                }));
            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        }

        using var ownerRead = await ownerClient.GetAsync($"/api/breeding-farms/{farmId}/settings");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        Assert.Contains("Sítio Aurora", await ownerRead.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatingToAnExistingOfficialRegistrationReturnsConflict()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");
        await CreateFarmAsync(client, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-DUPLICATE"
        });
        var secondFarmId = await CreateFarmAsync(client, new
        {
            name = "Sítio Boreal",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-DUPLICATE-2"
        });

        using var conflict = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/breeding-farms/{secondFarmId}/settings",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Sítio Boreal",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com",
                officialRegistrationNumber = "REG-DUPLICATE"
            }));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task StaleConcurrentUpdatesReturnOneConcurrencyConflict()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");
        var farmId = await CreateFarmAsync(client, new
        {
            name = "Sítio Aurora",
            responsibleName = "Owner Principal",
            contactEmail = "owner@example.com",
            officialRegistrationNumber = "REG-CONCURRENT-SETTINGS"
        });

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(
                database.GetConnectionString(),
                npgsqlOptions => npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    CriatorioVirtualDbContext.DefaultSchema))
            .Options;
        await using var firstContext = new CriatorioVirtualDbContext(options);
        await using var secondContext = new CriatorioVirtualDbContext(options);
        var firstFarm = await firstContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId);
        var secondFarm = await secondContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId);

        firstFarm.UpdateSettings(
            "Sítio Atualizado A",
            "Owner Principal",
            "owner@example.com",
            null,
            "REG-CONCURRENT-SETTINGS",
            new BreedingFarmAddress(null, null, null, null, null, null, null),
            DateTimeOffset.UtcNow);
        secondFarm.UpdateSettings(
            "Sítio Atualizado B",
            "Owner Principal",
            "owner@example.com",
            null,
            "REG-CONCURRENT-SETTINGS",
            new BreedingFarmAddress(null, null, null, null, null, null, null),
            DateTimeOffset.UtcNow);

        var saveResults = await Task.WhenAll(
            TrySaveAsync(firstContext),
            TrySaveAsync(secondContext));

        Assert.Equal(1, saveResults.Count(wasSaved => wasSaved));
        Assert.Equal(1, saveResults.Count(wasSaved => !wasSaved));
    }

    private static async Task<bool> TrySaveAsync(CriatorioVirtualDbContext context)
    {
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
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

    private static async Task<Guid> CreateFarmAsync(HttpClient client, object body)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("breedingFarmId").GetGuid();
    }

    private static async Task<Guid> RegisterAndAuthenticateAsync(
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

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        await AuthenticateExistingUserAsync(client, email, "StrongPassword!123");
        return userId;
    }

    private static async Task AuthenticateExistingUserAsync(HttpClient client, string email, string password)
    {
        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
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
