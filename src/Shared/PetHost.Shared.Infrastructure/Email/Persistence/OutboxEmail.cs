namespace PetHost.Shared.Infrastructure.Email.Persistence;

/// <summary>
/// Um e-mail na caixa de saída persistente (<c>email.outbox_emails</c>). Sai da tabela assim
/// que é entregue; o que falha fica com a tentativa agendada ou, esgotadas as tentativas,
/// marcado como falho.
/// </summary>
public sealed class OutboxEmail
{
    public const int AddressMaxLength = 320;
    public const int NameMaxLength = 200;
    public const int SubjectMaxLength = 300;
    public const int LastErrorMaxLength = 2000;

    public Guid Id { get; init; }

    public string ToAddress { get; init; } = string.Empty;

    public string? ToName { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string TextBody { get; init; } = string.Empty;

    public string HtmlBody { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Tentativas já iniciadas. Conta na hora em que o despachante pega o e-mail.</summary>
    public int Attempts { get; init; }

    /// <summary>
    /// Quando pode ser pego de novo. Enquanto um despachante tenta entregar, fica no futuro
    /// (a "reserva"): se a API cair no meio, o e-mail volta para a fila quando ela vence.
    /// </summary>
    public DateTimeOffset NextAttemptAt { get; init; }

    public string? LastError { get; init; }

    /// <summary>Preenchido quando as tentativas acabam: o e-mail sai da fila e fica só para consulta.</summary>
    public DateTimeOffset? FailedAt { get; init; }

    public static OutboxEmail From(EmailMessage message, Guid id, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new OutboxEmail
        {
            Id = id,
            ToAddress = message.ToAddress,
            ToName = message.ToName,
            Subject = message.Subject,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            CreatedAt = now,
            NextAttemptAt = now,
        };
    }

    public EmailMessage ToMessage() => new(ToAddress, ToName, Subject, TextBody, HtmlBody);
}
