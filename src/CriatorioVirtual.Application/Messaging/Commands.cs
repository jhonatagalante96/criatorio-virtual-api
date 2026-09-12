namespace CriatorioVirtual.Application.Messaging;

public interface ICommand<out TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Applies only local state changes. External I/O belongs in an <see cref="ICommandPreProcessor{TCommand}"/>.</summary>
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandPreProcessor<in TCommand>
{
    /// <summary>Performs preparation, including external I/O, before the database transaction starts.</summary>
    Task Process(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandFailureCompensator
{
    /// <summary>Reverts external side effects when the database command fails.</summary>
    Task CompensateAsync(CancellationToken cancellationToken);
}

public interface ICommandExecutor
{
    Task<TResult> Execute<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
