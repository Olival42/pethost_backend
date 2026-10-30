using Stripe;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// O cliente da API do Stripe, quando há chave configurada. Os módulos usam isto em vez de
/// montar o próprio cliente: um cliente (e um pool de conexões) para a API inteira.
/// </summary>
public interface IStripeClientProvider
{
    /// <summary>O cliente, ou <c>null</c> sem <see cref="StripeOptions.SecretKey"/>.</summary>
    IStripeClient? Client { get; }

    /// <summary>A chave publicável, que acompanha o <c>clientSecret</c> dos componentes embutidos.</summary>
    string PublishableKey { get; }
}
