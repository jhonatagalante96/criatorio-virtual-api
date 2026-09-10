using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class ListBirdsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListBirdsQuery, ListBirdsResult>
{
    public async Task<ListBirdsResult> Handle(
        ListBirdsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListBirdsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListBirdsResult.BreedingFarmNotSelected();
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
            return ListBirdsResult.BreedingFarmNotFound();
        }

        var birds = dbContext.Birds
            .AsNoTracking()
            .Where(bird => bird.BreedingFarmId == breedingFarmId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            birds = birds.Where(bird =>
                EF.Functions.ILike(bird.Name, searchPattern, "\\") ||
                (bird.RingNumber != null && EF.Functions.ILike(bird.RingNumber, searchPattern, "\\")) ||
                dbContext.Species.Any(species =>
                    species.Id == bird.SpeciesId &&
                    (EF.Functions.ILike(species.PopularName, searchPattern, "\\") ||
                     EF.Functions.ILike(species.ScientificName, searchPattern, "\\"))));
        }

        if (query.Sex is not null)
        {
            birds = birds.Where(bird => bird.Sex == query.Sex.Value);
        }

        if (query.SpeciesId is not null)
        {
            birds = birds.Where(bird => bird.SpeciesId == query.SpeciesId.Value);
        }

        if (query.Status is not null)
        {
            birds = birds.Where(bird => bird.Status == query.Status.Value);
        }

        if (query.IdentificationPending is not null)
        {
            birds = birds.Where(bird => (bird.RingNumber == null) == query.IdentificationPending.Value);
        }

        var totalCount = await birds.CountAsync(cancellationToken);
        var orderedBirds = ApplyOrdering(birds, query.SortBy, query.SortDirection, dbContext);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var skip = (long)(query.Page - 1) * query.PageSize;
        BirdListProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await orderedBirds
                .Skip((int)skip)
                .Take(query.PageSize)
                .Join(
                    dbContext.Species.AsNoTracking(),
                    bird => bird.SpeciesId,
                    species => species.Id,
                    (bird, species) => new BirdListProjection(
                        bird.Id,
                        bird.Name,
                        bird.SpeciesId,
                        species.ScientificName,
                        species.PopularName,
                        bird.Sex,
                        bird.BirthDate,
                        bird.RingNumber,
                        bird.Status,
                        bird.RingNumber == null,
                        bird.CreatedAtUtc))
                .ToArrayAsync(cancellationToken);
        }

        var items = rows
            .Select(row => new BirdListItemResult(
                row.BirdId,
                row.Name,
                row.SpeciesId,
                row.SpeciesScientificName,
                row.SpeciesPopularName,
                row.Sex,
                row.BirthDate,
                row.RingNumber,
                row.Status,
                row.IdentificationPending,
                CalculateAgeInYears(row.BirthDate, today),
                row.CreatedAtUtc))
            .ToArray();

        return ListBirdsResult.Succeeded(
            breedingFarmId,
            items,
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static IQueryable<Bird> ApplyOrdering(
        IQueryable<Bird> birds,
        BirdSortField sortField,
        BirdSortDirection sortDirection,
        CriatorioVirtualDbContext dbContext)
    {
        if (sortField == BirdSortField.Species)
        {
            return sortDirection == BirdSortDirection.Descending
                ? birds.OrderByDescending(bird => dbContext.Species
                    .Where(species => species.Id == bird.SpeciesId)
                    .Select(species => species.PopularName)
                    .First())
                    .ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => dbContext.Species
                    .Where(species => species.Id == bird.SpeciesId)
                    .Select(species => species.PopularName)
                    .First())
                    .ThenBy(bird => bird.Id);
        }

        var descending = sortDirection == BirdSortDirection.Descending;
        return sortField switch
        {
            BirdSortField.Name => descending
                ? birds.OrderByDescending(bird => bird.Name).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.Name).ThenBy(bird => bird.Id),
            BirdSortField.RingNumber => descending
                ? birds.OrderByDescending(bird => bird.RingNumber).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.RingNumber).ThenBy(bird => bird.Id),
            BirdSortField.BirthDate => descending
                ? birds.OrderByDescending(bird => bird.BirthDate).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.BirthDate).ThenBy(bird => bird.Id),
            BirdSortField.Sex => descending
                ? birds.OrderByDescending(bird => bird.Sex).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.Sex).ThenBy(bird => bird.Id),
            BirdSortField.Status => descending
                ? birds.OrderByDescending(bird => bird.Status).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.Status).ThenBy(bird => bird.Id),
            BirdSortField.CreatedAt => descending
                ? birds.OrderByDescending(bird => bird.CreatedAtUtc).ThenBy(bird => bird.Id)
                : birds.OrderBy(bird => bird.CreatedAtUtc).ThenBy(bird => bird.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sortField), sortField, "The bird sort field is not supported.")
        };
    }

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

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record BirdListProjection(
        Guid BirdId,
        string Name,
        Guid SpeciesId,
        string SpeciesScientificName,
        string SpeciesPopularName,
        BirdSex Sex,
        DateOnly? BirthDate,
        string? RingNumber,
        BirdStatus Status,
        bool IdentificationPending,
        DateTimeOffset CreatedAtUtc);
}
