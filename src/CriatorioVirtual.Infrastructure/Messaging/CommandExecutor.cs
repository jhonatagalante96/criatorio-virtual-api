using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CriatorioVirtual.Infrastructure.Messaging;

public sealed class CommandExecutor(CriatorioVirtualDbContext dbContext, IServiceProvider serviceProvider) : ICommandExecutor
{
    public async Task<TResult> Execute<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var preProcessor in serviceProvider.GetServices<ICommandPreProcessor<TCommand>>())
        {
            await preProcessor.Process(command, cancellationToken);
        }

        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await handler.Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
