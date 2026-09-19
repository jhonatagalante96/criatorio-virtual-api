using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Birds;
using CriatorioVirtual.Domain.Reproductions;

namespace CriatorioVirtual.Application.Reproductions;

public sealed record CreateReproductionCommand(
    Guid UserId,
    Guid MaleBirdId,
    Guid FemaleBirdId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes) : ICommand<CreateReproductionResult>;

public enum CreateReproductionStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    BirdNotEligible,
    TransferPending,
    MaleBirdSexInvalid,
    FemaleBirdSexInvalid,
    InvalidData
}

public sealed record CreateReproductionResult(
    CreateReproductionStatus Status,
    ReproductionResult? Reproduction)
{
    public static CreateReproductionResult Created(ReproductionResult reproduction) =>
        new(CreateReproductionStatus.Created, reproduction);

    public static CreateReproductionResult UserNotFound() =>
        new(CreateReproductionStatus.UserNotFound, null);

    public static CreateReproductionResult BreedingFarmNotSelected() =>
        new(CreateReproductionStatus.BreedingFarmNotSelected, null);

    public static CreateReproductionResult BreedingFarmNotFound() =>
        new(CreateReproductionStatus.BreedingFarmNotFound, null);

    public static CreateReproductionResult BirdNotFound() =>
        new(CreateReproductionStatus.BirdNotFound, null);

    public static CreateReproductionResult BirdNotEligible() =>
        new(CreateReproductionStatus.BirdNotEligible, null);

    public static CreateReproductionResult TransferPending() =>
        new(CreateReproductionStatus.TransferPending, null);

    public static CreateReproductionResult MaleBirdSexInvalid() =>
        new(CreateReproductionStatus.MaleBirdSexInvalid, null);

    public static CreateReproductionResult FemaleBirdSexInvalid() =>
        new(CreateReproductionStatus.FemaleBirdSexInvalid, null);

    public static CreateReproductionResult InvalidData() =>
        new(CreateReproductionStatus.InvalidData, null);
}

public sealed record UpdateReproductionCommand(
    Guid UserId,
    Guid ReproductionId,
    Guid? MaleBirdId,
    Guid? FemaleBirdId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes,
    bool HasMaleBirdId,
    bool HasFemaleBirdId,
    bool HasStartDate,
    bool HasEndDate,
    bool HasNotes) : ICommand<UpdateReproductionResult>;

public enum UpdateReproductionStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ReproductionNotFound,
    BirdNotFound,
    BirdNotEligible,
    TransferPending,
    MaleBirdSexInvalid,
    FemaleBirdSexInvalid,
    InvalidState,
    InvalidData
}

public sealed record UpdateReproductionResult(
    UpdateReproductionStatus Status,
    ReproductionResult? Reproduction)
{
    public static UpdateReproductionResult Updated(ReproductionResult reproduction) =>
        new(UpdateReproductionStatus.Updated, reproduction);

    public static UpdateReproductionResult UserNotFound() =>
        new(UpdateReproductionStatus.UserNotFound, null);

    public static UpdateReproductionResult BreedingFarmNotSelected() =>
        new(UpdateReproductionStatus.BreedingFarmNotSelected, null);

    public static UpdateReproductionResult BreedingFarmNotFound() =>
        new(UpdateReproductionStatus.BreedingFarmNotFound, null);

    public static UpdateReproductionResult ReproductionNotFound() =>
        new(UpdateReproductionStatus.ReproductionNotFound, null);

    public static UpdateReproductionResult BirdNotFound() =>
        new(UpdateReproductionStatus.BirdNotFound, null);

    public static UpdateReproductionResult BirdNotEligible() =>
        new(UpdateReproductionStatus.BirdNotEligible, null);

    public static UpdateReproductionResult TransferPending() =>
        new(UpdateReproductionStatus.TransferPending, null);

    public static UpdateReproductionResult MaleBirdSexInvalid() =>
        new(UpdateReproductionStatus.MaleBirdSexInvalid, null);

    public static UpdateReproductionResult FemaleBirdSexInvalid() =>
        new(UpdateReproductionStatus.FemaleBirdSexInvalid, null);

    public static UpdateReproductionResult InvalidState() =>
        new(UpdateReproductionStatus.InvalidState, null);

    public static UpdateReproductionResult InvalidData() =>
        new(UpdateReproductionStatus.InvalidData, null);
}

