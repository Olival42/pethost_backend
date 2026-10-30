using Microsoft.Extensions.Options;
using Stripe;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>Cria o <see cref="StripeClient"/> uma vez, com a chave secreta da configuração.</summary>
internal sealed class StripeClientProvider(IOptions<StripeOptions> options) : IStripeClientProvider
{
    private readonly Lazy<IStripeClient?> _client = new(() =>
        options.Value.IsConfigured ? new StripeClient(options.Value.SecretKey.Trim()) : null);

    public IStripeClient? Client => _client.Value;

    public string PublishableKey => options.Value.PublishableKey;
}
