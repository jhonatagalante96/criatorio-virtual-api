namespace CriatorioVirtual.Infrastructure.Persistence;

public static class RelationalNames
{
    public static string PrimaryKey(string table) => Compose("pk", table);

    public static string ForeignKey(string dependentTable, string principalTable, params string[] columns) =>
        Compose("fk", dependentTable, principalTable, JoinColumns(columns));

    public static string Index(string table, params string[] columns) =>
        Compose("ix", table, JoinColumns(columns));

    public static string Check(string table, string rule) => Compose("ck", table, rule);

    private static string JoinColumns(string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Length == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        return string.Join("_", columns);
    }

    private static string Compose(params string[] parts)
    {
        if (parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Database name parts cannot be null, empty, or whitespace.", nameof(parts));
        }

        return string.Join("_", parts).ToLowerInvariant();
    }
}
