using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.Infrastructure.Storage;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Birds;

public sealed class BirdAttachmentEndpointTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xD9];
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/8ZkAAAAASUVORK5CYII=");

    [Fact]
    public async Task AttachmentEndpointsRequireAuthenticationAndASelectedBreedingFarm()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);

        using var unauthenticated = await client.GetAsync($"/api/birds/{Guid.NewGuid()}/attachments");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndAuthenticateAsync(factory, client, "attachments-no-farm@example.com");
        using var withoutFarm = await client.GetAsync($"/api/birds/{Guid.NewGuid()}/attachments");
        Assert.Equal(HttpStatusCode.Conflict, withoutFarm.StatusCode);
    }

    [Fact]
    public async Task UploadListAndDownloadRoundTripPersistsOnlyPrivateMetadata()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-roundtrip@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Aurora");
        var bytes = JpegBytes;

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "bird.jpg",
            "image/jpeg",
            bytes);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();
        Assert.Equal(birdId, uploadBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal("bird.jpg", uploadBody.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("image/jpeg", uploadBody.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(bytes.Length, uploadBody.RootElement.GetProperty("length").GetInt64());
        Assert.Equal(
            $"/api/birds/{birdId}/attachments/{attachmentId}/content",
            uploadBody.RootElement.GetProperty("downloadUrl").GetString());

        using var listing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listingBody = JsonDocument.Parse(await listing.Content.ReadAsStreamAsync());
        Assert.Equal(farmId, listingBody.RootElement.GetProperty("breedingFarmId").GetGuid());
        var item = Assert.Single(listingBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(attachmentId, item.GetProperty("attachmentId").GetGuid());
        Assert.Equal("bird.jpg", item.GetProperty("fileName").GetString());

        using var download = await client.GetAsync(
            $"/api/birds/{birdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/jpeg", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var attachment = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
        Assert.Equal(farmId, attachment.BreedingFarmId);
        Assert.Equal(birdId, attachment.BirdId);
        Assert.DoesNotContain(storage.RootPath, attachment.ObjectKey, StringComparison.OrdinalIgnoreCase);
        var physicalPath = Path.Combine(
            storage.RootPath,
            farmId.ToString("N"),
            attachment.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physicalPath));
    }

    [Fact]
    public async Task AttachmentAccessDoesNotCrossBreedingFarmBoundaries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "attachments-boundary-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "attachments-boundary-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var ownerBirdId = await AddBirdAsync(factory, ownerFarmId, speciesId, "Owner bird");

        using var upload = await UploadAsync(
            ownerClient,
            ownerBirdId,
            await GetAntiforgeryTokenAsync(ownerClient),
            "owner.pdf",
            "application/pdf",
            [9, 8, 7]);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var foreignList = await otherClient.GetAsync($"/api/birds/{ownerBirdId}/attachments");
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);
        using var foreignDownload = await otherClient.GetAsync(
            $"/api/birds/{ownerBirdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);

        using var foreignUpload = await UploadAsync(
            otherClient,
            ownerBirdId,
            await GetAntiforgeryTokenAsync(otherClient),
            "foreign.jpg",
            "image/jpeg",
            JpegBytes);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpload.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Equal(
            1,
            await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == ownerFarmId));
        Assert.Equal(
            0,
            await dbContext.BirdAttachments.CountAsync(candidate => candidate.BreedingFarmId == otherFarmId));
    }

    [Fact]
    public async Task UploadRejectsEmptyOversizedAndMismatchedFilesWithoutPersistence()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-validation@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Validation bird");

        using var empty = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "empty.jpg",
            "image/jpeg",
            []);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var mismatch = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "bird.pdf",
            "image/jpeg",
            [1, 2, 3]);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        using var oversized = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "large.jpg",
            "image/jpeg",
            new byte[10 * 1024 * 1024 + 1]);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        Assert.Empty(await dbContext.BirdAttachments.ToArrayAsync());
        Assert.False(Directory.Exists(Path.Combine(storage.RootPath, farmId.ToString("N"))));
    }

    [Fact]
    public async Task PrimaryPhotoCanBeSelectedReplacedAndClearedAndIsReflectedInQueries()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "primary-photo-owner@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Primary photo bird");

        using var firstUpload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "first.jpg",
            "image/jpeg",
            JpegBytes);
        using var firstUploadBody = JsonDocument.Parse(await firstUpload.Content.ReadAsStreamAsync());
        var firstAttachmentId = firstUploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var secondUpload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "second.png",
            "image/png",
            PngBytes);
        using var secondUploadBody = JsonDocument.Parse(await secondUpload.Content.ReadAsStreamAsync());
        var secondAttachmentId = secondUploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var documentUpload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "document.pdf",
            "application/pdf",
            [7, 8, 9]);
        using var documentUploadBody = JsonDocument.Parse(await documentUpload.Content.ReadAsStreamAsync());
        var documentAttachmentId = documentUploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var firstSelection = await SetPrimaryPhotoAsync(
            client,
            birdId,
            firstAttachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, firstSelection.StatusCode);
        using var firstSelectionBody = JsonDocument.Parse(await firstSelection.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, firstSelectionBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(firstAttachmentId, firstSelectionBody.RootElement.GetProperty("primaryPhotoId").GetGuid());

        using var firstListing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        Assert.Equal(HttpStatusCode.OK, firstListing.StatusCode);
        using var firstListingBody = JsonDocument.Parse(await firstListing.Content.ReadAsStreamAsync());
        var firstItems = firstListingBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.True(firstItems.Single(item => item.GetProperty("attachmentId").GetGuid() == firstAttachmentId).GetProperty("isPrimary").GetBoolean());
        Assert.False(firstItems.Single(item => item.GetProperty("attachmentId").GetGuid() == secondAttachmentId).GetProperty("isPrimary").GetBoolean());
        Assert.False(firstItems.Single(item => item.GetProperty("attachmentId").GetGuid() == documentAttachmentId).GetProperty("isPrimary").GetBoolean());

        using var firstDetails = await client.GetAsync($"/api/birds/{birdId}");
        Assert.Equal(HttpStatusCode.OK, firstDetails.StatusCode);
        using var firstDetailsBody = JsonDocument.Parse(await firstDetails.Content.ReadAsStreamAsync());
        Assert.Equal(firstAttachmentId, firstDetailsBody.RootElement.GetProperty("primaryPhotoId").GetGuid());

        using var replacement = await SetPrimaryPhotoAsync(
            client,
            birdId,
            secondAttachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);

        using var replacementListing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        using var replacementListingBody = JsonDocument.Parse(await replacementListing.Content.ReadAsStreamAsync());
        var replacementItems = replacementListingBody.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.False(replacementItems.Single(item => item.GetProperty("attachmentId").GetGuid() == firstAttachmentId).GetProperty("isPrimary").GetBoolean());
        Assert.True(replacementItems.Single(item => item.GetProperty("attachmentId").GetGuid() == secondAttachmentId).GetProperty("isPrimary").GetBoolean());

        using var clear = await ClearPrimaryPhotoAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        using var clearBody = JsonDocument.Parse(await clear.Content.ReadAsStreamAsync());
        Assert.Equal(birdId, clearBody.RootElement.GetProperty("birdId").GetGuid());
        Assert.Equal(JsonValueKind.Null, clearBody.RootElement.GetProperty("primaryPhotoId").ValueKind);

        using var repeatedClear = await ClearPrimaryPhotoAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, repeatedClear.StatusCode);

        using var documentSelection = await SetPrimaryPhotoAsync(
            client,
            birdId,
            documentAttachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.BadRequest, documentSelection.StatusCode);
    }

    [Fact]
    public async Task PrimaryPhotoSelectionIsTenantScopedAndBlockedDuringTransfer()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "primary-photo-boundary-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "primary-photo-boundary-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, ownerFarmId, speciesId, "Boundary bird");

        using var upload = await UploadAsync(
            ownerClient,
            birdId,
            await GetAntiforgeryTokenAsync(ownerClient),
            "owner.jpg",
            "image/jpeg",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var foreignSelection = await SetPrimaryPhotoAsync(
            otherClient,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(otherClient));
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);

        using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.Null(bird.PrimaryPhotoId);
        bird.MarkTransferPending(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync();

        using var blockedSelection = await SetPrimaryPhotoAsync(
            ownerClient,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(ownerClient));
        Assert.Equal(HttpStatusCode.Conflict, blockedSelection.StatusCode);

        using var blockedClear = await ClearPrimaryPhotoAsync(
            ownerClient,
            birdId,
            await GetAntiforgeryTokenAsync(ownerClient));
        Assert.Equal(HttpStatusCode.Conflict, blockedClear.StatusCode);
    }

    [Fact]
    public async Task DatabaseRejectsDeletingAnAttachmentUsedAsPrimaryPhoto()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "primary-photo-fk@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Primary photo FK bird");

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "primary.jpg",
            "image/jpeg",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var selection = await SetPrimaryPhotoAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var attachment = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
        dbContext.BirdAttachments.Remove(attachment);

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentPrimaryPhotoSelectionsLeaveOneValidPrimaryReference()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "primary-photo-concurrency@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Concurrent bird");

        var attachmentIds = new List<Guid>();
        foreach (var fileName in new[] { "first.jpg", "second.jpg" })
        {
            using var upload = await UploadAsync(
                client,
                birdId,
                await GetAntiforgeryTokenAsync(client),
                fileName,
                "image/jpeg",
                JpegBytes);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            using var body = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
            attachmentIds.Add(body.RootElement.GetProperty("attachmentId").GetGuid());
        }

        var requests = attachmentIds
            .Select(async attachmentId => await SetPrimaryPhotoAsync(
                client,
                birdId,
                attachmentId,
                await GetAntiforgeryTokenAsync(client)))
            .ToArray();
        var responses = await Task.WhenAll(requests);
        using var first = responses[0];
        using var second = responses[1];
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var bird = await dbContext.Birds.SingleAsync(candidate => candidate.Id == birdId);
        Assert.NotNull(bird.PrimaryPhotoId);
        Assert.Contains(bird.PrimaryPhotoId.Value, attachmentIds);
    }

    [Fact]
    public async Task RemoveAttachmentRequiresConfirmationAndHidesItFromListAndDownload()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-remove@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Remove bird");

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "remove.pdf",
            "application/pdf",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var withoutConfirmation = await DeleteAttachmentAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client),
            confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutConfirmation.StatusCode);

        var deleteToken = await GetAntiforgeryTokenAsync(client);
        var concurrentRemovals = await Task.WhenAll(
            DeleteAttachmentAsync(client, birdId, attachmentId, deleteToken),
            DeleteAttachmentAsync(client, birdId, attachmentId, deleteToken));
        foreach (var concurrentRemoval in concurrentRemovals)
        {
            using (concurrentRemoval)
            {
                Assert.Equal(HttpStatusCode.NoContent, concurrentRemoval.StatusCode);
            }
        }

        using var listing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listingBody = JsonDocument.Parse(await listing.Content.ReadAsStreamAsync());
        Assert.Empty(listingBody.RootElement.GetProperty("items").EnumerateArray());

        using var download = await client.GetAsync($"/api/birds/{birdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var attachment = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
        Assert.NotNull(attachment.DeletedAtUtc);
        Assert.False(attachment.StorageCleanupPending);
        Assert.False(File.Exists(GetPhysicalPath(storage.RootPath, farmId, attachment.ObjectKey)));

        using var repeated = await DeleteAttachmentAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
    }

    [Fact]
    public async Task RemovePrimaryPhotoRequiresReplacementBeforeDeletion()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-remove-primary@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Primary remove bird");

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "primary.jpg",
            "image/jpeg",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();
        using var selection = await SetPrimaryPhotoAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.OK, selection.StatusCode);

        using var removal = await DeleteAttachmentAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.Conflict, removal.StatusCode);

        using var listing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        using var listingBody = JsonDocument.Parse(await listing.Content.ReadAsStreamAsync());
        Assert.Single(listingBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(File.Exists(GetPhysicalPath(
            storage.RootPath,
            farmId,
            (await GetAttachmentAsync(factory, attachmentId)).ObjectKey)));
    }

    [Fact]
    public async Task StorageCleanupFailureLeavesRetryablePendingStateAndKeepsAttachmentHidden()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        var failingStorage = new FailingDeleteStorage(storage.RootPath);
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(
            database.GetConnectionString(),
            certificate,
            storage.RootPath,
            failingStorage);
        await MigrateAsync(factory);
        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "attachments-remove-retry@example.com");
        var farmId = await CreateFarmAsync(client);
        await SelectFarmAsync(client, farmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, farmId, speciesId, "Retry remove bird");

        using var upload = await UploadAsync(
            client,
            birdId,
            await GetAntiforgeryTokenAsync(client),
            "retry.pdf",
            "application/pdf",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var failedRemoval = await DeleteAttachmentAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedRemoval.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var pending = await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
            Assert.NotNull(pending.DeletedAtUtc);
            Assert.True(pending.StorageCleanupPending);
            Assert.True(File.Exists(GetPhysicalPath(storage.RootPath, farmId, pending.ObjectKey)));
        }

        using var hiddenListing = await client.GetAsync($"/api/birds/{birdId}/attachments");
        using var hiddenListingBody = JsonDocument.Parse(await hiddenListing.Content.ReadAsStreamAsync());
        Assert.Empty(hiddenListingBody.RootElement.GetProperty("items").EnumerateArray());
        using var hiddenDownload = await client.GetAsync($"/api/birds/{birdId}/attachments/{attachmentId}/content");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDownload.StatusCode);

        failingStorage.FailDelete = false;
        using var retry = await DeleteAttachmentAsync(
            client,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);

        await using var verifiedScope = factory.Services.CreateAsyncScope();
        var verifiedContext = verifiedScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var cleaned = await verifiedContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
        Assert.False(cleaned.StorageCleanupPending);
        Assert.False(File.Exists(GetPhysicalPath(storage.RootPath, farmId, cleaned.ObjectKey)));
    }

    [Fact]
    public async Task RemoveAttachmentIsTenantScopedAndOwnerOnly()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        await using var storage = new TemporaryStorage();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, storage.RootPath);
        await MigrateAsync(factory);
        using var ownerClient = CreateClient(factory);
        using var otherClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, ownerClient, "attachments-remove-owner@example.com");
        var ownerFarmId = await CreateFarmAsync(ownerClient);
        await SelectFarmAsync(ownerClient, ownerFarmId);
        await RegisterAndAuthenticateAsync(factory, otherClient, "attachments-remove-other@example.com");
        var otherFarmId = await CreateFarmAsync(otherClient);
        await SelectFarmAsync(otherClient, otherFarmId);
        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await AddBirdAsync(factory, ownerFarmId, speciesId, "Tenant remove bird");

        using var upload = await UploadAsync(
            ownerClient,
            birdId,
            await GetAntiforgeryTokenAsync(ownerClient),
            "tenant.pdf",
            "application/pdf",
            JpegBytes);
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadBody.RootElement.GetProperty("attachmentId").GetGuid();

        using var foreignRemoval = await DeleteAttachmentAsync(
            otherClient,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(otherClient));
        Assert.Equal(HttpStatusCode.NotFound, foreignRemoval.StatusCode);

        await using (var membershipScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = membershipScope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var otherUser = await dbContext.Users.SingleAsync(
                candidate => candidate.Email == "attachments-remove-other@example.com");
            otherUser.SelectedBreedingFarmId = ownerFarmId;
            dbContext.BreedingFarmUsers.Add(new BreedingFarmUser(
                ownerFarmId,
                otherUser.Id,
                BreedingFarmRole.Viewer,
                DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync();
        }

        using var nonOwnerRemoval = await DeleteAttachmentAsync(
            otherClient,
            birdId,
            attachmentId,
            await GetAntiforgeryTokenAsync(otherClient));
        Assert.Equal(HttpStatusCode.NotFound, nonOwnerRemoval.StatusCode);

        using var ownerListing = await ownerClient.GetAsync($"/api/birds/{birdId}/attachments");
        using var ownerListingBody = JsonDocument.Parse(await ownerListing.Content.ReadAsStreamAsync());
        Assert.Single(ownerListingBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.NotEqual(ownerFarmId, otherFarmId);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        string storageRootPath,
        IPrivateObjectStorage? storageOverride = null) =>
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
                if (storageOverride is not null)
                {
                    services.RemoveAll<IPrivateObjectStorage>();
                    services.AddSingleton(storageOverride);
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

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid birdId,
        string antiforgeryToken,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/birds/{birdId}/attachments");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add(
            HttpSecurityServiceCollectionExtensions.AntiforgeryHeaderName,
            antiforgeryToken);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SetPrimaryPhotoAsync(
        HttpClient client,
        Guid birdId,
        Guid attachmentId,
        string antiforgeryToken)
    {
        using var request = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/primary-photo",
            antiforgeryToken,
            new { attachmentId });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ClearPrimaryPhotoAsync(
        HttpClient client,
        Guid birdId,
        string antiforgeryToken)
    {
        using var request = CreateBrowserRequest(
            HttpMethod.Delete,
            $"/api/birds/{birdId}/primary-photo",
            antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> DeleteAttachmentAsync(
        HttpClient client,
        Guid birdId,
        Guid attachmentId,
        string antiforgeryToken,
        bool confirmed = true)
    {
        using var request = CreateBrowserRequest(
            HttpMethod.Delete,
            $"/api/birds/{birdId}/attachments/{attachmentId}",
            antiforgeryToken,
            new { confirmed });
        return await client.SendAsync(request);
    }

    private static async Task<BirdAttachment> GetAttachmentAsync(
        WebApplicationFactory<Program> factory,
        Guid attachmentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        return await dbContext.BirdAttachments.SingleAsync(candidate => candidate.Id == attachmentId);
    }

    private static string GetPhysicalPath(string rootPath, Guid farmId, string objectKey) =>
        Path.Combine(rootPath, farmId.ToString("N"), objectKey.Replace('/', Path.DirectorySeparatorChar));

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
            new
            {
                name = "Sítio Aurora",
                responsibleName = "Owner Principal",
                contactEmail = "owner@example.com"
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("breedingFarmId").GetGuid();
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

    private static async Task<Guid> AddBirdAsync(
        WebApplicationFactory<Program> factory,
        Guid farmId,
        Guid speciesId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var now = DateTimeOffset.UtcNow;
        var bird = new Bird(
            Guid.NewGuid(),
            now,
            farmId,
            name,
            speciesId,
            BirdSex.Female,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(now.UtcDateTime));
        dbContext.Birds.Add(bird);
        await dbContext.SaveChangesAsync();
        return bird.Id;
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

    private sealed class TemporaryStorage : IAsyncDisposable
    {
        public TemporaryStorage() =>
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CriatorioVirtualAttachmentTests",
                Guid.NewGuid().ToString("N"));

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

    private sealed class FailingDeleteStorage : IPrivateObjectStorage
    {
        private readonly FileSystemPrivateObjectStorage innerStorage;

        public FailingDeleteStorage(string rootPath) =>
            innerStorage = new FileSystemPrivateObjectStorage(Options.Create(new PrivateStorageOptions
            {
                PrivateRootPath = rootPath
            }));

        public bool FailDelete { get; set; } = true;

        public Task<PrivateObjectDescriptor> PutAsync(
            PrivateObjectUpload upload,
            CancellationToken cancellationToken = default) =>
            innerStorage.PutAsync(upload, cancellationToken);

        public Task<Stream> OpenReadAsync(
            Guid breedingFarmId,
            string objectKey,
            CancellationToken cancellationToken = default) =>
            innerStorage.OpenReadAsync(breedingFarmId, objectKey, cancellationToken);

        public Task DeleteAsync(
            Guid breedingFarmId,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            if (FailDelete)
            {
                throw new IOException("Simulated private storage failure.");
            }

            return innerStorage.DeleteAsync(breedingFarmId, objectKey, cancellationToken);
        }

        public Task MoveAsync(
            Guid sourceBreedingFarmId,
            Guid destinationBreedingFarmId,
            string objectKey,
            CancellationToken cancellationToken = default) =>
            innerStorage.MoveAsync(sourceBreedingFarmId, destinationBreedingFarmId, objectKey, cancellationToken);
    }
}
