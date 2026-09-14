using System.Text.Json;
using System.Text.Json.Serialization;
using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Documents;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class ListBirdDocumentsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListBirdDocumentsQuery, ListBirdDocumentsResult>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<ListBirdDocumentsResult> Handle(
        ListBirdDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListBirdDocumentsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListBirdDocumentsResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive,
                cancellationToken);
        if (!hasActiveMembership)
        {
            return ListBirdDocumentsResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return ListBirdDocumentsResult.BirdNotFound();
        }

        var documents = await dbContext.BirdDocuments
            .AsNoTracking()
            .Where(document =>
                document.BirdId == query.BirdId &&
                (document.Type == BirdDocumentType.Badge ||
                 document.Type == BirdDocumentType.GenealogyCertificate ||
                 document.Type == BirdDocumentType.ProvenanceDocument))
            .OrderByDescending(document => document.GeneratedAtUtc)
            .ThenByDescending(document => document.Id)
            .ToArrayAsync(cancellationToken);

        return ListBirdDocumentsResult.Succeeded(
            breedingFarmId,
            query.BirdId,
            documents
                .Select(document => new BirdDocumentListItem(
                    document.Id,
                    document.BirdId,
                    document.Type,
                    document.ModelId,
                    document.PrintSize,
                    DeserializeSelectedFields(document.SelectedFieldsJson),
                    document.FileName,
                    document.ContentType,
                    document.Length,
                    document.GeneratedAtUtc,
                    document.CertificateModelId))
                .ToArray());
    }

    private static IReadOnlyCollection<DocumentField> DeserializeSelectedFields(string json) =>
        JsonSerializer.Deserialize<DocumentField[]>(json, JsonOptions) ?? [];

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
