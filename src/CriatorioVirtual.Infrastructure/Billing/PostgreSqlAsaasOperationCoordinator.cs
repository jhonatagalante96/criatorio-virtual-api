using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CriatorioVirtual.Infrastructure.Billing;

public sealed class PostgreSqlAsaasOperationCoordinator(CriatorioVirtualDbContext dbContext)
    : IAsaasOperationCoordinator
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        var (key1, key2) = GetAdvisoryLockKeys(operationKey);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await ExecuteAdvisoryLockCommandAsync(
                "SELECT pg_advisory_lock(@key1, @key2)",
                key1,
                key2,
                cancellationToken);
            return new Lease(dbContext, key1, key2);
        }
        catch
        {
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }
    }

    private async Task ExecuteAdvisoryLockCommandAsync(
        string commandText,
        int key1,
        int key2,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();

        var firstKey = command.CreateParameter();
        firstKey.ParameterName = "key1";
        firstKey.DbType = DbType.Int32;
        firstKey.Value = key1;
        command.Parameters.Add(firstKey);

        var secondKey = command.CreateParameter();
        secondKey.ParameterName = "key2";
        secondKey.DbType = DbType.Int32;
        secondKey.Value = key2;
        command.Parameters.Add(secondKey);

        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static (int First, int Second) GetAdvisoryLockKeys(string operationKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(operationKey));
        return (
            BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(0, sizeof(int))),
            BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(sizeof(int), sizeof(int))));
    }

    private sealed class Lease(CriatorioVirtualDbContext dbContext, int key1, int key2) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                var coordinator = new PostgreSqlAsaasOperationCoordinator(dbContext);
                await coordinator.ExecuteAdvisoryLockCommandAsync(
                    "SELECT pg_advisory_unlock(@key1, @key2)",
                    key1,
                    key2,
                    CancellationToken.None);
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }
}
