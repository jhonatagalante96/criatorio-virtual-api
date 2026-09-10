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
    DateTimeOffset CreatedAtUtc);
