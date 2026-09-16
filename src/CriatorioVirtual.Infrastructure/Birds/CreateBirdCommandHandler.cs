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

        var species = command.SpeciesId is null
            ? null
            : await dbContext.Species
                .AsNoTracking()
                .Where(candidate => candidate.Id == command.SpeciesId.Value && candidate.IsActive)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.DefaultImageFileName,
                    candidate.DefaultImageContentType
                })
                .SingleOrDefaultAsync(cancellationToken);
        if (species is null)
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
        var birdId = Guid.NewGuid();
        var rootNode = new GenealogyNode(
            Guid.NewGuid(),
            now,
            breedingFarmId,
            birdId);
        var bird = new Bird(
            birdId,
            now,
            breedingFarmId,
            command.Name!,
            species.Id,
            command.Sex!.Value,
            command.BirthDate,
            normalizedRingNumber,
            command.FatherBirdId,
            command.ExternalFatherName,
            command.MotherBirdId,
            command.ExternalMotherName,
            command.Notes,
            DateOnly.FromDateTime(now.UtcDateTime),
            null,
            command.ExternalFatherSex,
            command.ExternalMotherSex,
            species.DefaultImageFileName,
            species.DefaultImageContentType);

        dbContext.Birds.Add(bird);
        dbContext.GenealogyNodes.Add(rootNode);
        await AddParentNodeAsync(
            dbContext,
            command.UserId,
            rootNode,
            breedingFarmId,
            birdId,
            "father",
            command.FatherBirdId,
            parents,
            now,
            cancellationToken);
        await AddParentNodeAsync(
            dbContext,
            command.UserId,
            rootNode,
            breedingFarmId,
            birdId,
            "mother",
            command.MotherBirdId,
            parents,
            now,
            cancellationToken);
        AddExternalParentNode(
            dbContext,
            rootNode,
            breedingFarmId,
            birdId,
            ExternalGenealogyParentLink.FatherPosition,
            command.ExternalFatherName,
            command.ExternalFatherSex,
            now);
        AddExternalParentNode(
            dbContext,
            rootNode,
            breedingFarmId,
            birdId,
            ExternalGenealogyParentLink.MotherPosition,
            command.ExternalMotherName,
            command.ExternalMotherSex,
            now);
        return CreateBirdResult.Created(ToResult(bird, rootNode, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    private static async Task AddParentNodeAsync(
        CriatorioVirtualDbContext dbContext,
        Guid userId,
        GenealogyNode rootNode,
        Guid breedingFarmId,
        Guid childBirdId,
        string position,
        Guid? parentId,
        IReadOnlyCollection<Bird> parents,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        if (parentId is not { } selectedParentId)
        {
            return;
        }

        var parent = parents.Single(candidate => candidate.Id == selectedParentId);
        dbContext.GenealogyNodes.Add(new GenealogyNode(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            rootNode.Id,
            position,
            parent.Id,
            parent.Name,
            parent.Sex,
            parent.BirthDate,
            parent.RingNumber,
            parent.Status));
        await BirdGenealogySnapshotMaterializer.AddLinkedParentAsync(
            dbContext,
            userId,
            breedingFarmId,
            rootNode.Id,
            childBirdId,
            null,
            parent,
            position,
            createdAtUtc,
            cancellationToken);
    }

    private static void AddExternalParentNode(
        CriatorioVirtualDbContext dbContext,
        GenealogyNode rootNode,
        Guid breedingFarmId,
        Guid childBirdId,
        string position,
        string? name,
        BirdSex? sex,
        DateTimeOffset createdAtUtc)
    {
        var normalizedName = Normalize(name);
        if (normalizedName is null)
        {
            return;
        }

        var externalNode = new ExternalGenealogyNode(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            rootNode.Id,
            normalizedName,
            sex!.Value);
        dbContext.ExternalGenealogyNodes.Add(externalNode);
        dbContext.ExternalGenealogyParentLinks.Add(new ExternalGenealogyParentLink(
            Guid.NewGuid(),
            createdAtUtc,
            breedingFarmId,
            rootNode.Id,
            childBirdId,
            null,
            position,
            null,
            externalNode.Id,
            null,
            null,
            null,
            null,
            null,
            null));
    }

    private static BirdResult ToResult(Bird bird, GenealogyNode rootNode, DateOnly today) =>
        new(
            bird.Id,
            rootNode.Id,
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
