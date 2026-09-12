using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Transfers;

public sealed record CompleteExternalTransferCommand(
    Guid UserId,
    Guid BirdId,
    string? RecipientName,
    string? Notes,
    bool Confirmed) : ICommand<CompleteExternalTransferResult>;

public enum CompleteExternalTransferStatus
{
    Completed,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    ConfirmationRequired,
    BirdNotEligible,
    InternalTransferPending,
    AlreadyCompleted,
    InvalidData
}

public sealed record CompleteExternalTransferResult(
    CompleteExternalTransferStatus Status,
    ExternalTransferResult? Transfer)
{
    public static CompleteExternalTransferResult Completed(ExternalTransferResult transfer) =>
        new(CompleteExternalTransferStatus.Completed, transfer);

    public static CompleteExternalTransferResult UserNotFound() =>
        new(CompleteExternalTransferStatus.UserNotFound, null);

    public static CompleteExternalTransferResult BreedingFarmNotSelected() =>
        new(CompleteExternalTransferStatus.BreedingFarmNotSelected, null);

    public static CompleteExternalTransferResult BreedingFarmNotFound() =>
        new(CompleteExternalTransferStatus.BreedingFarmNotFound, null);

    public static CompleteExternalTransferResult BirdNotFound() =>
        new(CompleteExternalTransferStatus.BirdNotFound, null);

    public static CompleteExternalTransferResult ConfirmationRequired() =>
        new(CompleteExternalTransferStatus.ConfirmationRequired, null);

    public static CompleteExternalTransferResult BirdNotEligible() =>
        new(CompleteExternalTransferStatus.BirdNotEligible, null);

    public static CompleteExternalTransferResult InternalTransferPending() =>
        new(CompleteExternalTransferStatus.InternalTransferPending, null);

    public static CompleteExternalTransferResult AlreadyCompleted() =>
        new(CompleteExternalTransferStatus.AlreadyCompleted, null);

    public static CompleteExternalTransferResult InvalidData() =>
        new(CompleteExternalTransferStatus.InvalidData, null);
}

public sealed record ExternalTransferResult(
    Guid ExternalTransferId,
    Guid BirdId,
    Guid BreedingFarmId,
    string RecipientName,
    string? Notes,
    string BirdStatus,
    DateTimeOffset CompletedAtUtc);
