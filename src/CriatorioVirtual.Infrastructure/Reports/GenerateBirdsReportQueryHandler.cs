using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Reports;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reports;

public sealed class GenerateBirdsReportQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GenerateBirdsReportQuery, GenerateBirdsReportResult>
{
    private static readonly BirdSex[] ReportSexes =
    [
        BirdSex.Male,
        BirdSex.Female,
        BirdSex.Unknown
    ];

    private static readonly BirdReportClassification[] ReportClassifications =
    [
        BirdReportClassification.Matrix,
        BirdReportClassification.Offspring
    ];

    public async Task<GenerateBirdsReportResult> Handle(
        GenerateBirdsReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GenerateBirdsReportResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GenerateBirdsReportResult.BreedingFarmNotSelected();
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
            return GenerateBirdsReportResult.BreedingFarmNotFound();
        }

        var breedingFarmName = await dbContext.BreedingFarms
            .AsNoTracking()
            .Where(farm => farm.Id == breedingFarmId)
            .Select(farm => farm.Name)
            .SingleOrDefaultAsync(cancellationToken);
        if (breedingFarmName is null)
        {
            return GenerateBirdsReportResult.BreedingFarmNotFound();
        }

        var tenantBirdReferences = await dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId)
            .Select(bird => new BirdReference(
                bird.Id,
                bird.FatherBirdId,
                bird.MotherBirdId))
            .ToArrayAsync(cancellationToken);
        var tenantBirdIds = tenantBirdReferences
            .Select(reference => reference.BirdId)
            .ToHashSet();

        var genealogyParentIds = tenantBirdReferences
            .SelectMany(reference => new[] { reference.FatherBirdId, reference.MotherBirdId })
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value);
        var snapshotParentIds = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(node =>
                node.BreedingFarmId == breedingFarmId &&
                !node.IsRoot &&
                node.LinkedBirdId != null)
            .Select(node => node.LinkedBirdId!.Value)
            .ToArrayAsync(cancellationToken);
        var reproductionParentIds = await dbContext.Reproductions
            .AsNoTracking()
            .Where(reproduction => reproduction.BreedingFarmId == breedingFarmId)
            .Select(reproduction => new ReproductionReference(
                reproduction.MaleBirdId,
                reproduction.FemaleBirdId))
            .ToArrayAsync(cancellationToken);

        var matrixBirdIds = genealogyParentIds
            .Concat(snapshotParentIds)
            .Concat(reproductionParentIds.SelectMany(reference => new[] { reference.MaleBirdId, reference.FemaleBirdId }))
            .Where(tenantBirdIds.Contains)
            .ToHashSet();

        var birds = dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId);
        if (query.Status is not null)
        {
            birds = birds.Where(bird => bird.Status == query.Status.Value);
        }

        if (query.Sex is not null)
        {
            birds = birds.Where(bird => bird.Sex == query.Sex.Value);
        }

        if (query.SpeciesId is not null)
        {
            birds = birds.Where(bird => bird.SpeciesId == query.SpeciesId.Value);
        }

        var rows = await birds
            .Join(
                dbContext.Species.AsNoTracking(),
                bird => bird.SpeciesId,
                species => species.Id,
                (bird, species) => new BirdReportProjection(
                    bird.Id,
                    bird.Name,
                    bird.RingNumber,
                    species.ScientificName,
                    species.PopularName,
                    bird.Sex,
                    bird.BirthDate,
                    bird.Status))
            .ToArrayAsync(cancellationToken);

        var groups = ReportClassifications
            .SelectMany(classification => ReportSexes.Select(sex => new BirdReportGroupResult(
                classification,
                sex,
                rows
                    .Where(row =>
                        matrixBirdIds.Contains(row.BirdId) == (classification == BirdReportClassification.Matrix) &&
                        row.Sex == sex)
                    .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.Name, StringComparer.Ordinal)
                    .ThenBy(row => row.BirdId)
                    .Select(row => new BirdReportItemResult(
                        row.BirdId,
                        row.Name,
                        row.RingNumber,
                        row.SpeciesScientificName,
                        row.SpeciesPopularName,
                        row.Sex,
                        row.BirthDate,
                        row.Status))
                    .ToArray())))
            .ToArray();

        return GenerateBirdsReportResult.Succeeded(
            new BirdsReportResult(
                breedingFarmId,
                breedingFarmName,
                DateTimeOffset.UtcNow,
                groups));
    }

    private sealed record BirdReference(Guid BirdId, Guid? FatherBirdId, Guid? MotherBirdId);

    private sealed record ReproductionReference(Guid MaleBirdId, Guid FemaleBirdId);

    private sealed record BirdReportProjection(
        Guid BirdId,
        string Name,
        string? RingNumber,
        string SpeciesScientificName,
        string SpeciesPopularName,
        BirdSex Sex,
        DateOnly? BirthDate,
        BirdStatus Status);
}
