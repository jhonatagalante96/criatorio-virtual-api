using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Application.Storage;

namespace CriatorioVirtual.Application.Birds;

public static class BirdAttachmentUploadLimits
{
    public const long MaxFileLength = 10 * 1024 * 1024;

    public const long MaxRequestLength = MaxFileLength + (64 * 1024);
}

public sealed record UploadBirdAttachmentCommand(
    Guid UserId,
    Guid BirdId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content) : ICommand<UploadBirdAttachmentResult>;

public enum UploadBirdAttachmentStatus
{
    Created,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    InvalidData,
    StorageUnavailable
}

public sealed record UploadBirdAttachmentResult(
    UploadBirdAttachmentStatus Status,
    BirdAttachmentResult? Attachment)
{
    public static UploadBirdAttachmentResult Created(BirdAttachmentResult attachment) =>
        new(UploadBirdAttachmentStatus.Created, attachment);

    public static UploadBirdAttachmentResult UserNotFound() =>
        new(UploadBirdAttachmentStatus.UserNotFound, null);

    public static UploadBirdAttachmentResult BreedingFarmNotSelected() =>
        new(UploadBirdAttachmentStatus.BreedingFarmNotSelected, null);

    public static UploadBirdAttachmentResult BreedingFarmNotFound() =>
        new(UploadBirdAttachmentStatus.BreedingFarmNotFound, null);

    public static UploadBirdAttachmentResult BirdNotFound() =>
        new(UploadBirdAttachmentStatus.BirdNotFound, null);

    public static UploadBirdAttachmentResult InvalidData() =>
        new(UploadBirdAttachmentStatus.InvalidData, null);

    public static UploadBirdAttachmentResult StorageUnavailable() =>
        new(UploadBirdAttachmentStatus.StorageUnavailable, null);
}

public sealed record ListBirdAttachmentsQuery(
    Guid UserId,
    Guid BirdId) : IQuery<ListBirdAttachmentsResult>;

public enum ListBirdAttachmentsStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound
}

public sealed record ListBirdAttachmentsResult(
    ListBirdAttachmentsStatus Status,
    Guid? BreedingFarmId,
    Guid? BirdId,
    IReadOnlyCollection<BirdAttachmentResult> Attachments)
{
    public static ListBirdAttachmentsResult Succeeded(
        Guid breedingFarmId,
        Guid birdId,
        IReadOnlyCollection<BirdAttachmentResult> attachments) =>
        new(ListBirdAttachmentsStatus.Success, breedingFarmId, birdId, attachments);

    public static ListBirdAttachmentsResult UserNotFound() =>
        new(ListBirdAttachmentsStatus.UserNotFound, null, null, []);

    public static ListBirdAttachmentsResult BreedingFarmNotSelected() =>
        new(ListBirdAttachmentsStatus.BreedingFarmNotSelected, null, null, []);

    public static ListBirdAttachmentsResult BreedingFarmNotFound() =>
        new(ListBirdAttachmentsStatus.BreedingFarmNotFound, null, null, []);

    public static ListBirdAttachmentsResult BirdNotFound() =>
        new(ListBirdAttachmentsStatus.BirdNotFound, null, null, []);
}

public sealed record GetBirdAttachmentContentQuery(
    Guid UserId,
    Guid BirdId,
    Guid AttachmentId) : IQuery<GetBirdAttachmentContentResult>;

public enum GetBirdAttachmentContentStatus
{
    Success,
    UserNotFound,
    BreedingFarmNotSelected,
    BreedingFarmNotFound,
    BirdNotFound,
    AttachmentNotFound,
    StorageUnavailable
}

public sealed record GetBirdAttachmentContentResult(
    GetBirdAttachmentContentStatus Status,
    BirdAttachmentContent? Content)
{
    public static GetBirdAttachmentContentResult Succeeded(BirdAttachmentContent content) =>
        new(GetBirdAttachmentContentStatus.Success, content);

    public static GetBirdAttachmentContentResult UserNotFound() =>
        new(GetBirdAttachmentContentStatus.UserNotFound, null);

    public static GetBirdAttachmentContentResult BreedingFarmNotSelected() =>
        new(GetBirdAttachmentContentStatus.BreedingFarmNotSelected, null);

    public static GetBirdAttachmentContentResult BreedingFarmNotFound() =>
        new(GetBirdAttachmentContentStatus.BreedingFarmNotFound, null);

    public static GetBirdAttachmentContentResult BirdNotFound() =>
        new(GetBirdAttachmentContentStatus.BirdNotFound, null);

    public static GetBirdAttachmentContentResult AttachmentNotFound() =>
        new(GetBirdAttachmentContentStatus.AttachmentNotFound, null);

    public static GetBirdAttachmentContentResult StorageUnavailable() =>
        new(GetBirdAttachmentContentStatus.StorageUnavailable, null);
}

public sealed record BirdAttachmentResult(
    Guid AttachmentId,
    Guid BirdId,
    string FileName,
    string ContentType,
    long Length,
    DateTimeOffset CreatedAtUtc,
    bool IsPrimary = false);

public sealed record BirdAttachmentContent(
    Guid AttachmentId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content);
