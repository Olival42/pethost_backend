using PetHost.Modules.Listings.Application.Abstractions;

namespace PetHost.Modules.Listings.Application.Listings.Responses;

/// <summary>
/// Os cantinhos do anfitrião logado, ativos e inativos. O anfitrião vem <b>uma vez</b>, no topo
/// — todos são dele —, e cada cantinho sai sem <c>host</c> (regra de lista "de uma pessoa", §13).
/// </summary>
/// <param name="Host">Nome e foto do anfitrião.</param>
/// <param name="Listings">Do mais novo para o mais antigo, na visão do dono. Lista vazia quando não há.</param>
public sealed record MyListingsResponse(ListingHostSummary? Host, IReadOnlyList<ListingResponse> Listings);
