using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.BreedingFarms.IdentityTemplates;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.BreedingFarms;

public sealed class BreedingFarmVisualIdentityEndpointTests
{
    private const string Route = "/api/breeding-farms/visual-identity";
    private const string TemplateId = "classico";
    private const string TemplateVersion = "1.3.0";

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task OwnerCanReadUploadReplaceAndRemovePrivateVisualIdentity()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "visual-identity-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var initial = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using (var initialBody = JsonDocument.Parse(await initial.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(farmId, initialBody.RootElement.GetProperty("breedingFarmId").GetGuid());
            Assert.Equal(JsonValueKind.Null, initialBody.RootElement.GetProperty("identity").ValueKind);
        }

        using var firstUpload = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "logo.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.OK, firstUpload.StatusCode);
        using var firstBody = JsonDocument.Parse(await firstUpload.Content.ReadAsStreamAsync());
        var firstIdentity = firstBody.RootElement.GetProperty("identity");
        Assert.Equal("Upload", firstIdentity.GetProperty("source").GetString());
        Assert.Equal("logo.png", firstIdentity.GetProperty("fileName").GetString());
        Assert.Equal("image/png", firstIdentity.GetProperty("contentType").GetString());
        var contentUrl = firstIdentity.GetProperty("contentUrl").GetString();
        Assert.Equal($"{Route}/content", contentUrl);

        var firstObjectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        var firstPath = GetPhysicalPath(storage.RootPath, farmId, firstObjectKey);
        Assert.True(File.Exists(firstPath));
        using var firstContent = await client.GetAsync(contentUrl);
        Assert.Equal(HttpStatusCode.OK, firstContent.StatusCode);
        Assert.Equal("no-store", firstContent.Headers.CacheControl?.NoStore == true ? "no-store" : null);
        Assert.Equal(PngBytes, await firstContent.Content.ReadAsByteArrayAsync());

        var jpeg = await new SpeciesDefaultImageReader().ReadAsync("0047.jpg", "image/jpeg");
        Assert.NotNull(jpeg);
        using var replacement = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "replacement.jpg",
            "image/jpeg",
            jpeg.Content);
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var secondObjectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        Assert.NotEqual(firstObjectKey, secondObjectKey);
        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, secondObjectKey)));

        using var removeRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            Route,
            await GetAntiforgeryTokenAsync(client));
        using var removed = await client.SendAsync(removeRequest);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var removedBody = JsonDocument.Parse(await removed.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, removedBody.RootElement.GetProperty("identity").ValueKind);

        using var afterRemoval = await client.GetAsync(Route);
        using var afterRemovalBody = JsonDocument.Parse(await afterRemoval.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, afterRemovalBody.RootElement.GetProperty("identity").ValueKind);
        using var missingContent = await client.GetAsync($"{Route}/content");
        Assert.Equal(HttpStatusCode.NotFound, missingContent.StatusCode);
        Assert.False(File.Exists(GetPhysicalPath(storage.RootPath, farmId, secondObjectKey)));
    }

    [Fact]
    public async Task UploadRejectsInvalidFormatDimensionsAndSizeBeforePersistence()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "visual-identity-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var extensionMismatch = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "logo.jpg",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.BadRequest, extensionMismatch.StatusCode);

        using var invalidSignature = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "not-an-image.png",
            "image/png",
            [1, 2, 3, 4]);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSignature.StatusCode);

        var oversizedDimensions = PngBytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversizedDimensions.AsSpan(16, 4), 8193);
        using var invalidDimensions = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "huge.png",
            "image/png",
            oversizedDimensions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDimensions.StatusCode);

        using var oversizedFile = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "large.png",
            "image/png",
            new byte[BreedingFarmVisualIdentityUploadLimits.MaxFileLength + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversizedFile.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var farm = await dbContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId);
        Assert.Null(farm.VisualIdentityReference);
        Assert.False(Directory.Exists(Path.Combine(storage.RootPath, farmId.ToString("N"))));

        var orphanReference = "orphan-reference";
        var constraintException = await Assert.ThrowsAnyAsync<DbException>(() =>
            dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE app.breeding_farms SET \"VisualIdentityReference\" = {orphanReference} WHERE \"Id\" = {farmId}"));
        Assert.Contains("ck_breeding_farms_visual_identity_reference_source_pair", constraintException.Message);
    }

    [Fact]
    public async Task TemplateCatalogPreviewAndApplyAreVersionedDeterministicAndPersisted()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        var catalog = new TestVisualIdentityTemplateCatalog(new BreedingFarmVisualIdentityTemplateCatalog().GetAll());
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath, catalog);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "identity-template-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);

        using var catalogResponse = await client.GetAsync($"{Route}/templates");
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        using var catalogBody = JsonDocument.Parse(await catalogResponse.Content.ReadAsStreamAsync());
        var templates = catalogBody.RootElement.EnumerateArray().ToArray();
        Assert.Equal(4, templates.Length);
        var template = templates.Single(item => item.GetProperty("id").GetString() == TemplateId);
        Assert.Equal(TemplateVersion, template.GetProperty("version").GetString());
        Assert.Equal("1:1", template.GetProperty("aspectRatio").GetString());
        Assert.Equal($"/api/breeding-farms/visual-identity/templates/classico/{TemplateVersion}/preview", template.GetProperty("previewUrl").GetString());
        Assert.Empty(template.GetProperty("options").EnumerateArray());

        using var publicPreview = await client.GetAsync(template.GetProperty("previewUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, publicPreview.StatusCode);
        Assert.Equal("image/png", publicPreview.Content.Headers.ContentType?.MediaType);
        var publicPreviewBytes = await publicPreview.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, publicPreviewBytes.Take(8));
        Assert.Equal(1024U, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(publicPreviewBytes.AsSpan(16, 4)));
        Assert.Equal(1024U, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(publicPreviewBytes.AsSpan(20, 4)));

        var payload = new
        {
            templateId = TemplateId,
            version = TemplateVersion,
            config = new System.Collections.Generic.Dictionary<string, string>()
        };
        using var previewRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"{Route}/templates/preview",
            await GetAntiforgeryTokenAsync(client),
            payload);
        using var preview = await client.SendAsync(previewRequest);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        var previewBytes = await preview.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, previewBytes.Take(8));

        using var repeatedPreviewRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"{Route}/templates/preview",
            await GetAntiforgeryTokenAsync(client),
            payload);
        using var repeatedPreview = await client.SendAsync(repeatedPreviewRequest);
        Assert.Equal(HttpStatusCode.OK, repeatedPreview.StatusCode);
        Assert.Equal(previewBytes, await repeatedPreview.Content.ReadAsByteArrayAsync());

        using var beforeApply = await client.GetAsync(Route);
        using var beforeApplyBody = JsonDocument.Parse(await beforeApply.Content.ReadAsStreamAsync());
        Assert.Equal(JsonValueKind.Null, beforeApplyBody.RootElement.GetProperty("identity").ValueKind);

        using var applyRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/template",
            await GetAntiforgeryTokenAsync(client),
            payload);
        using var applied = await client.SendAsync(applyRequest);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var appliedBody = JsonDocument.Parse(await applied.Content.ReadAsStreamAsync());
        var identity = appliedBody.RootElement.GetProperty("identity");
        Assert.Equal("Template", identity.GetProperty("source").GetString());
        Assert.Equal("identity-template.png", identity.GetProperty("fileName").GetString());
        Assert.Equal("image/png", identity.GetProperty("contentType").GetString());
        Assert.Equal(TemplateId, identity.GetProperty("modelId").GetString());
        Assert.Equal(TemplateVersion, identity.GetProperty("version").GetString());
        Assert.Equal("Sítio Aurora", identity.GetProperty("configuration").GetProperty("name").GetString());
        Assert.False(identity.GetProperty("configuration").TryGetProperty("subtitle", out _));
        Assert.Equal($"{Route}/content", identity.GetProperty("contentUrl").GetString());
        Assert.Equal(previewBytes.LongLength, identity.GetProperty("length").GetInt64());

        var objectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        var generatedPath = GetPhysicalPath(storage.RootPath, farmId, objectKey);
        Assert.True(File.Exists(generatedPath));
        Assert.Equal(previewBytes, await File.ReadAllBytesAsync(generatedPath));
        using var storedContent = await client.GetAsync(identity.GetProperty("contentUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, storedContent.StatusCode);
        Assert.Equal(previewBytes, await storedContent.Content.ReadAsByteArrayAsync());

        catalog.Deactivate(TemplateId);
        using var activeCatalog = await client.GetAsync($"{Route}/templates");
        using var activeCatalogBody = JsonDocument.Parse(await activeCatalog.Content.ReadAsStreamAsync());
        Assert.Equal(3, activeCatalogBody.RootElement.GetArrayLength());
        using var retiredPreview = await client.GetAsync(template.GetProperty("previewUrl").GetString());
        Assert.Equal(HttpStatusCode.Conflict, retiredPreview.StatusCode);
        using var retiredTemplatePreviewRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"{Route}/templates/preview",
            await GetAntiforgeryTokenAsync(client),
            payload);
        using var retiredTemplatePreview = await client.SendAsync(retiredTemplatePreviewRequest);
        Assert.Equal(HttpStatusCode.Conflict, retiredTemplatePreview.StatusCode);

        using var unchangedIdentity = await client.GetAsync(Route);
        using var unchangedIdentityBody = JsonDocument.Parse(await unchangedIdentity.Content.ReadAsStreamAsync());
        Assert.Equal(TemplateId, unchangedIdentityBody.RootElement.GetProperty("identity").GetProperty("modelId").GetString());
        using var unchangedContent = await client.GetAsync($"{Route}/content");
        Assert.Equal(HttpStatusCode.OK, unchangedContent.StatusCode);
        Assert.Equal(previewBytes, await unchangedContent.Content.ReadAsByteArrayAsync());

        using var uploadReplacement = await UploadIdentityAsync(
            client,
            await GetAntiforgeryTokenAsync(client),
            "replacement.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.OK, uploadReplacement.StatusCode);
        Assert.False(File.Exists(generatedPath));
        var uploadObjectKey = await GetVisualIdentityReferenceAsync(factory, farmId);
        var uploadPath = GetPhysicalPath(storage.RootPath, farmId, uploadObjectKey);
        Assert.True(File.Exists(uploadPath));

        using var removeRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            Route,
            await GetAntiforgeryTokenAsync(client));
        using var removed = await client.SendAsync(removeRequest);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.False(File.Exists(uploadPath));

        using var invalidConfigurationRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/template",
            await GetAntiforgeryTokenAsync(client),
            new { templateId = "premium", version = TemplateVersion, config = new { prompt = "arbitrary" } });
        using var invalidConfiguration = await client.SendAsync(invalidConfigurationRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalidConfiguration.StatusCode);

        using var unknownTemplateRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"{Route}/templates/preview",
            await GetAntiforgeryTokenAsync(client),
            new { templateId = "unknown", version = TemplateVersion, config = new { } });
        using var unknownTemplate = await client.SendAsync(unknownTemplateRequest);
        Assert.Equal(HttpStatusCode.NotFound, unknownTemplate.StatusCode);
    }

    [Fact]
    public async Task MigrationPreservesLegacyTemplateIdentityRows()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync("20260916174357_ReceiveAsaasWebhookInbox");

        var farmId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO app.breeding_farms (
                "Id", "Name", "ResponsibleName", "ContactEmail", "ContactPhone",
                "OfficialRegistrationNumber", "AddressStreet", "AddressNumber", "AddressComplement",
                "AddressNeighborhood", "AddressCity", "AddressState", "AddressPostalCode",
                "CreatedAtUtc", "UpdatedAtUtc", "VisualIdentitySource", "VisualIdentityReference",
                "VisualIdentityFileName", "VisualIdentityContentType", "VisualIdentityLength")
            VALUES (
                {farmId}, 'Legado', 'Responsável', 'legacy@example.com', NULL,
                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                {now}, {now}, 2, 'template:minimal', NULL, NULL, NULL)
            """);

        await migrator.MigrateAsync();

        var preservedRows = await dbContext.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value"
            FROM app.breeding_farms
            WHERE "Id" = {farmId}
              AND "VisualIdentitySource" = 2
              AND "VisualIdentityReference" = 'template:minimal'
              AND "VisualIdentityTemplateModelId" IS NULL
            """).SingleAsync();
        Assert.Equal(1, preservedRows);
    }

    [Fact]
    public async Task VisualIdentityRequiresAnAuthenticatedOwnerOfTheSelectedFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);

        using var unauthenticated = await ownerClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, ownerClient, "visual-identity-access-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);

        var otherUserId = await RegisterAndAuthenticateAsync(
            factory,
            otherClient,
            "visual-identity-access-other@example.com");
        using var withoutFarm = await otherClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);

        using var ownerUpload = await UploadIdentityAsync(
            ownerClient,
            await GetAntiforgeryTokenAsync(ownerClient),
            "private-logo.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.OK, ownerUpload.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(candidate => candidate.Id == otherUserId);
            otherUser.SelectedBreedingFarmId = ownerFarmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                ownerFarmId,
                otherUserId,
                BreedingFarmRole.Manager,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var unauthorizedMetadata = await otherClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedMetadata.StatusCode);
        using var unauthorizedContent = await otherClient.GetAsync($"{Route}/content");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedContent.StatusCode);
        using var unauthorizedPreviewRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"{Route}/templates/preview",
            await GetAntiforgeryTokenAsync(otherClient),
            new { templateId = TemplateId, version = TemplateVersion, config = new { } });
        using var unauthorizedPreview = await otherClient.SendAsync(unauthorizedPreviewRequest);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedPreview.StatusCode);
        using var unauthorizedApplyRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"{Route}/template",
            await GetAntiforgeryTokenAsync(otherClient),
            new { templateId = TemplateId, version = TemplateVersion, config = new { } });
        using var unauthorizedApply = await otherClient.SendAsync(unauthorizedApplyRequest);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedApply.StatusCode);
        using var unauthorizedUpload = await UploadIdentityAsync(
            otherClient,
            await GetAntiforgeryTokenAsync(otherClient),
            "foreign.png",
            "image/png",
            PngBytes);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedUpload.StatusCode);

        using var unauthorizedRemoveRequest = CreateBrowserRequest(
            HttpMethod.Delete,
            Route,
            await GetAntiforgeryTokenAsync(otherClient));
        using var unauthorizedRemove = await otherClient.SendAsync(unauthorizedRemoveRequest);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedRemove.StatusCode);

        using var ownerMetadata = await ownerClient.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, ownerMetadata.StatusCode);
        using var ownerBody = JsonDocument.Parse(await ownerMetadata.Content.ReadAsStreamAsync());
        Assert.Equal(
            "Upload",
            ownerBody.RootElement.GetProperty("identity").GetProperty("source").GetString());
        Assert.NotEqual(ownerFarmId, otherFarmId);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRootPath,
        IVisualIdentityTemplateCatalog? templateCatalog = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None",
                ["Storage:PrivateRootPath"] = storageRootPath
            }));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                if (templateCatalog is not null)
                {
                    services.RemoveAll<IVisualIdentityTemplateCatalog>();
                    services.AddSingleton(templateCatalog);
                }
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task<HttpResponseMessage> UploadIdentityAsync(
        HttpClient client,
        string antiforgeryToken,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Route);
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName, antiforgeryToken);
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        request.Content = form;
        return await client.SendAsync(request);
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
            new { email, password = "StrongPassword!123", confirmPassword = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailConfirmed = true;
            userId = user.Id;
            await dbContext.SaveChangesAsync();
        }

        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return userId;
    }

    private static async Task<Guid> CreateFarmAsync(HttpClient client)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new { name = "Sítio Aurora", responsibleName = "Owner Principal", contactEmail = "owner@example.com" }));
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

    private static async Task<string> GetVisualIdentityReferenceAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return (await dbContext.BreedingFarms.SingleAsync(candidate => candidate.Id == farmId))
            .VisualIdentityReference!;
    }

    private static string GetPhysicalPath(string storageRootPath, Guid farmId, string objectKey) =>
        Path.Combine(storageRootPath, farmId.ToString("N"), objectKey.Replace('/', Path.DirectorySeparatorChar));

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(Path.GetTempPath(), "CriatorioVirtualIdentityTests", Guid.NewGuid().ToString("N"));

        public string RootPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestVisualIdentityTemplateCatalog(
        IEnumerable<VisualIdentityTemplateDefinition> templates) : IVisualIdentityTemplateCatalog
    {
        private readonly List<VisualIdentityTemplateDefinition> templates = templates.ToList();

        public IReadOnlyList<VisualIdentityTemplateDefinition> GetAll() => templates;

        public void Deactivate(string templateId)
        {
            var index = templates.FindIndex(template => template.Id == templateId);
            Assert.NotEqual(-1, index);
            templates[index] = templates[index] with { IsActive = false };
        }
    }
}
