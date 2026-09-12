using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class ListBirdAttachmentsQueryHandler(CriatorioVirtualDbContext dbContext)
    : IQueryHandler<ListBirdAttachmentsQuery, ListBirdAttachmentsResult>
{
    public async Task<ListBirdAttachmentsResult> Handle(
        ListBirdAttachmentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return ListBirdAttachmentsResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ListBirdAttachmentsResult.BreedingFarmNotSelected();
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
            return ListBirdAttachmentsResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return ListBirdAttachmentsResult.BirdNotFound();
        }

        var attachments = await dbContext.BirdAttachments
            .AsNoTracking()
            .Where(attachment =>
                attachment.BreedingFarmId == breedingFarmId &&
                attachment.BirdId == query.BirdId)
            .OrderByDescending(attachment => attachment.CreatedAtUtc)
            .ThenByDescending(attachment => attachment.Id)
            .Select(attachment => new BirdAttachmentResult(
                attachment.Id,
                attachment.BirdId,
                attachment.FileName,
                attachment.ContentType,
                attachment.Length,
                attachment.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return ListBirdAttachmentsResult.Succeeded(breedingFarmId, query.BirdId, attachments);
    }
}
