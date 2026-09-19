using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Birds;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UpdateBirdCommandHandler(
    CriatorioVirtualDbContext dbContext,
    IBirdLockCoordinator birdLockCoordinator)
    : ICommandHandler<UpdateBirdCommand, UpdateBirdResult>
{
    public async Task<UpdateBirdResult> Handle(
        UpdateBirdCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return UpdateBirdResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return UpdateBirdResult.BreedingFarmNotSelected();
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
            return UpdateBirdResult.BreedingFarmNotFound();
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
            return UpdateBirdResult.BirdNotFound();
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
            return UpdateBirdResult.TransferPending();
        }

        var genealogyRootId = await dbContext.GenealogyNodes
            .AsNoTracking()
            .Where(node => node.BirdId == bird.Id && node.IsRoot)
            .Select(node => node.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (genealogyRootId == Guid.Empty)
        {
            return UpdateBirdResult.BirdNotFound();
        }

        if (string.IsNullOrWhiteSpace(command.Name) ||
            command.Sex is null ||
            !Enum.IsDefined(command.Sex.Value))
        {
            return UpdateBirdResult.InvalidData();
        }

        var species = command.SpeciesId is null || command.SpeciesId == Guid.Empty
            ? null
            : await dbContext.Species
                .AsNoTracking()
                .Where(candidate => candidate.Id == command.SpeciesId.Value && candidate.IsActive)
                .Select(candidate => new
                {
                    candidate.DefaultImageFileName,
                    candidate.DefaultImageContentType
                })
                .SingleOrDefaultAsync(cancellationToken);
        if (species is null)
        {
            return UpdateBirdResult.SpeciesNotFound();
        }

        var normalizedRingNumber = Normalize(command.RingNumber);
        if (normalizedRingNumber is not null &&
            !string.Equals(normalizedRingNumber, bird.RingNumber, StringComparison.Ordinal) &&
            await dbContext.Birds.AnyAsync(
                candidate => candidate.RingNumber == normalizedRingNumber,
                cancellationToken))
        {
            return UpdateBirdResult.DuplicateRingNumber();
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        try
        {
            bird.UpdateDetails(
                command.Name!,
                command.SpeciesId!.Value,
                command.Sex!.Value,
                command.BirthDate,
                normalizedRingNumber,
                command.Notes,
                today,
                now);
            bird.SetDefaultImage(
                species.DefaultImageFileName,
                species.DefaultImageContentType,
                now);
        }
        catch (ArgumentException)
        {
            return UpdateBirdResult.InvalidData();
        }

        return UpdateBirdResult.Updated(ToResult(bird, genealogyRootId, today));
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

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
