using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.StartConversation;

/// <summary>
/// O tutor toca em "Puxar conversa" no cantinho. Uma conversa por dupla tutor–anfitrião: se já
/// existe, volta a mesma.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="ListingId">O cantinho onde o tutor tocou; o anfitrião sai dele.</param>
public sealed record StartConversationCommand(Guid UserId, Guid ListingId) : ICommand<StartedConversation>;
