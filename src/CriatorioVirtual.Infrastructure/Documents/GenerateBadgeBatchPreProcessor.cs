using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class GenerateBadgeBatchPreProcessor(
    CriatorioVirtualDbContext dbContext,
    IServiceScopeFactory scopeFactory,
    IPrivateObjectStorage storage,
    IPdfDocumentAssembler assembler,
    IOptions<BadgeBatchOptions> options,
    BadgeBatchGenerationSession session)
    : ICommandPreProcessor<GenerateBadgeBatchCommand>
{
    private const string BirdNotFoundError = "bird_not_found";
    private const string MissingRingNumberError = "missing_ring_number";
    private const string InvalidDataError = "invalid_data";
    private const string StorageUnavailableError = "storage_unavailable";

    public async Task Process(
        GenerateBadgeBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryCreateBadgeConfiguration(command, out var badge) ||
            command.BirdIds is null ||
            command.BirdIds.Count == 0 ||
            command.BirdIds.Count > options.Value.MaxBirdCount ||
            command.BirdIds.Any(id => id == Guid.Empty) ||
            command.BirdIds.Distinct().Count() != command.BirdIds.Count)
        {
            session.SetStatus(GenerateBadgeBatchStatus.InvalidData);
            return;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            session.SetStatus(GenerateBadgeBatchStatus.UserNotFound);
            return;
        }

        if (user.SelectedBreedingFarmId is null)
        {
            session.SetStatus(GenerateBadgeBatchStatus.BreedingFarmNotSelected);
            return;
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            session.SetStatus(GenerateBadgeBatchStatus.BreedingFarmNotFound);
            return;
        }

        var birdReferences = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => breedingFarmId == bird.BreedingFarmId && command.BirdIds.Contains(bird.Id))
            .Select(bird => new BirdReference(bird.Id, bird.RingNumber))
            .ToDictionaryAsync(bird => bird.BirdId, cancellationToken);

        var itemResults = new List<GenerateBadgeBatchItemResult>(command.BirdIds.Count);
        var preparedDocuments = new List<BadgeBatchDocumentPreparation>();
        var renderedDocuments = new List<RenderedDocument>();
        foreach (var birdId in command.BirdIds)
        {
            if (!birdReferences.TryGetValue(birdId, out var bird))
            {
                itemResults.Add(new GenerateBadgeBatchItemResult(
                    birdId,
                    GenerateBadgeBatchItemStatus.BirdNotFound,
                    BirdNotFoundError,
                    null));
                continue;
            }

            if (string.IsNullOrWhiteSpace(bird.RingNumber))
            {
                itemResults.Add(new GenerateBadgeBatchItemResult(
                    birdId,
                    GenerateBadgeBatchItemStatus.MissingRingNumber,
                    MissingRingNumberError,
                    null));
                continue;
            }

            var preparationResult = await PrepareBadgeAsync(
                command,
                badge,
                birdId,
                cancellationToken);
            if (preparationResult.Status is GenerateBirdDocumentStatus.UserNotFound or
                GenerateBirdDocumentStatus.BreedingFarmNotSelected or
                GenerateBirdDocumentStatus.BreedingFarmNotFound)
            {
                await CompensateAndSetStatusAsync(
                    preparationResult.Status switch
                    {
                        GenerateBirdDocumentStatus.UserNotFound => GenerateBadgeBatchStatus.UserNotFound,
                        GenerateBirdDocumentStatus.BreedingFarmNotSelected => GenerateBadgeBatchStatus.BreedingFarmNotSelected,
                        _ => GenerateBadgeBatchStatus.BreedingFarmNotFound
                    },
                    cancellationToken);
                return;
            }

            if (preparationResult.Status != GenerateBirdDocumentStatus.Generated ||
                preparationResult.Preparation is null ||
                preparationResult.StoredObject is null ||
                preparationResult.Rendered is null)
            {
                var itemStatus = preparationResult.Status == GenerateBirdDocumentStatus.StorageUnavailable
                    ? GenerateBadgeBatchItemStatus.StorageUnavailable
                    : GenerateBadgeBatchItemStatus.InvalidData;
                itemResults.Add(new GenerateBadgeBatchItemResult(
                    birdId,
                    itemStatus,
                    itemStatus == GenerateBadgeBatchItemStatus.StorageUnavailable
                        ? StorageUnavailableError
                        : InvalidDataError,
                    null));
                continue;
            }

            var preparation = preparationResult.Preparation;
            var storedObject = preparationResult.StoredObject;
            session.AddStoredObject(storedObject);
            renderedDocuments.Add(preparationResult.Rendered);
            preparedDocuments.Add(new BadgeBatchDocumentPreparation(
                preparation.DocumentId,
                preparation.BirdId,
                preparation.BreedingFarmId,
                preparation.ModelId!.Value,
                preparation.PrintSize!.Value,
                preparation.SelectedFields,
                preparation.SelectedFieldsJson,
                preparation.SnapshotJson,
                preparation.FileName,
                storedObject.ContentType,
                storedObject.Length,
                preparation.PageCount,
                preparation.WidthMillimeters,
                preparation.HeightMillimeters,
                preparation.GeneratedAtUtc,
                storedObject));
            itemResults.Add(new GenerateBadgeBatchItemResult(
                birdId,
                GenerateBadgeBatchItemStatus.Generated,
                null,
                null));
        }

        if (preparedDocuments.Count == 0)
        {
            var status = itemResults.Count > 0 &&
                itemResults.All(item => item.Status == GenerateBadgeBatchItemStatus.StorageUnavailable)
                ? GenerateBadgeBatchStatus.StorageUnavailable
                : GenerateBadgeBatchStatus.NoDocumentsGenerated;
            session.SetItems(breedingFarmId, itemResults, status);
            return;
        }

        RenderedDocument aggregatePdf;
        try
        {
            aggregatePdf = assembler.Assemble(
                renderedDocuments,
                $"badge-batch-{Guid.NewGuid():N}.pdf");
        }
        catch (ArgumentException)
        {
            await session.CompensateAsync(CancellationToken.None);
            session.SetStatus(GenerateBadgeBatchStatus.AggregateUnavailable);
            return;
        }

        session.SetPrepared(
            breedingFarmId,
            itemResults,
            preparedDocuments,
            aggregatePdf);
    }

    private async Task<BadgePreparationResult> PrepareBadgeAsync(
        GenerateBadgeBatchCommand command,
        BadgeRenderConfiguration badge,
        Guid birdId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var generationPreProcessor = scope.ServiceProvider
            .GetRequiredService<ICommandPreProcessor<GenerateBirdDocumentCommand>>();
        var generationSession = scope.ServiceProvider
            .GetRequiredService<BirdDocumentGenerationSession>();
        try
        {
            await generationPreProcessor.Process(
                new GenerateBirdDocumentCommand(
                    command.UserId,
                    birdId,
                    BirdDocumentType.Badge,
                    badge.ModelId,
                    badge.PrintSize,
                    badge.SelectedFields),
                cancellationToken);

            if (generationSession.Status != GenerateBirdDocumentStatus.Generated ||
                generationSession.Preparation is null ||
                generationSession.StoredObject is null)
            {
                return new BadgePreparationResult(
                    generationSession.Status,
                    generationSession.Preparation,
                    generationSession.StoredObject,
                    null);
            }

            try
            {
                await using var content = await storage.OpenReadAsync(
                    generationSession.StoredObject.BreedingFarmId,
                    generationSession.StoredObject.ObjectKey,
                    cancellationToken);
                await using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                if (buffer.Length != generationSession.StoredObject.Length)
                {
                    await generationSession.CompensateAsync(CancellationToken.None);
                    return new BadgePreparationResult(
                        GenerateBirdDocumentStatus.StorageUnavailable,
                        null,
                        null,
                        null);
                }

                var preparation = generationSession.Preparation;
                return new BadgePreparationResult(
                    generationSession.Status,
                    preparation,
                    generationSession.StoredObject,
                    new RenderedDocument(
                        buffer.ToArray(),
                        preparation.FileName,
                        preparation.ContentType,
                        preparation.PageCount,
                        preparation.WidthMillimeters,
                        preparation.HeightMillimeters));
            }
            catch (FileNotFoundException)
            {
                await generationSession.CompensateAsync(CancellationToken.None);
                return new BadgePreparationResult(GenerateBirdDocumentStatus.StorageUnavailable, null, null, null);
            }
            catch (DirectoryNotFoundException)
            {
                await generationSession.CompensateAsync(CancellationToken.None);
                return new BadgePreparationResult(GenerateBirdDocumentStatus.StorageUnavailable, null, null, null);
            }
            catch (IOException)
            {
                await generationSession.CompensateAsync(CancellationToken.None);
                return new BadgePreparationResult(GenerateBirdDocumentStatus.StorageUnavailable, null, null, null);
            }
            catch (UnauthorizedAccessException)
            {
                await generationSession.CompensateAsync(CancellationToken.None);
                return new BadgePreparationResult(GenerateBirdDocumentStatus.StorageUnavailable, null, null, null);
            }
        }
        catch
        {
            await generationSession.CompensateAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task CompensateAndSetStatusAsync(
        GenerateBadgeBatchStatus status,
        CancellationToken cancellationToken)
    {
        await session.CompensateAsync(CancellationToken.None);
        session.SetStatus(status);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool TryCreateBadgeConfiguration(
        GenerateBadgeBatchCommand command,
        out BadgeRenderConfiguration badge)
    {
        badge = null!;
        if (command.UserId == Guid.Empty ||
            command.ModelId is null ||
            command.PrintSize is null ||
            command.SelectedFields is null)
        {
            return false;
        }

        try
        {
            badge = new BadgeRenderConfiguration(
                command.ModelId.Value,
                command.PrintSize.Value,
                command.SelectedFields);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record BirdReference(Guid BirdId, string? RingNumber);

    private sealed record BadgePreparationResult(
        GenerateBirdDocumentStatus Status,
        BirdDocumentPreparation? Preparation,
        PrivateObjectDescriptor? StoredObject,
        RenderedDocument? Rendered);
}
