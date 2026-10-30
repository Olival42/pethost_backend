namespace PetHost.Modules.Conversations.Application.Messages.Responses;

/// <summary>
/// Uma página de mensagens, da mais nova para a mais antiga. Para a página seguinte (mais
/// antiga), o cliente manda o id da última mensagem desta em <c>before</c>.
/// </summary>
/// <param name="HasMore">Há mensagens mais antigas que as desta página.</param>
public sealed record MessagePageResponse(IReadOnlyList<MessageResponse> Items, int PageSize, bool HasMore);
