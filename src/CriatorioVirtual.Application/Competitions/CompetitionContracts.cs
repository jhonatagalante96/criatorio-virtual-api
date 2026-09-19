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

public sealed record UpdateBirdCompetitionCommand(
    Guid UserId,
    Guid BirdId,
    Guid CompetitionId,
    string? Name,
    DateOnly? CompetitionDate,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes) : ICommand<UpdateBirdCompetitionResult>;

public enum UpdateBirdCompetitionStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    CompetitionNotFound,
    InvalidData
}

public sealed record UpdateBirdCompetitionResult(
    UpdateBirdCompetitionStatus Status,
    BirdCompetitionResult? Competition)
{
    public static UpdateBirdCompetitionResult Updated(BirdCompetitionResult competition) =>
        new(UpdateBirdCompetitionStatus.Updated, competition);

    public static UpdateBirdCompetitionResult UserNotFound() =>
        new(UpdateBirdCompetitionStatus.UserNotFound, null);

    public static UpdateBirdCompetitionResult BreedingFarmNotSelected() =>
        new(UpdateBirdCompetitionStatus.BreedingFarmNotSelected, null);

    public static UpdateBirdCompetitionResult BreedingFarmNotFound() =>
        new(UpdateBirdCompetitionStatus.BreedingFarmNotFound, null);

    public static UpdateBirdCompetitionResult BirdNotFound() =>
        new(UpdateBirdCompetitionStatus.BirdNotFound, null);

    public static UpdateBirdCompetitionResult CompetitionNotFound() =>
        new(UpdateBirdCompetitionStatus.CompetitionNotFound, null);

    public static UpdateBirdCompetitionResult InvalidData() =>
        new(UpdateBirdCompetitionStatus.InvalidData, null);
}

public sealed record DeleteBirdCompetitionCommand(
    Guid UserId,
    Guid BirdId,
    Guid CompetitionId,
    bool Confirmed) : ICommand<DeleteBirdCompetitionResult>;

public enum DeleteBirdCompetitionStatus
{
    Deleted,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    CompetitionNotFound,
    ConfirmationRequired,
    InvalidData
}

public sealed record DeleteBirdCompetitionResult(DeleteBirdCompetitionStatus Status)
{
    public static DeleteBirdCompetitionResult Deleted() =>
        new(DeleteBirdCompetitionStatus.Deleted);

    public static DeleteBirdCompetitionResult UserNotFound() =>
        new(DeleteBirdCompetitionStatus.UserNotFound);

    public static DeleteBirdCompetitionResult BreedingFarmNotSelected() =>
        new(DeleteBirdCompetitionStatus.BreedingFarmNotSelected);

    public static DeleteBirdCompetitionResult BreedingFarmNotFound() =>
        new(DeleteBirdCompetitionStatus.BreedingFarmNotFound);

    public static DeleteBirdCompetitionResult BirdNotFound() =>
        new(DeleteBirdCompetitionStatus.BirdNotFound);

    public static DeleteBirdCompetitionResult CompetitionNotFound() =>
        new(DeleteBirdCompetitionStatus.CompetitionNotFound);

    public static DeleteBirdCompetitionResult ConfirmationRequired() =>
        new(DeleteBirdCompetitionStatus.ConfirmationRequired);

    public static DeleteBirdCompetitionResult InvalidData() =>
        new(DeleteBirdCompetitionStatus.InvalidData);
}

public sealed record ListCompetitionsQuery(
    Guid UserId,
    Guid? BirdId,
    string? Category,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string? Search,
    int Page,
    int PageSize) : IQuery<ListCompetitionsResult>;

public enum ListCompetitionsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record ListCompetitionsResult(
    ListCompetitionsStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<CompetitionListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ListCompetitionsResult Succeeded(
        Guid breedingFarmId,
        IReadOnlyCollection<CompetitionListItemResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(ListCompetitionsStatus.Success, breedingFarmId, items, page, pageSize, totalCount);

    public static ListCompetitionsResult UserNotFound() =>
        new(ListCompetitionsStatus.UserNotFound, null, [], 0, 0, 0);

    public static ListCompetitionsResult BreedingFarmNotSelected() =>
        new(ListCompetitionsStatus.BreedingFarmNotSelected, null, [], 0, 0, 0);

    public static ListCompetitionsResult BreedingFarmNotFound() =>
        new(ListCompetitionsStatus.BreedingFarmNotFound, null, [], 0, 0, 0);
}

public sealed record CompetitionBirdSummaryResult(
    Guid BirdId,
    string Name,
    string? RingNumber);

public sealed record CompetitionListItemResult(
    Guid CompetitionId,
    Guid BreedingFarmId,
    CompetitionBirdSummaryResult Bird,
    string Name,
    DateOnly? CompetitionDate,
    string? Category,
    int? Placement,
    string? Location,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
