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

public sealed record BreedingFarmAddressResult(
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State,
    string? PostalCode);

public sealed record BreedingFarmSettingsResult(
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName,
    string ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressResult Address,
    DateTimeOffset UpdatedAtUtc);

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

public sealed record GetBreedingFarmSettingsQuery(
    Guid UserId,
    Guid BreedingFarmId) : IQuery<BreedingFarmSettingsResult?>;

public enum UpdateBreedingFarmSettingsStatus
{
    Updated,
    NotFound,
    DuplicateOfficialRegistration
}

public sealed record UpdateBreedingFarmSettingsCommand(
    Guid UserId,
    Guid BreedingFarmId,
    string Name,
    string ResponsibleName,
    string? ContactEmail,
    string? ContactPhone,
    string? OfficialRegistrationNumber,
    BreedingFarmAddressInput? Address) : ICommand<UpdateBreedingFarmSettingsResult>;

public sealed record UpdateBreedingFarmSettingsResult(
    UpdateBreedingFarmSettingsStatus Status,
    BreedingFarmSettingsResult? Settings)
{
    public static UpdateBreedingFarmSettingsResult Updated(BreedingFarmSettingsResult settings) =>
        new(UpdateBreedingFarmSettingsStatus.Updated, settings);

    public static UpdateBreedingFarmSettingsResult NotFound() =>
        new(UpdateBreedingFarmSettingsStatus.NotFound, null);

    public static UpdateBreedingFarmSettingsResult DuplicateOfficialRegistration() =>
        new(UpdateBreedingFarmSettingsStatus.DuplicateOfficialRegistration, null);
}
