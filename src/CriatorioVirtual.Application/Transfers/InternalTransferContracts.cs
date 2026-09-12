using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Transfers;

public sealed record SearchInternalTransferDestinationsQuery(
    Guid UserId,
    string? Search,
    int Page,
    int PageSize) : IQuery<SearchInternalTransferDestinationsResult>;

public enum SearchInternalTransferDestinationsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record SearchInternalTransferDestinationsResult(
    SearchInternalTransferDestinationsStatus Status,
    Guid? SourceBreedingFarmId,
    IReadOnlyCollection<InternalTransferDestinationResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static SearchInternalTransferDestinationsResult Succeeded(
        Guid sourceBreedingFarmId,
        IReadOnlyCollection<InternalTransferDestinationResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(SearchInternalTransferDestinationsStatus.Success, sourceBreedingFarmId, items, page, pageSize, totalCount);

    public static SearchInternalTransferDestinationsResult UserNotFound() =>
        new(SearchInternalTransferDestinationsStatus.UserNotFound, null, [], 0, 0, 0);

    public static SearchInternalTransferDestinationsResult BreedingFarmNotSelected() =>
        new(SearchInternalTransferDestinationsStatus.BreedingFarmNotSelected, null, [], 0, 0, 0);

    public static SearchInternalTransferDestinationsResult BreedingFarmNotFound() =>
        new(SearchInternalTransferDestinationsStatus.BreedingFarmNotFound, null, [], 0, 0, 0);
}

public sealed record InternalTransferDestinationResult(
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName);

public sealed record RequestInternalTransferCommand(
    Guid UserId,
    Guid? BirdId,
    Guid? DestinationBreedingFarmId,
    bool Confirmed) : ICommand<RequestInternalTransferResult>;

public enum RequestInternalTransferStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    DestinationBreedingFarmNotFound,
    SameBreedingFarm,
    ConfirmationRequired,
    BirdNotEligible,
    TransferPending,
    InvalidData
}

public sealed record RequestInternalTransferResult(
    RequestInternalTransferStatus Status,
    InternalTransferRequestResult? TransferRequest)
{
    public static RequestInternalTransferResult Created(InternalTransferRequestResult transferRequest) =>
        new(RequestInternalTransferStatus.Created, transferRequest);

    public static RequestInternalTransferResult UserNotFound() =>
        new(RequestInternalTransferStatus.UserNotFound, null);

    public static RequestInternalTransferResult BreedingFarmNotSelected() =>
        new(RequestInternalTransferStatus.BreedingFarmNotSelected, null);

    public static RequestInternalTransferResult BreedingFarmNotFound() =>
        new(RequestInternalTransferStatus.BreedingFarmNotFound, null);

    public static RequestInternalTransferResult BirdNotFound() =>
        new(RequestInternalTransferStatus.BirdNotFound, null);

    public static RequestInternalTransferResult DestinationBreedingFarmNotFound() =>
        new(RequestInternalTransferStatus.DestinationBreedingFarmNotFound, null);

    public static RequestInternalTransferResult SameBreedingFarm() =>
        new(RequestInternalTransferStatus.SameBreedingFarm, null);

    public static RequestInternalTransferResult ConfirmationRequired() =>
        new(RequestInternalTransferStatus.ConfirmationRequired, null);

    public static RequestInternalTransferResult BirdNotEligible() =>
        new(RequestInternalTransferStatus.BirdNotEligible, null);

    public static RequestInternalTransferResult TransferPending() =>
        new(RequestInternalTransferStatus.TransferPending, null);

    public static RequestInternalTransferResult InvalidData() =>
        new(RequestInternalTransferStatus.InvalidData, null);
}

public sealed record InternalTransferRequestResult(
    Guid TransferRequestId,
    Guid BirdId,
    Guid SourceBreedingFarmId,
    Guid DestinationBreedingFarmId,
    Guid RequestedByUserId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
