using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class UpdateBirdCommandHandler(CriatorioVirtualDbContext dbContext)
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

        if (bird.Status == BirdStatus.Transferred)
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

        if (command.SpeciesId is null ||
            command.SpeciesId == Guid.Empty ||
            !await dbContext.Species.AnyAsync(
                species => species.Id == command.SpeciesId.Value && species.IsActive,
                cancellationToken))
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
                command.SpeciesId.Value,
                command.Sex!.Value,
                command.BirthDate,
                normalizedRingNumber,
                command.Notes,
                today,
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
            bird.UpdatedAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
