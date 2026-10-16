# Armazenamento de imagens

Fotos de perfil, de pet, do cantinho e, no futuro, do anfitrião ficam num **bucket compatível com S3**:

| Ambiente | Onde | Como sobe |
|---|---|---|
| Desenvolvimento | **RustFS** (container do `docker-compose`) | Sozinho, com `docker compose up`. Nada a configurar. |
| Testes de integração | RustFS via Testcontainers (`TestImageBucket`, no TestKit) | Sozinho, como o Postgres e o Redis. |
| Produção (ou teste real) | **Supabase Storage** (sem cartão), **Cloudflare R2** ou **Backblaze B2** | Variáveis `STORAGE_*` no `.env` (passo a passo abaixo). |

O código é o mesmo nos três: muda só a configuração. O RustFS substitui o MinIO, cujas imagens oficiais não são mais publicadas no Docker Hub.

São **dois buckets**: o de imagens, **público para leitura** (fotos de perfil, pet, cantinho), e o de **documentos, privado** (documento de identidade e selfie da verificação do anfitrião). Seção 8.

## Sumário

- [Armazenamento de imagens](#armazenamento-de-imagens)
  - [Sumário](#sumário)
  - [1. Como funciona](#1-como-funciona)
  - [2. Endpoints](#2-endpoints)
  - [3. Regras do arquivo e erros](#3-regras-do-arquivo-e-erros)
  - [4. Cloudflare R2: custo e configuração](#4-cloudflare-r2-custo-e-configuração)
    - [Dá para usar de graça?](#dá-para-usar-de-graça)
    - [Passo a passo](#passo-a-passo)
    - [Supabase Storage (sem cartão)](#supabase-storage-sem-cartão)
  - [5. Desenvolvimento local (RustFS)](#5-desenvolvimento-local-rustfs)
  - [6. Como dar foto a uma entidade nova](#6-como-dar-foto-a-uma-entidade-nova)
  - [7. Testes](#7-testes)
  - [8. Bucket privado (documentos)](#8-bucket-privado-documentos)

---

## 1. Como funciona

```
front ──multipart (file)──▶ API ──PutObject──▶ bucket (R2 / RustFS)
                             │
                             └── grava a URL pública na entidade (users.avatar_url, pets.photo_url...)
front ◀── JSON com a URL ──── API
front ──GET da URL──────────▶ bucket público (direto, sem passar pela API)
```

- **A imagem passa pela API.** O front manda o arquivo; a API confere, envia ao bucket, grava a URL e devolve a entidade atualizada. As chaves do bucket ficam só no servidor.
- **O bucket é público para leitura.** A URL gravada no banco abre direto no navegador, com cache de 1 ano (`immutable`), sem token. O token só é exigido para **enviar, trocar ou remover** uma foto, e só o dono da entidade pode fazer isso.
- **O upload devolve o hash da imagem.** `StoredImage` traz a URL e o SHA-256 dos bytes (`ContentHash`). Quem guarda várias fotos usa o hash para recusar a mesma imagem duas vezes; o pet faz isso (`409 PET_PHOTO_ALREADY_EXISTS`).
- **O nome do arquivo é aleatório**: `<pasta>/<guid>.<ext>`, por exemplo `pets/01a1236f...d4c0.png`. Ninguém adivinha o link, e nada do nome original vai para o bucket.
- **A foto só entra por upload.** `photoUrl`, `photos` e `avatarUrl` não são aceitos nos corpos JSON (cadastro e PATCH): o campo é ignorado. Assim, só existe no banco URL que a própria API gerou.
- **Trocar a foto apaga a anterior; remover apaga a atual.** Se gravar no banco falhar, a imagem nova é apagada (compensação). Se apagar a antiga falhar, o pedido não cai: o arquivo fica órfão e vai para o log (`Warning`, evento 6002).

As peças genéricas, que servem a qualquer módulo:

| Peça | Onde | Papel |
|---|---|---|
| `IImageStorage` | `Shared.Contracts/Storage` | Enviar (`SaveAsync`) e apagar (`DeleteAsync`) uma imagem. |
| `IPrivateImageStorage` | `Shared.Contracts/Storage` | O mesmo no bucket **privado**, mais ler (`OpenAsync`). Guarda e devolve a **chave**, nunca URL (seção 8). |
| `ImageReplacement` | `Shared.Contracts/Storage` | O fluxo inteiro de trocar/remover: enviar → gravar → apagar a anterior, ou desfazer. |
| `ImageFolders` | `Shared.Contracts/Storage` | Pastas: `avatars`, `pets`, `listings` (fotos do cantinho), `conversations` (fotos do chat, apagadas pela retenção da conversa — [`modulo-conversations.md`](modulo-conversations.md)) e `hosts` (reservada). |
| `ImageRules` / `ImageErrors` | `Shared.Contracts/Storage` | Limites e erros do arquivo, iguais em todo endpoint. |
| `S3ImageStorage` | `Shared.Infrastructure/Storage` | Implementação S3 (AWS SDK), com a checagem do formato pelo conteúdo. |
| `ImageMetadataStripper` | `Shared.Infrastructure/Storage` | Tira EXIF/XMP/IPTC e afins antes de gravar (seção 3.1). Usado pelos dois buckets. |
| `[ImageUploadEndpoint]` + `IFormFile.ToImageUpload()` | `Shared.Infrastructure/Http` | Marca a action de upload (multipart, limite de corpo, 413) e converte o arquivo. |

## 2. Endpoints

| Método | Rota | Quem | O que faz | Sucesso |
|---|---|---|---|---|
| `PUT` | `/api/v1/hosts/me/application/documents` | anfitrião | Fotos da verificação num pedido só (partes `front`, `back`, `selfie`) no bucket **privado** (seção 8). | `200` |
| `PUT` | `/api/v1/owners/me/avatar` | tutor | Troca a foto de perfil. Resposta: o tutor, com `user.avatarUrl`. | `200` |
| `DELETE` | `/api/v1/owners/me/avatar` | tutor | Tira a foto de perfil. Idempotente. | `200` |
| `POST` | `/api/v1/pets/{petId}/photos` | dono do pet | Adiciona uma foto ao pet (até 3). Resposta: o pet, com `photos`. | `200` |
| `PATCH` | `/api/v1/pets/{petId}/photos/{photoId}` | dono do pet | Substitui a imagem de uma foto (mesmo id e posição). | `200` |
| `DELETE` | `/api/v1/pets/{petId}/photos/{photoId}` | dono do pet | Tira uma foto do pet. | `200` |
| `PUT` | `/api/v1/pets/{petId}/photos/order` | dono do pet | Reordena as fotos (`{ "photoIds": [...] }`); a primeira vira a capa. Não mexe no bucket. | `200` |

O upload é `multipart/form-data` com a imagem na parte **`file`**:

```bash
curl -X POST http://localhost:8080/api/v1/pets/{petId}/photos \
  -H "Authorization: Bearer <token>" \
  -F "file=@pipoca.jpg"
```

No front (`fetch`), sem definir o `Content-Type` à mão, porque o navegador põe o `boundary`:

```js
const form = new FormData();
form.append("file", input.files[0]);
await fetch(`/api/v1/pets/${petId}/photos`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body: form });
```

Pet de outra conta responde `404 PET_NOT_FOUND`, e nada é enviado ao bucket, porque a posse é conferida antes do upload. Hosts e Listings ganham as rotas deles quando tiverem funcionalidades (seção 6).

## 3. Regras do arquivo e erros

- Formatos: **JPG/JPEG, PNG ou WebP** (`.jpg` e `.jpeg` são o mesmo formato). O formato é descoberto pelos **primeiros bytes** do arquivo, não pelo nome nem pelo `Content-Type` que o cliente declara: um `.exe` renomeado para `.png` não passa.
- Tamanho: até **5 MB** por imagem. O corpo do pedido tem teto de 8 MB: entre 5 e 8 MB a resposta é o 400 com a mensagem de tamanho; acima de 8 MB, o servidor recusa sem ler (413).

| Status | Código | Campo | Mensagem |
|---|---|---|---|
| `400` | `VALIDATION_ERROR` | `file` | `Send the image in the 'file' field of a multipart/form-data request.` |
| `400` | `VALIDATION_ERROR` | `file` | `The image file is empty.` |
| `400` | `VALIDATION_ERROR` | `file` | `The image must be at most 5 MB.` |
| `400` | `VALIDATION_ERROR` | `file` | `The image must be a JPG/JPEG, PNG or WebP file.` |
| `400` | `VALIDATION_ERROR` | `file` | `The image file is corrupted and could not be read.` Formato certo, estrutura quebrada (arquivo cortado, bloco com tamanho inválido). |
| `413` | `PAYLOAD_TOO_LARGE` | — | `The request body must be at most 8 MB. Images up to 5 MB are accepted.` |
| `415` | `UNSUPPORTED_MEDIA_TYPE` | — | `Unsupported content type. Send multipart/form-data, with the image in the 'file' field.` Corpo em JSON ou imagem crua (`Content-Type: image/jpeg`). |

### 3.1 Metadados (EXIF) removidos

Foto de celular costuma levar a **localização GPS** de onde foi tirada (muitas vezes a casa de quem enviou), além de modelo do aparelho, data e hora. Antes de gravar — no bucket público e no privado — a API tira os metadados (`ImageMetadataStripper`, chamado pelo `ImageUploadReader`):

| Formato | Sai | Fica |
|---|---|---|
| JPEG | APP1 (EXIF, XMP), APP2 que não é perfil de cor (FlashPix, MPF), APP3–APP13 e APP15 (dados de fabricante, IPTC), comentários, miniatura JFXX e **tudo depois do fim da imagem** (imagens extras que alguns celulares anexam, cada uma com o próprio EXIF). | JFIF, perfil de cor (ICC), bloco Adobe, tabelas e dados da imagem — e a **orientação**, regravada num EXIF mínimo só com ela (senão foto em pé apareceria deitada). |
| PNG | `eXIf`, `tEXt`, `zTXt`, `iTXt`, `tIME` e o que vier depois do `IEND`. | Os demais blocos (incluindo perfil de cor). A orientação de PNG não é mantida — câmera e celular gravam JPEG. |
| WebP | Chunks `EXIF` e `XMP ` (e as flags deles no `VP8X`). | Os demais (incluindo `ICCP`). |

A imagem **não é decodificada nem recomprimida**: só os blocos de metadados saem, o resto é copiado byte a byte. Não perde qualidade, não gasta CPU e o hash de foto repetida continua estável (a mesma foto com EXIF diferente dá o mesmo hash). Sem biblioteca de imagem nova no projeto.

Arquivo com a estrutura quebrada (cortado antes do fim, bloco com tamanho maior que o arquivo) é recusado com `The image file is corrupted and could not be read.` — gravar como veio poderia deixar o EXIF passar.

> A API não redimensiona a imagem.

## 4. Cloudflare R2: custo e configuração

### Dá para usar de graça?

Sim, dentro da cota gratuita mensal do R2 (valores da página de preços da Cloudflare; confira lá antes de ir para produção, porque podem mudar):

| Item | Grátis por mês | O que conta |
|---|---|---|
| Armazenamento | 10 GB | O total de imagens guardadas. Com fotos de ~300 KB, são ~30 mil fotos. |
| Operações classe A | 1 milhão | Escritas: cada upload (`PutObject`), listagem. |
| Operações classe B | 10 milhões | Leituras pelo bucket (cada imagem servida que não estava no cache). |
| Saída de dados (egress) | **Grátis, sem limite** | O grande diferencial em relação ao S3 da AWS. |

- Para ativar o R2, a Cloudflare costuma pedir um **meio de pagamento** cadastrado, mesmo no plano gratuito. Nada é cobrado enquanto o uso ficar dentro da cota.
- O endereço público `r2.dev` é para **desenvolvimento**: tem limite de requisições e não usa o cache do CDN. Em produção, ligue um **domínio próprio** ao bucket (o domínio precisa estar na Cloudflare). Aí as leituras saem do cache e quase não gastam operações classe B.

### Passo a passo

1. **Conta e R2.** Em [dash.cloudflare.com](https://dash.cloudflare.com), abra **R2 Object Storage** e ative o plano (o gratuito).
2. **Bucket.** **Create bucket** → nome `pethost-images` (ou outro; vai em `STORAGE_BUCKET_NAME`) → localização *Automatic*.
3. **Acesso público.** No bucket: **Settings → Public access**.
   - Desenvolvimento: ative **R2.dev subdomain**. A URL `https://pub-<hash>.r2.dev` vai em `STORAGE_PUBLIC_BASE_URL`.
   - Produção: **Custom Domains → Connect domain** (ex.: `img.pethost.com.br`). A URL `https://img.pethost.com.br` vai em `STORAGE_PUBLIC_BASE_URL`.
4. **Chave de API.** Na página do R2: **Manage R2 API Tokens → Create API token**.
   - Permissão: **Object Read & Write**.
   - Escopo: **apenas o bucket** `pethost-images`.
   - Ao criar, copie o **Access Key ID** e o **Secret Access Key** (o secret aparece uma vez só) e o endpoint **S3** (`https://<account-id>.r2.cloudflarestorage.com`).
5. **`.env`.** Troque o bloco `STORAGE_*` pelo do R2. O `.env.example` traz o modelo comentado:

   ```env
   STORAGE_SERVICE_URL=https://<account-id>.r2.cloudflarestorage.com
   STORAGE_REGION=auto
   STORAGE_ACCESS_KEY_ID=<Access Key ID>
   STORAGE_SECRET_ACCESS_KEY=<Secret Access Key>
   STORAGE_BUCKET_NAME=pethost-images
   STORAGE_PRIVATE_BUCKET_NAME=pethost-documents
   STORAGE_PUBLIC_BASE_URL=https://pub-<hash>.r2.dev
   STORAGE_FORCE_PATH_STYLE=false
   # Vazio = links assinados dos documentos usam STORAGE_SERVICE_URL (o navegador alcança o R2).
   STORAGE_SIGNED_URL_SERVICE_URL=
   ```

6. **Subir.** `docker compose up -d`. A API passa a gravar no R2; o RustFS continua subindo, mas fica sem uso. Teste com o `curl` da seção 2: a URL devolvida deve começar com `STORAGE_PUBLIC_BASE_URL` e abrir no navegador.

> Não precisa configurar CORS no bucket: o upload passa pela API, e o navegador só faz `GET` simples (tag `<img>`) nas imagens.

Se a configuração estiver incompleta (endpoint, chave, bucket ou URL pública faltando ou inválida), **a API não sobe** e o log diz qual `Storage:*` falta. É de propósito: melhor falhar na subida do que em cada upload.

### Supabase Storage (sem cartão)

Não pede cartão e aceita bucket público. O plano grátis tem pouco espaço (cerca de 1 GB) e **pausa o projeto** depois de um tempo sem uso; para reativar, é no painel. Confira os limites atuais na página de preços.

1. **Projeto.** Em [supabase.com](https://supabase.com), **New project**. Em **Region**, escolha **South America (São Paulo)**. A senha do banco que ele pede não é usada pela API (o PetHost tem o próprio Postgres).
2. **Bucket.** **Storage → New bucket**:
   - nome `pethost-images`;
   - ligue **Public bucket**;
   - opcional, como segunda barreira: limite de 5 MB e tipos `image/jpeg, image/png, image/webp`.
3. **Chaves S3.** **Storage → S3 Configuration** (em alguns painéis, *Project Settings → Storage*):
   - confira que a conexão S3 está ligada;
   - copie o **Endpoint** (`https://<ref>.supabase.co/storage/v1/s3`) e a **Region**;
   - em **Access keys → New access key**, copie o **Access key ID** e o **Secret access key** (o secret aparece uma vez só).

   Essas chaves dão acesso total ao Storage do projeto: ficam **só no servidor**, nunca no front.
4. **`.env`.** O `.env` já traz o bloco comentado. Tire o `# ` das linhas `STORAGE_*` e preencha; `<ref>` é o trecho antes de `.supabase.co`, e aparece duas vezes:

   ```env
   STORAGE_SERVICE_URL=https://<ref>.supabase.co/storage/v1/s3
   STORAGE_REGION=sa-east-1
   STORAGE_ACCESS_KEY_ID=<Access key ID>
   STORAGE_SECRET_ACCESS_KEY=<Secret access key>
   STORAGE_BUCKET_NAME=pethost-images
   STORAGE_PRIVATE_BUCKET_NAME=pethost-documents
   STORAGE_PUBLIC_BASE_URL=https://<ref>.supabase.co/storage/v1/object/public/pethost-images
   STORAGE_FORCE_PATH_STYLE=true
   STORAGE_SIGNED_URL_SERVICE_URL=
   ```

   `STORAGE_FORCE_PATH_STYLE=true` é obrigatório no Supabase. A URL pública usa `/object/public/`, e não o endpoint S3.
5. **Subir.** `docker compose up -d` e teste um upload. A URL devolvida deve abrir no navegador.

| Sintoma | Causa provável |
|---|---|
| `SignatureDoesNotMatch` | Região diferente da mostrada no painel, endpoint sem `/storage/v1/s3`, ou chave errada (gere outra). |
| Upload dá certo, mas a URL dá 400/404 | Bucket não está como **Public**, ou `STORAGE_PUBLIC_BASE_URL` com o nome do bucket errado. |
| Tudo parou de funcionar depois de dias | Projeto pausado por inatividade: reative no painel. |

## 5. Desenvolvimento local (RustFS)

O `docker compose up` sobe dois serviços:

- **`rustfs`**: o armazenamento, na porta `9000` (API S3), com o console web em [http://localhost:9001](http://localhost:9001). O login do console é `RUSTFS_ACCESS_KEY` / `RUSTFS_SECRET_KEY`, por padrão `pethost` / `pethost_local_dev`. Os dados ficam no volume `rustfs-data`.
- **`rustfs-init`**: roda uma vez, cria o bucket `pethost-images` com leitura pública e o bucket `pethost-documents` **sem** política (privado), e termina. A API só sobe depois dele.

A API fala com o RustFS por `http://rustfs:9000` (rede do Docker) e grava URLs `http://localhost:9000/pethost-images/...`, que o navegador da máquina abre.

## 6. Como dar foto a uma entidade nova

Exemplo: foto do anfitrião (`hosts`) ou fotos do cantinho (`listing_photos`). Nada no `Shared` muda:

1. **Domínio:** um método que troca a URL, conferindo-a (URL absoluta `http`/`https`, ≤ 500), como `Pet.ChangePhoto`.
2. **Application:** dois comandos, `ChangeXPhoto(…, ImageUpload? Image)` e `RemoveXPhoto(…)`, com validador trivial. O handler:
   1. carrega a entidade e confere a posse (sem isso, um estranho envia arquivos ao bucket);
   2. chama `ImageReplacement.ReplaceAsync(storage, image, ImageFolders.Hosts, apply, ct)`, onde `apply` grava a URL e devolve `ImageChange(resposta, urlAnterior)`. Veja `PetPhoto.SetAsync`.
   Para várias fotos (o cantinho tem até 5), o `apply` adiciona uma linha em `listing_photos`; remover uma foto é `RemoveAsync` com a URL daquela linha. **Feito** no módulo Listings, com reordenação (`PUT /listings/{id}/photos/order`) — [`modulo-listings.md`](modulo-listings.md) seção 6.
   Para várias fotos (como o pet, até 3), siga `pet_photos`: tabela própria com vaga única por dono e `content_hash` único por dono (sem foto repetida), `POST` adiciona, `PATCH .../{photoId}` substitui (devolve a URL anterior para apagar), `DELETE .../{photoId}` tira.
3. **Presentation:** a action com `[ImageUploadEndpoint]` e o parâmetro `IFormFile? file`, chamando `file.ToImageUpload()`.
4. **Corpo JSON:** não aceite a URL da foto no cadastro nem no PATCH.

## 7. Testes

- **Unitários:** `ImageSignatureTests` (formatos aceitos e recusados), `ImageMetadataStripperTests` (EXIF com GPS removido e orientação mantida em big e little-endian, ICC e Adobe mantidos, XMP/IPTC/comentários/MPF fora, JPEG progressivo, lixo depois do fim descartado, chunks de PNG e WebP com flags do `VP8X` ajustadas, arquivo cortado recusado), `ImageReplacementTests` (ordem do fluxo e compensação), e os handlers de foto do Pets e do Owners, com `IImageStorage` simulado.
- **Integração:** `PetPhotoTests` e `OwnerAvatarTests` sobem o RustFS via `TestImageBucket` e conferem:
  - a imagem no bucket e o link público abrindo sem token;
  - a foto anterior apagada na troca;
  - arquivo que não é imagem, grande demais (400) e corpo acima do teto (413);
  - pet de outra conta: 404, sem nada enviado;
  - URL de foto no JSON ignorada.

Um fixture que suba a API sem mexer com imagem usa `TestImageBucket.UnusedSettings`, como o do Auth.

`TestImages.PngVariant(n)` gera PNGs diferentes com um chunk privado (`vaRi`) antes do `IEND` — byte solto depois do fim não serve, porque a limpeza de metadados o descarta e as variantes virariam a mesma foto. `TestImages.Jpeg` é um JPEG com a estrutura inteira: JPEG cortado agora é recusado.

## 8. Bucket privado (documentos)

Documento de identidade e selfie da verificação do anfitrião ([`modulo-hosts.md`](modulo-hosts.md)) são dado pessoal sensível: **nunca** vão para o bucket público.

| | Bucket de imagens | Bucket de documentos |
|---|---|---|
| Configuração | `Storage:BucketName` | `Storage:PrivateBucketName` (padrão `pethost-documents`) |
| Leitura | Pública, por URL, cache de 1 ano | Por **URL assinada** que vence (padrão 5 min), gerada pela API para quem tem permissão; `Cache-Control: private, no-store` |
| O que vai para o banco | URL pública | **Chave** do arquivo (`host-applications/<guid>.<ext>`) |
| Quem vê | Qualquer um com o link | O admin: pede o link na rota `GET /api/v1/host-applications/{id}/documents/{kind}` (cada link vai para a trilha) e abre a imagem direto do bucket até o link vencer |
| Quanto tempo fica | Até ser trocada ou removida | Até o prazo de guarda (padrão: apagado na decisão do admin) |

- Mesmo endpoint e credenciais do bucket de imagens; precisa ser **outro bucket**. A API não sobe se `PrivateBucketName` faltar ou for igual a `BucketName`.
- Mesmas regras de arquivo (JPG/JPEG, PNG, WebP, até 5 MB, formato pelo conteúdo), pela mesma leitura (`ImageUploadReader`).
- **Supabase:** crie um segundo bucket com **Public bucket desligado** e ponha o nome em `STORAGE_PRIVATE_BUCKET_NAME`. As chaves S3 do Supabase valem para o projeto inteiro: em produção, um projeto separado só para documentos (com chave própria e poucos membros no painel) reduz o estrago se uma chave vazar.
- **R2:** um segundo bucket **sem** acesso público (não ligue `r2.dev` nem domínio próprio nele).
### 8.1 URLs assinadas

A imagem não passa pela API: a rota do admin confere a permissão, registra na trilha e devolve uma URL **pré-assinada** do S3 (`IPrivateImageStorage.GetSignedUrlAsync`):

```json
{ "data": { "kind": "selfie", "url": "http://localhost:9000/pethost-documents/host-applications/0192....png?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Expires=300&X-Amz-Signature=...", "expiresAt": 1791729300 } }
```

- `expiresAt` vem em **Unix time (segundos, UTC)**, como o `expiresAt` da sessão: `new Date(expiresAt * 1000)` no front.
- O front põe `url` no `src` da imagem, sem token. Depois de `expiresAt` o bucket responde **403**; é só pedir outro link.
- A assinatura cobre bucket, chave, prazo e host: mudar qualquer parte da URL (inclusive esticar o `X-Amz-Expires`) dá 403.
- Antes de assinar, a API confere que o arquivo ainda existe (foto já apagada → `404 HOST_APPLICATION_DOCUMENT_NOT_FOUND`, sem link).
- Quem tem a URL abre a imagem sem login até ela vencer. Por isso o prazo é curto, a resposta vai com `Cache-Control: no-store` e a URL só sai na rota do admin (nunca em listagem nem para quem enviou). A trilha registra quando o link foi **emitido**; o que acontece depois disso fica nos logs do bucket.

| Configuração | Variável | Padrão | Para que serve |
|---|---|---|---|
| `Storage:SignedUrlLifetimeMinutes` | `STORAGE_SIGNED_URL_LIFETIME_MINUTES` | `5` | Validade do link (1 a 60; fora disso a API não sobe). |
| `Storage:SignedUrlServiceUrl` | `STORAGE_SIGNED_URL_SERVICE_URL` | vazio (= `ServiceUrl`) | Host que vai na URL assinada: o que o **navegador** alcança. Supabase e R2: deixe vazio. **Só o RustFS** precisa (`http://localhost:9000`, já no bloco do RustFS do `.env.example`): a API fala com ele por `http://rustfs:9000` (rede do Docker), que o navegador não resolve. O host entra na assinatura, então não dá para trocar depois. |

> **Link aponta para o lugar errado?** Se a URL assinada começa com `http://localhost:9000` mas as fotos estão no Supabase/R2, sobrou `STORAGE_SIGNED_URL_SERVICE_URL` do bloco do RustFS no `.env`: esvazie e recrie a API (`docker compose up -d api`). Confira com `docker exec pethost-api printenv Storage__SignedUrlServiceUrl`.

- Nos testes, o `TestImageBucket` cria os dois buckets; `CountPrivateAsync`, `ListPrivateKeysAsync` e `PrivateObjectUrl` conferem que as fotos estão lá e que o acesso anônimo é recusado.
