using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Application.Reproductions;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Reproductions;

public sealed class LinkReproductionOriginCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<LinkReproductionOriginCommand, LinkReproductionOriginResult>
{
    public async Task<LinkReproductionOriginResult> Handle(
        LinkReproductionOriginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Confirmed)
        {
            return LinkReproductionOriginResult.ConfirmationRequired();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return LinkReproductionOriginResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return LinkReproductionOriginResult.BreedingFarmNotSelected();
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
            return LinkReproductionOriginResult.BreedingFarmNotFound();
        }

        var reproduction = await dbContext.Reproductions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.ReproductionId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (reproduction is null)
        {
            return LinkReproductionOriginResult.ReproductionNotFound();
        }

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return LinkReproductionOriginResult.BirdNotFound();
        }

        if (bird.Id == reproduction.MaleBirdId || bird.Id == reproduction.FemaleBirdId)
        {
            return LinkReproductionOriginResult.SameBirdAsParent();
        }

        if (!BirdEligibility.Evaluate(bird.RingNumber, bird.Status).IsEligible)
        {
            return LinkReproductionOriginResult.BirdNotEligible();
        }

        var hasExistingOrigin = bird.FatherBirdId is not null ||
            bird.MotherBirdId is not null ||
            !string.IsNullOrWhiteSpace(bird.ExternalFatherName) ||
            !string.IsNullOrWhiteSpace(bird.ExternalMotherName);
        var isSameOrigin =
            bird.FatherBirdId == reproduction.MaleBirdId &&
            bird.MotherBirdId == reproduction.FemaleBirdId &&
            string.IsNullOrWhiteSpace(bird.ExternalFatherName) &&
            string.IsNullOrWhiteSpace(bird.ExternalMotherName);
        if (hasExistingOrigin && !isSameOrigin)
        {
            return LinkReproductionOriginResult.BirdAlreadyLinked();
        }

        var parentLinks = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate => candidate.BreedingFarmId == breedingFarmId)
            .Select(candidate => new BirdLinkProjection(
                candidate.Id,
                candidate.FatherBirdId,
                candidate.MotherBirdId))
            .ToDictionaryAsync(candidate => candidate.BirdId, cancellationToken);
        parentLinks[bird.Id] = new BirdLinkProjection(
            bird.Id,
            reproduction.MaleBirdId,
            reproduction.FemaleBirdId);

        if (CreatesCycle(bird.Id, reproduction.MaleBirdId, parentLinks) ||
            CreatesCycle(bird.Id, reproduction.FemaleBirdId, parentLinks))
        {
            return LinkReproductionOriginResult.CycleDetected();
        }

        var parentBirds = await dbContext.Birds
            .AsNoTracking()
            .Where(candidate =>
                candidate.BreedingFarmId == breedingFarmId &&
                (candidate.Id == reproduction.MaleBirdId || candidate.Id == reproduction.FemaleBirdId))
            .ToDictionaryAsync(candidate => candidate.Id, cancellationToken);
        if (!parentBirds.TryGetValue(reproduction.MaleBirdId, out var maleBird) ||
            !parentBirds.TryGetValue(reproduction.FemaleBirdId, out var femaleBird))
        {
            return LinkReproductionOriginResult.InvalidData();
        }

        var rootNode = await dbContext.GenealogyNodes
            .SingleOrDefaultAsync(
                candidate => candidate.BirdId == bird.Id && candidate.IsRoot,
                cancellationToken);
        if (rootNode is null)
        {
            rootNode = new GenealogyNode(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                breedingFarmId,
                bird.Id);
            dbContext.GenealogyNodes.Add(rootNode);
        }
        else
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM app.genealogy_nodes WHERE \"Id\" = {rootNode.Id} FOR UPDATE",
                cancellationToken);
        }

        var existingNodes = await dbContext.GenealogyNodes
            .Where(candidate => candidate.GenealogyRootId == rootNode.Id && !candidate.IsRoot)
            .ToArrayAsync(cancellationToken);
        if (isSameOrigin && HasExpectedNodes(existingNodes, reproduction.MaleBirdId, reproduction.FemaleBirdId))
        {
            return LinkReproductionOriginResult.Linked(ToResult(
                bird,
                rootNode,
                DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        var now = DateTimeOffset.UtcNow;
        if (!isSameOrigin)
        {
            try
            {
                bird.LinkReproductionOrigin(
                    reproduction.MaleBirdId,
                    reproduction.FemaleBirdId,
                    now);
            }
            catch (InvalidOperationException)
            {
                return LinkReproductionOriginResult.BirdAlreadyLinked();
            }
            catch (ArgumentException)
            {
                return LinkReproductionOriginResult.InvalidData();
            }
        }

        dbContext.GenealogyNodes.RemoveRange(existingNodes);
        dbContext.GenealogyNodes.Add(CreateSnapshotNode(
            rootNode,
            breedingFarmId,
            "father",
            maleBird,
            now));
        dbContext.GenealogyNodes.Add(CreateSnapshotNode(
            rootNode,
            breedingFarmId,
            "mother",
            femaleBird,
            now));

        return LinkReproductionOriginResult.Linked(ToResult(bird, rootNode, DateOnly.FromDateTime(now.UtcDateTime)));
    }

    private static GenealogyNode CreateSnapshotNode(
        GenealogyNode rootNode,
        Guid breedingFarmId,
        string position,
        Bird parent,
        DateTimeOffset createdAtUtc) =>
        new(
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
            parent.Status);

    private static bool HasExpectedNodes(
        IReadOnlyCollection<GenealogyNode> nodes,
        Guid maleBirdId,
        Guid femaleBirdId) =>
        nodes.Count(node =>
            node.Position == "father" &&
            node.LinkedBirdId == maleBirdId) == 1 &&
        nodes.Count(node =>
            node.Position == "mother" &&
            node.LinkedBirdId == femaleBirdId) == 1 &&
        nodes.All(node =>
            (node.Position == "father" && node.LinkedBirdId == maleBirdId) ||
            (node.Position == "mother" && node.LinkedBirdId == femaleBirdId));

    private static bool CreatesCycle(
        Guid birdId,
        Guid parentId,
        IReadOnlyDictionary<Guid, BirdLinkProjection> parentLinks)
    {
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(parentId);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == birdId)
            {
                return true;
            }

            if (!visited.Add(current) || !parentLinks.TryGetValue(current, out var links))
            {
                continue;
            }

            if (links.FatherBirdId is { } fatherBirdId)
            {
                pending.Push(fatherBirdId);
            }

            if (links.MotherBirdId is { } motherBirdId)
            {
                pending.Push(motherBirdId);
            }
        }

        return false;
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
            bird.UpdatedAtUtc);

    private sealed record BirdLinkProjection(
        Guid BirdId,
        Guid? FatherBirdId,
        Guid? MotherBirdId);
}
