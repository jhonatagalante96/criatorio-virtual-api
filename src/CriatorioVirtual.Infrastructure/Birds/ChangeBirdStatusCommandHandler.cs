using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class ChangeBirdStatusCommandHandler(CriatorioVirtualDbContext dbContext)
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

        if (bird.Status == BirdStatus.Transferred)
        {
            return ChangeBirdStatusResult.TransferPending();
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
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
