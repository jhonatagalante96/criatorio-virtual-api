using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class ChangeBirdStatusCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBirdLockCoordinator birdLockCoordinator)
    : ICommandHandler<ChangeBirdStatusCommand, ChangeBirdStatusResult>
{
    public async Task<ChangeBirdStatusResult> Handle(
        ChangeBirdStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return ChangeBirdStatusResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return ChangeBirdStatusResult.BreedingFarmNotSelected();
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
            return ChangeBirdStatusResult.BreedingFarmNotFound();
        }

        await birdLockCoordinator.AcquireLockAsync(command.BirdId, breedingFarmId, cancellationToken);

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return ChangeBirdStatusResult.BirdNotFound();
        }

        var genealogyRootId = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(node => node.BirdId == bird.Id && node.IsRoot)
            .Select(node => node.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (genealogyRootId == Guid.Empty)
        {
            return ChangeBirdStatusResult.BirdNotFound();
        }

        if (!command.Confirmed)
        {
            return ChangeBirdStatusResult.ConfirmationRequired();
        }

        if (command.Status is not (BirdStatus.Archived or BirdStatus.Deceased or BirdStatus.Escaped))
        {
            return ChangeBirdStatusResult.InvalidStatus();
        }

        if (bird.Status == BirdStatus.Transferred ||
            await dbContext.InternalTransferRequests
                .AsNoTracking()
                .AnyAsync(
                    transferRequest =>
                        transferRequest.BirdId == bird.Id &&
                        transferRequest.Status == InternalTransferRequestStatus.Pending,
                    cancellationToken))
        {
            return ChangeBirdStatusResult.TransferPending();
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var previousStatus = bird.Status;
        try
        {
            bird.ChangeStatus(
                command.Status,
                command.DeathDate,
                command.Notes,
                today,
                now);
        }
        catch (InvalidOperationException)
        {
            return ChangeBirdStatusResult.StatusChangeNotAllowed();
        }
        catch (ArgumentException)
        {
            return ChangeBirdStatusResult.InvalidData();
        }

        dbContext.BirdStatusTransitions.Add(new BirdStatusTransition(
            Guid.NewGuid(),
            now,
            bird.Id,
            bird.BreedingFarmId,
            command.UserId,
            previousStatus,
            bird.Status));

        return ChangeBirdStatusResult.Updated(ToResult(bird, genealogyRootId, today));
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
