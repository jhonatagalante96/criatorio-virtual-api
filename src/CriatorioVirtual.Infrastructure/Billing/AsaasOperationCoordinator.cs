namespace CriatorioVirtual.Infrastructure.Billing;

public interface IAsaasOperationCoordinator
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string operationKey,
        CancellationToken cancellationToken = default);
}

public sealed class AsaasOperationCoordinator : IAsaasOperationCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(operationKey, out entry!))
            {
                entry = new Entry();
                _entries.Add(operationKey, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Lease(this, operationKey, entry);
        }
        catch
        {
            ReleaseReference(operationKey, entry, releaseSemaphore: false);
            throw;
        }
    }

    private void Release(string operationKey, Entry entry) =>
        ReleaseReference(operationKey, entry, releaseSemaphore: true);

    private void ReleaseReference(string operationKey, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (_sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 && _entries.TryGetValue(operationKey, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(operationKey);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class Lease(AsaasOperationCoordinator owner, string operationKey, Entry entry) : IAsyncDisposable
    {
        private bool _disposed;

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Release(operationKey, entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
