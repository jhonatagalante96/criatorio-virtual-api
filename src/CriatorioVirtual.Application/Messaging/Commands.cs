namespace CriatorioVirtual.Application.Messaging;

public interface ICommand<out TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandPreProcessor<in TCommand>
{
    Task Process(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandExecutor
{
    Task<TResult> Execute<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
