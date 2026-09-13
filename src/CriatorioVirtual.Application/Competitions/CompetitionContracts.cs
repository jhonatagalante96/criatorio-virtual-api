using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Competitions;

public sealed record CreateBirdCompetitionCommand(
    Guid UserId,
    Guid BirdId,
    string? Name,
    DateOnly? CompetitionDate,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes) : ICommand<CreateBirdCompetitionResult>;

public enum CreateBirdCompetitionStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    InvalidData
}

public sealed record CreateBirdCompetitionResult(
    CreateBirdCompetitionStatus Status,
    BirdCompetitionResult? Competition)
{
    public static CreateBirdCompetitionResult Created(BirdCompetitionResult competition) =>
        new(CreateBirdCompetitionStatus.Created, competition);

    public static CreateBirdCompetitionResult UserNotFound() =>
        new(CreateBirdCompetitionStatus.UserNotFound, null);

    public static CreateBirdCompetitionResult BreedingFarmNotSelected() =>
        new(CreateBirdCompetitionStatus.BreedingFarmNotSelected, null);

    public static CreateBirdCompetitionResult BreedingFarmNotFound() =>
        new(CreateBirdCompetitionStatus.BreedingFarmNotFound, null);

    public static CreateBirdCompetitionResult BirdNotFound() =>
        new(CreateBirdCompetitionStatus.BirdNotFound, null);

    public static CreateBirdCompetitionResult InvalidData() =>
        new(CreateBirdCompetitionStatus.InvalidData, null);
}

public sealed record BirdCompetitionResult(
    Guid CompetitionId,
    Guid BirdId,
    string Name,
    DateOnly? CompetitionDate,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
