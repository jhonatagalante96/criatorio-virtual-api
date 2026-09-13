using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Persistence;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class GenerateBadgeBatchCommandHandler(
    CriatorioVirtualDbContext dbContext,
    BadgeBatchGenerationSession session)
    : ICommandHandler<GenerateBadgeBatchCommand, GenerateBadgeBatchResult>
{
    public Task<GenerateBadgeBatchResult> Handle(
        GenerateBadgeBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (session.Status != GenerateBadgeBatchStatus.Generated ||
            session.AggregatePdf is null)
        {
            return Task.FromResult(session.Status switch
            {
                GenerateBadgeBatchStatus.UserNotFound => GenerateBadgeBatchResult.UserNotFound(),
                GenerateBadgeBatchStatus.BreedingFarmNotSelected => GenerateBadgeBatchResult.BreedingFarmNotSelected(),
                GenerateBadgeBatchStatus.BreedingFarmNotFound => GenerateBadgeBatchResult.BreedingFarmNotFound(),
                GenerateBadgeBatchStatus.NoDocumentsGenerated => GenerateBadgeBatchResult.NoDocumentsGenerated(
                    session.BreedingFarmId,
                    session.Items),
                GenerateBadgeBatchStatus.StorageUnavailable => GenerateBadgeBatchResult.StorageUnavailable(
                    session.BreedingFarmId,
                    session.Items),
                GenerateBadgeBatchStatus.AggregateUnavailable => GenerateBadgeBatchResult.AggregateUnavailable(),
                _ => GenerateBadgeBatchResult.InvalidData()
            });
        }

        var generatedItems = session.Items.ToDictionary(item => item.BirdId);
        foreach (var preparation in session.Documents)
        {
            var document = BirdDocument.CreateBadge(
                preparation.DocumentId,
                preparation.GeneratedAtUtc,
                preparation.BirdId,
                preparation.BreedingFarmId,
                preparation.ModelId,
                preparation.PrintSize,
                preparation.StoredObject.ObjectKey,
                preparation.FileName,
                preparation.ContentType,
                preparation.Length,
                preparation.GeneratedAtUtc,
                preparation.SelectedFieldsJson,
                preparation.SnapshotJson);
            dbContext.BirdDocuments.Add(document);
            generatedItems[preparation.BirdId] = new GenerateBadgeBatchItemResult(
                preparation.BirdId,
                GenerateBadgeBatchItemStatus.Generated,
                null,
                new BirdDocumentResult(
                    document.Id,
                    document.BirdId,
                    document.Type,
                    document.ModelId,
                    document.PrintSize,
                    preparation.SelectedFields,
                    document.FileName,
                    document.ContentType,
                    document.Length,
                    preparation.PageCount,
                    preparation.WidthMillimeters,
                    preparation.HeightMillimeters,
                    document.GeneratedAtUtc));
        }

        return Task.FromResult(
            GenerateBadgeBatchResult.Generated(
                session.BreedingFarmId,
                command.BirdIds!
                    .Select(birdId => generatedItems[birdId])
                    .ToArray(),
                session.AggregatePdf));
    }
}
