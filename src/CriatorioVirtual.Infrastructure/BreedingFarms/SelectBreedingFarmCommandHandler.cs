using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class SelectBreedingFarmCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IQueryHandler<ListBreedingFarmsQuery, BreedingFarmSelectionResult> listBreedingFarms)
    : ICommandHandler<SelectBreedingFarmCommand, SelectBreedingFarmResult>
{
    public async Task<SelectBreedingFarmResult> Handle(
        SelectBreedingFarmCommand command,
        CancellationToken cancellationToken)
    {
        var hasActiveMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == command.BreedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveMembership)
        {
            return SelectBreedingFarmResult.NotFound();
        }

        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return SelectBreedingFarmResult.NotFound();
        }

        user.SelectedBreedingFarmId = command.BreedingFarmId;

        var selection = await listBreedingFarms.Handle(
            new ListBreedingFarmsQuery(command.UserId),
            cancellationToken);
        return SelectBreedingFarmResult.Selected(
            new BreedingFarmSelectionResult(
                command.BreedingFarmId,
                selection.BreedingFarms
                    .Select(farm => farm with
                    {
                        IsSelected = farm.BreedingFarmId == command.BreedingFarmId
                    })
                    .ToArray()));
    }
}
