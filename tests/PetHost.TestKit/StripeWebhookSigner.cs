using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PetHost.TestKit;

/// <summary>
/// Monta o header <c>Stripe-Signature</c> como o Stripe faz (<c>t=...,v1=HMAC-SHA256</c>), para os
/// testes mandarem webhooks assinados sem a Stripe CLI.
/// </summary>
public static class StripeWebhookSigner
{
    /// <summary>Segredo de webhook usado nos testes.</summary>
    public const string Secret = "whsec_test_pethost_webhook_secret";

    public static string Sign(string payload, string secret = Secret, DateTimeOffset? signedAt = null)
    {
        var timestamp = (signedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    /// <summary>Corpo de um evento <c>account.updated</c> de conta conectada.</summary>
    public static string AccountUpdated(string accountId, string eventId = "evt_test_account_updated") =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2025-08-27.basil",
          "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "livemode": false,
          "type": "account.updated",
          "account": "{{accountId}}",
          "data": { "object": { "id": "{{accountId}}", "object": "account", "payouts_enabled": true } }
        }
        """;
}
