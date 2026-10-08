using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Shared.Infrastructure.Validation;

/// <summary>
/// Registra um handler já embrulhado no <see cref="ValidationCommandHandlerDecorator{TCommand,TResponse}"/>.
/// Quem depende de <c>ICommandHandler&lt;,&gt;</c> recebe o decorator; o handler
/// concreto nunca é resolvido direto, então não há caminho que escape da validação.
/// </summary>
public static class CommandHandlerRegistration
{
    public static IServiceCollection AddValidatedCommandHandler<THandler, TCommand, TResponse>(
        this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        services.AddScoped<THandler>();

        services.AddScoped<ICommandHandler<TCommand, TResponse>>(provider =>
            new ValidationCommandHandlerDecorator<TCommand, TResponse>(
                provider.GetRequiredService<THandler>(),
                provider.GetServices<IValidator<TCommand>>()));

        return services;
    }
}
