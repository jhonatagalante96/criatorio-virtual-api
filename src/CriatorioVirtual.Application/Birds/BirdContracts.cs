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
    string? Notes,
    BirdSex? ExternalFatherSex = null,
    BirdSex? ExternalMotherSex = null) : ICommand<CreateBirdResult>;

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
    TransferPending,
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

    public static UpdateBirdResult TransferPending() =>
        new(UpdateBirdStatus.TransferPending, null);

    public static UpdateBirdResult InvalidData() =>
        new(UpdateBirdStatus.InvalidData, null);
}

public sealed record UpdateBirdGenealogyCommand(
    Guid UserId,
    Guid BirdId,
    Guid? FatherBirdId,
    string? ExternalFatherName,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    BirdSex? ExternalFatherSex = null,
    BirdSex? ExternalMotherSex = null) : ICommand<UpdateBirdGenealogyResult>;

public enum UpdateBirdGenealogyStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    ParentNotFound,
    ParentSexInvalid,
    DuplicateParent,
    CycleDetected,
    TransferPending,
    InvalidData
}

public sealed record UpdateBirdGenealogyResult(
    UpdateBirdGenealogyStatus Status,
    BirdResult? Bird)
{
    public static UpdateBirdGenealogyResult Updated(BirdResult bird) =>
        new(UpdateBirdGenealogyStatus.Updated, bird);

    public static UpdateBirdGenealogyResult UserNotFound() =>
        new(UpdateBirdGenealogyStatus.UserNotFound, null);

    public static UpdateBirdGenealogyResult BreedingFarmNotSelected() =>
        new(UpdateBirdGenealogyStatus.BreedingFarmNotSelected, null);

    public static UpdateBirdGenealogyResult BreedingFarmNotFound() =>
        new(UpdateBirdGenealogyStatus.BreedingFarmNotFound, null);

    public static UpdateBirdGenealogyResult BirdNotFound() =>
        new(UpdateBirdGenealogyStatus.BirdNotFound, null);

    public static UpdateBirdGenealogyResult ParentNotFound() =>
        new(UpdateBirdGenealogyStatus.ParentNotFound, null);

    public static UpdateBirdGenealogyResult ParentSexInvalid() =>
        new(UpdateBirdGenealogyStatus.ParentSexInvalid, null);

    public static UpdateBirdGenealogyResult DuplicateParent() =>
        new(UpdateBirdGenealogyStatus.DuplicateParent, null);

    public static UpdateBirdGenealogyResult CycleDetected() =>
        new(UpdateBirdGenealogyStatus.CycleDetected, null);

    public static UpdateBirdGenealogyResult TransferPending() =>
        new(UpdateBirdGenealogyStatus.TransferPending, null);

    public static UpdateBirdGenealogyResult InvalidData() =>
        new(UpdateBirdGenealogyStatus.InvalidData, null);
}

public sealed record UpdateExternalGenealogyParentCommand(
    Guid UserId,
    Guid BirdId,
    Guid AncestorId,
    string Position,
    Guid? LinkedBirdId,
    string? ExternalName) : ICommand<UpdateExternalGenealogyParentResult>;

public enum UpdateExternalGenealogyParentStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    Forbidden,
    BreedingFarmNotFound,
    BirdNotFound,
    AncestorNotFound,
    ParentNotFound,
    ParentSexInvalid,
    CycleDetected,
    TransferPending,
    InvalidData
}

