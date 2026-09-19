using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriatorioVirtual.Infrastructure.Birds;

public interface IBirdLockCoordinator
{
    Task AcquireLockAsync(Guid birdId, CancellationToken cancellationToken = default);
    Task AcquireLockAsync(Guid birdId, Guid breedingFarmId, CancellationToken cancellationToken = default);
}

public sealed class BirdLockCoordinator(CriatorioVirtualDbContext dbContext) : IBirdLockCoordinator
{
    public async Task AcquireLockAsync(Guid birdId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} FOR UPDATE",
            cancellationToken);
    }

    public async Task AcquireLockAsync(Guid birdId, Guid breedingFarmId, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM app.birds WHERE \"Id\" = {birdId} AND \"BreedingFarmId\" = {breedingFarmId} FOR UPDATE",
            cancellationToken);
    }
}
