using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

internal static class BreedingFarmVisualIdentityAccess
{
    public static async Task<BreedingFarmVisualIdentityFarmLookup> FindCurrentOwnerFarmAsync(
        CriatorioVirtualDbContext dbContext,
        Guid userId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return new(BreedingFarmVisualIdentityAccessStatus.UserNotFound, null);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new(BreedingFarmVisualIdentityAccessStatus.UserNotFound, null);
        }

        if (user.SelectedBreedingFarmId is not { } breedingFarmId)
        {
            return new(BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotSelected, null);
        }

        IQueryable<BreedingFarm> farms = tracking
            ? dbContext.BreedingFarms
            : dbContext.BreedingFarms.AsNoTracking();
        var farm = await farms.SingleOrDefaultAsync(
            candidate =>
                candidate.Id == breedingFarmId &&
                dbContext.BreedingFarmUsers.Any(membership =>
                    membership.BreedingFarmId == candidate.Id &&
                    membership.UserId == userId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner),
            cancellationToken);

        return farm is null
            ? new(BreedingFarmVisualIdentityAccessStatus.BreedingFarmNotFound, null)
            : new(BreedingFarmVisualIdentityAccessStatus.Success, farm);
    }
}

internal sealed record BreedingFarmVisualIdentityFarmLookup(
    BreedingFarmVisualIdentityAccessStatus Status,
    BreedingFarm? Farm);
