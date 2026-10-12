# PetHost — Módulo Listings (cantinhos)

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md), o [`modulo-hosts.md`](modulo-hosts.md) e o [`dicionario-de-dados.md`](dicionario-de-dados.md)_

O **cantinho** é o espaço que o anfitrião oferece — o "quarto" da metáfora do hotel. Tem descrição, endereço (com a coordenada), quem mora na casa, o que aceita, capacidade, diária, os **dias da semana** em que recebe pets e até 5 fotos em ordem. O tutor encontra cantinhos pela **busca por raio** (5 a 50 km do endereço dele) e abre o detalhe.

## Sumário

1. [Quem tem cantinho](#1-quem-tem-cantinho)
2. [Estrutura](#2-estrutura)
3. [Endpoints](#3-endpoints)
4. [Regras](#4-regras)
5. [Busca por raio e geocodificação](#5-busca-por-raio-e-geocodificação)
6. [Fotos (até 5, em ordem)](#6-fotos-até-5-em-ordem)
7. [Banco](#7-banco)
8. [Comunicação com outros módulos](#8-comunicação-com-outros-módulos)
9. [Testes](#9-testes)
10. [Decisões e pendências](#10-decisões-e-pendências)

---

## 1. Quem tem cantinho

Só **anfitrião verificado** (perfil em `host.hosts`, que nasce na aprovação do admin). O cantinho guarda o **id do anfitrião** (`host_id` → `host.hosts.id`, FK lógica), nunca o da conta; o anfitrião sai **sempre do token**.

| Anfitrião | Quantos cantinhos | Tipo da casa | Capacidade |
|---|---|---|---|
| **Pessoa física** | **1** (ativo ou inativo — reative em vez de criar outro) | obrigatório (`house` ou `apartment`) | 1 a **10** pets |
| **Pessoa jurídica** (empresa) | **vários**, um por filial | opcional (filial é espaço comercial) | 1 a **200** pets por filial |

- Conta de anfitrião sem perfil (ainda não aprovada): `404 LISTING_HOST_NOT_FOUND`.
- Anfitrião inativo ou suspenso não cadastra nem reativa cantinho: `403 LISTING_HOST_DEACTIVATED`.
- Pessoa física com cantinho tentando outro: `422 LISTING_LIMIT_REACHED`.

## 2. Estrutura

```
src/Modules/Listings/
├── Domain/          Listing, ListingId, ListingDetails (+ Data), ListingAddress (+ Data), GeoPoint,
│                    ListingPhoto, ListingHost, HomeType, AcceptedSpecies, Weekdays (+ *Values),
│                    NearbySearch, ListingsErrors, IListingRepository, IListingsUnitOfWork
├── Application/     CreateListing, UpdateListing (+ ListingPatch), DeactivateListing, ReactivateListing,
│                    GetListingById, ListMyListings, SearchListings, Add/Replace/Remove/ReorderListingPhoto(s),
│                    ListingAccess, ListingLocator, portas IListingHosts, IAccountAddresses, IGeocoder
├── Infrastructure/  ListingsDbContext (schema listing), configurações, ListingRepository (busca por raio),
│                    ListingHosts / AccountAddresses (adaptadores), NominatimGeocoder + CachedGeocoder
└── Presentation/    ListingsController
```

## 3. Endpoints

| Método | Rota | Quem | O quê |
|---|---|---|---|
| `POST` | `/api/v1/listings` | host | Cadastra um cantinho. `201` + `Location`. |
| `GET` | `/api/v1/listings?radiusKm=10[&latitude=&longitude=][&page=&pageSize=]` | qualquer logado | Busca por raio, do mais perto ao mais longe. Paginada. |
| `GET` | `/api/v1/listings/me` | host | Todos os cantinhos do anfitrião logado (ativos e inativos), `{ host, listings }`, sem paginação. |
| `GET` | `/api/v1/listings/{listingId}` | qualquer logado | Um cantinho. Dono: tudo, inclusive inativo. Outros: visão pública, só se ativo. |
| `PATCH` | `/api/v1/listings/{listingId}` | host dono | Altera (só o que vier). Endereço novo recalcula a coordenada. |
| `POST` | `/api/v1/listings/{listingId}/deactivation` | host dono | Inativa. Idempotente. |
| `DELETE` | `/api/v1/listings/{listingId}/deactivation` | host dono | Reativa. Idempotente. |
| `POST` | `/api/v1/listings/{listingId}/photos` | host dono | Adiciona foto (multipart, parte `file`), no fim da fila. |
| `PUT` | `/api/v1/listings/{listingId}/photos/order` | host dono | Reordena: `{ "photoIds": [...] }` com todas as fotos. |
| `PATCH` | `/api/v1/listings/{listingId}/photos/{photoId}` | host dono | Substitui a imagem (mesmo id e posição). |
| `DELETE` | `/api/v1/listings/{listingId}/photos/{photoId}` | host dono | Tira a foto; as de trás sobem uma posição. |

"Por id" serve aos dois casos pedidos: o anfitrião logado vê o **seu** cantinho completo (inclusive inativo) e qualquer outra conta vê o cantinho ativo na visão pública. Cantinho de outro anfitrião nas rotas de alteração responde `404 LISTING_NOT_FOUND` (§8: recurso de outra conta é 404).

### 3.1 Corpo do cadastro / PATCH

```json
{
  "title": "Casa com quintal na Zona 7",
  "description": "Casa térrea, quintal gramado e muito carinho.",
  "homeType": "house",
  "hasYard": true, "hasDogs": true, "hasCats": false, "hasKids": false,
  "acceptedSpecies": ["dog", "cat"],
  "acceptsSmall": true, "acceptsMedium": true, "acceptsLarge": false,
  "capacity": 3,
  "nightlyRate": 60.50,
  "houseRules": "Nada de subir no sofá.",
  "availableWeekdays": ["monday", "tuesday", "saturday"],
  "address": {
    "zipCode": "87020-000", "street": "Rua das Flores", "number": "120",
    "complement": null, "neighborhood": "Zona 7", "city": "Maringá", "state": "PR"
  }
}
```

- `nightlyRate` é **decimal em reais** (2 casas, de 10.00 a 10000.00), por pet e por noite.
- `acceptedSpecies` usa os mesmos valores de `pets.species`; `availableWeekdays`, `monday` … `sunday`. Na resposta saem na ordem fixa (tipos na ordem da lista; dias de segunda a domingo).
- **Não** se manda latitude/longitude: a API geocodifica o endereço.
- PATCH: `null`/ausente mantém; `""` limpa opcional; lista enviada substitui a atual; o endereço também é parcial.

### 3.2 Resposta

`ListingResponse`: `id`, `hostId`, `host` (`{ id, name, avatarUrl, isCompany, isAvailable }` — empresa pelo nome fantasia), os campos acima, `address`, `location` (`{ latitude, longitude }`), `photos` (`[{ id, url, position }]`), `isActive`, `deactivatedAt`, `createdAt`, `updatedAt`.

**Visão pública** (quem não é dono nem admin): `address` só com `neighborhood`, `city` e `state`; sem `zipCode`, `street`, `number`, `complement` e sem `location` (escopo, regra 7: endereço só depois do pagamento; "Bairro, não coordenada").

Item da busca (`ListingSummaryResponse`): `id`, `hostId`, `host`, `title`, `neighborhood`, `city`, `state`, **`distanceKm`** (1 casa), `nightlyRate`, `capacity`, `homeType`, `hasYard`, `acceptedSpecies`, `accepts*`, `availableWeekdays`, `coverPhotoUrl`. Nunca endereço nem coordenada.

## 4. Regras

Validadas no domínio (`ListingDetails`, `ListingAddress`), com **todos os erros juntos num só 400**. O validador do comando é trivial: as regras dependem de o anfitrião ser PF ou PJ, e o validador não consulta outro módulo (§10).

| Campo | Regra |
|---|---|
| `title` | obrigatório, até 80 |
| `description` | obrigatório, até 4000 |
| `homeType` | `house`/`apartment`; obrigatório só para pessoa física |
| `hasYard`, `hasDogs`, `hasCats`, `hasKids` | obrigatórios |
| `acceptedSpecies` | ao menos um, valores conhecidos |
| `acceptsSmall/Medium/Large` | obrigatórios; com cachorro ou gato aceito, ao menos um `true` |
| `capacity` | 1 a 10 (PF) ou 1 a 200 (PJ) |
| `nightlyRate` | 10.00 a 10000.00, no máximo 2 casas |
| `houseRules` | opcional, até 2000 |
| `availableWeekdays` | ao menos um dia, valores conhecidos |
| `address.*` | CEP com 8 dígitos (aceita máscara), rua (120), número (10, "S/N" aceito), complemento opcional (60), bairro (80), cidade (80), UF válida |

Inativar é idempotente e não apaga nada (escopo, restrição 9). Cantinho inativo pode ser editado e ter fotos trocadas pelo dono. Mudança de estado vai para a trilha: `listing.created`, `listing.updated` (campos pelo nome, ou `photos.added/replaced/removed/reordered`), `listing.deactivated`, `listing.reactivated`.

## 5. Busca por raio e geocodificação

**Origem da busca:** se o cliente manda `latitude` e `longitude` (as duas juntas), é esse ponto (ex.: GPS do celular); só uma das duas: `400` em `latitude`. Sem elas, é o **endereço da conta logada** (`IUserDirectory`), geocodificado. Conta sem endereço: `422 LISTING_SEARCH_ADDRESS_REQUIRED`; endereço fora do mapa: `422 LISTING_SEARCH_ADDRESS_NOT_LOCATED`.

**Raio:** `radiusKm` de **5 a 50** (padrão 10). Fora disso, `400` no campo `radiusKm`.

**O que aparece:** só cantinhos **ativos** de anfitrião **ativo e sem suspensão** (`IHostDirectory.GetUnavailableIdsAsync`), ordenados pela distância (empate: mais novo primeiro). Paginação padrão (§13).

**No banco** (`ListingRepository.SearchNearbyAsync`): (1) uma caixa em volta do círculo filtra por `latitude`/`longitude` (índice parcial `ix_listings_latitude_longitude_active`); (2) a distância exata é calculada no Postgres pela fórmula de **haversine** (`asin`, `sin`, `cos`, `power`, `LEAST`); (3) conta, ordena e pagina pela distância; (4) carrega só os cantinhos da página, com as fotos. Sem PostGIS: para o volume do MVP (uma cidade) é suficiente.

**Geocodificação** (`IGeocoder` → `CachedGeocoder` → `NominatimGeocoder`):

- **Nominatim (OpenStreetMap)**, gratuito e sem chave. O mapa do OSM no Brasil nem sempre tem número ou CEP, então tenta do mais preciso ao mais grosso e usa o primeiro que achar: rua + número + cidade + UF → rua, bairro, cidade → bairro, cidade → CEP. O bairro basta para "2 km de você".
- Política de uso respeitada: `User-Agent` identificando a aplicação e **no máximo 1 pedido por segundo** (`GeocodingRateGate`, fila única por instância).
- **Cache no Redis** (vale para todas as instâncias da API): endereço achado fica 7 dias; "não achou", 1 hora; falha não é guardada. A busca pelo endereço da conta consulta o mapa uma vez por endereço. Chave `pethost:listing:geocoding:<sha256 do endereço normalizado>` — o endereço não vai em texto para o Redis —, valor `found|lat|lng` ou `not-found`, expiração pelo TTL da chave. Redis fora do ar não derruba o pedido: a consulta vai direto ao mapa (log `Warning`). A conexão é a do `Shared` (`AddRedis`), a mesma do Auth.
- Serviço fora do ar ou lento: `503 LISTING_GEOCODER_UNAVAILABLE` (sufixo `_UNAVAILABLE` → 503 no mapeador). Nada é gravado.
- No cadastro, a validação e o limite PF são conferidos **antes** da consulta: nenhum pedido ao mapa é gasto com cadastro que seria recusado. No PATCH o mapa só é consultado se o endereço mudou.

Configuração (seção `Geocoding`, todos com padrão):

| Chave | Padrão | Para quê |
|---|---|---|
| `BaseUrl` | `https://nominatim.openstreetmap.org` | Troque por instância própria se o volume crescer |
| `UserAgent` | `PetHost/1.0 (projeto academico)` | Obrigatório pela política |
| `Email` | — | Contato opcional enviado ao Nominatim |
| `TimeoutSeconds` | 10 | Tempo máximo de cada pedido |
| `MinIntervalMilliseconds` | 1100 | Intervalo entre pedidos |
| `CacheHours` | 168 | Cache do "achou" |
| `NotFoundCacheMinutes` | 60 | Cache do "não achou" |

Nos testes de integração o geocodificador é trocado por um falso (`FakeGeocoder`): nenhum teste depende de serviço externo.

## 6. Fotos (até 5, em ordem)

Mesmo fluxo das fotos de pet ([`armazenamento-de-imagens.md`](armazenamento-de-imagens.md)): bucket público, pasta `listings/`, formato conferido pelo conteúdo, metadados removidos, `ImageReplacement` (troca apaga a antiga; falha desfaz). A posse é conferida antes do upload.

**Ordem:** a posição vai de 1 a 5, **sempre sem buraco**; a 1 é a capa (`coverPhotoUrl` na busca).

- Foto nova entra **no fim** (`count + 1`).
- Tirar uma foto **sobe** as de trás uma posição.
- `PUT /photos/order` recebe **todas** as fotos, cada uma uma vez, na ordem nova. Id que não é foto deste cantinho: `404 LISTING_PHOTO_NOT_FOUND` (como nas outras rotas de foto); lista faltando ou com repetição: `400` em `photoIds`. Mesma ordem: não grava.
- No banco, a unicidade `(listing_id, position)` é uma constraint **deferrable** (`uq_listing_photos_listing_id_position`, conferida no commit): reordenar e remover trocam posições entre fotos numa transação só.

Erros: 6ª foto `422 LISTING_PHOTO_LIMIT_REACHED`; mesma imagem duas vezes `409 LISTING_PHOTO_ALREADY_EXISTS`; foto de outro cantinho `404 LISTING_PHOTO_NOT_FOUND`.

## 7. Banco

Schema `listing`, migration `CreateListingsTables`.

- `listing.listings`: colunas do [dicionário](dicionario-de-dados.md#listings--cantinhos). `host_id` **não é único** (empresa tem filiais); índice `ix_listings_host_id`. Conjuntos em bits: `accepted_species` (`int`, 12 bits) e `available_weekdays` (`smallint`, 7 bits, `1 << DayOfWeek`). CHECKs: tipo da casa, capacidade (1–200), diária (10–10000), bits (> 0 e dentro do conjunto), latitude e longitude.
- `listing.listing_photos`: FK física com `ON DELETE CASCADE`, posição 1–5 (CHECK), posição única **deferrable**, hash único por cantinho.

O limite da pessoa física fica na aplicação (o banco não sabe o tipo do anfitrião, que mora em outro schema). Dois cadastros simultâneos da mesma PF poderiam passar — aceito no MVP.

## 8. Comunicação com outros módulos

Nenhuma referência a outro módulo (§5, verificado em `ListingsModuleBoundaryTests`):

| Porta (Application) | Adaptador (Infrastructure) | Contrato |
|---|---|---|
| `IListingHosts` | `ListingHosts` | `IHostDirectory` (anfitrião, PF/PJ, ativo/suspenso, ids indisponíveis) + `IUserDirectory` (nome e foto) |
| `IAccountAddresses` | `AccountAddresses` | `IUserDirectory` (endereço da conta, origem da busca) |
| `IGeocoder` | `CachedGeocoder` (Redis) → `NominatimGeocoder` | HTTP externo + `IConnectionMultiplexer` do `Shared` |

O `IHostDirectory` ganhou `HostReference.IsSuspended` (com `IsCompany` e `IsAvailable`) e `GetUnavailableIdsAsync()`.

## 9. Testes

- **Domínio** (`Listings.Domain.UnitTests`): `ListingDetails` (cada regra, PF × PJ, diária, porte), `ListingAddress`, `GeoPoint` (faixas, haversine Maringá–Londrina), valores dos enums, `Listing` (limite PF/PJ, alterações, ativo/inativo, fotos e reordenação).
- **Application** (`Listings.Application.UnitTests`): cada handler, caminho feliz e cada falha (limite sem consultar o mapa, endereço não achado, mapa fora, posse, visão pública × dono, busca por coordenada e por endereço da conta, filtros inválidos).
- **Integração** (`Listings.IntegrationTests`, Postgres + Redis + RustFS em container): cache do geocodificador no Redis (uma consulta por endereço, TTL de "achou" e "não achou", falha não guardada, valor ilegível ignorado, endereço fora da chave); cadastro PF/PJ, 400 com todos os campos, 403/404/422/503, visão pública, inativar/reativar, PATCH com nova coordenada, busca por raio (5/10/50 km, ordem, ocultar inativo e suspenso, endereço da conta), fotos com reordenação e compactação no banco.

## 10. Decisões e pendências

| Decisão | Por quê |
|---|---|
| PF com 1 cantinho, PJ com vários | Pedido do produto: empresa (hotel, creche, resort de pets) tem filiais, cada uma com endereço, capacidade e fotos próprios. |
| Capacidade 10 (PF) × 200 (PJ) | Casa não comporta o que um hotel comporta; "capacidade honesta". |
| Tipo da casa opcional para PJ | Filial é espaço comercial, não casa nem apartamento. |
| Diária em `decimal` (reais) | Pedido do produto e §11 (dinheiro `numeric`). A conversão para centavos fica no módulo de pagamentos (Stripe). |
| `is_active` em vez de `status` (draft/active/paused) | O cadastro é num passo só (não há rascunho) e o pedido era inativar/reativar, como nos pets. |
| Tipos aceitos em bits (`accepted_species`) em vez da tabela `listing_species` | Conjunto pequeno e fixo, lido sempre junto com o cantinho; filtro futuro por tipo é `(accepted_species & bit) <> 0`. |
| Dias da semana em bits (`available_weekdays`) | Requisito novo; mesma lógica. A agenda (datas bloqueadas) fica por cima. |
| Geocodificação no backend (Nominatim) | Pedido do produto: a coordenada sai do endereço, nunca do cliente. Gratuito, sem chave. |
| Coordenada e rua escondidas na visão pública | Escopo, regra 7, e "Bairro, não coordenada". |

**Pendências**

1. Filtro de busca por `stripe_payouts_enabled`: o onboarding do Stripe ainda não existe; quando existir, somar à lista de anfitriões indisponíveis.
2. Filtros extras da busca (datas/agenda, tipo e porte do pet, preço) e selos — dependem dos módulos de agenda e reservas.
3. ~~Cache do geocodificador por instância (memória).~~ Feito: o cache está no Redis. Ainda por instância: a fila de 1 pedido/s ao Nominatim (`GeocodingRateGate`); com várias instâncias, cada uma respeita o limite sozinha — mover para um lock no Redis ou usar instância própria do Nominatim.
