using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Domain.Messages;

/// <summary>
/// Uma mensagem da conversa: texto, foto ou os dois. Espelha a tabela
/// <c>conversation.messages</c>. Durante a estadia, as fotos daqui fazem o papel de diário.
/// </summary>
/// <remarks>
/// Quem enviou é guardado pelo lado (<see cref="ConversationParty"/>), não pela conta: a conversa
/// tem exatamente dois lados. A mensagem enviada enquanto a dupla tem uma reserva em andamento
/// leva o id dela (<see cref="BookingId"/>, FK lógica para <c>booking.bookings.id</c>): é o
/// histórico da reserva. A URL da foto vem do bucket de imagens, nunca do cliente.
/// </remarks>
public sealed class Message : Entity<MessageId>
{
    /// <summary>Tamanho máximo do texto.</summary>
    public const int BodyMaxLength = 2000;

    /// <summary>Tamanho máximo da URL da foto.</summary>
    public const int ImageUrlMaxLength = 500;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Message()
    {
    }

    private Message(
        ConversationId conversationId,
        ConversationParty sentBy,
        string? body,
        string? imageUrl,
        Guid? bookingId,
        DateTimeOffset now)
        : base(MessageId.New())
    {
        ConversationId = conversationId;
        SentBy = sentBy;
        Body = body;
        ImageUrl = imageUrl;
        BookingId = bookingId;
        CreatedAt = now;
    }

    public ConversationId ConversationId { get; private set; }

    /// <summary>Quem enviou: o tutor ou o anfitrião da conversa.</summary>
    public ConversationParty SentBy { get; private set; }

    /// <summary>Texto, sem espaços nas pontas. Nulo em foto sem legenda.</summary>
    public string? Body { get; private set; }

    /// <summary>URL pública absoluta da foto no bucket. Nula em mensagem só de texto.</summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Reserva em andamento da dupla no momento do envio (<c>booking.bookings.id</c>).</summary>
    public Guid? BookingId { get; private set; }

    /// <summary>Quando quem recebeu leu. Nulo = não lida (contador). O remetente não vê.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A mensagem tem foto.</summary>
    public bool HasPhoto => ImageUrl is not null;

    /// <summary>Mensagem de texto. Texto vazio ou só com espaços é recusado.</summary>
    public static Result<Message> Text(
        ConversationId conversationId,
        ConversationParty sentBy,
        string? body,
        Guid? bookingId,
        DateTimeOffset now)
    {
        var text = Normalize(body);

        if (text is null)
            return Result<Message>.Failure(ConversationsErrors.BodyRequired);

        if (text.Length > BodyMaxLength)
            return Result<Message>.Failure(ConversationsErrors.BodyTooLong);

        if (HasControlCharacters(text))
            return Result<Message>.Failure(ConversationsErrors.BodyInvalidCharacters);

        return Result<Message>.Success(new Message(conversationId, sentBy, text, imageUrl: null, bookingId, now));
    }

    /// <summary>Mensagem com foto e legenda opcional. Os erros dos dois campos voltam juntos.</summary>
    public static Result<Message> Photo(
        ConversationId conversationId,
        ConversationParty sentBy,
        string? imageUrl,
        string? caption,
        Guid? bookingId,
        DateTimeOffset now)
    {
        var errors = new List<Error>();

        var url = CheckImageUrl(imageUrl);
        if (url.IsFailure)
            errors.AddRange(url.Errors!);

        var text = Normalize(caption);
        if (text is { Length: > BodyMaxLength })
            errors.Add(ConversationsErrors.BodyTooLong);

        if (text is not null && HasControlCharacters(text))
            errors.Add(ConversationsErrors.BodyInvalidCharacters);

        if (errors.Count > 0)
            return Result<Message>.Failure(errors);

        return Result<Message>.Success(new Message(conversationId, sentBy, text, url.Value, bookingId, now));
    }

    /// <summary>
    /// O texto tem caractere de controle além de quebra de linha, tabulação e retorno de carro. Nenhum
    /// deles aparece numa mensagem digitada, e o nulo (<c>\0</c>) o Postgres nem aceita em texto: sem
    /// esta regra, ele virava 500 na gravação. O validador usa a mesma regra para o erro sair no campo.
    /// </summary>
    public static bool HasControlCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    }

    /// <summary>Texto sem espaços nas pontas; vazio vira nulo.</summary>
    private static string? Normalize(string? text)
    {
        var trimmed = text?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static Result<string> CheckImageUrl(string? imageUrl)
    {
        var text = imageUrl?.Trim();

        if (string.IsNullOrEmpty(text))
            return Result<string>.Failure(ConversationsErrors.ImageUrlInvalid);

        if (text.Length > ImageUrlMaxLength)
            return Result<string>.Failure(ConversationsErrors.ImageUrlTooLong);

        // Recusa javascript:, data: e caminho relativo: o front usa isto como src de imagem.
        var isWebAddress = Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        return isWebAddress
            ? Result<string>.Success(text)
            : Result<string>.Failure(ConversationsErrors.ImageUrlInvalid);
    }
}
