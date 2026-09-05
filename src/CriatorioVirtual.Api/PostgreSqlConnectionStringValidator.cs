using Npgsql;

namespace CriatorioVirtual.Api;

public static class PostgreSqlConnectionStringValidator
{
    public static void Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        _ = new NpgsqlConnectionStringBuilder(connectionString);
    }
}
