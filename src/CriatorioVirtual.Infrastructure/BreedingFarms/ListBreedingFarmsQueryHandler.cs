using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class ListBreedingFarmsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListBreedingFarmsQuery, BreedingFarmSelectionResult>
{
    public async Task<BreedingFarmSelectionResult> Handle(
        ListBreedingFarmsQuery query,
        CancellationToken cancellationToken)
    {
        var selectedBreedingFarmId = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == query.UserId)
            .Select(user => user.SelectedBreedingFarmId)
            .SingleOrDefaultAsync(cancellationToken);

        var farmRows = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == query.UserId &&
                membership.IsActive &&
                membership.Role == BreedingFarmRole.Owner)
            .Join(
                dbContext.BreedingFarms.AsNoTracking(),
                membership => membership.BreedingFarmId,
                farm => farm.Id,
                (membership, farm) => new
                {
                    BreedingFarmId = farm.Id,
                    farm.Name,
                    farm.ResponsibleName,
                    membership.Role,
                    IsSelected = farm.Id == selectedBreedingFarmId
                })
            .OrderBy(farm => farm.Name)
            .ThenBy(farm => farm.BreedingFarmId)
            .ToArrayAsync(cancellationToken);

        var farms = farmRows
            .Select(farm => new BreedingFarmSummaryResult(
                farm.BreedingFarmId,
                farm.Name,
                farm.ResponsibleName,
                farm.Role,
                farm.IsSelected))
            .ToArray();

        if (selectedBreedingFarmId is not null &&
            !farms.Any(farm => farm.BreedingFarmId == selectedBreedingFarmId.Value))
        {
            selectedBreedingFarmId = null;
        }

        return new BreedingFarmSelectionResult(selectedBreedingFarmId, farms);
    }
}
