# PetHost — Módulo Reviews (avaliações)

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md), o [`escopo-mvp.md`](escopo-mvp.md) (seção 3.1, Avaliação; regras 9 e 10) e o [`dicionario-de-dados.md`](dicionario-de-dados.md)_

A **avaliação** fecha a estadia: depois do check-out, **cada lado avalia o outro**, uma vez por reserva, em até **7 dias**, com **nota inteira de 1 a 5** e comentário opcional. O **tutor** avalia o anfitrião e o cantinho — é pública (sem login) e alimenta a **nota** e o selo **Bem avaliado** da busca. O **anfitrião** avalia o tutor e, se quiser, **cada pet** — só os anfitriões e o próprio tutor veem. A avaliação é **às cegas**: ninguém lê a do outro antes de escrever a sua — as duas são publicadas juntas quando os dois lados avaliam, ou no fim do prazo. O avaliado **responde** em público; qualquer um que vê **denuncia**, e o admin oculta a avaliação procedente. O anfitrião vê a **nota do tutor** no pedido de reserva e o **histórico de notas do pet**. E-mails avisam quem foi avaliado (na publicação), lembram quem ainda não avaliou e alertam a moderação. Só o backend está feito; o front (Next.js) vem depois.

## Sumário

- [PetHost — Módulo Reviews (avaliações)](#pethost--módulo-reviews-avaliações)
  - [Sumário](#sumário)
  - [1. Como funciona](#1-como-funciona)
  - [2. Anfitrião ou cantinho? (pessoa física × empresa)](#2-anfitrião-ou-cantinho-pessoa-física--empresa)
  - [3. Estrutura](#3-estrutura)
  - [4. Endpoints](#4-endpoints)
    - [4.1 Avaliar, editar, apagar](#41-avaliar-editar-apagar)
    - [4.2 Ler](#42-ler)
    - [4.3 Resposta do avaliado](#43-resposta-do-avaliado)
    - [4.4 Denúncia e moderação](#44-denúncia-e-moderação)
    - [4.5 Resposta (JSON)](#45-resposta-json)
    - [4.6 Erros](#46-erros)
  - [5. Regras](#5-regras)
    - [5.1 Avaliação às cegas](#51-avaliação-às-cegas)
  - [6. Nota e selo "Bem avaliado" na busca](#6-nota-e-selo-bem-avaliado-na-busca)
  - [7. E-mails](#7-e-mails)
  - [8. Banco](#8-banco)
  - [9. Comunicação com outros módulos](#9-comunicação-com-outros-módulos)
  - [10. Configuração](#10-configuração)
  - [11. Testes](#11-testes)
  - [12. Decisões e pendências](#12-decisões-e-pendências)

## 1. Como funciona

1. A estadia termina: o anfitrião (ou o admin, ou a plataforma 3 dias depois) marca o check-out e a reserva fica `completed`.
2. Cada lado manda `POST /api/v1/reviews` com a reserva, a nota e o comentário; o lado sai do **papel do token** (tutor ou anfitrião). O anfitrião pode mandar também `pets` (uma nota por pet da reserva). Tutor, anfitrião, cantinho e pets **saem da reserva** (módulo Bookings), nunca do corpo.
3. **Às cegas:** a avaliação fica guardada (só o autor lê) até o outro lado também avaliar — aí as duas são publicadas na hora — ou até o fim do prazo (7 dias depois do check-out), quando é publicada sozinha.
4. Na publicação, o avaliado recebe um e-mail ("você recebeu uma avaliação", com a nota e quem escreveu — nunca o texto).
5. Quem não avaliou recebe **um** lembrete por e-mail 24h depois do check-out (configurável), enquanto o prazo não acaba.
6. Publicada, a avaliação do tutor aparece em `GET /api/v1/reviews?hostId=` / `?listingId=` para qualquer pessoa, com o autor só pelo primeiro nome e a foto. A do anfitrião, em `GET /api/v1/owners/{ownerId}/reviews` (e as notas dos pets em `GET /api/v1/pets/{petId}/reviews`), só para anfitriões e admin. No pedido de reserva, o anfitrião vê a nota do tutor (`ownerRating`).
7. Enquanto não é publicada (e dentro do prazo), o autor edita (`PATCH`). Apagar (`DELETE`) vale a qualquer momento, mas aquele lado não avalia a reserva de novo.
8. O avaliado responde uma vez, em público (`PUT .../reply`), e pode trocar ou apagar a resposta.
9. Quem vê denuncia (`POST .../report`). O admin analisa a fila; **procedente oculta** a avaliação (some das listas e da média), com a observação como motivo, e pode desfazer.

## 2. Anfitrião ou cantinho? (pessoa física × empresa)

A ideia inicial era gravar a avaliação **no anfitrião** quando ele é pessoa física (tem um cantinho só) e **no cantinho** quando é empresa (um cantinho por filial). **Não foi feito assim**, porque:

- a escolha dependeria do tipo do anfitrião no momento da avaliação, e as consultas teriam que olhar duas colunas diferentes conforme o caso;
- a pessoa física também pode trocar de cantinho (inativar um e criar outro): a nota precisa continuar dela;
- para a empresa, às vezes interessa a nota da **filial** (o cantinho) e às vezes a da **empresa inteira** (o anfitrião).

**O que foi feito:** toda avaliação grava **sempre** `owner_id`, `host_id` **e** `listing_id`, copiados da reserva. A diferença PF/PJ fica só na **leitura**:

| Anfitrião | Para mostrar | Filtro | Nota no card da busca (seção 6) |
|---|---|---|---|
| Pessoa física (um cantinho) | a nota da pessoa | `?hostId=` | do anfitrião |
| Empresa (um cantinho por filial) | a nota da filial | `?listingId=` | do cantinho |
| Empresa | a nota da empresa inteira | `?hostId=` | — |

## 3. Estrutura

```
src/Modules/Reviews/
├── PetHost.Modules.Reviews.Domain/          Reviews/ (Review, ReviewParty, ReviewPetRating, ReviewedStay, IReviewRepository, IReviewsUnitOfWork)
│                                            Reports/ (ReviewReport, motivo e situação, IReviewReportRepository)
│                                            Reminders/ (ReviewReminder, IReviewReminderRepository) · Errors/ReviewsErrors
├── PetHost.Modules.Reviews.Application/     Reviews/{CreateReview, UpdateReview, DeleteReview, ReplyToReview, RemoveReviewReply,
│                                            UnhideReview, ListReviews, GetReviewById, ListMyReviews, ListOwnerReviews, GetRatingSummary}
│                                            Reports/{ReportReview, ResolveReviewReport, ListReviewReports, GetReviewReportById}
│                                            Reminders/SendReviewReminders · Abstractions/ (IReviewBookings, IReviewParties, IReviewQueries, IReviewNotifier)
├── PetHost.Modules.Reviews.Infrastructure/  ReviewsDbContext (schema review), configurações, repositórios, consultas, UoW,
│                                            ReviewDirectory (contrato IReviewDirectory), Adapters/ (Bookings, Owners/Hosts/Auth/Pets),
│                                            Notifications/ (e-mails), Reminders/ (tarefa em segundo plano)
└── PetHost.Modules.Reviews.Presentation/    ReviewsController · OwnerReviewsController · ReviewModerationController (admin)
tests/Modules/Reviews/                       Domain.UnitTests · Application.UnitTests · IntegrationTests
```

## 4. Endpoints

### 4.1 Avaliar, editar, apagar

| Método e rota | Quem | O que faz | Sucesso |
|---|---|---|---|
| `POST /api/v1/reviews` | tutor ou anfitrião | Avalia a estadia: `{ bookingId, rating, comment?, pets? }` (`pets` só do anfitrião: `[{ petId, rating, comment? }]`) | `201` + `Location` |
| `PATCH /api/v1/reviews/{reviewId}` | autor | Muda `rating`, `comment` (`""` apaga) e/ou `pets` (substitui a lista). Só antes da publicação | `200` |
| `DELETE /api/v1/reviews/{reviewId}` | autor | Apaga (soft delete) | `200` `ApiResponse<Unit>` |

### 4.2 Ler

| Método e rota | Quem | O que faz |
|---|---|---|
| `GET /api/v1/reviews?hostId=&listingId=&page=&pageSize=` | **anônimo** | O que os tutores escreveram dos anfitriões, da mais nova para a mais antiga (sem apagadas nem ocultas) |
| `GET /api/v1/reviews/summary?hostId=` ou `?listingId=` | **anônimo** | Nota média (1 casa) e contagem. Exatamente um dos dois |
| `GET /api/v1/reviews/{reviewId}` | depende | Do tutor: qualquer pessoa. Do anfitrião: anfitriões e o tutor avaliado. O autor vê a sua mesmo oculta; o admin vê tudo |
| `GET /api/v1/reviews/me` | tutor ou anfitrião | As que **escrevi** (com `bookingId`, `editableUntil` e, se oculta, `moderation`) |
| `GET /api/v1/reviews/me/received` | tutor ou anfitrião | As que **recebi** (sem as ocultas). Daqui o avaliado responde |
| `GET /api/v1/owners/{ownerId}/reviews` | anfitrião ou admin | O que os anfitriões escreveram do tutor, com as notas dos pets (o `ownerId` vem na reserva) |
| `GET /api/v1/owners/{ownerId}/reviews/summary` | anfitrião ou admin | Nota média e contagem do tutor (a mesma que sai no pedido de reserva, `ownerRating`) |
| `GET /api/v1/pets/{petId}/reviews` | anfitrião ou admin | Histórico do pet ("currículo"): as notas que os anfitriões deram, estadia por estadia (`reviewId`, `hostId`, `listingId`, `host`, `rating`, `comment`, `stayEndedAt`). O `petId` vem na reserva |
| `GET /api/v1/pets/{petId}/reviews/summary` | anfitrião ou admin | Nota média e contagem do pet |

Tudo que é lido por outra pessoa (listas, leitura por id, notas) só traz avaliações **publicadas**; as que esperam a publicação às cegas só aparecem para o autor (`/me` e a leitura por id).

Listas paginadas (§13): `page` a partir de 1, `pageSize` de 1 a 100.

### 4.3 Resposta do avaliado

| Método e rota | Quem | O que faz |
|---|---|---|
| `PUT /api/v1/reviews/{reviewId}/reply` | o avaliado | `{ text }` (até 1.000). Cria ou troca a resposta pública. Sem prazo |
| `DELETE /api/v1/reviews/{reviewId}/reply` | o avaliado | Apaga a resposta (idempotente) |

O avaliado é o anfitrião, na avaliação do tutor, e o tutor, na do anfitrião. Para os outros (inclusive o autor), `404 REVIEW_NOT_FOUND`.

### 4.4 Denúncia e moderação

| Método e rota | Quem | O que faz |
|---|---|---|
| `POST /api/v1/reviews/{reviewId}/report` | tutor ou anfitrião que vê a avaliação | `{ reason, description? }` → `201` + `Location: /api/v1/reviews/reports/{id}` |
| `GET /api/v1/reviews/reports?status=pending&page=` | admin | A fila: `pending` (padrão), `upheld`, `dismissed` ou `all`, da mais antiga para a mais nova, com a avaliação |
| `GET /api/v1/reviews/reports/{reportId}` | admin | Uma denúncia, com a avaliação |
| `PUT /api/v1/reviews/reports/{reportId}/resolution` | admin | `{ outcome: "upheld" \| "dismissed", note }`. Procedente **oculta** a avaliação com `note` como motivo |
| `DELETE /api/v1/reviews/{reviewId}/moderation-hide` | admin | Volta a mostrar a avaliação ocultada (procedente dado por engano) |

Motivos (`reason`): `offensive` (ofensa), `false_information` (não aconteceu), `personal_data` (expõe dado pessoal), `spam`, `other` (descrição obrigatória).

### 4.5 Resposta (JSON)

```json
{ "id": "0192…", "authorSide": "host", "hostId": "0192…", "listingId": "0192…", "ownerId": "0192…",
  "rating": 5, "comment": "Tutora pontual.", "author": { "name": "Cida", "avatarUrl": "https://…" },
  "pets": [ { "petId": "0192…", "name": "Pipoca", "rating": 4, "comment": "Latiu à noite." } ],
  "reply": { "text": "Obrigada!", "repliedAt": "…" },
  "stayEndedAt": "2026-10-10T12:00:00+00:00", "createdAt": "…", "updatedAt": "…",
  "bookingId": "0192…", "editableUntil": "2026-10-17T12:00:00+00:00", "publishedAt": "2026-10-17T12:00:00+00:00",
  "moderation": { "hiddenAt": "…", "reason": "Linguagem ofensiva." } }
```

- `ownerId` e `pets` só na avaliação do anfitrião (`authorSide: "host"`); na do tutor, o tutor autor não é exposto.
- `author.name`: primeiro nome (pessoa física) ou nome fantasia (empresa). Some se a conta não existe mais.
- `bookingId`, `editableUntil`, `publishedAt` (quando os outros passam a ler) e `moderation` só para o **autor** e o **admin**.
- Campo nulo some do JSON (`reply` sem resposta, `pets[].name` de pet apagado).

Nota (`GET .../summary`): `{ "average": 4.7, "count": 3 }`; sem avaliação, `{ "count": 0 }`.

### 4.6 Erros

| HTTP | Código | Quando |
|---|---|---|
| 400 | `VALIDATION_ERROR` | `bookingId`/`rating` ausentes; nota fora de 1–5; texto (comentário, resposta, descrição, observação) com mais de 1.000 caracteres ou caractere de controle (quebra de linha e tabulação passam); pet repetido, fora da reserva ou mandado pelo tutor (campo `pets`); motivo, desfecho ou status desconhecido; paginação inválida; `summary` sem exatamente um id |
| 401 | `UNAUTHORIZED` | Sem token nas rotas logadas |
| 403 | `FORBIDDEN` | Papel errado (ex.: admin avaliando; tutor na rota do tutor avaliado) |
| 403 | `REVIEW_OWNER_DEACTIVATED` / `REVIEW_HOST_DEACTIVATED` | Perfil inativo ou suspenso tentando avaliar, editar, responder ou denunciar |
| 404 | `REVIEW_OWNER_NOT_FOUND` / `REVIEW_HOST_NOT_FOUND` | Conta sem o perfil do papel |
| 404 | `REVIEW_BOOKING_NOT_FOUND` | Reserva inexistente **ou de outra pessoa** (§8) |
| 404 | `REVIEW_NOT_FOUND` | Avaliação inexistente, apagada, oculta ou ainda não publicada (para quem não é o autor), ou de outra pessoa (editar, apagar, responder) |
| 404 | `REVIEW_REPORT_NOT_FOUND` | Denúncia inexistente |
| 409 | `REVIEW_ALREADY_EXISTS` | Esse lado já avaliou a reserva, mesmo apagada |
| 409 | `REVIEW_REPORT_ALREADY_EXISTS` | Essa conta já denunciou a avaliação |
| 409 | `REVIEW_CONFLICT` | Duas mudanças ao mesmo tempo (`xmin`) |
| 422 | `REVIEW_BOOKING_NOT_COMPLETED` | Reserva sem check-out |
| 422 | `REVIEW_WINDOW_CLOSED` | Mais de 7 dias depois do check-out (criar ou editar) |
| 422 | `REVIEW_ALREADY_PUBLISHED` | Editar avaliação já publicada (às cegas: depois de ler a do outro lado, ninguém muda a sua) |
| 422 | `REVIEW_HIDDEN_BY_MODERATION` | Editar ou responder avaliação oculta pelo admin |
| 422 | `REVIEW_REPORT_OWN_REVIEW` | O autor denunciando a própria avaliação |
| 422 | `REVIEW_REPORT_ALREADY_RESOLVED` | Analisar denúncia já analisada |

## 5. Regras

1. **Só quem ficou avalia.** O perfil do token (tutor ou anfitrião) precisa ser o daquele lado da reserva; senão é `404`.
2. **Só estadia concluída** (`completed`, com `checked_out_at`).
3. **Prazo de 7 dias** a partir do check-out para criar (`Review.WindowDays`). Editar, só enquanto a avaliação não foi publicada (no máximo, o mesmo prazo).
4. **Uma por lado por reserva** (índice único em `booking_id, author_side`). A apagada continua ocupando a vaga.
5. **Pets:** só o anfitrião dá nota, só aos pets daquela reserva, uma vez cada. São opcionais.
6. **Quem vê o quê:** a avaliação do tutor (sobre o anfitrião) é pública; a do anfitrião (sobre o tutor e os pets) é para os anfitriões decidirem sobre os pedidos e para o tutor avaliado — não aparece sem login nem para outros tutores.
7. **Resposta:** só o avaliado, uma, pública, sem prazo; ele troca ou apaga quando quiser. Avaliação oculta ou ainda não publicada não recebe resposta.
8. **Denúncia:** quem vê a avaliação (publicada) denuncia, menos o autor; uma por conta por avaliação. Procedente oculta (some das listas, da leitura pública, da média e do selo); o autor continua vendo, com o motivo, e ainda pode apagar, mas não edita. O admin pode desfazer.
9. **Apagar é direito do autor**, a qualquer momento, mesmo com o perfil inativo ou suspenso, ou com a avaliação oculta. Editar, responder e denunciar exigem perfil ativo.
10. **Privacidade:** em público o autor aparece pelo primeiro nome (ou nome fantasia) e a foto; nunca a reserva. E-mails nunca levam o texto.
11. **Rate limit:** avaliar e denunciar mandam e-mail, mas não ganharam política própria (§13): o volume já é limitado pelo domínio — uma avaliação por lado por reserva concluída, uma denúncia por conta por avaliação. Valem o limite geral.
12. **Trilha (§15):** `review.created`, `review.updated` (nomes dos campos), `review.deleted`, `review.replied`, `review.reply_removed`, `review.reported`, `review.report_resolved` (observação como motivo), `review.hidden` (com o motivo), `review.unhidden`, `review.reports_listed`, `review.report_viewed` e `review.reminder_sent` (alvo: a reserva). **Nunca** o comentário, a resposta nem a descrição.

### 5.1 Avaliação às cegas

Para nenhum lado escrever sabendo o que o outro disse (e evitar a nota de retaliação):

- A avaliação nasce com `published_at` = **fim do prazo** (check-out + 7 dias). Até lá, só o autor lê.
- Quando o outro lado também avalia (e não apagou a sua), as **duas** são publicadas **na hora**, na mesma transação, e cada avaliado recebe o e-mail.
- Se só um lado avaliar, a avaliação é publicada sozinha no fim do prazo; a tarefa em segundo plano (seção 7) manda o e-mail ao avaliado.
- **Publicada, não muda mais** (`422 REVIEW_ALREADY_PUBLISHED`): depois de ler a do outro, ninguém reescreve a sua. Apagar continua valendo.
- Antes da publicação, para quem não é o autor, a avaliação não existe: `404` na leitura, na resposta e na denúncia; fora das listas, da nota média, do selo, do histórico do pet e da nota do tutor.
- A visibilidade é `published_at <= agora`: não precisa de tarefa para "abrir" a avaliação no fim do prazo; a tarefa só manda o aviso (`subject_notified_at`).

## 6. Nota e selo "Bem avaliado" na busca

O Reviews publica o contrato **`IReviewDirectory`** (`Shared.Contracts/Reviews`): média e contagem por anfitrião e por cantinho (das avaliações dos tutores) e por tutor (das dos anfitriões — o Bookings mostra no pedido), só das avaliações visíveis (publicadas, sem apagadas nem ocultas). O Listings lê pela porta `IListingReviews` e decide qual nota mostrar (`ListingRatings`): **empresa** usa a do **cantinho** (a filial); **pessoa física** usa a do **anfitrião** (continua valendo se ele trocar de cantinho). Uma consulta de cada tipo para a página inteira.

- Card da busca (`GET /listings`) e detalhe (`GET /listings/{id}`): `rating: { average, count }` (média com 1 casa), omitido enquanto ninguém avaliou.
- Selo `well_rated` ("Bem avaliado"): média **4,5 ou mais** com **3 ou mais** avaliações (escopo, regra 10). Detalhes em [`modulo-listings.md`](modulo-listings.md) seção 5.1.

## 7. E-mails

Todos pela caixa de saída (`IEmailOutbox`), em segundo plano, **best-effort**: o que disparou o e-mail já está gravado; falha ao enfileirar vai para o log. Nunca levam o texto da avaliação, da resposta ou da denúncia.

| E-mail | Para quem | Quando |
|---|---|---|
| "Você recebeu uma avaliação" | o avaliado | Na **publicação**: na hora, quando o segundo lado avalia; ou pela tarefa, no fim do prazo. Leva a nota e quem escreveu. Só se ele pode receber aviso (perfil e conta ativos, sem suspensão) |
| "Como foi a estadia? Deixe sua avaliação" | cada lado que não avaliou | Uma vez, `ReviewReminder:DelayHours` (padrão 24) depois do check-out, até o fim do prazo. Leva até quando dá para avaliar |
| "Nova denúncia de avaliação" | `ReviewEmail:AdminAlertEmail` | A cada denúncia. Vazio = sem e-mail (o admin consulta a fila) |

A tarefa em segundo plano (`ReviewMaintenanceService`) roda a cada `ReviewMaintenance:IntervalMinutes` dois comandos: `SendReviewReminders` (o lembrete) e `NotifyPublishedReviews` (o aviso das avaliações publicadas no fim do prazo). Nos dois, o envio é anotado (`review.review_reminders`, `reviews.subject_notified_at`) **antes** de ir para a caixa de saída: se a gravação falhar, ninguém recebe e a próxima rodada tenta de novo — nunca um e-mail repetido.

## 8. Banco

Schema `review`. Migrations: `CreateReviewsTable`, `AddReviewSidesRepliesModerationAndReminders` (as avaliações que já existiam ficam com `author_side = 'owner'`), `AddBlindReviewPublication` (as que já existiam contam como publicadas e avisadas na criação).

**`reviews`**

| Coluna | Tipo | Nulo | Descrição |
|---|---|---|---|
| `id` | `uuid` (v7) | não | PK |
| `booking_id` | `uuid` | não | Reserva (FK lógica). Único com `author_side` (`uq_reviews_booking_id_author_side`) |
| `author_side` | `varchar(10)` | não | `owner` ou `host`: quem escreveu |
| `owner_id` / `host_id` / `listing_id` | `uuid` | não | Tutor, anfitrião e cantinho da reserva (FKs lógicas) |
| `stay_ended_at` | `timestamptz` | não | Check-out; base do prazo |
| `rating` | `smallint` | não | 1 a 5 (`ck_reviews_rating`) |
| `comment` | `varchar(1000)` | sim | Comentário |
| `reply_text` / `replied_at` | `varchar(1000)` / `timestamptz` | sim | Resposta do avaliado (as duas juntas, `ck_reviews_reply`) |
| `hidden_at` / `hidden_by` / `hidden_reason` | `timestamptz` / `uuid` / `varchar(1000)` | sim | Ocultação pelo admin (as três juntas, `ck_reviews_hidden`) |
| `published_at` | `timestamptz` | não | A partir de quando os outros leem (às cegas): fim do prazo, ou antes, quando o outro lado avalia |
| `subject_notified_at` | `timestamptz` | sim | Quando o avaliado recebeu o e-mail da publicação |
| `created_at` / `updated_at` | `timestamptz` | não | |
| `deleted_at` | `timestamptz` | sim | Apagada pelo autor (soft delete) |
| `xmin` | sistema | — | Concorrência otimista |

Índices parciais (`WHERE deleted_at IS NULL`): (`host_id`, `author_side`, `created_at`), (`listing_id`, `author_side`, `created_at`), (`owner_id`, `author_side`, `created_at`); e (`published_at`) `WHERE subject_notified_at IS NULL AND deleted_at IS NULL`, para a tarefa do aviso.

**`review_pet_ratings`** — nota de cada pet na avaliação do anfitrião. PK (`review_id`, `pet_id`); `review_id` com FK física e `ON DELETE CASCADE`; `pet_id` FK lógica; `rating` 1 a 5; `comment` até 1.000.

**`review_reports`** — denúncias. `review_id` (FK física, cascade), `reported_by` (conta), `reporter_side`, `reason`, `description`, `status` (`pending`/`upheld`/`dismissed`), `created_at`, `resolved_at`/`resolved_by`/`resolution_note` (preenchidos juntos na análise, `ck_review_reports_resolution`), `xmin`. Único (`review_id`, `reported_by`); índice (`status`, `created_at`) para a fila.

**`review_reminders`** — lembretes enviados: PK (`booking_id`, `side`), `sent_at`.

## 9. Comunicação com outros módulos

Só por `Shared.Contracts` (§5); ninguém referencia o Reviews e o Reviews não referencia ninguém (teste `ReviewsModuleBoundaryTests`).

| Porta / contrato | Por onde | Para quê |
|---|---|---|
| `IReviewBookings` → `IBookingDirectory` | `GetForReviewAsync` → `BookingReviewView` (tutor, anfitrião, cantinho, **pets**, status, `checkedOutAt`); `ListCompletedForReviewAsync` (novo) | A reserva avaliada; as estadias concluídas para o lembrete |
| `IReviewParties` → `IOwnerDirectory`, `IHostDirectory`, `IUserDirectory`, `IPetDirectory` | — | Perfil da conta logada (e se está ativo); nome curto, foto e e-mail; nome dos pets. Uma consulta por módulo |
| **`IReviewDirectory`** (o Reviews implementa) | `GetHostRatingsAsync`, `GetListingRatingsAsync`, `GetOwnerRatingsAsync` → `RatingSummary` | O Listings mostra a nota e liga o selo "Bem avaliado" (porta `IListingReviews`); o Bookings mostra a nota do tutor ao anfitrião no pedido (porta `IBookingReviews`) |

## 10. Configuração

| Chave | Padrão | Para quê |
|---|---|---|
| `ReviewMaintenance:Enabled` | `true` | Liga a tarefa (lembrete e aviso das publicadas). Os testes desligam e chamam os comandos |
| `ReviewMaintenance:IntervalMinutes` | `60` | De quanto em quanto tempo a tarefa roda |
| `ReviewMaintenance:ReminderDelayHours` | `24` | Horas depois do check-out para lembrar (1 a 167) |
| `ReviewMaintenance:BatchSize` | `200` | Estadias (lembrete) e avaliações (aviso) por rodada (até 1.000) |
| `ReviewEmail:AppUrl` | vazio | Endereço do app no botão dos e-mails |
| `ReviewEmail:AdminAlertEmail` | vazio | Quem recebe o aviso de denúncia nova |

No compose: `REVIEW_REMINDER_DELAY_HOURS`, `REVIEW_MAINTENANCE_INTERVAL_MINUTES`, `REVIEW_EMAIL_APP_URL`, `REVIEW_EMAIL_ADMIN_ALERT_EMAIL` (ver `.env.example`).

## 11. Testes

- **Domínio:** `Review` nos dois lados (cada ramo de `Create`, `Update` com pets, prazo, publicação e ocultação), publicação às cegas (`Publish`, `IsPublished`, aviso), resposta (`Reply`/`RemoveReply`: quem pode, texto, oculta), `Hide`/`Unhide`, `ReviewReport` (`Submit`, `Resolve`), valores dos enums, `ReviewReminder`.
- **Application:** cada handler no caminho feliz e em cada falha (sem perfil, inativo, reserva de outra pessoa, já avaliou, oculta, de outra pessoa, erro ao gravar), sem gravar, notificar nem registrar na trilha quando falha; quem vê o quê na leitura; o lembrete (os dois lados, quem já avaliou, quem não pode receber, falha ao gravar); validadores, uma asserção por regra.
- **Application (fase 3):** a publicação das duas quando o segundo lado avalia (e só aí o e-mail), o aviso das publicadas no fim do prazo, o histórico do pet, a nota do pet e a visibilidade das não publicadas.
- **Listings:** o selo `well_rated` (limiares) e qual nota cada cantinho usa (`ListingRatings`). **Bookings:** a nota do tutor no pedido (anfitrião e admin veem; o tutor, não).
- **Integração** (Postgres, Redis e bucket em container): o ciclo do tutor (avaliar → ler anônimo → editar → apagar → `409`); o anfitrião avaliando tutor e pet (visibilidade, rota do tutor, nota do tutor, e-mail recebido); a resposta; denúncia → fila → procedente oculta (lista, nota e leitura pública) → o autor vê o motivo → o admin desfaz; nota e selo no card e no detalhe da busca; o lembrete (uma vez por lado, pula quem avaliou); às cegas (guardada até o outro lado, publicada e avisada no fim do prazo, edição travada depois de publicada); o histórico e a nota do pet; a nota do tutor no pedido de reserva; o Unit of Work (índices únicos, `xmin`, os dois lados na mesma reserva).

## 12. Decisões e pendências

**Decidido:**

- Grava sempre tutor, anfitrião **e** cantinho; PF/PJ só muda o filtro e a nota do card (seções 2 e 6).
- Uma avaliação por lado por reserva, com prazo de 7 dias para criar e editar (escopo, regra 9). Soft delete; a apagada não libera outra.
- A avaliação do anfitrião sobre o tutor **não é pública**: só anfitriões (para decidir sobre pedidos), o admin e o tutor avaliado.
- A nota dos pets fica dentro da avaliação do anfitrião (uma por pet da reserva), sem rota própria.
- A ocultação só acontece pela análise de uma denúncia (com motivo); voltar a mostrar não pede motivo, como o desbloqueio da moderação do chat.
- Avaliação às cegas (seção 5.1): publicada quando os dois lados avaliam ou no fim do prazo; publicada, não muda mais.
- O histórico do pet e a nota do tutor são só para anfitriões (e admin); o tutor vê o que recebeu em `/reviews/me/received`.