public sealed record ChangeReproductionStatusCommand(
    Guid UserId,
    Guid ReproductionId,
    ReproductionStatus Status,
    bool Confirmed,
    DateOnly? EndDate) : ICommand<ChangeReproductionStatusResult>;

public enum ChangeReproductionStatusStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ReproductionNotFound,
    ConfirmationRequired,
    InvalidStatus,
    InvalidState,
    InvalidData
}

public sealed record ChangeReproductionStatusResult(
    ChangeReproductionStatusStatus Status,
    ReproductionResult? Reproduction)
{
    public static ChangeReproductionStatusResult Updated(ReproductionResult reproduction) =>
        new(ChangeReproductionStatusStatus.Updated, reproduction);

    public static ChangeReproductionStatusResult UserNotFound() =>
        new(ChangeReproductionStatusStatus.UserNotFound, null);

    public static ChangeReproductionStatusResult BreedingFarmNotSelected() =>
        new(ChangeReproductionStatusStatus.BreedingFarmNotSelected, null);

    public static ChangeReproductionStatusResult BreedingFarmNotFound() =>
        new(ChangeReproductionStatusStatus.BreedingFarmNotFound, null);

    public static ChangeReproductionStatusResult ReproductionNotFound() =>
        new(ChangeReproductionStatusStatus.ReproductionNotFound, null);

    public static ChangeReproductionStatusResult ConfirmationRequired() =>
        new(ChangeReproductionStatusStatus.ConfirmationRequired, null);

    public static ChangeReproductionStatusResult InvalidStatus() =>
        new(ChangeReproductionStatusStatus.InvalidStatus, null);

    public static ChangeReproductionStatusResult InvalidState() =>
        new(ChangeReproductionStatusStatus.InvalidState, null);

    public static ChangeReproductionStatusResult InvalidData() =>
        new(ChangeReproductionStatusStatus.InvalidData, null);
}

public sealed record LinkReproductionOriginCommand(
    Guid UserId,
    Guid ReproductionId,
    Guid BirdId,
    bool Confirmed) : ICommand<LinkReproductionOriginResult>;

public enum LinkReproductionOriginStatus
{
    Linked,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ReproductionNotFound,
    BirdNotFound,
    BirdNotEligible,
    BirdAlreadyLinked,
    SameBirdAsParent,
    CycleDetected,
    ConfirmationRequired,
    InvalidData
}

public sealed record LinkReproductionOriginResult(
    LinkReproductionOriginStatus Status,
    BirdResult? Bird)
{
    public static LinkReproductionOriginResult Linked(BirdResult bird) =>
        new(LinkReproductionOriginStatus.Linked, bird);

    public static LinkReproductionOriginResult UserNotFound() =>
        new(LinkReproductionOriginStatus.UserNotFound, null);

    public static LinkReproductionOriginResult BreedingFarmNotSelected() =>
        new(LinkReproductionOriginStatus.BreedingFarmNotSelected, null);

    public static LinkReproductionOriginResult BreedingFarmNotFound() =>
        new(LinkReproductionOriginStatus.BreedingFarmNotFound, null);

    public static LinkReproductionOriginResult ReproductionNotFound() =>
        new(LinkReproductionOriginStatus.ReproductionNotFound, null);

    public static LinkReproductionOriginResult BirdNotFound() =>
        new(LinkReproductionOriginStatus.BirdNotFound, null);

    public static LinkReproductionOriginResult BirdNotEligible() =>
        new(LinkReproductionOriginStatus.BirdNotEligible, null);

    public static LinkReproductionOriginResult BirdAlreadyLinked() =>
        new(LinkReproductionOriginStatus.BirdAlreadyLinked, null);

    public static LinkReproductionOriginResult SameBirdAsParent() =>
        new(LinkReproductionOriginStatus.SameBirdAsParent, null);

    public static LinkReproductionOriginResult CycleDetected() =>
        new(LinkReproductionOriginStatus.CycleDetected, null);

    public static LinkReproductionOriginResult ConfirmationRequired() =>
        new(LinkReproductionOriginStatus.ConfirmationRequired, null);

    public static LinkReproductionOriginResult InvalidData() =>
        new(LinkReproductionOriginStatus.InvalidData, null);
}

public sealed record ReproductionResult(
    Guid ReproductionId,
    Guid BreedingFarmId,
    Guid MaleBirdId,
    Guid FemaleBirdId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    ReproductionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
