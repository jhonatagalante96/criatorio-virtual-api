using CriatorioVirtual.Application.Messaging;

namespace CriatorioVirtual.Application.BreedingFarms;

public sealed record BreedingFarmAddressInput(
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State,
    string? PostalCode);

public sealed record CreateBreedingFarmCommand(
    Guid UserId,
    string? Name,
    string? ResponsibleName,
    string? ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressInput? Address) : ICommand<CreateBreedingFarmResult>;

public enum CreateBreedingFarmStatus
{
    Created,
    AccountNotFound,
    DuplicateOfficialRegistration
}

public sealed record CreateBreedingFarmResult(
    CreateBreedingFarmStatus Status,
    Guid? BreedingFarmId,
    Guid? OwnerUserId)
{
    public static CreateBreedingFarmResult Created(Guid breedingFarmId, Guid ownerUserId) =>
        new(CreateBreedingFarmStatus.Created, breedingFarmId, ownerUserId);

    public static CreateBreedingFarmResult AccountNotFound() =>
        new(CreateBreedingFarmStatus.AccountNotFound, null, null);

    public static CreateBreedingFarmResult DuplicateOfficialRegistration() =>
        new(CreateBreedingFarmStatus.DuplicateOfficialRegistration, null, null);
}
