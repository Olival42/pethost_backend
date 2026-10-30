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
/// histórico da reserva. A foto fica no bucket <b>privado</b> (<see cref="ImageKey"/>, a chave
/// gerada pelo storage, nunca do cliente) e só abre por URL assinada, para quem é da conversa.
/// Fotos antigas, de antes do bucket privado, ainda têm a URL pública em <see cref="ImageUrl"/>.
/// </remarks>
public sealed class Message : Entity<MessageId>
{
    /// <summary>Tamanho máximo do texto.</summary>
    public const int BodyMaxLength = 2000;

    /// <summary>Tamanho máximo da URL da foto (fotos antigas, públicas).</summary>
    public const int ImageUrlMaxLength = 500;

    /// <summary>Tamanho máximo da chave da foto no bucket privado.</summary>
    public const int ImageKeyMaxLength = 200;

    /// <summary>Pasta das fotos do chat no bucket privado (<c>PrivateImageFolders.Conversations</c>).</summary>
    public const string ImageKeyPrefix = "conversations/";

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private Message()
    {
    }

    private Message(
        ConversationId conversationId,
        ConversationParty sentBy,
        string? body,
        string? imageKey,
        Guid? bookingId,
        DateTimeOffset now,
        MessageId id)
        : base(id)
    {
        ConversationId = conversationId;
        SentBy = sentBy;
        Body = body;
        ImageKey = imageKey;
        BookingId = bookingId;
        CreatedAt = now;
    }

    public ConversationId ConversationId { get; private set; }

    /// <summary>Quem enviou: o tutor ou o anfitrião da conversa.</summary>
    public ConversationParty SentBy { get; private set; }

    /// <summary>Texto, sem espaços nas pontas. Nulo em foto sem legenda.</summary>
    public string? Body { get; private set; }

    /// <summary>
    /// URL pública da foto, só nas mensagens de antes do bucket privado. As novas usam
    /// <see cref="ImageKey"/>.
    /// </summary>
    public string? ImageUrl { get; private set; }

    /// <summary>Chave da foto no bucket privado. Nula em mensagem só de texto (e nas fotos antigas).</summary>
    public string? ImageKey { get; private set; }

    /// <summary>Reserva em andamento da dupla no momento do envio (<c>booking.bookings.id</c>).</summary>
    public Guid? BookingId { get; private set; }

    /// <summary>Quando quem recebeu leu. Nulo = não lida (contador). O remetente não vê.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A mensagem tem foto.</summary>
    public bool HasPhoto => ImageUrl is not null || ImageKey is not null;

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

        return Result<Message>.Success(new Message(conversationId, sentBy, text, imageKey: null, bookingId, now, MessageId.New()));
    }

    /// <summary>Mensagem com foto e legenda opcional. Os erros dos dois campos voltam juntos.</summary>
    /// <param name="imageKey">Chave da foto no bucket privado.</param>
    /// <param name="id">
    /// Id da mensagem; sem ele, um novo. A foto do upload direto usa o id do upload: confirmar de
    /// novo acha a mesma mensagem em vez de criar outra.
    /// </param>
    public static Result<Message> Photo(
        ConversationId conversationId,
        ConversationParty sentBy,
        string? imageKey,
        string? caption,
        Guid? bookingId,
        DateTimeOffset now,
        MessageId? id = null)
    {
        var errors = new List<Error>();

        var key = CheckImageKey(imageKey);
        if (key.IsFailure)
            errors.AddRange(key.Errors!);

        var text = Normalize(caption);
        if (text is { Length: > BodyMaxLength })
            errors.Add(ConversationsErrors.BodyTooLong);

        if (text is not null && HasControlCharacters(text))
            errors.Add(ConversationsErrors.BodyInvalidCharacters);

        if (errors.Count > 0)
            return Result<Message>.Failure(errors);

        return Result<Message>.Success(new Message(conversationId, sentBy, text, key.Value, bookingId, now, id ?? MessageId.New()));
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

    private static Result<string> CheckImageKey(string? imageKey)
    {
        var text = imageKey?.Trim();

        // A chave vem do storage, não do cliente: estes erros só aparecem com o storage mal configurado.
        if (string.IsNullOrEmpty(text) || !text.StartsWith(ImageKeyPrefix, StringComparison.Ordinal) || text.Contains("..", StringComparison.Ordinal))
            return Result<string>.Failure(ConversationsErrors.ImageKeyInvalid);

        return text.Length > ImageKeyMaxLength
            ? Result<string>.Failure(ConversationsErrors.ImageKeyTooLong)
            : Result<string>.Success(text);
    }
}
