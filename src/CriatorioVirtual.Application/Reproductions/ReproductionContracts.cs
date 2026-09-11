using CriatorioVirtual.Application.Messaging;
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

    public static CreateReproductionResult MaleBirdSexInvalid() =>
        new(CreateReproductionStatus.MaleBirdSexInvalid, null);

    public static CreateReproductionResult FemaleBirdSexInvalid() =>
        new(CreateReproductionStatus.FemaleBirdSexInvalid, null);

    public static CreateReproductionResult InvalidData() =>
        new(CreateReproductionStatus.InvalidData, null);
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