public sealed record UpdateExternalGenealogyParentResult(UpdateExternalGenealogyParentStatus Status)
{
    public static UpdateExternalGenealogyParentResult Updated() => new(UpdateExternalGenealogyParentStatus.Updated);
    public static UpdateExternalGenealogyParentResult UserNotFound() => new(UpdateExternalGenealogyParentStatus.UserNotFound);
    public static UpdateExternalGenealogyParentResult BreedingFarmNotSelected() => new(UpdateExternalGenealogyParentStatus.BreedingFarmNotSelected);
    public static UpdateExternalGenealogyParentResult Forbidden() => new(UpdateExternalGenealogyParentStatus.Forbidden);
    public static UpdateExternalGenealogyParentResult BreedingFarmNotFound() => new(UpdateExternalGenealogyParentStatus.BreedingFarmNotFound);
    public static UpdateExternalGenealogyParentResult BirdNotFound() => new(UpdateExternalGenealogyParentStatus.BirdNotFound);
    public static UpdateExternalGenealogyParentResult AncestorNotFound() => new(UpdateExternalGenealogyParentStatus.AncestorNotFound);
    public static UpdateExternalGenealogyParentResult ParentNotFound() => new(UpdateExternalGenealogyParentStatus.ParentNotFound);
    public static UpdateExternalGenealogyParentResult ParentSexInvalid() => new(UpdateExternalGenealogyParentStatus.ParentSexInvalid);
    public static UpdateExternalGenealogyParentResult CycleDetected() => new(UpdateExternalGenealogyParentStatus.CycleDetected);
    public static UpdateExternalGenealogyParentResult TransferPending() => new(UpdateExternalGenealogyParentStatus.TransferPending);
    public static UpdateExternalGenealogyParentResult InvalidData() => new(UpdateExternalGenealogyParentStatus.InvalidData);
}

public sealed record DeleteExternalGenealogyParentCommand(
    Guid UserId,
    Guid BirdId,
    Guid AncestorId,
    string Position) : ICommand<DeleteExternalGenealogyParentResult>;

public enum DeleteExternalGenealogyParentStatus
{
    Deleted,
    UserNotFound,
    BreedingFarmNotSelected,
    Forbidden,
    BreedingFarmNotFound,
    BirdNotFound,
    AncestorNotFound,
    TransferPending,
    InvalidData
}

public sealed record DeleteExternalGenealogyParentResult(DeleteExternalGenealogyParentStatus Status)
{
    public static DeleteExternalGenealogyParentResult Deleted() => new(DeleteExternalGenealogyParentStatus.Deleted);
    public static DeleteExternalGenealogyParentResult UserNotFound() => new(DeleteExternalGenealogyParentStatus.UserNotFound);
    public static DeleteExternalGenealogyParentResult BreedingFarmNotSelected() => new(DeleteExternalGenealogyParentStatus.BreedingFarmNotSelected);
    public static DeleteExternalGenealogyParentResult Forbidden() => new(DeleteExternalGenealogyParentStatus.Forbidden);
    public static DeleteExternalGenealogyParentResult BreedingFarmNotFound() => new(DeleteExternalGenealogyParentStatus.BreedingFarmNotFound);
    public static DeleteExternalGenealogyParentResult BirdNotFound() => new(DeleteExternalGenealogyParentStatus.BirdNotFound);
    public static DeleteExternalGenealogyParentResult AncestorNotFound() => new(DeleteExternalGenealogyParentStatus.AncestorNotFound);
    public static DeleteExternalGenealogyParentResult TransferPending() => new(DeleteExternalGenealogyParentStatus.TransferPending);
    public static DeleteExternalGenealogyParentResult InvalidData() => new(DeleteExternalGenealogyParentStatus.InvalidData);
}

public sealed record SearchBirdParentOptionsQuery(
    Guid UserId,
    string? Search,
    BirdSex? Sex,
    int Limit) : IQuery<SearchBirdParentOptionsResult>;

public enum SearchBirdParentOptionsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound
}

public sealed record SearchBirdParentOptionsResult(
    SearchBirdParentOptionsStatus Status,
    Guid? BreedingFarmId,
    IReadOnlyCollection<BirdParentOptionResult> Items)
{
    public static SearchBirdParentOptionsResult Succeeded(
        Guid breedingFarmId,
        IReadOnlyCollection<BirdParentOptionResult> items) =>
        new(SearchBirdParentOptionsStatus.Success, breedingFarmId, items);

    public static SearchBirdParentOptionsResult UserNotFound() =>
        new(SearchBirdParentOptionsStatus.UserNotFound, null, []);

    public static SearchBirdParentOptionsResult BreedingFarmNotSelected() =>
        new(SearchBirdParentOptionsStatus.BreedingFarmNotSelected, null, []);

    public static SearchBirdParentOptionsResult BreedingFarmNotFound() =>
        new(SearchBirdParentOptionsStatus.BreedingFarmNotFound, null, []);
}

