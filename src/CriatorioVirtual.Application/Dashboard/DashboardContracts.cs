using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.Dashboard;

public sealed record GetDashboardQuery(Guid UserId) : IQuery<GetDashboardResult>;

public enum GetDashboardStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record GetDashboardResult(
    GetDashboardStatus Status,
    DashboardResult? Dashboard)
{
    public static GetDashboardResult Succeeded(DashboardResult dashboard) =>
        new(GetDashboardStatus.Success, dashboard);

    public static GetDashboardResult UserNotFound() =>
        new(GetDashboardStatus.UserNotFound, null);

    public static GetDashboardResult BreedingFarmNotSelected() =>
        new(GetDashboardStatus.BreedingFarmNotSelected, null);

    public static GetDashboardResult BreedingFarmNotFound() =>
        new(GetDashboardStatus.BreedingFarmNotFound, null);
}

public static class DashboardLimits
{
    public const int MaxRecentActivities = 10;
}

public sealed record DashboardResult(
    Guid BreedingFarmId,
    DashboardIndicatorsResult Indicators,
    IReadOnlyCollection<DashboardPendingResult> Pending,
    IReadOnlyCollection<DashboardActivityResult> Activities);

public sealed record DashboardIndicatorsResult(
    int ActiveBirdCount,
    int PendingIdentificationCount,
    int ActiveReproductionCount);

public sealed record DashboardPendingResult(
    string Code,
    string ResourceType,
    string Title,
    int Count);

public sealed record DashboardActivityResult(
    string ActivityType,
    string ResourceType,
    Guid ResourceId,
    string Title,
    DateTimeOffset OccurredAtUtc);
