using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class GetBreedingFarmSettingsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<GetBreedingFarmSettingsQuery, BreedingFarmSettingsResult?>
{
    public async Task<BreedingFarmSettingsResult?> Handle(
        GetBreedingFarmSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var farm = await dbContext.BreedingFarms
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == query.BreedingFarmId &&
                    dbContext.BreedingFarmUsers.Any(membership =>
                        membership.BreedingFarmId == candidate.Id &&
                        membership.UserId == query.UserId &&
                        membership.Role == BreedingFarmRole.Owner &&
                        membership.IsActive),
                cancellationToken);

        return farm is null ? null : BreedingFarmSettingsMapping.ToResult(farm);
    }
}
