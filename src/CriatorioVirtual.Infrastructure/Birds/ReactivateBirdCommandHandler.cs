using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class ReactivateBirdCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<ReactivateBirdCommand, ReactivateBirdResult>
{
    public async Task<ReactivateBirdResult> Handle(
        ReactivateBirdCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return ReactivateBirdResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ReactivateBirdResult.BreedingFarmNotSelected();
        }

        var breedingFarmId = user.SelectedBreedingFarmId.Value;
        var hasActiveOwnerMembership = await dbContext.BreedingFarmUsers
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.BreedingFarmId == breedingFarmId &&
                    membership.UserId == command.UserId &&
                    membership.IsActive &&
                    membership.Role == BreedingFarmRole.Owner,
                cancellationToken);
        if (!hasActiveOwnerMembership)
        {
            return ReactivateBirdResult.BreedingFarmNotFound();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return ReactivateBirdResult.BirdNotFound();
        }

        if (bird.Status == BirdStatus.Transferred)
        {
            return ReactivateBirdResult.TransferPending();
        }

        if (bird.Status != BirdStatus.Archived)
        {
            return ReactivateBirdResult.StatusChangeNotAllowed();
        }

        var genealogyRootId = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(node => node.BirdId == bird.Id && node.IsRoot)
            .Select(node => node.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (genealogyRootId == Guid.Empty)
        {
            return ReactivateBirdResult.BirdNotFound();
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            bird.Reactivate(now);
            dbContext.BirdStatusTransitions.Add(new BirdStatusTransition(
                Guid.NewGuid(),
                now,
                bird.Id,
                bird.BreedingFarmId,
                command.UserId,
                BirdStatus.Archived,
                BirdStatus.Active));

            // Save inside the command transaction so a concurrent reactivation can be
            // converted into a conflict without creating a duplicate transition.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return ReactivateBirdResult.StatusChangeNotAllowed();
        }
        catch (InvalidOperationException)
        {
            dbContext.ChangeTracker.Clear();
            return ReactivateBirdResult.StatusChangeNotAllowed();
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return ReactivateBirdResult.Updated(ToResult(bird, genealogyRootId, today));
    }

    private static BirdResult ToResult(Bird bird, Guid genealogyRootId, DateOnly today) =>
        new(
            bird.Id,
            genealogyRootId,
            bird.BreedingFarmId,
            bird.Name,
            bird.SpeciesId,
            bird.Sex,
            bird.BirthDate,
            bird.DeathDate,
            bird.RingNumber,
            bird.FatherBirdId,
            bird.ExternalFatherName,
            bird.ExternalFatherSex,
            bird.MotherBirdId,
            bird.ExternalMotherName,
            bird.ExternalMotherSex,
            bird.Notes,
            bird.Status,
            bird.IdentificationPending,
            bird.CalculateAgeInYears(today),
            bird.CreatedAtUtc,
            bird.UpdatedAtUtc,
            bird.PrimaryPhotoId,
            bird.DefaultImageFileName);
}
