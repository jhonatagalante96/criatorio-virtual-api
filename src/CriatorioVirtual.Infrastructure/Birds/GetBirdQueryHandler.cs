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
                    candidate.MotherBirdId,
                    candidate.ExternalMotherName,
                    candidate.Notes,
                    candidate.Status,
                    candidate.RingNumber == null,
                    candidate.CreatedAtUtc,
                    candidate.UpdatedAtUtc))
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
        var father = GetParent(bird.FatherBirdId, parentById);
        var mother = GetParent(bird.MotherBirdId, parentById);
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
                father is null ? null : ToResult(father),
                father is null ? bird.ExternalFatherName : null,
                mother?.BirdId,
                mother is null ? null : ToResult(mother),
                mother is null ? bird.ExternalMotherName : null,
                bird.Notes,
                bird.Status,
                bird.IdentificationPending,
                CalculateAgeInYears(bird.BirthDate, today),
                bird.CreatedAtUtc,
                bird.UpdatedAtUtc));
    }

    private static BirdParentResult ToResult(BirdParentProjection parent) =>
        new(
            parent.BirdId,
            parent.Name,
            parent.Sex,
            parent.BirthDate,
            parent.RingNumber,
            parent.Status);

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
        Guid? MotherBirdId,
        string? ExternalMotherName,
        string? Notes,
        BirdStatus Status,
        bool IdentificationPending,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record BirdParentProjection(
        Guid BirdId,
        string Name,
        BirdSex Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        BirdStatus Status);
}
