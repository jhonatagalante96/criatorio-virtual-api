using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;
using CriatorioVirtual.Domain.Transfers;

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

public enum InternalTransferDirection
{
    Sent,
    Received
}

public sealed record ListInternalTransferRequestsQuery(
    Guid UserId,
    InternalTransferDirection Direction,
    InternalTransferRequestStatus? Status,
    int Page,
    int PageSize) : IQuery<ListInternalTransferRequestsResult>;

public enum ListInternalTransferRequestsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record ListInternalTransferRequestsResult(
    ListInternalTransferRequestsStatus Status,
    Guid? BreedingFarmId,
    InternalTransferDirection Direction,
    IReadOnlyCollection<InternalTransferListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ListInternalTransferRequestsResult Succeeded(
        Guid breedingFarmId,
        InternalTransferDirection direction,
        IReadOnlyCollection<InternalTransferListItemResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(ListInternalTransferRequestsStatus.Success, breedingFarmId, direction, items, page, pageSize, totalCount);

    public static ListInternalTransferRequestsResult UserNotFound(InternalTransferDirection direction) =>
        new(ListInternalTransferRequestsStatus.UserNotFound, null, direction, [], 0, 0, 0);

    public static ListInternalTransferRequestsResult BreedingFarmNotSelected(InternalTransferDirection direction) =>
        new(ListInternalTransferRequestsStatus.BreedingFarmNotSelected, null, direction, [], 0, 0, 0);

    public static ListInternalTransferRequestsResult BreedingFarmNotFound(InternalTransferDirection direction) =>
        new(ListInternalTransferRequestsStatus.BreedingFarmNotFound, null, direction, [], 0, 0, 0);
}

public sealed record InternalTransferListItemResult(
    Guid TransferRequestId,
    Guid BirdId,
    string BirdName,
    string? RingNumber,
    Guid SourceBreedingFarmId,
    string SourceBreedingFarmName,
    Guid DestinationBreedingFarmId,
    string DestinationBreedingFarmName,
    InternalTransferRequestStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetInternalTransferRequestQuery(
    Guid UserId,
    Guid TransferRequestId) : IQuery<GetInternalTransferRequestResult>;

public enum GetInternalTransferRequestStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TransferRequestNotFound
}

public sealed record GetInternalTransferRequestResult(
    GetInternalTransferRequestStatus Status,
    InternalTransferDetailsResult? TransferRequest)
{
    public static GetInternalTransferRequestResult Succeeded(InternalTransferDetailsResult transferRequest) =>
        new(GetInternalTransferRequestStatus.Success, transferRequest);

    public static GetInternalTransferRequestResult UserNotFound() =>
        new(GetInternalTransferRequestStatus.UserNotFound, null);

    public static GetInternalTransferRequestResult BreedingFarmNotSelected() =>
        new(GetInternalTransferRequestStatus.BreedingFarmNotSelected, null);

    public static GetInternalTransferRequestResult BreedingFarmNotFound() =>
        new(GetInternalTransferRequestStatus.BreedingFarmNotFound, null);

    public static GetInternalTransferRequestResult TransferRequestNotFound() =>
        new(GetInternalTransferRequestStatus.TransferRequestNotFound, null);
}

public sealed record InternalTransferDetailsResult(
    Guid TransferRequestId,
    Guid BirdId,
    string BirdName,
    BirdSex BirdSex,
    string? RingNumber,
    BirdStatus BirdStatus,
    Guid SourceBreedingFarmId,
    string SourceBreedingFarmName,
    Guid DestinationBreedingFarmId,
    string DestinationBreedingFarmName,
    InternalTransferRequestStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AcceptInternalTransferCommand(
    Guid UserId,
    Guid TransferRequestId) : ICommand<AcceptInternalTransferResult>;

public enum AcceptInternalTransferStatus
{
    Accepted,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TransferRequestNotFound,
    TransferNotPending,
    BirdNotFound,
    InvalidState
}

public sealed record AcceptInternalTransferResult(
    AcceptInternalTransferStatus Status,
    InternalTransferRequestResult? TransferRequest)
{
    public static AcceptInternalTransferResult Accepted(InternalTransferRequestResult transferRequest) =>
        new(AcceptInternalTransferStatus.Accepted, transferRequest);

    public static AcceptInternalTransferResult UserNotFound() =>
        new(AcceptInternalTransferStatus.UserNotFound, null);

    public static AcceptInternalTransferResult BreedingFarmNotSelected() =>
        new(AcceptInternalTransferStatus.BreedingFarmNotSelected, null);

    public static AcceptInternalTransferResult BreedingFarmNotFound() =>
        new(AcceptInternalTransferStatus.BreedingFarmNotFound, null);

    public static AcceptInternalTransferResult TransferRequestNotFound() =>
        new(AcceptInternalTransferStatus.TransferRequestNotFound, null);

    public static AcceptInternalTransferResult TransferNotPending() =>
        new(AcceptInternalTransferStatus.TransferNotPending, null);

    public static AcceptInternalTransferResult BirdNotFound() =>
        new(AcceptInternalTransferStatus.BirdNotFound, null);

    public static AcceptInternalTransferResult InvalidState() =>
        new(AcceptInternalTransferStatus.InvalidState, null);
}

public sealed record RejectInternalTransferCommand(
    Guid UserId,
    Guid TransferRequestId) : ICommand<RejectInternalTransferResult>;

public enum RejectInternalTransferStatus
{
    Rejected,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TransferRequestNotFound,
    TransferNotPending,
    BirdNotFound,
    InvalidState
}

public sealed record RejectInternalTransferResult(
    RejectInternalTransferStatus Status,
    InternalTransferRequestResult? TransferRequest)
{
    public static RejectInternalTransferResult Rejected(InternalTransferRequestResult transferRequest) =>
        new(RejectInternalTransferStatus.Rejected, transferRequest);

    public static RejectInternalTransferResult UserNotFound() =>
        new(RejectInternalTransferStatus.UserNotFound, null);

    public static RejectInternalTransferResult BreedingFarmNotSelected() =>
        new(RejectInternalTransferStatus.BreedingFarmNotSelected, null);

    public static RejectInternalTransferResult BreedingFarmNotFound() =>
        new(RejectInternalTransferStatus.BreedingFarmNotFound, null);

    public static RejectInternalTransferResult TransferRequestNotFound() =>
        new(RejectInternalTransferStatus.TransferRequestNotFound, null);

    public static RejectInternalTransferResult TransferNotPending() =>
        new(RejectInternalTransferStatus.TransferNotPending, null);

    public static RejectInternalTransferResult BirdNotFound() =>
        new(RejectInternalTransferStatus.BirdNotFound, null);

    public static RejectInternalTransferResult InvalidState() =>
        new(RejectInternalTransferStatus.InvalidState, null);
}

public sealed record CancelInternalTransferCommand(
    Guid UserId,
    Guid TransferRequestId) : ICommand<CancelInternalTransferResult>;

public enum CancelInternalTransferStatus
{
    Cancelled,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    TransferRequestNotFound,
    TransferNotPending,
    BirdNotFound,
    InvalidState
}

public sealed record CancelInternalTransferResult(
    CancelInternalTransferStatus Status,
    InternalTransferRequestResult? TransferRequest)
{
    public static CancelInternalTransferResult Cancelled(InternalTransferRequestResult transferRequest) =>
        new(CancelInternalTransferStatus.Cancelled, transferRequest);

    public static CancelInternalTransferResult UserNotFound() =>
        new(CancelInternalTransferStatus.UserNotFound, null);

    public static CancelInternalTransferResult BreedingFarmNotSelected() =>
        new(CancelInternalTransferStatus.BreedingFarmNotSelected, null);

    public static CancelInternalTransferResult BreedingFarmNotFound() =>
        new(CancelInternalTransferStatus.BreedingFarmNotFound, null);

    public static CancelInternalTransferResult TransferRequestNotFound() =>
        new(CancelInternalTransferStatus.TransferRequestNotFound, null);

    public static CancelInternalTransferResult TransferNotPending() =>
        new(CancelInternalTransferStatus.TransferNotPending, null);

    public static CancelInternalTransferResult BirdNotFound() =>
        new(CancelInternalTransferStatus.BirdNotFound, null);

    public static CancelInternalTransferResult InvalidState() =>
        new(CancelInternalTransferStatus.InvalidState, null);
}
