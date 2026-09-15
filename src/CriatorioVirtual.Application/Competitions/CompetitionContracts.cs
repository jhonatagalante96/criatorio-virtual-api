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

public sealed record ListBirdCompetitionsQuery(
    Guid UserId,
    Guid BirdId) : IQuery<ListBirdCompetitionsResult>;

public enum ListBirdCompetitionsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record ListBirdCompetitionsResult(
    ListBirdCompetitionsStatus Status,
    Guid? BreedingFarmId,
    Guid? BirdId,
    IReadOnlyCollection<BirdCompetitionResult>? Competitions)
{
    public static ListBirdCompetitionsResult Succeeded(
        Guid breedingFarmId,
        Guid birdId,
        IReadOnlyCollection<BirdCompetitionResult> competitions) =>
        new(ListBirdCompetitionsStatus.Success, breedingFarmId, birdId, competitions);

    public static ListBirdCompetitionsResult UserNotFound() =>
        new(ListBirdCompetitionsStatus.UserNotFound, null, null, null);

    public static ListBirdCompetitionsResult BreedingFarmNotSelected() =>
        new(ListBirdCompetitionsStatus.BreedingFarmNotSelected, null, null, null);

    public static ListBirdCompetitionsResult BreedingFarmNotFound() =>
        new(ListBirdCompetitionsStatus.BreedingFarmNotFound, null, null, null);

    public static ListBirdCompetitionsResult BirdNotFound() =>
        new(ListBirdCompetitionsStatus.BirdNotFound, null, null, null);
}

public sealed record GetBirdCompetitionQuery(
    Guid UserId,
    Guid BirdId,
    Guid CompetitionId) : IQuery<GetBirdCompetitionResult>;

public enum GetBirdCompetitionStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    CompetitionNotFound
}

public sealed record GetBirdCompetitionResult(
    GetBirdCompetitionStatus Status,
    BirdCompetitionResult? Competition)
{
    public static GetBirdCompetitionResult Succeeded(BirdCompetitionResult competition) =>
        new(GetBirdCompetitionStatus.Success, competition);

    public static GetBirdCompetitionResult UserNotFound() =>
        new(GetBirdCompetitionStatus.UserNotFound, null);

    public static GetBirdCompetitionResult BreedingFarmNotSelected() =>
        new(GetBirdCompetitionStatus.BreedingFarmNotSelected, null);

    public static GetBirdCompetitionResult BreedingFarmNotFound() =>
        new(GetBirdCompetitionStatus.BreedingFarmNotFound, null);

    public static GetBirdCompetitionResult BirdNotFound() =>
        new(GetBirdCompetitionStatus.BirdNotFound, null);

    public static GetBirdCompetitionResult CompetitionNotFound() =>
        new(GetBirdCompetitionStatus.CompetitionNotFound, null);
}

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
