using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public interface IBirdLockCoordinator
{
    Task AcquireLockAsync(Guid birdId, CancellationToken cancellationToken = default);
    Task AcquireLockAsync(Guid birdId, Guid breedingFarmId, CancellationToken cancellationToken = default);
    Task AcquireLocksAsync(IEnumerable<Guid> birdIds, Guid breedingFarmId, CancellationToken cancellationToken = default);
    Task AcquireLocksAsync(IEnumerable<Guid> birdIds, CancellationToken cancellationToken = default);
}

public sealed class BirdLockCoordinator(CriatorioVirtualDbContext dbContext) : IBirdLockCoordinator
{
    public static Func<Guid, Task>? OnLockAcquiredAsync { get; set; }

    public async Task AcquireLockAsync(Guid birdId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} FOR UPDATE",
            cancellationToken);

        if (OnLockAcquiredAsync is not null)
        {
            await OnLockAcquiredAsync(birdId);
        }
    }

    public async Task AcquireLockAsync(Guid birdId, Guid breedingFarmId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);

        if (OnLockAcquiredAsync is not null)
        {
            await OnLockAcquiredAsync(birdId);
        }
    }

    public async Task AcquireLocksAsync(IEnumerable<Guid> birdIds, Guid breedingFarmId, CancellationToken cancellationToken = default)
    {
        var sortedBirdIds = birdIds.Distinct().OrderBy(id => id).ToList();
        foreach (var id in sortedBirdIds)
        {
            await AcquireLockAsync(id, breedingFarmId, cancellationToken);
        }
    }

    public async Task AcquireLocksAsync(IEnumerable<Guid> birdIds, CancellationToken cancellationToken = default)
    {
        var sortedBirdIds = birdIds.Distinct().OrderBy(id => id).ToList();
        foreach (var id in sortedBirdIds)
        {
            await AcquireLockAsync(id, cancellationToken);
        }
    }
}
