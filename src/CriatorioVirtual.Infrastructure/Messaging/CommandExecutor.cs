using CriatorioVirtual.Application.Messaging;
using CriatorioVirtual.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;

namespace CriatorioVirtual.Infrastructure.Messaging;

public sealed class CommandExecutor(CriatorioVirtualDbContext dbContext, IServiceProvider serviceProvider) : ICommandExecutor
{
    public async Task<TResult> Execute<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);

        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        var preProcessors = serviceProvider.GetServices<ICommandPreProcessor<TCommand>>().ToArray();
        var postProcessors = serviceProvider.GetServices<ICommandPostProcessor<TCommand, TResult>>().ToArray();
        var compensators = serviceProvider.GetServices<ICommandFailureCompensator>().ToArray();
        IDbContextTransaction? transaction = null;
        var committed = false;
        try
        {
            foreach (var preProcessor in preProcessors)
            {
                await preProcessor.Process(command, cancellationToken);
            }

            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var result = await handler.Handle(command, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            foreach (var postProcessor in postProcessors)
            {
                result = await postProcessor.Process(command, result, CancellationToken.None);
            }

            return result;
        }
        catch
        {
            if (!committed && transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            if (!committed)
            {
                foreach (var compensator in compensators)
                {
                    try
                    {
                        await compensator.CompensateAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Preserve the original command failure; compensators are best effort.
                    }
                }
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
