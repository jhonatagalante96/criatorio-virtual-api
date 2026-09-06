using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Domain.BreedingFarms;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

public sealed class UpdateBreedingFarmSettingsCommandHandler(CriatorioVirtualDbContext dbContext)
    : ICommandHandler<UpdateBreedingFarmSettingsCommand, UpdateBreedingFarmSettingsResult>
{
    public async Task<UpdateBreedingFarmSettingsResult> Handle(
        UpdateBreedingFarmSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var farm = await dbContext.BreedingFarms
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == command.BreedingFarmId &&
                    dbContext.BreedingFarmUsers.Any(membership =>
                        membership.BreedingFarmId == candidate.Id &&
                        membership.UserId == command.UserId &&
                        membership.Role == BreedingFarmRole.Owner &&
                        membership.IsActive),
                cancellationToken);
        if (farm is null)
        {
            return UpdateBreedingFarmSettingsResult.NotFound();
        }

        var officialRegistrationNumber = Normalize(command.OfficialRegistrationNumber);
        if (officialRegistrationNumber is not null &&
            !string.Equals(officialRegistrationNumber, farm.OfficialRegistrationNumber, StringComparison.Ordinal) &&
            await dbContext.BreedingFarms.AnyAsync(
                candidate =>
                    candidate.Id != farm.Id &&
                    candidate.OfficialRegistrationNumber == officialRegistrationNumber,
                cancellationToken))
        {
            return UpdateBreedingFarmSettingsResult.DuplicateOfficialRegistration();
        }

        var address = command.Address is null
            ? new BreedingFarmAddress(null, null, null, null, null, null, null)
            : new BreedingFarmAddress(
                command.Address.Street,
                command.Address.Number,
                command.Address.Complement,
                command.Address.Neighborhood,
                command.Address.City,
                command.Address.State,
                command.Address.PostalCode);

        farm.UpdateSettings(
            command.Name,
            command.ResponsibleName,
            Normalize(command.ContactEmail) ?? farm.ContactEmail,
            command.ContactPhone,
            officialRegistrationNumber,
            address,
            DateTimeOffset.UtcNow);

        return UpdateBreedingFarmSettingsResult.Updated(BreedingFarmSettingsMapping.ToResult(farm));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
