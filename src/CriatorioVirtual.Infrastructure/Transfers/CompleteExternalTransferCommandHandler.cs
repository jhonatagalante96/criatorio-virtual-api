using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Transfers;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Domain.Transfers;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Transfers;

public sealed class CompleteExternalTransferCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<CompleteExternalTransferCommand, CompleteExternalTransferResult>
{
    public async Task<CompleteExternalTransferResult> Handle(
        CompleteExternalTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var recipientName = Normalize(command.RecipientName);
        var notes = Normalize(command.Notes);
        if (command.BirdId == Guid.Empty ||
            string.IsNullOrWhiteSpace(recipientName) ||
            recipientName.Length > 200 ||
            notes?.Length > 2000)
        {
            return CompleteExternalTransferResult.InvalidData();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return CompleteExternalTransferResult.UserNotFound();
        }

        if (user.SelectedBreedingFarmId is null)
        {
            return CompleteExternalTransferResult.BreedingFarmNotSelected();
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
            return CompleteExternalTransferResult.BreedingFarmNotFound();
        }

        if (!command.Confirmed)
        {
            return CompleteExternalTransferResult.ConfirmationRequired();
        }

        // External completion and internal transfer creation must serialize on the bird row.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {command.BirdId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);

        var bird = await dbContext.Birds
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BirdId &&
                    candidate.BreedingFarmId == breedingFarmId,
                cancellationToken);
        if (bird is null)
        {
            return CompleteExternalTransferResult.BirdNotFound();
        }

        if (await dbContext.ExternalTransfers
                .AsNoTracking()
                .AnyAsync(
                    transfer =>
                        transfer.BirdId == bird.Id &&
                        transfer.BreedingFarmId == breedingFarmId,
                    cancellationToken))
        {
            return CompleteExternalTransferResult.AlreadyCompleted();
        }

        if (await dbContext.InternalTransferRequests
                .AsNoTracking()
                .AnyAsync(
                    transfer =>
                        transfer.BirdId == bird.Id &&
                        transfer.Status == InternalTransferRequestStatus.Pending,
                    cancellationToken))
        {
            return CompleteExternalTransferResult.InternalTransferPending();
        }

        if (!BirdEligibility.Evaluate(bird.RingNumber, bird.Status).IsEligible)
        {
            return CompleteExternalTransferResult.BirdNotEligible();
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            bird.CompleteExternalTransfer(now);
            var transfer = new ExternalTransfer(
                Guid.NewGuid(),
                now,
                breedingFarmId,
                bird.Id,
                recipientName!,
                notes);
            dbContext.ExternalTransfers.Add(transfer);

            return CompleteExternalTransferResult.Completed(ToResult(transfer));
        }
        catch (ArgumentException)
        {
            return CompleteExternalTransferResult.InvalidData();
        }
        catch (InvalidOperationException)
        {
            return CompleteExternalTransferResult.BirdNotEligible();
        }
    }

    private static ExternalTransferResult ToResult(ExternalTransfer transfer) =>
        new(
            transfer.Id,
            transfer.BirdId,
            transfer.BreedingFarmId,
            transfer.RecipientName,
            transfer.Notes,
            BirdStatus.Transferred.ToString(),
            transfer.CreatedAtUtc);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
