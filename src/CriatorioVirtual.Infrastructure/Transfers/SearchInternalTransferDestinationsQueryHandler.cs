using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class SearchInternalTransferDestinationsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<SearchInternalTransferDestinationsQuery, SearchInternalTransferDestinationsResult>
{
    public async Task<SearchInternalTransferDestinationsResult> Handle(
        SearchInternalTransferDestinationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return SearchInternalTransferDestinationsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return SearchInternalTransferDestinationsResult.BreedingFarmNotSelected();
        }

        var sourceBreedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == sourceBreedingFarmId &&
                    membership.UserId == query.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return SearchInternalTransferDestinationsResult.BreedingFarmNotFound();
        }

        var farms = dbContext.BreedingFarms
            .AsNoTracking()
            .Where(farm => farm.Id != sourceBreedingFarmId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchPattern = $"%{EscapeLikePattern(query.Search.Trim())}%";
            farms = farms.Where(farm =>
                EF.Functions.ILike(farm.Name, searchPattern, "\\") ||
                EF.Functions.ILike(farm.ResponsibleName, searchPattern, "\\"));
        }

        var totalCount = await farms.CountAsync(cancellationToken);
        var skip = (long)(query.Page - 1) * query.PageSize;
        InternalTransferDestinationProjection[] rows;
        if (skip > int.MaxValue)
        {
            rows = [];
        }
        else
        {
            rows = await farms
                .OrderBy(farm => farm.Name)
                .ThenBy(farm => farm.ResponsibleName)
                .ThenBy(farm => farm.Id)
                .Skip((int)skip)
                .Take(query.PageSize)
                .Select(farm => new InternalTransferDestinationProjection(
                    farm.Id,
                    farm.Name,
                    farm.ResponsibleName))
                .ToArrayAsync(cancellationToken);
        }

        return SearchInternalTransferDestinationsResult.Succeeded(
            sourceBreedingFarmId,
            rows.Select(row => new InternalTransferDestinationResult(
                row.BreedingFarmId,
                row.Name,
                row.ResponsibleName)).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record InternalTransferDestinationProjection(
        Guid BreedingFarmId,
        string Name,
        string ResponsibleName);
}
