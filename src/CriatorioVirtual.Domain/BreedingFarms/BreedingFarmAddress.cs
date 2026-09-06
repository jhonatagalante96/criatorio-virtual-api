namespace CriatorioVirtual.Domain.BreedingFarms;

public sealed class BreedingFarmAddress
{
    private BreedingFarmAddress()
    {
    }

    public BreedingFarmAddress(
        string? street,
        string? number,
        string? complement,
        string? neighborhood,
        string? city,
        string? state,
        string? postalCode)
    {
        Street = Normalize(street);
        Number = Normalize(number);
        Complement = Normalize(complement);
        Neighborhood = Normalize(neighborhood);
        City = Normalize(city);
        State = Normalize(state);
        PostalCode = Normalize(postalCode);
    }

    public string? Street { get; private set; }

    public string? Number { get; private set; }

    public string? Complement { get; private set; }

    public string? Neighborhood { get; private set; }

    public string? City { get; private set; }

    public string? State { get; private set; }

    public string? PostalCode { get; private set; }

    public bool IsEmpty =>
        Street is null &&
        Number is null &&
        Complement is null &&
        Neighborhood is null &&
        City is null &&
        State is null &&
        PostalCode is null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
