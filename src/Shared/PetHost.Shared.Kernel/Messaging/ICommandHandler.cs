using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Kernel.Messaging;

/// <summary>Um handler por caso de uso. Sem mediator (§9).</summary>
public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
