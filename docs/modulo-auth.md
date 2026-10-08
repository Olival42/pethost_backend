# PetHost — Módulo Auth

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md)_

Login, renovação e logout com JWT (access token) + refresh token no Redis, senha com Argon2id e admin criado por seed.

> **Cadastro desligado por enquanto.** O endpoint `POST /api/v1/auth/registrations` e o caso de uso `RegisterUser` foram removidos. O domínio continua sabendo criar tutor e anfitrião (`User.Register`), mas nada da API chama esse método hoje: contas de owner/host só existem se forem inseridas no banco.

## Sumário

1. [Estrutura](#1-estrutura)
2. [Endpoints](#2-endpoints)
3. [Envelope de resposta](#3-envelope-de-resposta)
4. [Códigos de erro e HTTP](#4-códigos-de-erro-e-http)
5. [Validações](#5-validações)
6. [Lógica](#6-lógica)
7. [Banco](#7-banco)
8. [Configuração](#8-configuração)
9. [Testes](#9-testes)
10. [Decisões e desvios do padrão](#10-decisões-e-desvios-do-padrão)
11. [Pendências](#11-pendências)

---

## 1. Estrutura

O projeto saiu do `pethost.csproj` único para a estrutura modular do §3 do `PROJECT_STANDARDS.md`.

```
PetHost.slnx · Directory.Build.props · Directory.Packages.props · .editorconfig · global.json · dotnet-tools.json
src/
├── Host/PetHost.Api/                       Program.cs, health checks, 401/403 no envelope
├── Shared/
│   ├── PetHost.Shared.Kernel/              Result, Error, ICommand/handlers, Entity, ValueObject, Unit
│   ├── PetHost.Shared.Contracts/           ApiResponse, ErrorResponse, ApiResponseMapper, Roles
│   └── PetHost.Shared.Infrastructure/      ToActionResult, status HTTP, decorator de validação, handler global de exceção
└── Modules/Auth/
    ├── Domain/                             User, Email, PasswordHash, UserId, UserRole, AuthErrors, IUserRepository
    ├── Application/                        CreateSession, RefreshSession, RevokeSession + portas
    ├── Infrastructure/                     AuthDbContext, migration, Argon2, JWT, Redis, seeder
    └── Presentation/                       SessionsController
tests/                                      8 projetos de teste (seção 9)
```

| Camada | Responsabilidade |
|---|---|
| Domain | Regras do usuário. Sem EF, sem ASP.NET. |
| Application | Orquestra os casos de uso. Fala com portas (`IPasswordHasher`, `IAccessTokenGenerator`, `IRefreshTokenStore`), não com implementações. |
| Infrastructure | EF Core + Postgres, Argon2id, JWT, Redis, seed. |
| Presentation | Rota → handler → `ToActionResult()`. Não conhece o Domain. |

**Padrões usados:** Result pattern (sem exceção para regra de negócio), Repository + Unit of Work, Decorator (validação antes do handler), Ports & Adapters (portas na Application, adaptadores na Infrastructure), Value Object (`Email`, `PasswordHash`), Factory (`User.Register`, `SessionResponseFactory`), Options pattern com validação na subida.

---

## 2. Endpoints

Todos são `[AllowAnonymous]`, recebem e devolvem JSON em camelCase.

| Método | Rota | O que faz | Sucesso |
|---|---|---|---|
| `POST` | `/api/v1/auth/sessions/login` | Login | `200` |
| `POST` | `/api/v1/auth/sessions/refresh` | Troca o refresh token por um par novo | `200` |
| `POST` | `/api/v1/auth/sessions/logout` | Logout: invalida o refresh token | `200` |
| `GET` | `/health/live` | O processo responde | `200` |
| `GET` | `/health/ready` | Postgres e Redis respondem | `200` / `503` |

### 2.1 Login — `POST /api/v1/auth/sessions/login`

```json
{
  "email": "camila@exemplo.com",
  "password": "senha-forte-123",
  "role": "owner"
}
```

O `role` é obrigatório porque o e-mail sozinho não identifica a conta — é a escolha "Sou tutor / Sou anfitrião" da tela de entrar. Aceita `owner`, `host` e `admin`.

| Status | Código | Quando |
|---|---|---|
| `200` | — | Corpo = [sessão](#24-resposta-de-sessão). |
| `400` | `VALIDATION_ERROR` | Campo faltando ou `role` desconhecido. |
| `401` | `AUTH_INVALID_CREDENTIALS` | E-mail inexistente, senha errada, e-mail malformado ou papel que não bate com a conta. Sempre a mesma resposta. |

### 2.2 Refresh — `POST /api/v1/auth/sessions/refresh`

```json
{ "refreshToken": "q3Jx...Vb8" }
```

| Status | Código | Quando |
|---|---|---|
| `200` | — | Par novo. O token enviado deixa de valer. |
| `400` | `VALIDATION_ERROR` | `refreshToken` vazio. |
| `401` | `AUTH_REFRESH_TOKEN_INVALID` | Token inexistente, expirado, já usado ou de conta que não existe mais. |

### 2.3 Logout — `POST /api/v1/auth/sessions/logout`

```json
{ "refreshToken": "q3Jx...Vb8" }
```

| Status | Código | Quando |
|---|---|---|
| `200` | — | Token revogado. Também `200` se o token já não valia (idempotente). Corpo: `{ "success": true, "data": {}, "timestamp": "..." }`. |
| `400` | `VALIDATION_ERROR` | `refreshToken` vazio. |

O access token já emitido continua válido até expirar (é um JWT sem estado). Por isso a vida dele é curta.

### 2.4 Resposta de sessão

Login e refresh devolvem o mesmo formato:

```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "q3Jx...Vb8",
    "expiresAt": 1791461700,
    "user": {
      "id": "0199c5a2-7f3e-7a41-9b1c-2d4e6f8a0b1c",
      "fullName": "Camila Souza",
      "email": "camila@exemplo.com",
      "role": "owner",
      "phone": "44 99999-0000",
      "neighborhood": "Zona 7",
      "city": "Maringá",
      "state": "PR"
    }
  },
  "timestamp": "2026-10-08T12:00:00.000+00:00"
}
```

| Campo | Tipo | Significado |
|---|---|---|
| `expiresAt` | `long` | Quando o access token expira, em Unix time (segundos, UTC) — igual ao claim `exp` do JWT. Use para agendar o refresh. |
| `user.*` | — | Campos `null` são omitidos (`phone`, `avatarUrl`, `neighborhood`, `city`, `state`). Nunca inclui hash de senha. |

Nas próximas chamadas: `Authorization: Bearer <accessToken>`.

---

## 3. Envelope de resposta

Toda resposta — inclusive 401, 403 e 500 — usa o `ApiResponse<T>` do §7. Campos `null` são omitidos.

```json
// sucesso
{ "success": true, "data": { ... }, "timestamp": "..." }

// erro de validação (400): detalhes agrupados por campo, em camelCase
{ "success": false, "error": {
    "code": "VALIDATION_ERROR", "message": "Validation failed",
    "details": [ { "field": "email", "messages": ["Email is required."] },
                 { "field": "password", "messages": ["Password must be at least 8 characters."] } ] },
  "timestamp": "..." }

// regra de negócio / credencial
{ "success": false, "error": { "code": "AUTH_INVALID_CREDENTIALS", "message": "Email or password is incorrect." }, "timestamp": "..." }

// 500: só o traceId, nunca stack trace
{ "success": false, "error": { "code": "UNEXPECTED_ERROR", "message": "An unexpected error occurred.", "details": { "traceId": "0HN7..." } }, "timestamp": "..." }
```

Corpo ilegível — JSON malformado, corpo vazio ou tipo errado (`"email": 123`) — responde `400` em qualquer endpoint, sem repassar a mensagem do parser:

```json
{ "success": false, "error": { "code": "VALIDATION_ERROR", "message": "Validation failed",
    "details": [ { "field": "body", "messages": ["The request body is missing or is not valid JSON."] } ] },
  "timestamp": "..." }
```

O middleware do JWT também responde no envelope: rota protegida sem token válido → `401 UNAUTHORIZED`; autenticado sem permissão → `403 FORBIDDEN`.

---

## 4. Códigos de erro e HTTP

| Código | HTTP | Mensagem |
|---|---|---|
| `VALIDATION_ERROR` | 400 | `Validation failed` + `details` por campo |
| `AUTH_INVALID_CREDENTIALS` | 401 | Email or password is incorrect. |
| `AUTH_REFRESH_TOKEN_INVALID` | 401 | The refresh token is invalid, expired or already used. |
| `UNAUTHORIZED` | 401 | Authentication is required to access this resource. |
| `FORBIDDEN` | 403 | You do not have permission to access this resource. |
| `AUTH_USER_NOT_FOUND` | 404 | User '{id}' was not found. |
| `AUTH_PASSWORD_HASH_INVALID` | 422 | Erro interno de integridade; não deve aparecer na prática. |
| `UNEXPECTED_ERROR` | 500 | An unexpected error occurred. |

Todos declarados em `AuthErrors` (Domain). O status é decidido em `ErrorCodeStatusMapper` pelo código: primeiro os códigos transversais do §8, depois o **sufixo** do código de módulo:

| Sufixo | HTTP |
|---|---|
| `_NOT_FOUND` | 404 |
| `_CONFLICT`, `_ALREADY_EXISTS`, `_ALREADY_REGISTERED` | 409 |
| `_UNAUTHORIZED`, `_INVALID_CREDENTIALS`, `_TOKEN_INVALID`, `_TOKEN_EXPIRED` | 401 |
| `_FORBIDDEN`, `_NOT_OWNED_BY_REQUESTER` | 403 |
| qualquer outro | 422 |

`AUTH_ADMIN_REGISTRATION_FORBIDDEN` (403) existe só no domínio, como barreira do `User.Register` contra criar admin. Com o cadastro desligado, nenhum endpoint o devolve hoje.

---

## 5. Validações

Duas camadas, como manda o §10:

- **Entrada** (FluentValidation, na Application): formato e obrigatoriedade. Acumula todos os erros e devolve `400`. Roda num decorator **antes** do handler — o handler nunca chama o validador.
- **Domínio** (agregado e value objects): invariantes. Devolve um erro com código do módulo.

| Campo | Regra |
|---|---|
| `email` | Login: obrigatório, ≤ 160 (formato **não** é checado: e-mail malformado vira `401`, não `400`) |
| `password` | Login: obrigatório, ≤ 128 (sem mínimo: não vaza a política de senha) |
| `role` | Login: `owner`, `host` ou `admin` (maiúscula/minúscula tanto faz) |
| `refreshToken` | Obrigatório no refresh e no logout |

O teto de 128 na senha existe porque o custo do Argon2 cresce com o tamanho da entrada.

No domínio, além disso: e-mail é aparado e convertido para minúsculas; nome é aparado; `role` nunca muda depois de criada a conta (o setter é privado e não há método que a altere); `User.Register` recusa `admin`.

---

## 6. Lógica

### 6.1 Login
1. Normaliza o e-mail. Se for inválido → `401` genérico.
2. Busca o usuário por **(e-mail, role)**.
3. Se não existir, **calcula um hash mesmo assim** e devolve `401`. Sem isso, "conta não existe" responderia mais rápido que "senha errada", e o tempo de resposta revelaria quem tem conta.
4. Confere a senha em tempo constante (`CryptographicOperations.FixedTimeEquals`).
5. Emite os tokens.

Todas as falhas devolvem o mesmo `AUTH_INVALID_CREDENTIALS`: de fora, não dá para saber se o e-mail existe.

### 6.2 Access token (JWT)

HMAC-SHA256, emitido em `JwtAccessTokenGenerator`.

| Claim | Valor |
|---|---|
| `sub` | id do usuário (UUID) |
| `email` | e-mail |
| `name` | nome completo |
| `role` | `owner`, `host` ou `admin` — usado por `[Authorize(Roles = Roles.Owner)]` |
| `jti` | id único do token |
| `iss`, `aud` | `Jwt:Issuer`, `Jwt:Audience` |
| `iat`, `nbf`, `exp` | emissão, início e expiração |

Na entrada, o host valida assinatura, issuer, audience e expiração, com tolerância de 30 s de relógio. `MapInboundClaims = false` mantém os nomes curtos (`sub`, `role`) no `User` do controller.

### 6.3 Refresh token (Redis)

- Valor opaco de 32 bytes aleatórios em Base64Url — **não** é JWT.
- Chave no Redis: `pethost:auth:refresh:<SHA-256 do token>`, valor = id do usuário, TTL = `Jwt:RefreshTokenLifetimeDays`. Guarda-se o hash, não o token: um dump do Redis não serve para logar.
- **Rotação:** cada refresh consome o token com `GETDEL` (ler e apagar num comando só) e emite outro. Reusar um token falha, e duas chamadas simultâneas com o mesmo token não podem ambas ter sucesso.
- **Logout:** apaga a chave. Não existe tabela de sessão nem limpeza de tokens vencidos — o TTL cuida disso.

### 6.4 Senha (Argon2id)

Biblioteca `Konscious.Security.Cryptography.Argon2`. Custo padrão = mínimo do OWASP: 19 MiB de memória, 2 iterações, paralelismo 1. Salt de 16 bytes, hash de 32 bytes.

O hash é gravado no formato PHC, que carrega os próprios parâmetros:

```
$argon2id$v=19$m=19456,t=2,p=1$<salt base64>$<hash base64>
```

Por isso dá para subir o custo depois sem invalidar senhas antigas: cada hash é conferido com os parâmetros com que foi criado. Hash corrompido no banco devolve `false` (vira `401`), nunca `500`.

### 6.5 Seed do admin

O dicionário de dados diz que o admin é criado direto no banco. `AuthDbSeeder` faz isso na subida da API:

- Só roda com `Seed:Admin:Enabled = true` e com e-mail e senha preenchidos.
- **Idempotente:** se já existe admin com aquele e-mail, não faz nada.
- **Nunca atualiza a senha** de um admin existente — trocar senha por variável de ambiente seria um jeito silencioso de sequestrar a conta.
- O e-mail aparece mascarado no log (`a***@pethost.com`); a senha nunca aparece.

O admin entra pelo login normal com `"role": "admin"`.

### 6.6 Subida da API

Ordem no `Program.cs`: Serilog (JSON) → MVC com envelope → JSON camelCase sem nulos → módulo Auth → JWT → health checks → handler global de exceção. Depois do build:

- Em **Development**, aplica as migrations automaticamente.
- Em produção, **não** aplica (o §11 diz que migration é passo do pipeline).
- O seed roda em qualquer ambiente, se estiver habilitado.
- Configuração do JWT inválida (chave com menos de 32 bytes, issuer vazio, tempo ≤ 0) **derruba a subida** com mensagem clara, em vez de virar 500 no primeiro login.

---

## 7. Banco

Schema `auth`, histórico de migrations em `auth.__ef_migrations_history`. Migration: `CreateUsersTable`.

```sql
CREATE TABLE auth.users (
    id             uuid         NOT NULL,
    full_name      varchar(120) NOT NULL,
    email          varchar(160) NOT NULL,
    password_hash  varchar(255) NOT NULL,
    role           varchar(10)  NOT NULL,
    phone          varchar(20),
    avatar_url     varchar(500),
    neighborhood   varchar(80),
    city           varchar(80),
    state          char(2),
    created_at     timestamptz  NOT NULL,
    updated_at     timestamptz  NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT ck_users_role CHECK (role IN ('owner', 'host', 'admin'))
);
CREATE UNIQUE INDEX uq_users_email_role ON auth.users (email, role);
```

Bate coluna por coluna com a tabela `users` do `dicionario-de-dados.md`. O `id` é UUID v7 gerado pelo domínio (`UserId.New()`), não pelo banco: o usuário já tem id antes do insert. O índice único é composto: é ele que deixa a mesma pessoa ter conta de tutor e de anfitrião.

Gerar nova migration:

```bash
dotnet dotnet-ef migrations add <Nome> --project src/Modules/Auth/PetHost.Modules.Auth.Infrastructure --startup-project src/Host/PetHost.Api --context AuthDbContext --output-dir Persistence/Migrations
```

(precisa das variáveis `ConnectionStrings__Postgres` e `ConnectionStrings__Redis` definidas, mesmo que o banco não esteja no ar).

---

## 8. Configuração

| Chave (`appsettings`) | Variável de ambiente | Padrão | Observação |
|---|---|---|---|
| `ConnectionStrings:Postgres` | `ConnectionStrings__Postgres` | — | Obrigatória |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | — | Obrigatória |
| `Jwt:Issuer` | `Jwt__Issuer` | — | Obrigatória |
| `Jwt:Audience` | `Jwt__Audience` | — | Obrigatória |
| `Jwt:Key` | `Jwt__Key` | — | Obrigatória, ≥ 32 bytes |
| `Jwt:AccessTokenLifetimeMinutes` | `Jwt__AccessTokenLifetimeMinutes` | `15` | `long` |
| `Jwt:RefreshTokenLifetimeDays` | `Jwt__RefreshTokenLifetimeDays` | `30` | `long` |
| `Argon2:MemorySizeKib` / `Iterations` / `DegreeOfParallelism` | `Argon2__...` | `19456` / `2` / `1` | |
| `Seed:Admin:Enabled` | `Seed__Admin__Enabled` | `false` | |
| `Seed:Admin:Email` | `Seed__Admin__Email` | — | |
| `Seed:Admin:Password` | `Seed__Admin__Password` | — | Nunca versionar |
| `Seed:Admin:FullName` | `Seed__Admin__FullName` | `PetHost Admin` | |

Rodar local sem Docker para a API (Postgres e Redis no compose):

```bash
docker compose up -d postgres redis
```

```powershell
$env:ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=pethost;Username=pethost;Password=pethost_local_dev"
$env:ConnectionStrings__Redis="localhost:6379,password=pethost_local_dev"
$env:Jwt__Issuer="https://api.pethost.local"; $env:Jwt__Audience="pethost-app"
$env:Jwt__Key="troque-esta-chave-de-desenvolvimento-por-uma-gerada-com-openssl-rand"
dotnet run --project src/Host/PetHost.Api
```

---

## 9. Testes

| Projeto | O que cobre | Resultado da última execução |
|---|---|---|
| `Shared.Kernel.UnitTests` | `Result`, `Map`, `BindAsync`, propagação de erros | 14 ✅ |
| `Shared.Contracts.UnitTests` | Montagem do envelope e agrupamento de validação | 6 ✅ |
| `Shared.Infrastructure.UnitTests` | Decorator de validação, código → status HTTP, `ToActionResult` | 25 ✅ |
| `Auth.Domain.UnitTests` | `Email`, `PasswordHash`, `User`, `UserRoleValues` | 53 ✅ |
| `Auth.Application.UnitTests` | Os 3 handlers de sessão e os validadores (com Moq e `FakeTimeProvider`) | 27 ✅ |
| `Auth.Infrastructure.UnitTests` | Argon2id (hash, verificação, PHC, entrada inválida) e geração de JWT | 32 ✅ |
| `ArchitectureTests` | Regras de dependência do §4, `sealed`, sincronia `Roles` ↔ `UserRoleValues` | 16 ✅ |
| `Auth.IntegrationTests` | Corpo malformado, fluxo HTTP completo com Postgres e Redis reais em container (Testcontainers), banco limpo com Respawn, seed, rotação e concorrência do refresh, validação do JWT pelo host. Os usuários de teste são inseridos direto no banco (não há mais cadastro pela API) | 26 ✅ |

**199 testes passando** nos 8 projetos, build com 0 avisos. Nomes no padrão `MethodName_Should_X_When_Y` (§14).

Como rodar: nesta máquina (SDK 10.0.400-preview) o `dotnet test` não reportou os resultados corretamente. Os testes rodam pelo executável de cada projeto. Os de integração precisam do Docker Desktop ligado:

```bash
dotnet build
```

```bash
./tests/Modules/Auth/PetHost.Modules.Auth.Application.UnitTests/bin/Debug/net10.0/PetHost.Modules.Auth.Application.UnitTests.exe
```

Cobertos pela integração, entre outros: a mesma pessoa com conta de tutora e de anfitriã (senha de uma não serve na outra), 5 refresh simultâneos com o mesmo token (só 1 vence), logout invalidando o refresh, token adulterado recusado pelo host, envelope em todo erro, `/health/ready` com Postgres e Redis.

Bugs encontrados durante o desenvolvimento:
1. A biblioteca do Argon2 estoura com senha vazia, e o login chamava `Hash("")` no caminho de "conta inexistente" → teria virado `500` em vez de `401`.
2. `AUTH_INVALID_CREDENTIALS` e `AUTH_EMAIL_ALREADY_REGISTERED` (este removido junto com o cadastro) caíam em `422` em vez de `401` e `409`, porque só os sufixos `_NOT_FOUND` e `_CONFLICT` eram reconhecidos.
3. Um teste de imutabilidade da `role` estava mal escrito e passava por motivo errado.
4. JSON malformado no corpo chegava ao handler como `null` e virava `500`. Achado num teste manual pelo Insomnia; corrigido com o `MalformedRequestBodyFilter` e coberto por 7 testes de integração.

---

## 10. Decisões e desvios do padrão

| Decisão | Por quê |
|---|---|
| Refresh token no **Redis**, sem tabela nova | Escolha sua. O dicionário fecha em 14 tabelas; o TTL do Redis faz a expiração sozinho. |
| Ids `uuid` v7 em vez de `int` identity | Decisão sua, vale para todas as tabelas. Gerado no domínio (`UserId.New()`), ordenado por tempo para não fragmentar o índice. A migration inicial `CreateUsersTable` foi recriada já com `uuid`. |
| Login exige `role` | O `escopo-mvp.md` define e-mail único **por papel**. |
| Cadastro removido por enquanto | Decisão sua. `User.Register` e `UserRegisteredDomainEvent` ficam no domínio para quando o cadastro voltar. |
| `SessionResponse` único para login e refresh | O §9 sugere um `...Response` por caso de uso; aqui o formato é idêntico e o cliente trata os três igual. |
| Login e logout em `POST /sessions/login` e `POST /sessions/logout` | Evita `DELETE` com corpo, que alguns proxies descartam. |
| `role` trafega como `string` nos commands | Mantém a Presentation sem referência ao Domain (§4). |
| Handlers registrados no DI da **Infrastructure** | O decorator de validação mora em `Shared.Infrastructure` (o §3 diz que é lá), e a Application não pode referenciar Infrastructure. |
| Sufixos de status estendidos | Ver seção 4. Sem isso, códigos citados no próprio §8 sairiam com o status errado. |
| `PetHost.slnx` em vez de `PetHost.sln` | Formato gerado pelo SDK 10. |
| Cobertura com `Microsoft.Testing.Extensions.CodeCoverage` em vez de `coverlet.collector` | O xunit.v3 roda no Microsoft.Testing.Platform; o coverlet é coletor do VSTest e não funciona ali. |
| `global.json` com `test.runner = Microsoft.Testing.Platform` | Opt-in exigido pelo SDK 10 para o runner do xunit.v3. |
| Projetos extras `Shared.Infrastructure.UnitTests` e `Auth.Infrastructure.UnitTests` | O §3 não lista, mas decorator, Argon2 e JWT têm lógica e não cruzam processo — não cabem em teste de integração. |
| Regras de analisador desligadas: `CA1716`, `CA1000`, `CA1002` | Brigam com os nomes e assinaturas que o próprio §6 define (`Error`, `Result<T>.Success`, `List<Error>`). |
| `CA1707` e `CA1711` desligadas só nos testes | Brigam com `MethodName_Should_X_When_Y` (§14) e com `[CollectionDefinition]` do xUnit. |
| Pasta `Migrations` marcada como código gerado | O §11 proíbe editar migration à mão, e o código do EF não segue o `.editorconfig`. |

**Conflito em aberto entre os documentos** (não afeta o Auth): o dicionário guarda dinheiro em centavos `int` (`*_cents`), o §11 do `PROJECT_STANDARDS.md` manda `numeric(18,2)` + moeda. Precisa ser decidido antes do módulo de pagamentos.

---

## 11. Pendências

| # | O quê | Impacto |
|---|---|---|
| 1 | Cadastro de tutor/anfitrião desligado. | Sem ele, contas owner/host só existem se forem inseridas direto no banco. Ao religar, lembrar do header `Location` no `201` (§13). |
| 2 | `dotnet test` não reporta corretamente neste SDK preview. | Rodar pelos executáveis até atualizar o SDK. |
| 3 | Seed dos tipos de pet (Cachorro, Gato, Pequenos animais). | É do módulo Pets, fora do escopo do Auth. |
