using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Reproductions;

namespace CriatorioVirtual.Application.Reproductions;

public sealed record ListReproductionsQuery(
    Guid UserId,
    ReproductionStatus? Status,
    Guid? BirdId,
    int Page,
    int PageSize) : IQuery<ListReproductionsResult>;

public enum ListReproductionsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record ListReproductionsResult(
    ListReproductionsStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<ReproductionListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ListReproductionsResult Succeeded(
        Guid breedingFarmId,
        IReadOnlyCollection<ReproductionListItemResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(ListReproductionsStatus.Success, breedingFarmId, items, page, pageSize, totalCount);

    public static ListReproductionsResult UserNotFound() =>
        new(ListReproductionsStatus.UserNotFound, null, [], 0, 0, 0);

    public static ListReproductionsResult BreedingFarmNotSelected() =>
        new(ListReproductionsStatus.BreedingFarmNotSelected, null, [], 0, 0, 0);

    public static ListReproductionsResult BreedingFarmNotFound() =>
        new(ListReproductionsStatus.BreedingFarmNotFound, null, [], 0, 0, 0);
}

public sealed record GetReproductionQuery(
    Guid UserId,
    Guid ReproductionId) : IQuery<GetReproductionResult>;

public enum GetReproductionStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    ReproductionNotFound
}

public sealed record GetReproductionResult(
    GetReproductionStatus Status,
    ReproductionDetailsResult? Reproduction)
{
    public static GetReproductionResult Succeeded(ReproductionDetailsResult reproduction) =>
        new(GetReproductionStatus.Success, reproduction);

    public static GetReproductionResult UserNotFound() =>
        new(GetReproductionStatus.UserNotFound, null);

    public static GetReproductionResult BreedingFarmNotSelected() =>
        new(GetReproductionStatus.BreedingFarmNotSelected, null);

    public static GetReproductionResult BreedingFarmNotFound() =>
        new(GetReproductionStatus.BreedingFarmNotFound, null);

    public static GetReproductionResult ReproductionNotFound() =>
        new(GetReproductionStatus.ReproductionNotFound, null);
}

public sealed record ReproductionListItemResult(
    Guid ReproductionId,
    Guid BreedingFarmId,
    ReproductionBirdResult MaleBird,
    ReproductionBirdResult FemaleBird,
    DateOnly StartDate,
    DateOnly? EndDate,
    ReproductionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReproductionDetailsResult(
    Guid ReproductionId,
    Guid BreedingFarmId,
    ReproductionBirdResult MaleBird,
    ReproductionBirdResult FemaleBird,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    ReproductionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReproductionBirdResult(
    Guid BirdId,
    string Name,
    BirdSex Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    BirdStatus Status,
    bool CanNavigate);
