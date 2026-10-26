# Upload direto ao bucket (URL assinada)

O front envia o arquivo **direto para o bucket**, sem os bytes passarem pelo Next.js nem pela API. A API confere quem pode enviar, assina uma URL de `PUT` que vale poucos minutos e, depois que o arquivo chegou, **confere o conteúdo real** antes de ligá-lo ao recurso (pet, cantinho, perfil, mensagem, documento).

Substitui os uploads por `multipart/form-data` ([`armazenamento-de-imagens.md`](armazenamento-de-imagens.md) seção 2), que continuam no ar durante a migração do front.

## Sumário

- [Upload direto ao bucket (URL assinada)](#upload-direto-ao-bucket-url-assinada)
  - [Sumário](#sumário)
  - [1. Fluxo](#1-fluxo)
  - [2. Finalidades](#2-finalidades)
  - [3. Pedir o upload — `POST /api/v1/uploads`](#3-pedir-o-upload--post-apiv1uploads)
  - [4. Enviar o arquivo — `PUT` no bucket](#4-enviar-o-arquivo--put-no-bucket)
  - [5. Confirmar — `POST /api/v1/uploads/{uploadId}/confirm`](#5-confirmar--post-apiv1uploadsuploadidconfirm)
  - [6. Ler mídia privada (chat e documentos)](#6-ler-mídia-privada-chat-e-documentos)
  - [7. Erros](#7-erros)
  - [8. Exemplo completo (TypeScript)](#8-exemplo-completo-typescript)
  - [9. Garantias e segurança](#9-garantias-e-segurança)
  - [10. Limpeza](#10-limpeza)
  - [11. Configuração (backend)](#11-configuração-backend)
  - [12. CORS do bucket](#12-cors-do-bucket)
  - [13. Testes](#13-testes)

---

## 1. Fluxo

```
front ──POST /uploads {purpose, contentType, sizeBytes, ...}──▶ API   confere token, papel e posse
front ◀── 201 {uploadId, uploadUrl, headers, expiresAt} ─────── API   assina PUT (tamanho + tipo)
front ──PUT uploadUrl (bytes, Content-Type)──────────────────▶ bucket privado, uploads/pending/<id>
front ──POST /uploads/{uploadId}/confirm {body?}─────────────▶ API   lê o arquivo, confere formato e
                                                                      tamanho reais, tira metadados,
                                                                      grava no destino e aplica a regra
front ◀── 200 {status: completed, result: <recurso>} ───────── API   apaga o arquivo pendente
```

1. **Pedir o upload.** O front diz a finalidade e os metadados do arquivo. A API autentica, confere o papel e a permissão **com as mesmas regras do caso de uso** (pet de outra conta é `404 PET_NOT_FOUND`, pet com 3 fotos é `422 PET_PHOTO_LIMIT_REACHED`, conversa bloqueada é `422 CONVERSATION_BLOCKED`...), gera uma chave exclusiva no bucket e devolve a URL assinada.
2. **Enviar.** O navegador faz o `PUT` direto no bucket, com o `Content-Type` declarado. O arquivo fica na área de espera do bucket **privado**: ninguém abre por link.
3. **Confirmar.** O front manda só o `uploadId`. A API confere que o upload é dele, que não venceu e o **arquivo realmente gravado**, e então aplica (troca o avatar, adiciona a foto, cria a mensagem). No chat, a mensagem e os eventos do hub só existem depois daqui.

## 2. Finalidades

| `purpose` | Quem (papel do token) | `resourceId` | `photoId` | `kind` | O que a confirmação faz | `result` |
|---|---|---|---|---|---|---|
| `owner-avatar` | owner | — | — | — | Troca a foto de perfil do tutor; a anterior é apagada **depois** de gravar a nova. Bucket público. | o tutor (`OwnerResponse`, com `user.avatarUrl`) |
| `host-avatar` | host | — | — | — | Idem, do anfitrião. Bucket público. | o anfitrião (`HostResponse`) |
| `pet-photo` | owner, host | `petId` (obrigatório) | opcional | — | Sem `photoId`: adiciona (até 3). Com `photoId`: substitui aquela foto (mesmo id e posição; a imagem antiga sai depois). Bucket público. | o pet (`PetResponse`, com `photos`) |
| `listing-photo` | host | `listingId` (obrigatório) | opcional | — | Sem `photoId`: adiciona no fim (até 5). Com `photoId`: substitui. Bucket público. | o cantinho (`ListingResponse`) |
| `chat-photo` | owner, host | `conversationId` (obrigatório) | — | — | Cria a mensagem com foto (legenda no `body` da confirmação) e avisa pelo hub. Bucket **privado**. | a mensagem (`MessageResponse`, com `imageUrl` assinada) |
| `host-document` | host | — | — | `front`, `back` ou `selfie` (obrigatório) | Grava a foto naquela vaga do rascunho da verificação; troca a anterior. Bucket **privado**. | a solicitação (`HostApplicationResponse`, sem a imagem) |

Campo que a finalidade não leva (`photoId` em `owner-avatar`, `kind` em `pet-photo`...) é `400`. Os ids são os mesmos das rotas de sempre.

## 3. Pedir o upload — `POST /api/v1/uploads`

| | |
|---|---|
| Autenticação | `Authorization: Bearer <accessToken>` (owner ou host) |
| Rate limit | `uploads`: 60 pedidos a cada 10 minutos por conta (`429` com `Retry-After`) |
| Corpo | JSON |

| Campo | Tipo | Obrigatório | Regra |
|---|---|---|---|
| `purpose` | string | sim | Uma da seção 2. |
| `contentType` | string | sim | `image/jpeg`, `image/png` ou `image/webp` (use `file.type`). Vai na assinatura. |
| `sizeBytes` | number | sim | `file.size`, de 1 a **5 242 880** (5 MB). Vai na assinatura. |
| `resourceId` | uuid | conforme a finalidade | `petId`, `listingId` ou `conversationId`. |
| `photoId` | uuid | não | Só `pet-photo` e `listing-photo`: substituir em vez de adicionar. |
| `kind` | string | conforme a finalidade | Só `host-document`. |

O nome do arquivo não é enviado: nada do nome original vai para o bucket.

```http
POST /api/v1/uploads
Authorization: Bearer eyJ...
Content-Type: application/json

{ "purpose": "pet-photo", "contentType": "image/jpeg", "sizeBytes": 482113, "resourceId": "0192f3a1-7c2e-7b10-9f3e-4b5a6c7d8e9f" }
```

`201 Created`:

```json
{
  "success": true,
  "data": {
    "uploadId": "01a1893a-5c95-7018-85bb-58361ace09f2",
    "purpose": "pet-photo",
    "uploadUrl": "https://<bucket>/pethost-documents/uploads/pending/01a1893a5c95701885bb58361ace09f2?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-SignedHeaders=content-length%3Bcontent-type%3Bhost&X-Amz-Expires=600&X-Amz-Signature=...",
    "method": "PUT",
    "headers": { "Content-Type": "image/jpeg" },
    "sizeBytes": 482113,
    "expiresAt": 1793210400,
    "confirmBy": 1793211600
  },
  "timestamp": "2026-10-29T19:30:00Z"
}
```

| Campo | O que é |
|---|---|
| `uploadId` | Guarde: é o que vai na confirmação. |
| `uploadUrl` | URL de `PUT`. Não altere nada nela (qualquer mudança dá 403). |
| `headers` | Headers **obrigatórios** do `PUT`, exatamente com esses valores. |
| `sizeBytes` | O corpo do `PUT` precisa ter exatamente esse tamanho. |
| `expiresAt` | Vencimento da URL, em Unix time (segundos, UTC): `new Date(expiresAt * 1000)`. Padrão: 10 min. |
| `confirmBy` | Prazo para confirmar, em Unix time. Padrão: 30 min depois do pedido. |

Cada conta tem até **10 uploads pendentes** ao mesmo tempo (`422 UPLOAD_PENDING_LIMIT_REACHED`).

## 4. Enviar o arquivo — `PUT` no bucket

```js
const put = await fetch(ticket.uploadUrl, { method: ticket.method, headers: ticket.headers, body: file });
// 200 = gravado. 403 = assinatura não confere (URL vencida, tamanho ou Content-Type diferentes).
```

- **Sem** `Authorization`, sem cookie (`credentials` padrão): a assinatura está na URL.
- Mande **o próprio `File`/`Blob`** como corpo: o navegador põe o `Content-Length` (é ele que tem de bater com `sizeBytes`).
- Só o header `Content-Type` (de `headers`). Header a mais que o bucket não espera pode cair no preflight do CORS.
- Para mostrar progresso, use `XMLHttpRequest` com `upload.onprogress` (o `fetch` não dá progresso de envio).
- A URL serve para **um** arquivo. Para enviar outro, peça outro upload.
- Errou (403, rede caiu)? Enquanto `expiresAt` não passou, dá para repetir o `PUT` na mesma URL.

## 5. Confirmar — `POST /api/v1/uploads/{uploadId}/confirm`

| | |
|---|---|
| Autenticação | o mesmo token (mesma conta **e** mesmo papel) do pedido |
| Corpo | opcional. `{ "body": "legenda" }` só no `chat-photo` (até 2.000 caracteres); ignorado nas outras finalidades. |

```http
POST /api/v1/uploads/01a1893a-5c95-7018-85bb-58361ace09f2/confirm
Authorization: Bearer eyJ...
Content-Type: application/json

{ "body": "Chegou bem!" }
```

`200 OK`:

```json
{
  "success": true,
  "data": {
    "uploadId": "01a1893a-5c95-7018-85bb-58361ace09f2",
    "purpose": "chat-photo",
    "status": "completed",
    "result": {
      "id": "01a1893a-5c95-7018-85bb-58361ace09f2",
      "conversationId": "0192f3a1-...",
      "sentBy": "host",
      "body": "Chegou bem!",
      "imageUrl": "https://<bucket>/pethost-documents/conversations/0192f3c8....png?X-Amz-...",
      "imageUrlExpiresAt": 1793210700,
      "createdAt": "2026-10-29T19:31:12Z"
    }
  },
  "timestamp": "2026-10-29T19:31:12Z"
}
```

`result` é o mesmo objeto que a rota antiga da finalidade devolvia (seção 2).

- **Idempotente.** Confirmar de novo um upload concluído devolve `200` com **a mesma resposta**, sem adicionar outra foto nem criar outra mensagem. Pode repetir à vontade depois de timeout de rede. A mensagem do chat usa o `uploadId` como id.
- **Arquivo ainda não chegou** (`422 UPLOAD_FILE_NOT_RECEIVED`): faça o `PUT` e confirme de novo.
- **Legenda inválida** (`400` no campo `body`): corrija e confirme de novo; o arquivo continua valendo.
- **Arquivo ou regra recusou** (`400` no campo `file`, `409`, `422`...): o upload acaba ali e o arquivo é apagado. Confirmar de novo dá `422 UPLOAD_ALREADY_REJECTED`; peça outro upload.
- **Duas confirmações ao mesmo tempo:** uma processa; a outra recebe `409 UPLOAD_CONFIRMATION_CONFLICT`. Tente de novo em instantes: vem a resposta da primeira.
- O resultado concluído fica guardado por 24 h; depois disso, confirmar de novo dá `404 UPLOAD_NOT_FOUND`.

## 6. Ler mídia privada (chat e documentos)

| Mídia | Onde fica | Como o front lê |
|---|---|---|
| Avatar, foto do pet, foto do cantinho | bucket **público** | `url` fixa, cache de 1 ano, direto no `<img>`. |
| Foto do chat | bucket **privado** | `imageUrl` assinada nas mensagens (listagem, conversa, hub, confirmação), válida até `imageUrlExpiresAt` (5 min). Venceu (o bucket dá 403): `GET /api/v1/conversations/{conversationId}/messages/{messageId}/photo` devolve `{ messageId, url, expiresAt }`. Só quem é da conversa (senão `404`). |
| Documento e selfie do anfitrião | bucket **privado** | Só o admin, por `GET /api/v1/host-applications/{id}/documents/{kind}` ([`armazenamento-de-imagens.md`](armazenamento-de-imagens.md) seção 8.1). Quem enviou não recebe a imagem de volta. |

Fotos do chat enviadas antes desta mudança continuam com a URL pública antiga (sem `imageUrlExpiresAt`).

## 7. Erros

Envelope de sempre (`success: false`, `error.code`, e `error.details[].errors[].code` na validação).

**Pedido (`POST /uploads`):**

| Status | Código | Quando |
|---|---|---|
| `400` | `VALIDATION_ERROR` → `UPLOAD_PURPOSE_REQUIRED` / `UPLOAD_PURPOSE_INVALID` (campo `purpose`) | Faltou ou não existe. |
| `400` | `VALIDATION_ERROR` → `UPLOAD_CONTENT_TYPE_REQUIRED` / `UPLOAD_CONTENT_TYPE_INVALID` (`contentType`) | Faltou ou não é JPEG/PNG/WebP. |
| `400` | `VALIDATION_ERROR` → `UPLOAD_SIZE_REQUIRED` / `UPLOAD_SIZE_INVALID` / `UPLOAD_SIZE_TOO_LARGE` (`sizeBytes`, `params.maxBytes`) | Faltou, ≤ 0 ou acima de 5 MB. |
| `400` | `VALIDATION_ERROR` → `UPLOAD_RESOURCE_ID_REQUIRED` (`params.resource`) / `UPLOAD_RESOURCE_ID_NOT_ALLOWED` (`resourceId`) | Conforme a finalidade. |
| `400` | `VALIDATION_ERROR` → `UPLOAD_PHOTO_ID_NOT_ALLOWED` (`photoId`) | Finalidade sem substituição. |
| `400` | `VALIDATION_ERROR` → `UPLOAD_KIND_REQUIRED` / `UPLOAD_KIND_INVALID` (`params.allowed`) / `UPLOAD_KIND_NOT_ALLOWED` (`kind`) | Conforme a finalidade. |
| `401` | `UNAUTHORIZED` | Sem token ou token inválido. |
| `403` | `UPLOAD_PURPOSE_FORBIDDEN` | A finalidade não é do papel do token (tutor pedindo `listing-photo`). |
| `404`/`403`/`409`/`422` | os do caso de uso | `PET_NOT_FOUND`, `PET_PHOTO_NOT_FOUND`, `PET_PHOTO_LIMIT_REACHED`, `LISTING_NOT_FOUND`, `LISTING_PHOTO_LIMIT_REACHED`, `CONVERSATION_NOT_FOUND`, `CONVERSATION_BLOCKED`, `CONVERSATION_RECIPIENT_SUSPENDED`, `OWNER_NOT_FOUND`, erros do rascunho do anfitrião... os mesmos das rotas antigas. |
| `422` | `UPLOAD_PENDING_LIMIT_REACHED` (`params.max`) | 10 uploads pendentes na conta. |
| `429` | `TOO_MANY_REQUESTS` | Política `uploads`. |

**`PUT` no bucket:** `403` (XML do provedor, fora do envelope) quando a URL venceu, foi alterada, ou o tamanho/`Content-Type` não batem com os assinados.

**Confirmação:**

| Status | Código | Quando | Dá para confirmar de novo? |
|---|---|---|---|
| `200` | — | Concluído (ou já estava). | sim, mesma resposta |
| `400` | `VALIDATION_ERROR` no campo `file` → `IMAGE_FILE_EMPTY`, `IMAGE_FILE_TOO_LARGE`, `IMAGE_UNSUPPORTED_FORMAT`, `IMAGE_FILE_CORRUPTED` | O conteúdo real não é uma imagem aceita (o `Content-Type` declarado não basta). No `host-document`, o campo é a vaga (`front`, `back`, `selfie`). | não (recusado) |
| `400` | `VALIDATION_ERROR` no campo `body` → `CONVERSATION_BODY_TOO_LONG` / `CONVERSATION_BODY_INVALID_CHARACTERS` | Legenda do chat. | **sim** |
| `404` | `UPLOAD_NOT_FOUND` | Não existe, é de outra conta ou de outro papel, ou já saiu da guarda de 24 h. | — |
| `409` | `UPLOAD_CONFIRMATION_CONFLICT` | Outra confirmação em andamento. | sim, em instantes |
| `409`/`422`/`403`/`404` | os do caso de uso | Ex.: `PET_PHOTO_ALREADY_EXISTS` (mesma imagem), `PET_PHOTO_LIMIT_REACHED` (encheu entre o pedido e a confirmação), `CONVERSATION_BLOCKED`. | não (recusado) |
| `422` | `UPLOAD_FILE_NOT_RECEIVED` | O `PUT` ainda não aconteceu (ou falhou). | **sim** |
| `422` | `UPLOAD_EXPIRED` | Passou de `confirmBy`. O arquivo é apagado. | não |
| `422` | `UPLOAD_ALREADY_REJECTED` | Já foi recusado numa confirmação anterior. | não |

## 8. Exemplo completo (TypeScript)

```ts
type Ticket = { uploadId: string; uploadUrl: string; method: "PUT"; headers: Record<string, string>; expiresAt: number; confirmBy: number };

export async function uploadDirect<T>(token: string, file: File, target: { purpose: string; resourceId?: string; photoId?: string; kind?: string }, body?: string): Promise<T> {
  const api = (path: string, init: RequestInit = {}) =>
    fetch(`${process.env.NEXT_PUBLIC_API_URL}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", ...init.headers },
    }).then(async (r) => ({ status: r.status, json: await r.json() }));

  // 1. pedir
  const requested = await api("/api/v1/uploads", {
    method: "POST",
    body: JSON.stringify({ ...target, contentType: file.type, sizeBytes: file.size }),
  });
  if (requested.status !== 201) throw requested.json.error; // 400/403/404/422/429
  const ticket: Ticket = requested.json.data;

  // 2. enviar direto ao bucket (sem Authorization)
  const put = await fetch(ticket.uploadUrl, { method: ticket.method, headers: ticket.headers, body: file });
  if (!put.ok) throw new Error(`Bucket recusou o envio (${put.status})`);

  // 3. confirmar (idempotente: pode repetir em caso de timeout)
  const confirmed = await api(`/api/v1/uploads/${ticket.uploadId}/confirm`, {
    method: "POST",
    body: JSON.stringify(body === undefined ? {} : { body }),
  });
  if (confirmed.status !== 200) throw confirmed.json.error;
  return confirmed.json.data.result as T;
}

// uso
const pet = await uploadDirect<PetResponse>(token, file, { purpose: "pet-photo", resourceId: petId });
const message = await uploadDirect<MessageResponse>(token, file, { purpose: "chat-photo", resourceId: conversationId }, "Olha ela!");
```

Converta HEIC (iPhone) para JPEG no front antes de pedir: só JPEG, PNG e WebP são aceitos.

## 9. Garantias e segurança

| Requisito | Como |
|---|---|
| Credenciais do bucket só no backend | O front só recebe URLs assinadas (`X-Amz-Signature`), que valem para um arquivo, um método e poucos minutos. |
| Cliente não escolhe chave nem URL | A chave (`uploads/pending/<uploadId>`) é gerada pela API; a confirmação recebe só o `uploadId`, e o destino final é decidido pelo caso de uso. |
| Não confiar no nome, tamanho ou tipo declarados | O tamanho e o tipo vão **na assinatura** (o bucket recusa outro corpo). Na confirmação, a API **lê o arquivo** e confere o formato pelos primeiros bytes, o tamanho real (5 MB) e a estrutura; o nome nunca é usado. |
| Metadados sensíveis removidos | Mesmo `ImageUploadReader` do upload por multipart: EXIF/GPS, XMP, IPTC e o que vem depois do fim da imagem saem antes de gravar no destino. |
| Pendente privado até as verificações | A área de espera é o bucket **privado**; o arquivo só vai para o destino (público ou privado) depois de conferido. |
| Permissão conferida no pedido **e** na confirmação | No pedido, o caso de uso roda com uma imagem de mentira (`ImageUpload.Preflight`), que passa por todas as checagens e para antes de gravar; na confirmação ele roda de verdade (as regras podem ter mudado no meio). |
| Upload de outra pessoa | `404 UPLOAD_NOT_FOUND`: conta **e** papel precisam ser os do pedido. |
| Confirmação idempotente | Estado no banco (`uploads.upload_sessions`) com reserva por `UPDATE` condicional; a resposta concluída é guardada e devolvida igual. Na foto do chat, o id da mensagem é o id do upload (a confirmação repetida acha a mesma mensagem); nas fotos do pet e do cantinho, a mesma imagem não entra duas vezes (hash). |
| Foto anterior apagada só depois de salvar a nova | É o `ImageReplacement` de sempre: grava a nova, salva no banco, só então apaga a anterior (falhou: apaga a nova). |
| Chat: mensagem e hub só depois da confirmação | O `PUT` não cria nada; a mensagem e `messageCreated`/`unreadCountChanged` saem do caso de uso na confirmação. |

## 10. Limpeza

`UploadCleanupService` roda a cada 5 minutos:

1. Upload pendente que passou de `confirmBy` vira `expired` e o arquivo sai do bucket.
2. Arquivo da área de espera mais velho que o prazo de confirmar (+10 min de folga) é apagado mesmo sem linha no banco (`PUT` repetido depois da confirmação, apagar que falhou).
3. Upload terminado (`completed`, `rejected`, `expired`) há mais de 24 h sai do banco.

Upload recusado tem o arquivo apagado na hora da confirmação. Como segunda barreira, dá para criar no R2/S3 uma **regra de ciclo de vida** que expira objetos do prefixo `uploads/pending/` em 1 dia.

## 11. Configuração (backend)

| Configuração | Variável (`.env`) | Padrão | Para que serve |
|---|---|---|---|
| `Uploads:UrlLifetimeMinutes` | `UPLOADS_URL_LIFETIME_MINUTES` | `10` | Validade da URL de `PUT` (1 a 60). |
| `Uploads:ConfirmWindowMinutes` | `UPLOADS_CONFIRM_WINDOW_MINUTES` | `30` | Prazo para confirmar (≥ a URL, até 24 h). |
| `Uploads:MaxPendingPerUser` | `UPLOADS_MAX_PENDING_PER_USER` | `10` | Pendentes por conta. |
| `Uploads:CleanupIntervalMinutes` | — | `5` | Intervalo da limpeza. |
| `Uploads:FinishedRetentionHours` | — | `24` | Guarda do resultado para a confirmação repetida. |
| `Uploads:ConfigureBucketCors` | `UPLOADS_CONFIGURE_BUCKET_CORS` | `false` (`true` no compose) | Grava o CORS do bucket privado na subida (seção 12). |
| `Storage:SignedUrlServiceUrl` | `STORAGE_SIGNED_URL_SERVICE_URL` | vazio | Host das URLs assinadas — também das de `PUT`. No RustFS local, `http://localhost:9000`. |
| `Storage:SignedUrlLifetimeMinutes` | `STORAGE_SIGNED_URL_LIFETIME_MINUTES` | `5` | Validade dos links de leitura (fotos do chat e documentos). |

A tabela fica no schema `uploads` (migration `CreateUploadSessionsTable`), aplicada na subida fora de produção como as outras.

## 12. CORS do bucket

O `PUT` sai do navegador (origem do front) para o bucket (outra origem): o bucket **privado** precisa de CORS. O de imagens públicas continua sem (só `GET` por `<img>`).

| Regra | Valor |
|---|---|
| Origens | as do front (as mesmas de `Cors:AllowedOrigins`, ex.: `http://localhost:3000`, `https://app.pethost.com.br`) |
| Métodos | `PUT` |
| Headers aceitos | `content-type` |
| Headers expostos | `ETag` |
| Max-Age | 3600 |

- **RustFS (compose):** a API grava sozinha na subida (`UPLOADS_CONFIGURE_BUCKET_CORS=true`), com as origens de `CORS_ALLOWED_ORIGIN`. Origem fora da lista recebe 403 no preflight.
- **Cloudflare R2:** bucket privado → **Settings → CORS Policy**:

  ```json
  [{ "AllowedOrigins": ["https://app.pethost.com.br"], "AllowedMethods": ["PUT"], "AllowedHeaders": ["content-type"], "ExposeHeaders": ["ETag"], "MaxAgeSeconds": 3600 }]
  ```

  E `UPLOADS_CONFIGURE_BUCKET_CORS=false` (a chave de objeto não pode mudar CORS; se ficar `true`, a API só registra um aviso no log, evento 6023).
- **Supabase Storage:** o CORS do endpoint S3 é gerenciado pelo próprio Supabase, não pelo bucket. Não foi testado aqui: teste o `PUT` do navegador antes de depender dele.

## 13. Testes

- **Integração, Pets** (`PetPhotoDirectUploadTests`, Postgres + RustFS reais): fluxo completo com o `PUT` direto no bucket; arquivo pendente sem acesso anônimo; foto só depois da confirmação; confirmação repetida com a mesma resposta e sem foto duplicada; pet de outra conta (`404`, nada assinado); limite de fotos no pedido; validação dos metadados; finalidade de outro papel (`403`); bucket recusando tamanho e `Content-Type` diferentes dos assinados; confirmar antes do `PUT` e depois de novo; upload de outra conta (`404`); conteúdo que não é imagem (recusado, arquivo apagado, `UPLOAD_ALREADY_REJECTED` depois); substituição apagando a imagem antiga só depois; upload vencido; limpeza.
- **Integração, Conversations:** foto do chat no bucket privado com URL assinada (sem assinatura: 403); link novo pela rota da foto (estranho: `404`); `chat-photo` direto criando a mensagem só na confirmação, com o id do upload e uma vez só; conversa de outra pessoa no pedido (`404`); retenção apagando a foto do bucket privado.
- **Unitários:** `SendPhotoMessageCommandHandler` (bucket privado, compensação, id repetido devolve a mesma mensagem sem avisar de novo), `Message.Photo` (chave fora da pasta, chave longa, id dado), `MessageResponse` com link assinado, retenção e exclusão apagando as chaves privadas.
