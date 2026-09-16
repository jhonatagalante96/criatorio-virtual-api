using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.BreedingFarmStatistics;

public sealed record GetBreedingFarmStatisticsQuery(
    Guid UserId,
    DateOnly From,
    DateOnly To) : IQuery<GetBreedingFarmStatisticsResult>;

public enum GetBreedingFarmStatisticsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record GetBreedingFarmStatisticsResult(
    GetBreedingFarmStatisticsStatus Status,
    BreedingFarmStatisticsData? Statistics)
{
    public static GetBreedingFarmStatisticsResult Succeeded(BreedingFarmStatisticsData statistics) =>
        new(GetBreedingFarmStatisticsStatus.Success, statistics);

    public static GetBreedingFarmStatisticsResult UserNotFound() =>
        new(GetBreedingFarmStatisticsStatus.UserNotFound, null);

    public static GetBreedingFarmStatisticsResult BreedingFarmNotSelected() =>
        new(GetBreedingFarmStatisticsStatus.BreedingFarmNotSelected, null);

    public static GetBreedingFarmStatisticsResult BreedingFarmNotFound() =>
        new(GetBreedingFarmStatisticsStatus.BreedingFarmNotFound, null);
}

public sealed record BreedingFarmStatisticsData(
    Guid BreedingFarmId,
    DateOnly From,
    DateOnly To,
    IReadOnlyCollection<BirdStatusCount> BirdsByStatus,
    IReadOnlyCollection<BirdSexCount> BirdsBySex,
    IReadOnlyCollection<BirdSpeciesCount> BirdsBySpecies,
    IReadOnlyCollection<DailyBreedingFarmStatistics> Daily,
    BreedingFarmTransferStatistics Transfers);

public sealed record BirdStatusCount(string Status, int Count);

public sealed record BirdSexCount(string Sex, int Count);

public sealed record BirdSpeciesCount(Guid SpeciesId, string PopularName, string ScientificName, int Count);

/// <summary>
/// Daily values use UTC calendar dates. Births are birds whose recorded BirthDate falls on the date;
/// reproduction completion is the EndDate of finished reproductions; transfer counts are completed
/// internal requests and completed external transfers, not requests created on that date.
/// </summary>
public sealed record DailyBreedingFarmStatistics(
    DateOnly Date,
    int BirdsRegisteredCount,
    int BirthsRecordedCount,
    int ReproductionsStartedCount,
    int ReproductionsCompletedCount,
    int InternalTransfersInCount,
    int InternalTransfersOutCount,
    int ExternalTransfersOutCount);

/// <summary>Request status counts describe all current requests involving the selected farm.</summary>
public sealed record BreedingFarmTransferStatistics(
    int InternalTransfersInCount,
    int InternalTransfersOutCount,
    int ExternalTransfersOutCount,
    IReadOnlyCollection<TransferRequestStatusCount> CurrentIncomingRequestsByStatus,
    IReadOnlyCollection<TransferRequestStatusCount> CurrentOutgoingRequestsByStatus);

public sealed record TransferRequestStatusCount(string Status, int Count);
