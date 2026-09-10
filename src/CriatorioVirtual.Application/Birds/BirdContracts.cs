using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.Birds;

namespace CriatorioVirtual.Application.Birds;

public sealed record CreateBirdCommand(
    Guid UserId,
    string? Name,
    BirdSex? Sex,
    Guid? SpeciesId,
    DateOnly? BirthDate,
    string? RingNumber,
    Guid? FatherBirdId,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? Notes) : ICommand<CreateBirdResult>;

public enum CreateBirdStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    SpeciesNotFound,
    ParentNotFound,
    ParentSexInvalid,
    DuplicateParent,
    DuplicateRingNumber
}

public sealed record CreateBirdResult(
    CreateBirdStatus Status,
    BirdResult? Bird)
{
    public static CreateBirdResult Created(BirdResult bird) => new(CreateBirdStatus.Created, bird);

    public static CreateBirdResult UserNotFound() => new(CreateBirdStatus.UserNotFound, null);

    public static CreateBirdResult BreedingFarmNotSelected() => new(CreateBirdStatus.BreedingFarmNotSelected, null);

    public static CreateBirdResult BreedingFarmNotFound() => new(CreateBirdStatus.BreedingFarmNotFound, null);

    public static CreateBirdResult SpeciesNotFound() => new(CreateBirdStatus.SpeciesNotFound, null);

    public static CreateBirdResult ParentNotFound() => new(CreateBirdStatus.ParentNotFound, null);

    public static CreateBirdResult ParentSexInvalid() => new(CreateBirdStatus.ParentSexInvalid, null);

    public static CreateBirdResult DuplicateParent() => new(CreateBirdStatus.DuplicateParent, null);

    public static CreateBirdResult DuplicateRingNumber() => new(CreateBirdStatus.DuplicateRingNumber, null);
}

public sealed record UpdateBirdCommand(
    Guid UserId,
    Guid BirdId,
    string? Name,
    BirdSex? Sex,
    Guid? SpeciesId,
    DateOnly? BirthDate,
    string? RingNumber,
    string? Notes) : ICommand<UpdateBirdResult>;

public enum UpdateBirdStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    SpeciesNotFound,
    DuplicateRingNumber,
    InvalidData
}

public sealed record UpdateBirdResult(
    UpdateBirdStatus Status,
    BirdResult? Bird)
{
    public static UpdateBirdResult Updated(BirdResult bird) =>
        new(UpdateBirdStatus.Updated, bird);

    public static UpdateBirdResult UserNotFound() =>
        new(UpdateBirdStatus.UserNotFound, null);

    public static UpdateBirdResult BreedingFarmNotSelected() =>
        new(UpdateBirdStatus.BreedingFarmNotSelected, null);

    public static UpdateBirdResult BreedingFarmNotFound() =>
        new(UpdateBirdStatus.BreedingFarmNotFound, null);

    public static UpdateBirdResult BirdNotFound() =>
        new(UpdateBirdStatus.BirdNotFound, null);

    public static UpdateBirdResult SpeciesNotFound() =>
        new(UpdateBirdStatus.SpeciesNotFound, null);

    public static UpdateBirdResult DuplicateRingNumber() =>
        new(UpdateBirdStatus.DuplicateRingNumber, null);

    public static UpdateBirdResult InvalidData() =>
        new(UpdateBirdStatus.InvalidData, null);
}

public sealed record ChangeBirdStatusCommand(
    Guid UserId,
    Guid BirdId,
    BirdStatus Status,
    bool Confirmed,
    DateOnly? DeathDate,
    string? Notes) : ICommand<ChangeBirdStatusResult>;

public enum ChangeBirdStatusStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    ConfirmationRequired,
    InvalidStatus,
    InvalidData,
    StatusChangeNotAllowed,
    TransferPending
}