public sealed record BirdParentOptionResult(
    Guid BirdId,
    string Name,
    BirdSex Sex,
    DateOnly? BirthDate,
    string? RingNumber);

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

public sealed record ReactivateBirdCommand(
    Guid UserId,
    Guid BirdId) : ICommand<ReactivateBirdResult>;

public enum ReactivateBirdStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    StatusChangeNotAllowed,
    TransferPending
}

public sealed record ReactivateBirdResult(
    ReactivateBirdStatus Status,
    BirdResult? Bird)
{
    public static ReactivateBirdResult Updated(BirdResult bird) =>
        new(ReactivateBirdStatus.Updated, bird);

    public static ReactivateBirdResult UserNotFound() =>
        new(ReactivateBirdStatus.UserNotFound, null);

    public static ReactivateBirdResult BreedingFarmNotSelected() =>
        new(ReactivateBirdStatus.BreedingFarmNotSelected, null);

    public static ReactivateBirdResult BreedingFarmNotFound() =>
        new(ReactivateBirdStatus.BreedingFarmNotFound, null);

    public static ReactivateBirdResult BirdNotFound() =>
        new(ReactivateBirdStatus.BirdNotFound, null);

    public static ReactivateBirdResult StatusChangeNotAllowed() =>
        new(ReactivateBirdStatus.StatusChangeNotAllowed, null);

    public static ReactivateBirdResult TransferPending() =>
        new(ReactivateBirdStatus.TransferPending, null);
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
    BirdSex? ExternalFatherSex,
    Guid? MotherBirdId,
    string? ExternalMotherName,
    BirdSex? ExternalMotherSex,
    string? Notes,
    BirdStatus Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? PrimaryPhotoId = null,
    string? DefaultImageFileName = null);

public sealed record SetBirdPrimaryPhotoCommand(
    Guid UserId,
    Guid BirdId,
    Guid? AttachmentId) : ICommand<SetBirdPrimaryPhotoResult>;

public enum SetBirdPrimaryPhotoStatus
{
    Updated,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    AttachmentNotFound,
    AttachmentNotImage,
    TransferPending,
    InvalidData
}

public sealed record SetBirdPrimaryPhotoResult(
    SetBirdPrimaryPhotoStatus Status,
    Guid? BirdId,
    Guid? PrimaryPhotoId)
{
    public static SetBirdPrimaryPhotoResult Updated(Guid birdId, Guid? primaryPhotoId) =>
        new(SetBirdPrimaryPhotoStatus.Updated, birdId, primaryPhotoId);

    public static SetBirdPrimaryPhotoResult UserNotFound() =>
        new(SetBirdPrimaryPhotoStatus.UserNotFound, null, null);

    public static SetBirdPrimaryPhotoResult BreedingFarmNotSelected() =>
        new(SetBirdPrimaryPhotoStatus.BreedingFarmNotSelected, null, null);

    public static SetBirdPrimaryPhotoResult BreedingFarmNotFound() =>
        new(SetBirdPrimaryPhotoStatus.BreedingFarmNotFound, null, null);

    public static SetBirdPrimaryPhotoResult BirdNotFound() =>
        new(SetBirdPrimaryPhotoStatus.BirdNotFound, null, null);

    public static SetBirdPrimaryPhotoResult AttachmentNotFound() =>
        new(SetBirdPrimaryPhotoStatus.AttachmentNotFound, null, null);

    public static SetBirdPrimaryPhotoResult AttachmentNotImage() =>
        new(SetBirdPrimaryPhotoStatus.AttachmentNotImage, null, null);

    public static SetBirdPrimaryPhotoResult TransferPending() =>
        new(SetBirdPrimaryPhotoStatus.TransferPending, null, null);

    public static SetBirdPrimaryPhotoResult InvalidData() =>
        new(SetBirdPrimaryPhotoStatus.InvalidData, null, null);
}

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

