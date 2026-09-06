using CriatorioVirtual.Application.BreedingFarms;
using CriatorioVirtual.Domain.BreedingFarms;

namespace CriatorioVirtual.Infrastructure.BreedingFarms;

internal static class BreedingFarmSettingsMapping
{
    public static BreedingFarmSettingsResult ToResult(BreedingFarm farm) =>
        new(
            farm.Id,
            farm.Name,
            farm.ResponsibleName,
            farm.ContactEmail,
            farm.ContactPhone,
            farm.OfficialRegistrationNumber,
            new BreedingFarmAddressResult(
                farm.Address.Street,
                farm.Address.Number,
                farm.Address.Complement,
                farm.Address.Neighborhood,
                farm.Address.City,
                farm.Address.State,
                farm.Address.PostalCode),
            farm.UpdatedAtUtc);
}
