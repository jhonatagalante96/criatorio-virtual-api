using CriatorioVirtual.Api;
using Xunit;

namespace CriatorioVirtual.IntegrationTests;

public sealed class PostgreSqlConnectionStringValidatorTests
{
    [Fact]
    public void Validate_DoesNotRequirePersistenceForTheHealthOnlyApplication()
    {
        PostgreSqlConnectionStringValidator.Validate(null);
    }

    [Fact]
    public void Validate_RejectsMalformedConfiguredConnectionString()
    {
        Assert.Throws<ArgumentException>(() => PostgreSqlConnectionStringValidator.Validate("Host=localhost;Invalid"));
    }
}