public static class BirdGenealogyLimits
{
    public const int DefaultMaxGenerations = 3;
    public const int MaxGenerations = 6;
}

public sealed record GetBirdGenealogyQuery(
    Guid UserId,
    Guid BirdId,
    int MaxGenerations) : IQuery<GetBirdGenealogyResult>;

public enum GetBirdGenealogyStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record GetBirdGenealogyResult(
    GetBirdGenealogyStatus Status,
    BirdGenealogyResult? Genealogy)
{
    public static GetBirdGenealogyResult Succeeded(BirdGenealogyResult genealogy) =>
        new(GetBirdGenealogyStatus.Success, genealogy);

    public static GetBirdGenealogyResult UserNotFound() =>
        new(GetBirdGenealogyStatus.UserNotFound, null);

    public static GetBirdGenealogyResult BreedingFarmNotSelected() =>
        new(GetBirdGenealogyStatus.BreedingFarmNotSelected, null);

    public static GetBirdGenealogyResult BreedingFarmNotFound() =>
        new(GetBirdGenealogyStatus.BreedingFarmNotFound, null);

    public static GetBirdGenealogyResult BirdNotFound() =>
        new(GetBirdGenealogyStatus.BirdNotFound, null);
}

public sealed record BirdGenealogyResult(
    Guid BreedingFarmId,
    Guid RootBirdId,
    int MaxGenerations,
    bool IsTruncated,
    IReadOnlyCollection<BirdGenealogyNodeResult> Nodes,
    IReadOnlyCollection<BirdGenealogyEdgeResult> Edges);

public enum BirdGenealogyNodeSource
{
    Private,
    Snapshot,
    External
}

public sealed record BirdGenealogyNodeResult(
    string NodeKey,
    Guid? BirdId,
    string Position,
    int Generation,
    string Name,
    BirdSex? Sex,
    DateOnly? BirthDate,
    string? RingNumber,
    BirdStatus? Status,
    BirdGenealogyNodeSource Source,
    bool IsSnapshot,
    bool IsAccessible,
    bool CanNavigate,
    bool CanEdit = false);

public sealed record BirdGenealogyEdgeResult(
    string ChildNodeKey,
    string ParentNodeKey,
    string Position);

public sealed record GetBirdEligibilityQuery(
    Guid UserId,
    Guid BirdId) : IQuery<GetBirdEligibilityResult>;

public enum GetBirdEligibilityStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record GetBirdEligibilityResult(
    GetBirdEligibilityStatus Status,
    BirdEligibilityResult? Bird)
{
    public static GetBirdEligibilityResult Succeeded(BirdEligibilityResult bird) =>
        new(GetBirdEligibilityStatus.Success, bird);

    public static GetBirdEligibilityResult UserNotFound() =>
        new(GetBirdEligibilityStatus.UserNotFound, null);

    public static GetBirdEligibilityResult BreedingFarmNotSelected() =>
        new(GetBirdEligibilityStatus.BreedingFarmNotSelected, null);

    public static GetBirdEligibilityResult BreedingFarmNotFound() =>
        new(GetBirdEligibilityStatus.BreedingFarmNotFound, null);

    public static GetBirdEligibilityResult BirdNotFound() =>
        new(GetBirdEligibilityStatus.BirdNotFound, null);
}

public sealed record BirdEligibilityResult(
    Guid BirdId,
    bool IsEligible,
    bool IdentificationPending,
    IReadOnlyCollection<BirdEligibilityIssueCode> Issues);

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
    BirdSex? ExternalFatherSex,
    Guid? MotherBirdId,
    BirdParentResult? Mother,
    string? ExternalMotherName,
    BirdSex? ExternalMotherSex,
    string? Notes,
    BirdStatus Status,
    bool IdentificationPending,
    int? AgeInYears,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? PrimaryPhotoId = null,
    string? DefaultImageFileName = null);

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
    DateTimeOffset CreatedAtUtc,
    Guid? PrimaryPhotoId = null,
    string? DefaultImageFileName = null);
