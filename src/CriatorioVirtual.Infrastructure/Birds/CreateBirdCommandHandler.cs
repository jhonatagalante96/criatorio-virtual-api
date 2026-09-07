using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public sealed class CreateBirdCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CreateBirdCommand, CreateBirdResult>
{
    public async Task<CreateBirdResult> Handle(
        CreateBirdCommand command,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CreateBirdResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return CreateBirdResult.BreedingFarmNotSelected();
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
            return CreateBirdResult.BreedingFarmNotFound();
        }

        if (command.SpeciesId is null ||
            !await dbContext.Species.AnyAsync(
                species => species.Id == command.SpeciesId.Value && species.IsActive,
                cancellationToken))
        {
            return CreateBirdResult.SpeciesNotFound();
        }

        var normalizedRingNumber = Normalize(command.RingNumber);
        if (normalizedRingNumber is not null &&
            await dbContext.Birds.AnyAsync(
                bird => bird.RingNumber == normalizedRingNumber,
                cancellationToken))
        {
            return CreateBirdResult.DuplicateRingNumber();
        }

        if (command.FatherBirdId is not null && command.FatherBirdId == command.MotherBirdId)
        {
            return CreateBirdResult.DuplicateParent();
        }

        var parentIds = new[] { command.FatherBirdId, command.MotherBirdId }
            .Where(parentId => parentId is not null)
            .Select(parentId => parentId!.Value)
            .ToArray();
        var parents = parentIds.Length == 0
            ? []
            : await dbContext.Birds
                .AsNoTracking()
                .Where(bird => bird.BreedingFarmId == breedingFarmId && parentIds.Contains(bird.Id))
                .ToArrayAsync(cancellationToken);

        if (parents.Length != parentIds.Length)
        {
            return CreateBirdResult.ParentNotFound();
        }

        if (command.FatherBirdId is not null &&
            parents.Single(parent => parent.Id == command.FatherBirdId.Value).Sex != BirdSex.Male)
        {
            return CreateBirdResult.ParentSexInvalid();
        }

        if (command.MotherBirdId is not null &&
            parents.Single(parent => parent.Id == command.MotherBirdId.Value).Sex != BirdSex.Female)
        {
            return CreateBirdResult.ParentSexInvalid();
        }

        var now = DateTimeOffset.UtcNow;
        var bird = new Bird(
            Guid.NewGuid(),
            now,
            breedingFarmId,
            command.Name!,
            command.SpeciesId.Value,
            command.Sex!.Value,
            command.BirthDate,
            normalizedRingNumber,
            command.FatherBirdId,
            command.ExternalFatherName,
            command.MotherBirdId,
            command.ExternalMotherName,
            command.Notes,
            DateOnly.FromDateTime(now.UtcDateTime));

        dbContext.Birds.Add(bird);
        return CreateBirdResult.Created(ToResult(bird, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    private static BirdResult ToResult(Bird bird, DateOnly today) =>
        new(
            bird.Id,
            bird.BreedingFarmId,
            bird.Name,
            bird.SpeciesId,
            bird.Sex,
            bird.BirthDate,
            bird.RingNumber,
            bird.FatherBirdId,
            bird.ExternalFatherName,
            bird.MotherBirdId,
            bird.ExternalMotherName,
            bird.Notes,
            bird.Status,
            bird.IdentificationPending,
            bird.CalculateAgeInYears(today),
            bird.CreatedAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
