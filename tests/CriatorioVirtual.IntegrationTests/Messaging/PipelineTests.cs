using System.Data;
using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using CriatorioVirtual.IntegrationTests.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography.X509Certificates;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriatorioVirtual.IntegrationTests.Messaging;

public sealed class PipelineTests
{
    [Fact]
    public async Task FailedCommand_RollsBackAndPostProcessorRunsAfterCommittedConnectionCloses()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();

        using var certificate = TestCertificate.Create();
        var services = CreateServices(database.GetConnectionString(), certificate);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
            await context.Database.MigrateAsync();

            var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.Execute<FailingCommand, bool>(new FailingCommand()));
            Assert.True(await executor.Execute<PostProcessorProbeCommand, bool>(new PostProcessorProbeCommand()));
        }

        await using var connection = new Npgsql.NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'app' AND table_name = 'pipeline_probe')", connection);
        Assert.False((bool)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Query_DoesNotStartAnExplicitTransaction()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await database.StartAsync();

        using var certificate = TestCertificate.Create();
        var services = CreateServices(database.GetConnectionString(), certificate);
        await using var scope = services.BuildServiceProvider().CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CriatorioVirtualDbContext>();
        await context.Database.MigrateAsync();

        var executor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();

        Assert.True(await executor.Execute<TransactionProbeQuery, bool>(new TransactionProbeQuery()));
    }

    private static ServiceCollection CreateServices(string connectionString, X509Certificate2 dataProtectionCertificate)
    {
        var services = new ServiceCollection();
        services.AddInfrastructurePersistence(connectionString, dataProtectionCertificate);
        services.RemoveAll<ICommandFailureCompensator>();
        services.AddScoped<ICommandPreProcessor<FailingCommand>, SlowExternalPreProcessor>();
        services.AddScoped<ICommandHandler<FailingCommand, bool>, FailingCommandHandler>();
        services.AddScoped<ICommandHandler<PostProcessorProbeCommand, bool>, PostProcessorProbeHandler>();
        services.AddScoped<ICommandPostProcessor<PostProcessorProbeCommand, bool>, PostProcessorProbe>();
        services.AddScoped<IQueryHandler<TransactionProbeQuery, bool>, TransactionProbeQueryHandler>();
        return services;
    }

    private sealed record FailingCommand : ICommand<bool>;

    private sealed class FailingCommandHandler(CriatorioVirtualDbContext context) : ICommandHandler<FailingCommand, bool>
    {
        public async Task<bool> Handle(FailingCommand command, CancellationToken cancellationToken)
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            await context.Database.ExecuteSqlRawAsync("CREATE TABLE app.pipeline_probe (id integer NOT NULL)", cancellationToken);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO app.pipeline_probe (id) VALUES (1)", cancellationToken);
            throw new InvalidOperationException("Expected command failure.");
        }
    }

    private sealed class SlowExternalPreProcessor(CriatorioVirtualDbContext context) : ICommandPreProcessor<FailingCommand>
    {
        public async Task Process(FailingCommand command, CancellationToken cancellationToken)
        {
            Assert.Null(context.Database.CurrentTransaction);
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }

    private sealed record PostProcessorProbeCommand : ICommand<bool>;

    private sealed class PostProcessorProbeHandler : ICommandHandler<PostProcessorProbeCommand, bool>
    {
        public Task<bool> Handle(PostProcessorProbeCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class PostProcessorProbe(CriatorioVirtualDbContext context)
        : ICommandPostProcessor<PostProcessorProbeCommand, bool>
    {
        public Task<bool> Process(
            PostProcessorProbeCommand command,
            bool result,
            CancellationToken cancellationToken)
        {
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            return Task.FromResult(result);
        }
    }

    private sealed record TransactionProbeQuery : IQuery<bool>;

    private sealed class TransactionProbeQueryHandler(CriatorioVirtualDbContext context) : IQueryHandler<TransactionProbeQuery, bool>
    {
        public Task<bool> Handle(TransactionProbeQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(context.Database.CurrentTransaction is null);
    }
}
