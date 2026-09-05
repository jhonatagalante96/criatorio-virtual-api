using CriatorioVirtual.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CriatorioVirtual.Infrastructure.Messaging;

public sealed class QueryExecutor(IServiceProvider serviceProvider) : IQueryExecutor
{
    public Task<TResult> Execute<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>
    {
        ArgumentNullException.ThrowIfNull(query);

        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return handler.Handle(query, cancellationToken);
    }
}
