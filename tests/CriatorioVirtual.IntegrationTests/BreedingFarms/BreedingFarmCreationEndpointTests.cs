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

public sealed class BreedingFarmCreationEndpointTests
{
    [Fact]
    public async Task Create_UsesAuthenticatedUserAndPersistsFarmOwnerAtomicallyWithoutAddress()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        var userId = await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");

        using var response = await client.SendAsync(CreateFarmRequest(
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactPhone = "+55 11 99999-0000",
                officialRegistrationNumber = "REG-001",
                address = (object?)null
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var breedingFarmId = responseDocument.RootElement.GetProperty("breedingFarmId").GetGuid();
        Assert.Equal(userId, responseDocument.RootElement.GetProperty("ownerUserId").GetGuid());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var farm = await dbContext.BreedingFarms.SingleAsync(candidate => candidate.Id == breedingFarmId);
        var owner = await dbContext.BreedingFarmUsers.SingleAsync(candidate => candidate.BreedingFarmId == breedingFarmId);

        Assert.Equal("Sítio Aurora", farm.Name);
        Assert.Equal("Owner Principal", farm.ResponsibleName);
        Assert.Equal("owner@example.com", farm.ContactEmail);
        Assert.Equal("+55 11 99999-0000", farm.ContactPhone);
        Assert.Equal("REG-001", farm.OfficialRegistrationNumber);
        Assert.True(farm.Address.IsEmpty);
        Assert.Equal(userId, owner.UserId);
        Assert.Equal(BreedingFarmRole.Owner, owner.Role);
        Assert.True(owner.IsActive);
    }

    [Fact]
    public async Task Create_RequiresAuthenticationAndValidRequiredFields()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.SendAsync(CreateFarmRequest(
            await GetAntiforgeryTokenAsync(client),
            new { name = "Sítio Aurora", responsibleName = "Owner", contactEmail = "owner@example.com" }));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "owner@example.com");
        using var invalid = await client.SendAsync(CreateFarmRequest(
            await GetAntiforgeryTokenAsync(client),
            new { name = " ", responsibleName = "Owner", contactEmail = "not-an-email" }));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Name", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ContactEmail", invalidBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_ConcurrentSameOfficialRegistrationReturnsOneConflictAndDoesNotDuplicateFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate);
        await MigrateAsync(factory);

        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "owner@example.com");
        await AuthenticateExistingUserAsync(secondClient, "owner@example.com", "StrongPassword!123");

        var firstRequest = CreateFarmRequest(
            await GetAntiforgeryTokenAsync(firstClient),
            new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com",
                officialRegistrationNumber = "REG-CONCURRENT"
            });
        var secondRequest = CreateFarmRequest(
            await GetAntiforgeryTokenAsync(secondClient),
            new
            {
                name = "Sítio Boreal",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com",
                officialRegistrationNumber = "REG-CONCURRENT"
            });

        var responses = await Task.WhenAll(firstClient.SendAsync(firstRequest), secondClient.SendAsync(secondRequest));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            firstRequest.Dispose();
            secondRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(1, await dbContext.BreedingFarms.CountAsync());
        Assert.Equal(1, await dbContext.BreedingFarmUsers.CountAsync());
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

    private static async Task<Guid> RegisterAndAuthenticateAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email)
    {
        var registrationToken = await GetAntiforgeryTokenAsync(client);
        using var registration = await client.SendAsync(CreateRegistrationRequest(email, registrationToken));
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
        using var login = await client.SendAsync(CreateLoginRequest(
            email,
            password,
            await GetAntiforgeryTokenAsync(client)));
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

    private static HttpRequestMessage CreateRegistrationRequest(string email, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = "StrongPassword!123",
                confirmPassword = "StrongPassword!123"
            })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateLoginRequest(string email, string password, string antiforgeryToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static HttpRequestMessage CreateFarmRequest(string antiforgeryToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/breeding-farms")
        {
            Content = JsonContent.Create(body)
        };
        AddBrowserHeaders(request, antiforgeryToken);
        return request;
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string antiforgeryToken)
    {
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
    }
}
