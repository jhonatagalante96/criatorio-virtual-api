using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CriatorioVirtual.Api;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Reproductions;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Birds;
using CriatorioVirtual.Infrastructure.Identity;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Transfers;

public sealed class BirdMutationTransactionalSerializationTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_UpdateBird_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var editClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-update-user@example.com");
        await AuthenticateExistingUserAsync(editClient, "race-update-user@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Concorrente", "Resp", "OCC-001");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Concorrente", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(editClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(transferClient, speciesId, "Ave Race Update", "100001");

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId, destinationBreedingFarmId = destFarmId, confirmed = true });

        var editRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(editClient),
            new
            {
                name = "Nome Editado Concorrente",
                sex = "Male",
                speciesId,
                birthDate = "2024-01-01",
                ringNumber = "100001",
                notes = "Notas editadas"
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var editTask = editClient.SendAsync(editRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, editTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var editStatus = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(editStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, editStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == birdId);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal("Ave Race Update", bird.Name);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            editRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_ChangeBirdStatus_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var statusClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-status-user@example.com");
        await AuthenticateExistingUserAsync(statusClient, "race-status-user@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Status", "Resp", "OCC-002");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Status", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(statusClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(transferClient, speciesId, "Ave Race Status", "100002");

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId, destinationBreedingFarmId = destFarmId, confirmed = true });

        var statusRequest = CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(statusClient),
            new
            {
                status = "Archived",
                confirmed = true
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var statusTask = statusClient.SendAsync(statusRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, statusTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var statusResult = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(statusResult, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, statusResult);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == birdId);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal(BirdStatus.Transferred, bird.Status);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            statusRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_UpdateBirdGenealogy_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var genealogyClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-genealogy-user@example.com");
        await AuthenticateExistingUserAsync(genealogyClient, "race-genealogy-user@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Genealogy", "Resp", "OCC-003");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Genealogy", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(genealogyClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var fatherId = await CreateBirdAsync(transferClient, speciesId, "Pai Ave", "100003", sex: "Male");
        var motherId = await CreateBirdAsync(transferClient, speciesId, "Mae Ave", "100004", sex: "Female");
        var birdId = await CreateBirdAsync(transferClient, speciesId, "Filho Concorrente", "100005");

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId, destinationBreedingFarmId = destFarmId, confirmed = true });

        var genealogyRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/genealogy",
            await GetAntiforgeryTokenAsync(genealogyClient),
            new
            {
                fatherBirdId = fatherId,
                motherBirdId = motherId
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var genTask = genealogyClient.SendAsync(genealogyRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, genTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var genStatus = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(genStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, genStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == birdId);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Null(bird.FatherBirdId);
            Assert.Null(bird.MotherBirdId);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            genealogyRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_SetPrimaryPhoto_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var photoClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-photo-user@example.com");
        await AuthenticateExistingUserAsync(photoClient, "race-photo-user@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Foto", "Resp", "OCC-004");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Foto", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(photoClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(transferClient, speciesId, "Ave Foto Race", "100006");

        using var upload = await UploadBirdAttachmentAsync(
            transferClient,
            birdId,
            "foto.jpg",
            "image/jpeg",
            JpegBytes);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadDoc = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("attachmentId").GetGuid();

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId, destinationBreedingFarmId = destFarmId, confirmed = true });

        var photoRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}/primary-photo",
            await GetAntiforgeryTokenAsync(photoClient),
            new { attachmentId });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var photoTask = photoClient.SendAsync(photoRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, photoTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var photoStatus = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(photoStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, photoStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == birdId);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Null(bird.PrimaryPhotoId);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            photoRequest.Dispose();
        }
    }

    [Fact]
    public async Task AcceptTransfer_ConcurrentWith_UpdateBird_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var sourceClient = CreateClient(factory);
        using var destClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "accept-src@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Aceite Race", "Resp", "OCC-005");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(factory, destClient, "accept-dst@example.com");
        var destFarmId = await CreateFarmAsync(destClient, "Destino Aceite Race", "Resp 2");
        await SelectFarmAsync(destClient, destFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave Aceite Concorrente", "100007");

        using var reqResp = await RequestTransferAsync(sourceClient, birdId, destFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, reqResp.StatusCode);
        using var reqDoc = JsonDocument.Parse(await reqResp.Content.ReadAsStreamAsync());
        var transferRequestId = reqDoc.RootElement.GetProperty("transferRequestId").GetGuid();

        var acceptRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destClient),
            new { confirmed = true });

        var updateRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/birds/{birdId}",
            await GetAntiforgeryTokenAsync(sourceClient),
            new
            {
                name = "Nome Modificado na Raça",
                sex = "Male",
                speciesId,
                birthDate = "2024-01-01",
                ringNumber = "100007",
                notes = "Tentativa concorrente com aceite"
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var acceptTask = destClient.SendAsync(acceptRequest);
        await firstAcquired;

        var updateTask = sourceClient.SendAsync(updateRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(acceptTask, updateTask);

        try
        {
            var acceptStatus = responses[0].StatusCode;
            var updateStatus = responses[1].StatusCode;

            Assert.DoesNotContain(acceptStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(updateStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.OK, acceptStatus);
            Assert.Contains(updateStatus, new[] { HttpStatusCode.Conflict, HttpStatusCode.NotFound });

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.Id == transferRequestId);
            Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal(destFarmId, bird.BreedingFarmId);
            Assert.Equal("Ave Aceite Concorrente", bird.Name);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            acceptRequest.Dispose();
            updateRequest.Dispose();
        }
    }

    [Fact]
    public async Task AcceptTransfer_ConcurrentWith_ChangeBirdStatus_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var sourceClient = CreateClient(factory);
        using var destClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "accept-status-src@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Aceite Status", "Resp", "OCC-006A");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(factory, destClient, "accept-status-dst@example.com");
        var destFarmId = await CreateFarmAsync(destClient, "Destino Aceite Status", "Resp 2");
        await SelectFarmAsync(destClient, destFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave Aceite Status", "100013");

        using var reqResp = await RequestTransferAsync(sourceClient, birdId, destFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, reqResp.StatusCode);
        using var reqDoc = JsonDocument.Parse(await reqResp.Content.ReadAsStreamAsync());
        var transferRequestId = reqDoc.RootElement.GetProperty("transferRequestId").GetGuid();

        var acceptRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destClient),
            new { confirmed = true });

        var statusRequest = CreateBrowserRequest(
            HttpMethod.Patch,
            $"/api/birds/{birdId}/status",
            await GetAntiforgeryTokenAsync(sourceClient),
            new
            {
                status = "Archived",
                confirmed = true
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var acceptTask = destClient.SendAsync(acceptRequest);
        await firstAcquired;

        var statusTask = sourceClient.SendAsync(statusRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(acceptTask, statusTask);

        try
        {
            var acceptStatus = responses[0].StatusCode;
            var statusResult = responses[1].StatusCode;

            Assert.DoesNotContain(acceptStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(statusResult, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.OK, acceptStatus);
            Assert.Contains(statusResult, new[] { HttpStatusCode.Conflict, HttpStatusCode.NotFound });

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.Id == transferRequestId);
            Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal(destFarmId, bird.BreedingFarmId);
            Assert.Equal(BirdStatus.Active, bird.Status);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            acceptRequest.Dispose();
            statusRequest.Dispose();
        }
    }

    [Fact]
    public async Task AcceptTransfer_ConcurrentWith_Reject_SerializesOneWinnerAndOneConflict()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var sourceClient = CreateClient(factory);
        using var destClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "accept-reject-src@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Conflito", "Resp", "OCC-006B");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(factory, destClient, "accept-reject-dst@example.com");
        var destFarmId = await CreateFarmAsync(destClient, "Destino Conflito", "Resp 2");
        await SelectFarmAsync(destClient, destFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave Accept vs Reject", "100014");

        using var reqResp = await RequestTransferAsync(sourceClient, birdId, destFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, reqResp.StatusCode);
        using var reqDoc = JsonDocument.Parse(await reqResp.Content.ReadAsStreamAsync());
        var transferRequestId = reqDoc.RootElement.GetProperty("transferRequestId").GetGuid();

        using var secondDestClient = CreateClient(factory);
        await AuthenticateExistingUserAsync(secondDestClient, "accept-reject-dst@example.com");

        var acceptRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destClient),
            new { confirmed = true });

        var rejectRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/reject",
            await GetAntiforgeryTokenAsync(secondDestClient),
            new { confirmed = true, rejectionReason = "Rejeitado na corrida" });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var acceptTask = destClient.SendAsync(acceptRequest);
        await firstAcquired;

        var rejectTask = secondDestClient.SendAsync(rejectRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(acceptTask, rejectTask);

        try
        {
            var acceptStatus = responses[0].StatusCode;
            var rejectStatus = responses[1].StatusCode;

            Assert.DoesNotContain(acceptStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(rejectStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.OK, acceptStatus);
            Assert.Equal(HttpStatusCode.Conflict, rejectStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.Id == transferRequestId);
            Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal(destFarmId, bird.BreedingFarmId);
            Assert.Equal(BirdStatus.Active, bird.Status);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            acceptRequest.Dispose();
            rejectRequest.Dispose();
        }
    }

    [Fact]
    public async Task AcceptTransfer_ConcurrentWith_Cancel_SerializesOneWinnerAndOneConflict()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var sourceClient = CreateClient(factory);
        using var destClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, sourceClient, "accept-cancel-src@example.com");
        var sourceFarmId = await CreateFarmAsync(sourceClient, "Origem Cancel Conflito", "Resp", "OCC-006C");
        await SelectFarmAsync(sourceClient, sourceFarmId);

        await RegisterAndAuthenticateAsync(factory, destClient, "accept-cancel-dst@example.com");
        var destFarmId = await CreateFarmAsync(destClient, "Destino Cancel Conflito", "Resp 2");
        await SelectFarmAsync(destClient, destFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(sourceClient, speciesId, "Ave Accept vs Cancel", "100015");

        using var reqResp = await RequestTransferAsync(sourceClient, birdId, destFarmId, confirmed: true);
        Assert.Equal(HttpStatusCode.Created, reqResp.StatusCode);
        using var reqDoc = JsonDocument.Parse(await reqResp.Content.ReadAsStreamAsync());
        var transferRequestId = reqDoc.RootElement.GetProperty("transferRequestId").GetGuid();

        var acceptRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/accept",
            await GetAntiforgeryTokenAsync(destClient),
            new { confirmed = true });

        var cancelRequest = CreateBrowserRequest(
            HttpMethod.Post,
            $"/api/internal-transfers/{transferRequestId}/cancel",
            await GetAntiforgeryTokenAsync(sourceClient),
            new { confirmed = true });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var acceptTask = destClient.SendAsync(acceptRequest);
        await firstAcquired;

        var cancelTask = sourceClient.SendAsync(cancelRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(acceptTask, cancelTask);

        try
        {
            var acceptStatus = responses[0].StatusCode;
            var cancelStatus = responses[1].StatusCode;

            Assert.DoesNotContain(acceptStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(cancelStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.OK, acceptStatus);
            Assert.Equal(HttpStatusCode.Conflict, cancelStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.Id == transferRequestId);
            Assert.Equal(InternalTransferRequestStatus.Accepted, transfer.Status);

            var bird = await db.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal(destFarmId, bird.BreedingFarmId);
            Assert.Equal(BirdStatus.Active, bird.Status);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            acceptRequest.Dispose();
            cancelRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_CreateReproduction_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var reproClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-repro-user@example.com");
        await AuthenticateExistingUserAsync(reproClient, "race-repro-user@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Repro Race", "Resp", "OCC-009");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Repro Race", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(reproClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var maleBirdId = await CreateBirdAsync(transferClient, speciesId, "Macho Repro Race", "100011", sex: "Male");
        var femaleBirdId = await CreateBirdAsync(transferClient, speciesId, "Femea Repro Race", "100012", sex: "Female");

        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId = maleBirdId, destinationBreedingFarmId = destFarmId, confirmed = true });

        var reproRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(reproClient),
            new
            {
                maleBirdId,
                femaleBirdId,
                startDate = "2024-01-01",
                notes = "Reproducao concorrente"
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, maleBirdId);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var reproTask = reproClient.SendAsync(reproRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, reproTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var reproStatus = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(reproStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, reproStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == maleBirdId);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var reproCount = await db.Reproductions.CountAsync(r => r.MaleBirdId == maleBirdId || r.FemaleBirdId == maleBirdId);
            Assert.Equal(0, reproCount);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            reproRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentWith_UpdateReproduction_SerializesWithout500()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var transferClient = CreateClient(factory);
        using var reproClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, transferClient, "race-repro-update@example.com");
        await AuthenticateExistingUserAsync(reproClient, "race-repro-update@example.com");

        var sourceFarmId = await CreateFarmAsync(transferClient, "Origem Repro Update", "Resp", "OCC-010");
        var destFarmId = await CreateFarmAsync(transferClient, "Destino Repro Update", "Resp 2");
        await SelectFarmAsync(transferClient, sourceFarmId);
        await SelectFarmAsync(reproClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var male1 = await CreateBirdAsync(transferClient, speciesId, "Macho 1", "100016", sex: "Male");
        var female1 = await CreateBirdAsync(transferClient, speciesId, "Femea 1", "100017", sex: "Female");
        var candidateMale = await CreateBirdAsync(transferClient, speciesId, "Macho Substituto", "100018", sex: "Male");

        // Create active reproduction with male1 and female1
        using var createReproResp = await reproClient.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/reproductions",
            await GetAntiforgeryTokenAsync(reproClient),
            new
            {
                maleBirdId = male1,
                femaleBirdId = female1,
                startDate = "2024-01-01",
                notes = "Reproducao ativa"
            }));
        Assert.Equal(HttpStatusCode.Created, createReproResp.StatusCode);
        using var reproDoc = JsonDocument.Parse(await createReproResp.Content.ReadAsStreamAsync());
        var reproductionId = reproDoc.RootElement.GetProperty("reproductionId").GetGuid();

        // Concurrently request transfer for candidateMale while updating reproduction to use candidateMale
        var transferRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(transferClient),
            new { birdId = candidateMale, destinationBreedingFarmId = destFarmId, confirmed = true });

        var updateReproRequest = CreateBrowserRequest(
            HttpMethod.Put,
            $"/api/reproductions/{reproductionId}",
            await GetAntiforgeryTokenAsync(reproClient),
            new
            {
                maleBirdId = candidateMale,
                femaleBirdId = female1,
                startDate = "2024-01-01"
            });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, candidateMale);

        var transferTask = transferClient.SendAsync(transferRequest);
        await firstAcquired;

        var updateReproTask = reproClient.SendAsync(updateReproRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(transferTask, updateReproTask);

        try
        {
            var transferStatus = responses[0].StatusCode;
            var reproStatus = responses[1].StatusCode;

            Assert.DoesNotContain(transferStatus, new[] { HttpStatusCode.InternalServerError });
            Assert.DoesNotContain(reproStatus, new[] { HttpStatusCode.InternalServerError });

            Assert.Equal(HttpStatusCode.Created, transferStatus);
            Assert.Equal(HttpStatusCode.Conflict, reproStatus);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var transfer = await db.InternalTransferRequests.SingleAsync(r => r.BirdId == candidateMale);
            Assert.Equal(InternalTransferRequestStatus.Pending, transfer.Status);

            var persistedRepro = await db.Reproductions.SingleAsync(r => r.Id == reproductionId);
            Assert.Equal(male1, persistedRepro.MaleBirdId); // candidateMale was rejected, male1 preserved!
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            transferRequest.Dispose();
            updateReproRequest.Dispose();
        }
    }

    [Fact]
    public async Task RequestTransfer_ConcurrentPendingCreations_OnlyOneSucceeds()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        var interceptor = new TestLockInterceptor();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, interceptor);
        await MigrateAsync(factory);

        using var firstClient = CreateClient(factory);
        using var secondClient = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, firstClient, "double-pending@example.com");
        await AuthenticateExistingUserAsync(secondClient, "double-pending@example.com");

        var sourceFarmId = await CreateFarmAsync(firstClient, "Origem Multi Pending", "Resp", "OCC-007");
        var dest1 = await CreateFarmAsync(firstClient, "Destino 1", "Resp D1");
        var dest2 = await CreateFarmAsync(firstClient, "Destino 2", "Resp D2");
        await SelectFarmAsync(firstClient, sourceFarmId);
        await SelectFarmAsync(secondClient, sourceFarmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(firstClient, speciesId, "Ave Unica Pending", "100009");

        var firstRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(firstClient),
            new { birdId, destinationBreedingFarmId = dest1, confirmed = true });

        var secondRequest = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(secondClient),
            new { birdId, destinationBreedingFarmId = dest2, confirmed = true });

        var (firstAcquired, secondAcquiring, releaseFirst) = CoordinateRace(interceptor, birdId);

        var firstTask = firstClient.SendAsync(firstRequest);
        await firstAcquired;

        var secondTask = secondClient.SendAsync(secondRequest);
        await secondAcquiring;
        await Task.Delay(50);
        releaseFirst();

        var responses = await Task.WhenAll(firstTask, secondTask);

        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.DoesNotContain(responses, r => r.StatusCode == HttpStatusCode.InternalServerError);
        }
        finally
        {
            foreach (var r in responses) r.Dispose();
            firstRequest.Dispose();
            secondRequest.Dispose();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var pendingCount = await dbContext.InternalTransferRequests
            .CountAsync(r => r.BirdId == birdId && r.Status == InternalTransferRequestStatus.Pending);
        Assert.Equal(1, pendingCount);
    }

    [Fact]
    public async Task BirdLockCoordinator_DirectTwoTransactions_CoordinatesPessimisticLockWithoutDeadlock()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();
        using var certificate = TestCertificate.Create();
        using var factory = CreateFactory(database.GetConnectionString(), certificate, new TestLockInterceptor());
        await MigrateAsync(factory);

        using var client = CreateClient(factory);
        await RegisterAndAuthenticateAsync(factory, client, "direct-lock@example.com");
        var farmId = await CreateFarmAsync(client, "Origem Lock Direto", "Resp", "OCC-008");
        await SelectFarmAsync(client, farmId);

        var speciesId = await GetSpeciesIdAsync(factory);
        var birdId = await CreateBirdAsync(client, speciesId, "Ave Lock Direto", "100010");

        var lockAcquiredSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var tx1Task = Task.Run(async () =>
        {
            await using var scope1 = factory.Services.CreateAsyncScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var lock1 = scope1.ServiceProvider.GetRequiredService<IBirdLockCoordinator>();

            await using var tx1 = await db1.Database.BeginTransactionAsync();
            await lock1.AcquireLockAsync(birdId, farmId);

            lockAcquiredSignal.SetResult(true);

            // Wait until tx2 starts waiting
            await proceedSignal.Task;

            // Mutate bird in tx1
            var bird1 = await db1.Birds.SingleAsync(b => b.Id == birdId);
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            bird1.UpdateDetails("Nome Atualizado Pela Tx1", speciesId, BirdSex.Male, bird1.BirthDate, bird1.RingNumber, null, today, now);
            await db1.SaveChangesAsync();
            await tx1.CommitAsync();
        });

        var tx2Task = Task.Run(async () =>
        {
            // Ensure tx1 acquired the lock first
            await lockAcquiredSignal.Task;

            await using var scope2 = factory.Services.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            var lock2 = scope2.ServiceProvider.GetRequiredService<IBirdLockCoordinator>();

            await using var tx2 = await db2.Database.BeginTransactionAsync();

            // Unblock tx1 after a short delay so tx2 enters the queue
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                proceedSignal.TrySetResult(true);
            });

            // This should block until tx1 commits, then succeed cleanly without deadlock
            await lock2.AcquireLockAsync(birdId, farmId);

            var bird2 = await db2.Birds.SingleAsync(b => b.Id == birdId);
            Assert.Equal("Nome Atualizado Pela Tx1", bird2.Name);

            await tx2.CommitAsync();
        });

        await Task.WhenAll(tx1Task, tx2Task);
    }

    private static (Task firstAcquired, Task secondAcquiring, Action releaseFirst) CoordinateRace(
        TestLockInterceptor interceptor,
        Guid targetBirdId)
    {
        var firstAcquiredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAcquiringTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        interceptor.AcquiredHandler = async (lockedBirdId) =>
        {
            if (lockedBirdId == targetBirdId && !firstAcquiredTcs.Task.IsCompleted)
            {
                firstAcquiredTcs.TrySetResult(true);
                await releaseFirstTcs.Task;
            }
        };

        interceptor.AcquiringHandler = (lockedBirdId) =>
        {
            if (lockedBirdId == targetBirdId && firstAcquiredTcs.Task.IsCompleted && !secondAcquiringTcs.Task.IsCompleted)
            {
                secondAcquiringTcs.TrySetResult(true);
            }
            return Task.CompletedTask;
        };

        return (firstAcquiredTcs.Task, secondAcquiringTcs.Task, () => releaseFirstTcs.TrySetResult(true));
    }

    public sealed class TestLockInterceptor
    {
        public Func<Guid, Task>? AcquiringHandler { get; set; }
        public Func<Guid, Task>? AcquiredHandler { get; set; }

        public Task OnAcquiringAsync(Guid birdId) => AcquiringHandler != null ? AcquiringHandler(birdId) : Task.CompletedTask;
        public Task OnAcquiredAsync(Guid birdId) => AcquiredHandler != null ? AcquiredHandler(birdId) : Task.CompletedTask;
    }

    public sealed class TestBirdLockCoordinator(
        CriatorioVirtualDbContext dbContext,
        TestLockInterceptor interceptor) : IBirdLockCoordinator
    {
        public async Task AcquireLockAsync(Guid birdId, CancellationToken cancellationToken = default)
        {
            await interceptor.OnAcquiringAsync(birdId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} FOR UPDATE",
                cancellationToken);
            await interceptor.OnAcquiredAsync(birdId);
        }

        public async Task AcquireLockAsync(Guid birdId, Guid breedingFarmId, CancellationToken cancellationToken = default)
        {
            await interceptor.OnAcquiringAsync(birdId);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
                cancellationToken);
            await interceptor.OnAcquiredAsync(birdId);
        }

        public async Task AcquireLocksAsync(IEnumerable<Guid> birdIds, Guid breedingFarmId, CancellationToken cancellationToken = default)
        {
            var sortedBirdIds = birdIds.Distinct().OrderBy(id => id).ToList();
            foreach (var id in sortedBirdIds)
            {
                await AcquireLockAsync(id, breedingFarmId, cancellationToken);
            }
        }

        public async Task AcquireLocksAsync(IEnumerable<Guid> birdIds, CancellationToken cancellationToken = default)
        {
            var sortedBirdIds = birdIds.Distinct().OrderBy(id => id).ToList();
            foreach (var id in sortedBirdIds)
            {
                await AcquireLockAsync(id, cancellationToken);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
        TestLockInterceptor interceptor) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EventLog:LogLevel:Default"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddInfrastructurePersistence(connectionString, certificate);
                services.AddSingleton(interceptor);
                services.AddScoped<IBirdLockCoordinator, TestBirdLockCoordinator>();
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

    private static async Task AuthenticateExistingUserAsync(HttpClient client, string email)
    {
        using var login = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/auth/login",
            await GetAntiforgeryTokenAsync(client),
            new { email, password = "StrongPassword!123" }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

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

    private static async Task<Guid> CreateFarmAsync(
        HttpClient client,
        string name,
        string responsibleName,
        string? registrationNumber = null)
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/breeding-farms",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                responsibleName,
                contactEmail = $"{Guid.NewGuid():N}@example.com",
                officialRegistrationNumber = registrationNumber
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

    private static async Task<Guid> CreateBirdAsync(
        HttpClient client,
        Guid speciesId,
        string name,
        string ringNumber,
        string sex = "Male")
    {
        using var response = await client.SendAsync(CreateBrowserRequest(
            HttpMethod.Post,
            "/api/birds",
            await GetAntiforgeryTokenAsync(client),
            new
            {
                name,
                sex,
                speciesId,
                birthDate = "2024-01-01",
                ringNumber,
                notes = "Observações"
            }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("birdId").GetGuid();
    }

    private static async Task<HttpResponseMessage> RequestTransferAsync(
        HttpClient client,
        Guid birdId,
        Guid destinationBreedingFarmId,
        bool confirmed)
    {
        using var request = CreateBrowserRequest(
            HttpMethod.Post,
            "/api/internal-transfers",
            await GetAntiforgeryTokenAsync(client),
            new { birdId, destinationBreedingFarmId, confirmed });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UploadBirdAttachmentAsync(
        HttpClient client,
        Guid birdId,
        string fileName,
        string contentType,
        byte[] bytes,
        string? caption = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/birds/{birdId}/attachments");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("X-XSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        if (caption is not null)
        {
            form.Add(new StringContent(caption), "caption");
        }
        request.Content = form;
        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/antiforgery/token");
        request.Headers.Add("Origin", "http://localhost:3000");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response.Headers.GetValues("X-XSRF-TOKEN").Single();
    }

    private static HttpRequestMessage CreateBrowserRequest(
        HttpMethod method,
        string uri,
        string antiforgeryToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        var migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync();
    }
}
