using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class GetBirdQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBirdQuery, GetBirdResult>
{
    public async Task<GetBirdResult> Handle(
        GetBirdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdResult.BreedingFarmNotSelected();
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
            return GetBirdResult.BreedingFarmNotFound();
        }

        var bird = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == query.BirdId &&
                candidate.BreedingFarmId == breedingFarmId)
            .Join(
                dbContext.Species.AsNoTracking(),
                candidate => candidate.SpeciesId,
                species => species.Id,
                (candidate, species) => new BirdDetailProjection(
                    candidate.Id,
                    dbContext.GenealogyNodes
                        .Where(node => node.BirdId == candidate.Id && node.IsRoot)
                        .Select(node => (Guid?)node.Id)
                        .FirstOrDefault(),
                    candidate.BreedingFarmId,
                    candidate.Name,
                    candidate.SpeciesId,
                    species.ScientificName,
                    species.PopularName,
                    candidate.Sex,
                    candidate.BirthDate,
                    candidate.DeathDate,
                    candidate.RingNumber,
                    candidate.FatherBirdId,
                    candidate.ExternalFatherName,
                    candidate.ExternalFatherSex,
                    candidate.MotherBirdId,
                    candidate.ExternalMotherName,
                    candidate.ExternalMotherSex,
                    candidate.Notes,
                    candidate.Status,
                    candidate.RingNumber == null,
                    candidate.CreatedAtUtc,
                    candidate.UpdatedAtUtc,
                    candidate.PrimaryPhotoId,
                    candidate.DefaultImageFileName))
            .SingleOrDefaultAsync(cancellationToken);
        if (bird is null)
        {
            return GetBirdResult.BirdNotFound();
        }

        var parentIds = new[] { bird.FatherBirdId, bird.MotherBirdId }
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value)
            .Distinct()
            .ToArray();
        var parents = parentIds.Length == 0
            ? []
            : await dbContext.Birds
                .AsNoTracking()
                .Where(parent =>
                    parent.BreedingFarmId == breedingFarmId &&
                    parentIds.Contains(parent.Id))
                .Select(parent => new BirdParentProjection(
                    parent.Id,
                    parent.Name,
                    parent.Sex,
                    parent.BirthDate,
                    parent.RingNumber,
                    parent.Status))
                .ToArrayAsync(cancellationToken);
        var parentById = parents.ToDictionary(parent => parent.BirdId);
        var snapshots = bird.GenealogyRootId is null
            ? []
            : await dbContext.GenealogyNodes
                .AsNoTracking()
                .Where(node =>
                    node.GenealogyRootId == bird.GenealogyRootId.Value &&
                    !node.IsRoot &&
                    (node.Position == "father" || node.Position == "mother"))
                .Select(node => new BirdParentSnapshotProjection(
                    node.Position,
                    node.LinkedBirdId,
                    node.SnapshotName,
                    node.SnapshotSex,
                    node.SnapshotBirthDate,
                    node.SnapshotRingNumber,
                    node.SnapshotStatus))
                .ToArrayAsync(cancellationToken);
        var snapshotByPosition = snapshots.ToDictionary(snapshot => snapshot.Position);
        var father = GetSnapshot("father", snapshotByPosition) ?? ToResultOrNull(GetParent(bird.FatherBirdId, parentById));
        var mother = GetSnapshot("mother", snapshotByPosition) ?? ToResultOrNull(GetParent(bird.MotherBirdId, parentById));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return GetBirdResult.Succeeded(
            new BirdDetailsResult(
                bird.BirdId,
                bird.GenealogyRootId,
                bird.BreedingFarmId,
                bird.Name,
                bird.SpeciesId,
                bird.SpeciesScientificName,
                bird.SpeciesPopularName,
                bird.Sex,
                bird.BirthDate,
                bird.DeathDate,
                bird.RingNumber,
                father?.BirdId,
                father,
                father is null ? bird.ExternalFatherName : null,
                father is null ? bird.ExternalFatherSex : null,
                mother?.BirdId,
                mother,
                mother is null ? bird.ExternalMotherName : null,
                mother is null ? bird.ExternalMotherSex : null,
                bird.Notes,
                bird.Status,
                bird.IdentificationPending,
                CalculateAgeInYears(bird.BirthDate, today),
                bird.CreatedAtUtc,
                bird.UpdatedAtUtc,
                bird.PrimaryPhotoId,
                bird.DefaultImageFileName));
    }

    private static BirdParentResult ToResult(BirdParentProjection parent) =>
        new(
            parent.BirdId,
            parent.Name,
            parent.Sex,
            parent.BirthDate,
            parent.RingNumber,
            parent.Status);

    private static BirdParentResult? ToResultOrNull(BirdParentProjection? parent) =>
        parent is null ? null : ToResult(parent);

    private static BirdParentResult? GetSnapshot(
        string position,
        IReadOnlyDictionary<string, BirdParentSnapshotProjection> snapshots) =>
        snapshots.TryGetValue(position, out var snapshot) &&
        snapshot.LinkedBirdId is { } linkedBirdId &&
        snapshot.Name is not null &&
        snapshot.Sex is { } sex &&
        snapshot.Status is { } status
            ? new BirdParentResult(
                linkedBirdId,
                snapshot.Name,
                sex,
                snapshot.BirthDate,
                snapshot.RingNumber,
                status)
            : null;

    private static BirdParentProjection? GetParent(
        Guid? parentId,
        IReadOnlyDictionary<Guid, BirdParentProjection> parents) =>
        parentId is { } id && parents.TryGetValue(id, out var parent)
            ? parent
            : null;

    private static int? CalculateAgeInYears(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is null)
        {
            return null;
        }

        var age = today.Year - birthDate.Value.Year;
        if (birthDate.Value.AddYears(age) > today)
        {
            age--;
        }

        return age;
    }

    private sealed record BirdDetailProjection(
        Guid BirdId,
        Guid? GenealogyRootId,
        Guid BreedingFarmId,
        string Name,
        Guid SpeciesId,
        string SpeciesScientificName,
        string SpeciesPopularName,
        BirdSex Sex,
        DateOnly? BirthDate,
        DateOnly? DeathDate,
        string? RingNumber,
        Guid? FatherBirdId,
        string? ExternalFatherName,
        BirdSex? ExternalFatherSex,
        Guid? MotherBirdId,
        string? ExternalMotherName,
        BirdSex? ExternalMotherSex,
        string? Notes,
        BirdStatus Status,
        bool IdentificationPending,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        Guid? PrimaryPhotoId,
        string? DefaultImageFileName);

    private sealed record BirdParentProjection(
        Guid BirdId,
        string Name,
        BirdSex Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        BirdStatus Status);

    private sealed record BirdParentSnapshotProjection(
        string Position,
        Guid? LinkedBirdId,
        string? Name,
        BirdSex? Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        BirdStatus? Status);
}
