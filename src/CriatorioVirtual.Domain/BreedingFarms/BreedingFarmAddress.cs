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
        State = NormalizeState(state);
        PostalCode = NormalizePostalCode(postalCode);
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

    private static string? NormalizeState(string? value)
    {
        var normalized = Normalize(value)?.ToUpperInvariant();
        if (normalized is not null &&
            (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException("The state must contain exactly two letters.", nameof(value));
        }

        return normalized;
    }

    private static string? NormalizePostalCode(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Any(character => !char.IsDigit(character) && character is not '-' and not ' '))
        {
            throw new ArgumentException("The postal code must contain only digits and optional separators.", nameof(value));
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
        {
            throw new ArgumentException("The postal code must contain eight digits.", nameof(value));
        }

        return digits;
    }
}
