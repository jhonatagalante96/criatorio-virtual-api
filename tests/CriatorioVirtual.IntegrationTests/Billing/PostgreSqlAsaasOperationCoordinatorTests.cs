using CriatorioVirtual.Infrastructure.Billing;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Billing;

public sealed class PostgreSqlAsaasOperationCoordinatorTests
{
    [Fact]
    public async Task SameOperationKey_IsSerializedAcrossIndependentApiInstances()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();

        var options = new DbContextOptionsBuilder<CriatorioVirtualDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        await using var firstDbContext = new CriatorioVirtualDbContext(options);
        await using var secondDbContext = new CriatorioVirtualDbContext(options);
        var firstCoordinator = new PostgreSqlAsaasOperationCoordinator(firstDbContext);
        var secondCoordinator = new PostgreSqlAsaasOperationCoordinator(secondDbContext);

        var firstLease = await firstCoordinator.AcquireAsync("subscription:one-stable-reference");
        var waitingLease = secondCoordinator.AcquireAsync("subscription:one-stable-reference").AsTask();
        var completedTask = await Task.WhenAny(waitingLease, Task.Delay(TimeSpan.FromMilliseconds(200)));

        if (completedTask == waitingLease)
        {
            await using var unexpectedLease = await waitingLease;
            await firstLease.DisposeAsync();
            Assert.Fail("The same operation key was not serialized across database sessions.");
        }

        await firstLease.DisposeAsync();
        await using var secondLease = await waitingLease.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
