using CriatorioVirtual.Application.Documents;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Documents;

public sealed class GetBirdDocumentContentQueryHandler(
    CriatorioVirtualDbContext dbContext,
    IPrivateObjectStorage storage)
    : IQueryHandler<GetBirdDocumentContentQuery, GetBirdDocumentContentResult>
{
    public async Task<GetBirdDocumentContentResult> Handle(
        GetBirdDocumentContentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.UserId, cancellationToken);
        if (user is null)
        {
            return GetBirdDocumentContentResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return GetBirdDocumentContentResult.BreedingFarmNotSelected();
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
            return GetBirdDocumentContentResult.BreedingFarmNotFound();
        }

        var birdExists = await dbContext.Birds
            .AsNoTracking()
            .AnyAsync(
                bird => bird.Id == query.BirdId && bird.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (!birdExists)
        {
            return GetBirdDocumentContentResult.BirdNotFound();
        }

        var document = await dbContext.BirdDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == query.DocumentId &&
                    candidate.BirdId == query.BirdId,
                cancellationToken);
        if (document is null)
        {
            return GetBirdDocumentContentResult.DocumentNotFound();
        }

        try
        {
            var content = await storage.OpenReadAsync(
                document.CreatedByBreedingFarmId,
                document.ObjectKey,
                cancellationToken);
            return GetBirdDocumentContentResult.Succeeded(
                new BirdDocumentContent(
                    document.Id,
                    document.FileName,
                    document.ContentType,
                    document.Length,
                    content));
        }
        catch (FileNotFoundException)
        {
            return GetBirdDocumentContentResult.StorageUnavailable();
        }
        catch (DirectoryNotFoundException)
        {
            return GetBirdDocumentContentResult.StorageUnavailable();
        }
        catch (IOException)
        {
            return GetBirdDocumentContentResult.StorageUnavailable();
        }
        catch (UnauthorizedAccessException)
        {
            return GetBirdDocumentContentResult.StorageUnavailable();
        }
    }
}