public sealed record ChangeBirdStatusResult(
    ChangeBirdStatusStatus Status,
    BirdResult? Bird)
{
    public static ChangeBirdStatusResult Updated(BirdResult bird) =>
        new(ChangeBirdStatusStatus.Updated, bird);

    public static ChangeBirdStatusResult UserNotFound() =>
        new(ChangeBirdStatusStatus.UserNotFound, null);

    public static ChangeBirdStatusResult BreedingFarmNotSelected() =>
        new(ChangeBirdStatusStatus.BreedingFarmNotSelected, null);

    public static ChangeBirdStatusResult BreedingFarmNotFound() =>
        new(ChangeBirdStatusStatus.BreedingFarmNotFound, null);

    public static ChangeBirdStatusResult BirdNotFound() =>
        new(ChangeBirdStatusStatus.BirdNotFound, null);

    public static ChangeBirdStatusResult ConfirmationRequired() =>
        new(ChangeBirdStatusStatus.ConfirmationRequired, null);

    public static ChangeBirdStatusResult InvalidStatus() =>
        new(ChangeBirdStatusStatus.InvalidStatus, null);

    public static ChangeBirdStatusResult InvalidData() =>
        new(ChangeBirdStatusStatus.InvalidData, null);

    public static ChangeBirdStatusResult StatusChangeNotAllowed() =>
        new(ChangeBirdStatusStatus.StatusChangeNotAllowed, null);

    public static ChangeBirdStatusResult TransferPending() =>
        new(ChangeBirdStatusStatus.TransferPending, null);
}

public sealed record BirdResult(
    Guid BirdId,
    Guid GenealogyRootId,
    Guid BreedingFarmId,
    string Name,
    Guid SpeciesId,
    BirdSex Sex,
    DateOnly? BirthDate,
    DateOnly? DeathDate,
    string? RingNumber,
    Guid? FatherBirdId,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    string? Notes,
    BirdStatus Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetBirdQuery(
    Guid UserId,
    Guid BirdId) : IQuery<GetBirdResult>;

public enum GetBirdStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record GetBirdResult(
    GetBirdStatus Status,
    BirdDetailsResult? Bird)
{
    public static GetBirdResult Succeeded(BirdDetailsResult bird) =>
        new(GetBirdStatus.Success, bird);

    public static GetBirdResult UserNotFound() =>
        new(GetBirdStatus.UserNotFound, null);

    public static GetBirdResult BreedingFarmNotSelected() =>
        new(GetBirdStatus.BreedingFarmNotSelected, null);

    public static GetBirdResult BreedingFarmNotFound() =>
        new(GetBirdStatus.BreedingFarmNotFound, null);

    public static GetBirdResult BirdNotFound() =>
        new(GetBirdStatus.BirdNotFound, null);
}

public sealed record BirdDetailsResult(
    Guid BirdId,
    Guid? GenealogyRootId,
    Guid BreedingFarmId,
    string Name,
    Guid SpeciesId,
    string SpeciesScientificName,
    string SpeciesPopularName,
    BirdSex Sex,
    DateOnly? BirthDate,
    DateOnly? DeathDate,
    string? RingNumber,
    Guid? FatherBirdId,
    BirdParentResult? Father,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    BirdParentResult? Mother,
    string? ExternalMotherName,
    string? Notes,
    BirdStatus Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BirdParentResult(
    Guid BirdId,
    string Name,
    BirdSex Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    BirdStatus Status);

public sealed record ListBirdsQuery(
    Guid UserId,
    string? Search,
    BirdSex? Sex,
    Guid? SpeciesId,
    BirdStatus? Status,
    bool? IdentificationPending,
    BirdSortField SortBy,
    BirdSortDirection SortDirection,
    int Page,
    int PageSize) : IQuery<ListBirdsResult>;

public enum BirdSortField
{
    Name,
    RingNumber,
    BirthDate,
    Species,
    Sex,
    Status,
    CreatedAt
}

public enum BirdSortDirection
{
    Ascending,
    Descending
}

public enum ListBirdsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record ListBirdsResult(
    ListBirdsStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<BirdListItemResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public static ListBirdsResult Succeeded(
        Guid breedingFarmId,
        IReadOnlyCollection<BirdListItemResult> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(ListBirdsStatus.Success, breedingFarmId, items, page, pageSize, totalCount);

    public static ListBirdsResult UserNotFound() =>
        new(ListBirdsStatus.UserNotFound, null, [], 0, 0, 0);

    public static ListBirdsResult BreedingFarmNotSelected() =>
        new(ListBirdsStatus.BreedingFarmNotSelected, null, [], 0, 0, 0);

    public static ListBirdsResult BreedingFarmNotFound() =>
        new(ListBirdsStatus.BreedingFarmNotFound, null, [], 0, 0, 0);
}

public sealed record BirdListItemResult(
    Guid BirdId,
    string Name,
    Guid SpeciesId,
    string SpeciesScientificName,
    string SpeciesPopularName,
    BirdSex Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    BirdStatus Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc);
