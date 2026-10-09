# PetHost — Módulo Auth

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md)_

Cadastro da conta base, login, renovação e logout com JWT (access token) + refresh token no Redis, perfil da conta (PATCH), troca de senha logada, inativar e reativar, suspensão pelo admin (os dois pelo contrato que o módulo do papel usa), troca entre a conta de tutor e a de anfitrião, "esqueci a senha" com token por e-mail, senha com Argon2id e admin criado por seed.

> **Conta de tutor é do módulo Owners.** O tutor se cadastra, edita, inativa e reativa em `/api/v1/owners` ([`modulo-owners.md`](modulo-owners.md)), que mexe na conta daqui pelos contratos `IAccountRegistrar`, `IAccountProfileEditor` e `IAccountStatusManager` — assim conta e perfil de tutor nunca ficam fora de sincronia. `POST /api/v1/users/register` cria só conta de **anfitrião**. Não há rota de inativar/reativar em `/users`: só o tutor tem isso, em `/owners`.

> **Trilha de auditoria.** Cadastro, login (sucesso e falha), troca de conta, senha (troca e "esqueci"), perfil, inativar/reativar e suspensão ficam registrados pelo contrato `IAuditTrail` (módulo Audit, [`modulo-audit.md`](modulo-audit.md)). Sem senha, token nem e-mail digitado; perfil vai pelo nome dos campos alterados.

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
│   ├── PetHost.Shared.Contracts/           ApiResponse, ErrorResponse, ApiResponseMapper, Roles, IUserDirectory (contrato de leitura do Auth)
│   └── PetHost.Shared.Infrastructure/      ToActionResult, status HTTP, decorator de validação, handler global de exceção, e-mail (fila + SMTP), GetUserId()
└── Modules/Auth/
    ├── Domain/                             User, UserId, UserRole, AuthErrors, IUserRepository + value objects (seção 5.1)
    ├── Application/                        RegisterAccount, GetCurrentUser, ChangePassword, SuspendAccount, ProfilePatch,
    │                                       GetLinkedAccounts, CreateSession, RefreshSession, RevokeSession, SwitchSession,
    │                                       ForgotPassword, ResetPassword + portas
    ├── Infrastructure/                     AuthDbContext, migrations, Argon2, JWT, Redis, e-mail de troca de senha, seeder, UserDirectory
    └── Presentation/                       UsersController, SessionsController, PasswordController
tests/                                      11 projetos de teste (seção 9)
```

| Camada | Responsabilidade |
|---|---|
| Domain | Regras do usuário. Sem EF, sem ASP.NET. |
| Application | Orquestra os casos de uso. Fala com portas (`IPasswordHasher`, `IAccessTokenGenerator`, `IRefreshTokenStore`, `IPasswordResetTokenStore`, `IPasswordResetNotifier`), não com implementações. |
| Infrastructure | EF Core + Postgres, Argon2id, JWT, Redis, e-mail, seed. |
| Presentation | Rota → handler → `ToActionResult()`. Não conhece o Domain. |

**Padrões usados:** Result pattern (sem exceção para regra de negócio), Repository + Unit of Work, Decorator (validação antes do handler), Ports & Adapters (portas na Application, adaptadores na Infrastructure), Value Object (`Email`, `Password`, `PasswordHash`, `FullName`, `PhoneNumber`, `AvatarUrl`, `StateCode`, `ZipCode`, `Address`), Factory (`User.Register`, `SessionResponseFactory`), Options pattern com validação na subida.

---

## 2. Endpoints

Recebem e devolvem JSON em camelCase. "Token" = precisa de `Authorization: Bearer <accessToken>`; as rotas `me` agem sempre sobre a conta do token, nunca sobre um id vindo da request.

> **Swagger (só em Development):** `http://localhost:8080/swagger` — todas as rotas de todos os módulos, com o formato de cada uma. Faça login em `POST /api/v1/auth/sessions/login`, copie o `accessToken`, clique em **Authorize** e cole (sem "Bearer"); o token fica salvo no navegador. O documento cru está em `/openapi/v1.json`. Em produção nenhum dos dois é publicado.

| Método | Rota | Acesso | O que faz | Sucesso |
|---|---|---|---|---|
| `POST` | `/api/v1/users/register` | anônimo | Cadastro da conta de anfitrião, já com sessão | `201` |
| `GET` | `/api/v1/users/me` | token | Dados da conta do token | `200` |
| `POST` | `/api/v1/users/me/password` | token | Troca a senha (pede a atual); encerra as sessões e devolve uma nova | `200` |
| `GET` | `/api/v1/users/me/accounts` | token | Contas da mesma pessoa (tutor/anfitrião) | `200` |
| `POST` | `/api/v1/auth/sessions/switch` | token | Troca para a outra conta da mesma pessoa | `200` |
| `POST` | `/api/v1/auth/sessions/login` | anônimo | Login | `200` |
| `POST` | `/api/v1/auth/sessions/refresh` | anônimo | Troca o refresh token por um par novo | `200` |
| `POST` | `/api/v1/auth/sessions/logout` | anônimo | Logout: invalida o refresh token | `200` |
| `POST` | `/api/v1/auth/password/forgot` | anônimo | Esqueci a senha: manda o token por e-mail | `200` |
| `POST` | `/api/v1/auth/password/reset` | anônimo | Troca a senha com o token do e-mail | `200` |
| `GET` | `/health/live` | anônimo | O processo responde | `200` |
| `GET` | `/health/ready` | anônimo | Postgres e Redis respondem | `200` / `503` |

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
| `200` | — | Corpo = [sessão](#26-resposta-de-sessão). |
| `400` | `VALIDATION_ERROR` | Campo faltando ou `role` desconhecido. |
| `401` | `AUTH_INVALID_CREDENTIALS` | E-mail inexistente, senha errada, e-mail malformado ou papel que não bate com a conta. Sempre a mesma resposta. |
| `403` | `AUTH_ACCOUNT_DEACTIVATED` | Senha certa, mas a conta está inativa. Só aparece depois da senha conferida. Tutor reativa em `POST /api/v1/owners/reactivate`. |

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

### 2.4 Esqueci a senha — `POST /api/v1/auth/password/forgot`

```json
{ "email": "camila@exemplo.com", "role": "owner" }
```

Gera um token de uso único e manda por e-mail para a conta **(e-mail, role)**. O `role` é obrigatório pelo mesmo motivo do login.

| Status | Código | Quando |
|---|---|---|
| `200` | — | **Sempre**, exista a conta ou não — o endpoint não revela quem tem cadastro. Corpo: `{ "success": true, "data": {}, "timestamp": "..." }`. |
| `400` | `VALIDATION_ERROR` | `email` ou `role` faltando, ou `role` desconhecido. |

O e-mail traz o token (e um link, se `PasswordReset:ResetUrl` estiver configurado). O token vale `PasswordReset:TokenLifetimeMinutes` (padrão 30 min), serve uma vez só, e **pedir de novo invalida o anterior**.

### 2.5 Troca de senha — `POST /api/v1/auth/password/reset`

```json
{ "token": "q3Jx...Vb8", "newPassword": "Nova@Senha123" }
```

| Status | Código | Quando |
|---|---|---|
| `200` | — | Senha trocada. **Todas as sessões da conta são encerradas** (todos os refresh tokens deixam de valer). |
| `400` | `VALIDATION_ERROR` | `token` vazio, ou senha nova fora da regra de senha forte (seção 5.1). Vem uma mensagem por regra que falhou. O token **não** é gasto: dá para corrigir a senha e tentar de novo. |
| `401` | `AUTH_PASSWORD_RESET_TOKEN_INVALID` | Token inexistente, vencido, já usado ou substituído por um pedido mais novo. |

### 2.6 Resposta de sessão

Cadastro, login, refresh, reativação e troca de conta devolvem o mesmo formato:

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
      "phone": "44999990000",
      "birthDate": "1990-05-10",
      "address": {
        "zipCode": "87020000",
        "street": "Rua das Flores",
        "number": "120",
        "complement": "Apto 3",
        "neighborhood": "Zona 7",
        "city": "Maringá",
        "state": "PR"
      },
      "isActive": true
    }
  },
  "timestamp": "2026-10-08T12:00:00.000+00:00"
}
```

| Campo | Tipo | Significado |
|---|---|---|
| `expiresAt` | `long` | Quando o access token expira, em Unix time (segundos, UTC) — igual ao claim `exp` do JWT. Use para agendar o refresh. |
| `user.*` | — | Os dados da própria pessoa, então o endereço vem completo. Campos `null` são omitidos (`avatarUrl`, `complement`; no admin, `phone`, `birthDate` e `address`). Nunca inclui hash de senha. |

Nas próximas chamadas: `Authorization: Bearer <accessToken>`.

### 2.7 Cadastro de anfitrião — `POST /api/v1/users/register`

```json
{
  "fullName": "Camila Souza",
  "email": "camila@exemplo.com",
  "password": "Nova@Senha123",
  "phone": "(44) 99999-0000",
  "birthDate": "1990-05-10",
  "address": {
    "zipCode": "87020-000", "street": "Rua das Flores", "number": "120", "complement": "Apto 3",
    "neighborhood": "Zona 7", "city": "Maringá", "state": "PR"
  }
}
```

| Status | Código | Quando |
|---|---|---|
| `201` | — | Conta de anfitrião criada. Header `Location: /api/v1/users/me`. Corpo = [sessão](#26-resposta-de-sessão). |
| `400` | `VALIDATION_ERROR` | Qualquer campo inválido — **todos de uma vez**, com o campo aninhado no endereço (`address.zipCode`, `address.state`...). Inclui senha fraca (uma mensagem por regra) e menos de 18 anos. |
| `409` | `AUTH_EMAIL_ALREADY_REGISTERED` | Esse e-mail já tem conta de anfitrião. O mesmo e-mail como tutor é permitido. |

Não tem `role`: o papel é sempre `host` (se o cliente mandar `role`, é ignorado). O tutor se cadastra com CPF em `POST /api/v1/owners/register`.

### 2.8 Minha conta — `GET /api/v1/users/me`

`GET` devolve o objeto `user` da [sessão](#26-resposta-de-sessão). **Não há rota de edição em `/users`:** o perfil da conta muda só pelo contrato `IAccountProfileEditor`, que o módulo do papel usa — hoje, o tutor em `PATCH /api/v1/owners/me` ([`modulo-owners.md`](modulo-owners.md)). A regra do patch fica em `ProfilePatch` (Application):

- Campo ausente ou `null` = não mexer. O endereço também é parcial: cada campo dele segue a mesma regra, e o endereço resultante é validado inteiro.
- Para **limpar** a foto ou o complemento, envie texto vazio.
- **Valor igual ao atual não é alteração:** passa sem erro e não mexe no `updatedAt`.
- `birthDate` (`yyyy-MM-dd`) segue as regras do cadastro: não futura, 18+. Quando a troca é permitida é decisão de quem chama.
- E-mail, senha e papel não mudam por aqui. Todos os erros juntos.

### 2.9 Troca de senha — `POST /api/v1/users/me/password`

```json
{ "currentPassword": "Tutora@123", "newPassword": "Outra@Senha789" }
```

1. Confere a senha atual. Errada: `400 VALIDATION_ERROR` no campo `currentPassword` — **não** `401`, porque o token é válido e um `401` faria o app achar que a sessão caiu.
2. A nova precisa ser forte (mesma regra do cadastro) e diferente da atual (`400` no campo `newPassword`).
3. Grava, **encerra todas as sessões da conta** (refresh tokens e access tokens, na hora) e devolve uma [sessão](#26-resposta-de-sessão) nova, emitida depois da revogação — o app troca os tokens e segue logado; os outros aparelhos caem.
4. Manda um e-mail "sua senha foi alterada", com a orientação de usar "Esqueci minha senha" se não foi a pessoa.

Vale só para a conta do token: a outra conta da mesma pessoa tem senha própria. Conta inativa: `403 AUTH_ACCOUNT_DEACTIVATED`.

### 2.10 Inativar e reativar

**Não há rota em `/users`.** O Auth oferece inativar e reativar só pelo contrato `IAccountStatusManager`, que o módulo do papel usa para mudar conta e perfil juntos — hoje, o tutor em `POST /api/v1/owners/me/deactivate` e `POST /api/v1/owners/reactivate` ([`modulo-owners.md`](modulo-owners.md) seção 3.4). O anfitrião ainda não inativa a conta (pendência 8).

O que a inativação faz na conta, venha de onde vier:

- Vale **só para aquela conta**: a outra conta da mesma pessoa continua ativa. Derruba todas as sessões: login passa a responder `403 AUTH_ACCOUNT_DEACTIVATED`; refresh, `401`; e o access token já emitido é recusado **na hora** com `401` (seção 6.6.1). Idempotente. O admin não pode ser inativado (`AUTH_ADMIN_DEACTIVATION_FORBIDDEN`).
- A reativação pede as credenciais do login (e-mail, senha, papel) — conta inativa não consegue entrar para pedir isso logada — e já abre a sessão. Credenciais erradas: `401 AUTH_INVALID_CREDENTIALS`. Numa conta ativa, funciona como login. Conta **suspensa** não reativa: `403 AUTH_ACCOUNT_SUSPENDED`.

**Suspensão (admin).** Também só pelo contrato (`IAccountStatusManager.SuspendAsync` / `LiftSuspensionAsync`); hoje o tutor é suspenso em `POST /api/v1/owners/{ownerId}/suspension` ([`modulo-owners.md`](modulo-owners.md) seção 3.6).

- Grava `suspended_at`, `suspension_reason` (obrigatório, até 500 caracteres) e `suspended_by` (o admin) e derruba as sessões na hora.
- Recusa, sempre depois da senha conferida: login (`403 AUTH_ACCOUNT_SUSPENDED`, antes de "inativa"), refresh (`401`), troca de conta (`404`, como conta inexistente), reativação (`403`), troca de senha (`403`). "Esqueci a senha" responde `200` mas não manda e-mail.
- Só o admin tira. O status de ativa/inativa não muda com a suspensão. O admin não pode ser suspenso (`AUTH_ADMIN_SUSPENSION_FORBIDDEN`).
- O motivo e a data aparecem para o admin em `user.suspensionReason` / `user.suspendedAt` (contrato `IUserDirectory`).

### 2.11 Contas da mesma pessoa e troca — `GET /api/v1/users/me/accounts` e `POST /api/v1/auth/sessions/switch`

`GET /users/me/accounts` lista as contas com o mesmo e-mail da conta do token, para o seletor "Tutor / Anfitrião":

```json
{ "success": true, "data": [ { "role": "owner", "isActive": true, "isCurrent": true },
                             { "role": "host",  "isActive": true, "isCurrent": false } ], "timestamp": "..." }
```

`POST /sessions/switch` com `{ "role": "host", "password": "..." }` abre uma sessão na outra conta e devolve a [sessão](#26-resposta-de-sessão) dela. A sessão atual continua valendo: o front descarta os tokens antigos ou guarda os dois para alternar.

**Por que pede a senha da conta de destino:** o e-mail ainda não é verificado. Sem a senha, quem criasse uma conta de anfitrião com o e-mail de outra pessoa entraria na conta de tutor dela. Quando houver verificação de e-mail, a senha pode sair da troca (pendência 4).

| Status | Código | Quando |
|---|---|---|
| `200` | — | Sessão na outra conta. |
| `400` | `VALIDATION_ERROR` | `role` diferente de `owner`/`host` ou senha vazia. |
| `404` | `AUTH_LINKED_ACCOUNT_NOT_FOUND` | Não há conta ativa nesse papel com o mesmo e-mail, ou a senha não confere, ou o papel é o da conta atual. Mesma resposta nos três casos. |

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

Corpo ilegível — JSON malformado ou corpo vazio — responde `400` em qualquer endpoint com o campo `body`, sem repassar a mensagem do parser:

```json
{ "success": false, "error": { "code": "VALIDATION_ERROR", "message": "Validation failed",
    "details": [ { "field": "body", "messages": ["The request body is missing or is not valid JSON."] } ] },
  "timestamp": "..." }
```

Valor com **tipo errado** num JSON válido (`"phone": 123`) aponta o próprio campo: `{ "field": "phone", "messages": ["The value has an invalid type or format."] }`. O leitor de JSON para no primeiro valor assim, então só esse campo aparece. Por isso a data de nascimento é lida como **texto** (`yyyy-MM-dd`) e validada pelo FluentValidation: `"birthDate": ""` ou `"10/05/1990"` vira erro no campo `birthDate`, junto com todos os outros erros de validação.

O middleware do JWT também responde no envelope: rota protegida sem token válido → `401 UNAUTHORIZED`; autenticado sem permissão → `403 FORBIDDEN`.

---

## 4. Códigos de erro e HTTP

| Código | HTTP | Mensagem |
|---|---|---|
| `VALIDATION_ERROR` | 400 | `Validation failed` + `details` por campo |
| `AUTH_INVALID_CREDENTIALS` | 401 | Email or password is incorrect. |
| `AUTH_REFRESH_TOKEN_INVALID` | 401 | The refresh token is invalid, expired or already used. |
| `AUTH_PASSWORD_RESET_TOKEN_INVALID` | 401 | The password reset token is invalid, expired or already used. |
| `AUTH_ACCOUNT_DEACTIVATED` | 403 | This account is deactivated. Reactivate it to sign in again. |
| `AUTH_ADMIN_DEACTIVATION_FORBIDDEN` | 403 | The admin account cannot be deactivated. |
| `AUTH_ACCOUNT_SUSPENDED` | 403 | This account is suspended. Contact support. |
| `AUTH_ADMIN_SUSPENSION_FORBIDDEN` | 403 | The admin account cannot be suspended. |
| `AUTH_LINKED_ACCOUNT_NOT_FOUND` | 404 | No active account with this email was found for the requested role, or the password is incorrect. |
| `AUTH_EMAIL_ALREADY_REGISTERED` | 409 | An account with this email already exists for this role. |
| `UNAUTHORIZED` | 401 | Authentication is required to access this resource. |
| `FORBIDDEN` | 403 | You do not have permission to access this resource. |
| `AUTH_USER_NOT_FOUND` | 404 | User '{id}' was not found. |
| `AUTH_PASSWORD_HASH_INVALID` | 422 | Erro interno de integridade; não deve aparecer na prática. |
| `TOO_MANY_REQUESTS` | 429 | Too many requests. Try again in {n} seconds. (com `Retry-After`) |
| `UNEXPECTED_ERROR` | 500 | An unexpected error occurred. |

Todos declarados em `AuthErrors` (Domain). O status é decidido em `ErrorCodeStatusMapper` pelo código: primeiro os códigos transversais do §8, depois o **sufixo** do código de módulo:

| Sufixo | HTTP |
|---|---|
| `_NOT_FOUND` | 404 |
| `_CONFLICT`, `_ALREADY_EXISTS`, `_ALREADY_REGISTERED` | 409 |
| `_UNAUTHORIZED`, `_INVALID_CREDENTIALS`, `_TOKEN_INVALID`, `_TOKEN_EXPIRED` | 401 |
| `_FORBIDDEN`, `_NOT_OWNED_BY_REQUESTER`, `_DEACTIVATED`, `_SUSPENDED` | 403 |
| qualquer outro | 422 |

`AUTH_ADMIN_REGISTRATION_FORBIDDEN` (403) existe só no domínio, como barreira do `User.Register` contra criar admin. Pela API o validador barra `role: "admin"` antes, com `400`.

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
| `email`, `role` | Esqueci a senha: mesmas regras do login |
| `token` | Troca de senha: obrigatório |
| `newPassword` | Troca de senha: **senha forte** (seção 5.1) |
| cadastro | Cada campo pela regra do próprio value object (seção 5.1): `fullName`, `email`, `password` (senha forte), `phone`, `birthDate` (obrigatório, não futuro, 18+), `address` (obrigatório, cada campo validado). Todos os erros juntos. |
| perfil (contrato `IAccountProfileEditor`) | Só os campos enviados, pelas mesmas regras; o endereço parcial é completado com o atual e validado inteiro. `avatarUrl` e `address.complement` vazios limpam o campo. `birthDate` como no cadastro. Todos os erros juntos. |
| troca de senha | `currentPassword` obrigatória e certa; `newPassword` forte e diferente da atual |
| troca de conta | `role` `owner`/`host`, `password` obrigatório |

O teto de 128 na senha existe porque o custo do Argon2 cresce com o tamanho da entrada.

No domínio, além disso: `role` nunca muda depois de criada a conta (o setter é privado e não há método que a altere); `User.Register` recusa `admin` e menor de 18 anos; `Deactivate` recusa o admin.

### 5.1 Value objects

Cada atributo do usuário com regra própria é um value object: só existe se for válido, e a regra mora num lugar só. O validador da entrada usa o próprio value object em vez de repetir a regra (`PasswordRules.StrongPassword()` chama `Password.Create`).

| Value object | Coluna | Regra |
|---|---|---|
| `Email` | `email` | Obrigatório, ≤ 160, formato de e-mail. Aparado e em minúsculas. |
| `Password` | — (só em memória) | **Senha forte:** 8 a 128 caracteres, com maiúscula, minúscula, número e caractere especial. Não é aparada. Devolve **todas** as regras que falharam. `ToString()` = `***`. |
| `PasswordHash` | `password_hash` | Hash PHC já calculado, ≤ 255. Nunca recebe senha em texto. |
| `FullName` | `full_name` | Obrigatório, ≤ 120, aparado. |
| `PhoneNumber` | `phone` | Telefone com DDD, guardado **só com dígitos** (`44999990000`). Aceita entrada formatada (`(44) 99999-0000`, `+55 ...`); 10 a 13 dígitos; letra é erro. |
| `AvatarUrl` | `avatar_url` | URL absoluta `http`/`https`, ≤ 500. Recusa `javascript:`, `data:` e caminho relativo. |
| `StateCode` | `state` | Uma das 27 UFs, em maiúsculas (`pr` → `PR`). |
| `ZipCode` | `zip_code` | CEP com 8 dígitos, guardado **só com dígitos** (`87020-000` → `87020000`). Não confere se existe — o front pode usar o ViaCEP para preencher o resto. |
| `Address` | `zip_code`, `street`, `street_number`, `complement`, `neighborhood`, `city`, `state` | Endereço completo: CEP, rua (≤ 120), número (texto, ≤ 10, aceita "S/N"), complemento (opcional, ≤ 60), bairro (≤ 80), cidade (≤ 80) e UF. Devolve **todos** os campos inválidos, cada um como `address.<campo>`. Mapeado como tipo complexo do EF: colunas na própria `users`. Bate com o endereço de cobrança do Stripe. |
| `UserId` | `id` | UUID v7 gerado pelo domínio. |

Bairro, cidade, rua e número são `string` dentro do `Address`: a regra deles é obrigatoriedade e tamanho, que o próprio `Address` confere.

A senha forte vale para senha **escolhida pelo usuário** — o cadastro e a troca de senha. Não vale para o login (conferir uma senha antiga não pode depender de uma regra que talvez nem existisse quando ela foi criada) nem para a senha do admin do seed.

Os value objects viram colunas por conversores do EF (`*Converter`). Valor inválido vindo do banco estoura na leitura — é bug de integridade, não entrada de usuário.

---

## 6. Lógica

### 6.1 Login
1. Normaliza o e-mail. Se for inválido → `401` genérico.
2. Busca o usuário por **(e-mail, role)**.
3. Se não existir, **calcula um hash mesmo assim** e devolve `401`. Sem isso, "conta não existe" responderia mais rápido que "senha errada", e o tempo de resposta revelaria quem tem conta.
4. Confere a senha em tempo constante (`CryptographicOperations.FixedTimeEquals`).
5. Emite os tokens.

Todas as falhas devolvem o mesmo `AUTH_INVALID_CREDENTIALS`: de fora, não dá para saber se o e-mail existe.

### 6.1.1 Credenciais

Login e reativação conferem e-mail + senha + papel pelo mesmo helper (`UserCredentials.FindAsync`): conta inexistente gasta o tempo de um hash de verdade, e todas as falhas viram o mesmo `401`. A checagem de conta inativa vem **depois** da senha conferida — sem a senha, ninguém descobre que uma conta existe e está inativa.

### 6.2 Esqueci a senha
1. Normaliza o e-mail e busca a conta por **(e-mail, role)**. E-mail inválido ou conta inexistente → `200` sem fazer nada.
2. Emite um token opaco de 32 bytes (Base64Url) e guarda no Redis só o **SHA-256** dele, com TTL = `PasswordReset:TokenLifetimeMinutes`.
3. Monta o e-mail (texto + HTML, em português) e deixa na **caixa de saída**. O envio por SMTP roda em segundo plano (`EmailDispatcher`), então o tempo de resposta não muda conforme a conta existe ou não, e o request não fica preso a um SMTP lento ou fora do ar.

Chaves no Redis: `pethost:auth:password-reset:<hash>` → id do usuário, e `pethost:auth:password-reset:user:<id>` → hash do token vigente. Um pedido novo troca esse ponteiro e apaga o token anterior.

### 6.3 Troca de senha
1. O validador roda antes: senha fora da regra → `400`, sem tocar no token.
2. Consome o token com `GETDEL` (uso único; duas trocas simultâneas com o mesmo token não podem as duas valer). Inválido → `401`.
3. Busca o usuário; conta que não existe mais → o mesmo `401`.
4. Cria o `Password`, calcula o hash Argon2id, `user.ChangePassword(...)`, salva.
5. **Derruba todas as sessões** da conta: apaga todos os refresh tokens dela. Quem estava logado com a senha antiga — inclusive quem a roubou — precisa entrar de novo.

### 6.4 E-mail

Fica em `Shared.Infrastructure/Email`, para outros módulos reaproveitarem:

- `IEmailOutbox` → caixa de saída em memória (`Channel`, até 1.000 mensagens).
- `EmailDispatcher` (`BackgroundService`) → envia um por vez. Falha de SMTP é registrada no log e o e-mail é descartado; a pessoa pede de novo. Um e-mail ainda na fila se perde se a API reiniciar.
- `SmtpEmailSender` → MailKit (o `System.Net.Mail.SmtpClient` é marcado como não recomendado pela Microsoft). TLS negociado sozinho.
- Configuração inválida (sem `Email:Host` ou `Email:FromAddress`) **derruba a subida**.

Em desenvolvimento, o compose sobe o **Mailpit**, que recebe tudo e não entrega a ninguém: os e-mails aparecem em `http://localhost:8025`.

### 6.5 Access token (JWT)

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
| `iat_ms` | emissão em milissegundos, para a revogação (seção 6.6.1) |

Na entrada, o host valida assinatura, issuer, audience e expiração, com tolerância de 30 s de relógio. `MapInboundClaims = false` mantém os nomes curtos (`sub`, `role`) no `User` do controller.

### 6.6 Refresh token (Redis)

- Valor opaco de 32 bytes aleatórios em Base64Url — **não** é JWT.
- Chave no Redis: `pethost:auth:refresh:<SHA-256 do token>`, valor = id do usuário, TTL = `Jwt:RefreshTokenLifetimeDays`. Guarda-se o hash, não o token: um dump do Redis não serve para logar.
- **Rotação:** cada refresh consome o token com `GETDEL` (ler e apagar num comando só) e emite outro. Reusar um token falha, e duas chamadas simultâneas com o mesmo token não podem ambas ter sucesso.
- **Logout:** apaga a chave. Não existe tabela de sessão nem limpeza de tokens vencidos — o TTL cuida disso.
- **Encerrar todas as sessões:** cada usuário tem um conjunto `pethost:auth:refresh:user:<id>` com as chaves dos tokens dele. A troca de senha e a inativação apagam todas de uma vez.

### 6.6.1 Revogação do access token

O JWT não tem estado, então "revogar" é guardar no Redis o instante (Unix, em milissegundos) da revogação: `pethost:auth:access-revoked:<id>`, com TTL = vida do access token + 1 min. A cada request autenticada, o host (`OnTokenValidated` em `JwtEnvelopeEvents`) lê essa chave e recusa com `401 UNAUTHORIZED` o token emitido **antes** desse instante.

- Acontece ao **inativar a conta** e ao **trocar a senha** (logado ou pelo "esqueci a senha").
- Custo: um `GET` no Redis por request autenticada.
- O instante de emissão vem da claim `iat_ms` (milissegundos), que o PetHost põe no token além do `iat` padrão (segundos). Assim a sessão aberta logo depois da revogação — a que a troca de senha devolve, ou um login em seguida — já vale, mesmo no mesmo segundo. Token sem `iat_ms` é comparado pelo `iat`.
- O logout continua invalidando só o refresh token: o access token daquela sessão vale até expirar.

### 6.7 Senha (Argon2id)

Biblioteca `Konscious.Security.Cryptography.Argon2`. Custo padrão = mínimo do OWASP: 19 MiB de memória, 2 iterações, paralelismo 1. Salt de 16 bytes, hash de 32 bytes.

O hash é gravado no formato PHC, que carrega os próprios parâmetros:

```
$argon2id$v=19$m=19456,t=2,p=1$<salt base64>$<hash base64>
```

Por isso dá para subir o custo depois sem invalidar senhas antigas: cada hash é conferido com os parâmetros com que foi criado. Hash corrompido no banco devolve `false` (vira `401`), nunca `500`.

### 6.8 Seed do admin

O dicionário de dados diz que o admin é criado direto no banco. `AuthDbSeeder` faz isso na subida da API:

- Só roda com `Seed:Admin:Enabled = true` e com e-mail e senha preenchidos.
- **Idempotente:** se já existe admin com aquele e-mail, não faz nada.
- **Nunca atualiza a senha** de um admin existente — trocar senha por variável de ambiente seria um jeito silencioso de sequestrar a conta.
- O e-mail aparece mascarado no log (`a***@pethost.com`); a senha nunca aparece.
- A senha do admin **não** passa pela regra de senha forte: vem do ambiente, não do usuário.

O admin entra pelo login normal com `"role": "admin"`.

### 6.9 Subida da API

Ordem no `Program.cs`: Serilog (JSON) → MVC com envelope → JSON camelCase sem nulos → e-mail → módulo Auth → JWT → health checks → handler global de exceção. Depois do build:

- Em **Development**, aplica as migrations automaticamente.
- Em produção, **não** aplica (o §11 diz que migration é passo do pipeline).
- O seed roda em qualquer ambiente, se estiver habilitado.
- Configuração do JWT inválida (chave com menos de 32 bytes, issuer vazio, tempo ≤ 0), do e-mail (sem host ou remetente) ou do `PasswordReset` (validade ≤ 0, `ResetUrl` sem `{token}`) **derruba a subida** com mensagem clara, em vez de virar 500 no primeiro uso.

---

## 7. Banco

Schema `auth`, histórico de migrations em `auth.__ef_migrations_history`. Migrations: `CreateUsersTable`, `AddProfileAndStatusToUsers` (nascimento, endereço completo, `is_active`, `deactivated_at`), `AddSuspensionToUsers`.

```sql
CREATE TABLE auth.users (
    id             uuid         NOT NULL,
    full_name      varchar(120) NOT NULL,
    email          varchar(160) NOT NULL,
    password_hash  varchar(255) NOT NULL,
    role           varchar(10)  NOT NULL,
    phone          varchar(20),
    avatar_url     varchar(500),
    birth_date     date,
    zip_code       char(8),
    street         varchar(120),
    street_number  varchar(10),
    complement     varchar(60),
    neighborhood   varchar(80),
    city           varchar(80),
    state          char(2),
    is_active      boolean      NOT NULL DEFAULT true,
    deactivated_at timestamptz,
    suspended_at      timestamptz,
    suspension_reason varchar(500),
    suspended_by      uuid,
    created_at     timestamptz  NOT NULL,
    updated_at     timestamptz  NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT ck_users_role CHECK (role IN ('owner', 'host', 'admin'))
);
CREATE UNIQUE INDEX uq_users_email_role ON auth.users (email, role);
```

Bate coluna por coluna com a tabela `users` do `dicionario-de-dados.md`. O `id` é UUID v7 gerado pelo domínio (`UserId.New()`), não pelo banco: o usuário já tem id antes do insert. O índice único é composto: é ele que deixa a mesma pessoa ter conta de tutor e de anfitrião. Telefone, nascimento e endereço são nulos no banco só por causa do admin; o cadastro de tutor e anfitrião sempre preenche.

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
| `Email:Host` | `Email__Host` | `localhost` em Development | Obrigatória. No compose: `mailpit` |
| `Email:Port` | `Email__Port` | `587` (`1025` em Development) | |
| `Email:Username` / `Email:Password` | `Email__Username` / `Email__Password` | vazio | Vazio = SMTP sem autenticação. Senha nunca versionada |
| `Email:FromAddress` | `Email__FromAddress` | `no-reply@pethost.local` em Development | Obrigatória |
| `Email:FromName` | `Email__FromName` | `PetHost` | |
| `PasswordReset:TokenLifetimeMinutes` | `PasswordReset__TokenLifetimeMinutes` | `30` | Também é o TTL no Redis |
| `PasswordReset:ResetUrl` | `PasswordReset__ResetUrl` | vazio | Tela de nova senha no front, com `{token}`. Vazio = e-mail só com o código |
| `Seed:Admin:Enabled` | `Seed__Admin__Enabled` | `false` | |
| `Seed:Admin:Email` | `Seed__Admin__Email` | — | |
| `Seed:Admin:Password` | `Seed__Admin__Password` | — | Nunca versionar |
| `Seed:Admin:FullName` | `Seed__Admin__FullName` | `PetHost Admin` | |
| `RateLimiting:Enabled` | `RateLimiting__Enabled` | `true` | `false` desliga todos os limites |
| `RateLimiting:Global:PermitLimit` / `WindowSeconds` | `RateLimiting__Global__...` | `120` / `60` | Limite geral, por usuário ou IP |
| `RateLimiting:Policies:<nome>:PermitLimit` / `WindowSeconds` | `RateLimiting__Policies__<nome>__...` | ver §13 do padrão | Ex.: `RateLimiting__Policies__credentials__PermitLimit=20` |
| `ForwardedHeaders:Enabled` | `ForwardedHeaders__Enabled` | `false` (`true` no compose) | Lê o IP real do `X-Forwarded-For` atrás do ngrok |

Rodar local sem Docker para a API (Postgres e Redis no compose):

```bash
docker compose up -d postgres redis mailpit
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
| `Auth.Domain.UnitTests` | `User` (cadastro, 18+, inativar/reativar, perfil sem tocar `updatedAt` quando nada muda, troca da data de nascimento, suspensão com motivo e admin, sem suspender admin), `UserRoleValues` e todos os value objects (`Email`, `Password`, `PasswordHash`, `FullName`, `PhoneNumber`, `AvatarUrl`, `StateCode`, `ZipCode`, `Address`) | 130 ✅ |
| `Auth.Application.UnitTests` | Handlers de sessão, senha, troca de senha logada e cadastro, a regra do PATCH de perfil (`ProfilePatch`) e os validadores, incluindo senha forte, endereço aninhado e data de nascimento no PATCH; login de conta suspensa e o que vai para a trilha (com Moq e `FakeTimeProvider`) | 80 ✅ |
| `Auth.Infrastructure.UnitTests` | Argon2id (hash, verificação, PHC, entrada inválida) e geração de JWT | 32 ✅ |
| `ArchitectureTests` | Regras de dependência do §4 nos dois módulos, fronteira do §5 (Auth, Owners e Audit não se referenciam), `sealed`, sincronia `Roles` ↔ `UserRoleValues` | 27 ✅ |
| `Auth.IntegrationTests` | Corpo malformado, fluxo HTTP completo com Postgres e Redis reais em container (Testcontainers), banco limpo com Respawn, seed, rotação e concorrência do refresh, validação do JWT pelo host. Esqueci a senha e troca de senha ponta a ponta, com o SMTP trocado por um coletor que guarda os e-mails. PATCH do perfil, troca de senha logada (sessões antigas caem, a nova vale), inativar/reativar, contas da mesma pessoa, troca de conta, revogação imediata do access token, `Location` do cadastro, validação do cadastro com todos os campos. Rate limit numa instância com limites baixos (`429` no envelope com `Retry-After`, política própria por endpoint, limite geral por usuário, health check de fora). Data de nascimento no PATCH do anfitrião. Inativar/reativar pelo contrato `IAccountStatusManager` | 67 ✅ |
| `Owners.*` | Ver [`modulo-owners.md`](modulo-owners.md) | 107 ✅ |
| `Audit.*` | Ver [`modulo-audit.md`](modulo-audit.md) | 8 ✅ |

**496 testes passando** nos 13 projetos, build com 0 avisos. Nomes no padrão `MethodName_Should_X_When_Y` (§14).

Como rodar (os de integração precisam do Docker Desktop ligado):

```bash
dotnet test --solution PetHost.slnx
```

Cobertos pela integração, entre outros: a mesma pessoa com conta de tutora e de anfitriã (senha de uma não serve na outra), 5 refresh simultâneos com o mesmo token (só 1 vence), logout invalidando o refresh, token adulterado recusado pelo host, envelope em todo erro, `/health/ready` com Postgres e Redis, troca de senha encerrando as sessões de dois aparelhos, token de troca reusado ou substituído por pedido novo, senha fraca devolvendo todas as regras sem gastar o token, inativar a conta de tutor sem afetar a de anfitriã, troca de conta recusando a senha da conta errada.

Bugs encontrados durante o desenvolvimento:
1. A biblioteca do Argon2 estoura com senha vazia, e o login chamava `Hash("")` no caminho de "conta inexistente" → teria virado `500` em vez de `401`.
2. `AUTH_INVALID_CREDENTIALS` e `AUTH_EMAIL_ALREADY_REGISTERED` caíam em `422` em vez de `401` e `409`, porque só os sufixos `_NOT_FOUND` e `_CONFLICT` eram reconhecidos.
3. Um teste de imutabilidade da `role` estava mal escrito e passava por motivo errado.
4. JSON malformado no corpo chegava ao handler como `null` e virava `500`. Achado num teste manual pelo Insomnia; corrigido com o `MalformedRequestBodyFilter` e coberto por 7 testes de integração.

---

## 10. Decisões e desvios do padrão

| Decisão | Por quê |
|---|---|
| Refresh token no **Redis**, sem tabela nova | Escolha sua. O TTL do Redis faz a expiração sozinho. |
| Cadastro de tutor num pedido só, orquestrado pelo Owners | Decisão sua. O Owners cria a conta pelo contrato `IAccountRegistrar` (mesmo validador e handler do `POST /users/register`) e a desfaz se o tutor não puder ser gravado (compensação). |
| Endereço como tipo complexo em `users` | Decisão sua: um endereço por conta, sem tabela nem join. Mesmo formato do endereço do cantinho e do Stripe. |
| Sem registro de aceite de termos | Decisão sua. Se precisar guardar a versão aceita, vira uma tabela própria. |
| Inativar é por conta, não por pessoa | Pedido seu: tutor e anfitrião da mesma pessoa são contas separadas. |
| Troca de conta pede a senha da conta de destino | O e-mail não é verificado; mesmo e-mail não prova que é a mesma pessoa. |
| Cadastro em `POST /users/register` (escolha sua) | Mesmo estilo de `/sessions/login`. |
| `/users/register` só para anfitrião; sem editar perfil nem inativar/reativar em `/users` | Pedido seu: o tutor nasce, muda de perfil e de status e volta pelo Owners, junto com o perfil de tutor. As rotas `PATCH /users/me`, `POST /users/me/deactivate` e `POST /users/reactivate` foram removidas; a regra continua no Auth (`ProfilePatch`, `AccountProfileEditor`, `AccountStatusManager`), usada pelos contratos `IAccountProfileEditor` e `IAccountStatusManager`. |
| `PATCH` em vez de `PUT` no perfil | Pedido seu: a pessoa manda só o que quer trocar. Texto vazio limpa os opcionais, porque `null` não distingue "não mexer" de "apagar". Valor igual ao atual não é alteração. |
| Data de nascimento editável, com trava decidida por quem chama | Pedido seu: ela serve para validar documentos. O Auth só aplica as regras do cadastro (`User.ChangeBirthDate`); o tutor troca pelo Owners, que pede a senha e trava no primeiro pagamento. |
| Troca de senha logada pede a senha atual e devolve sessão nova | Dado sensível exige reautenticação; todas as sessões caem (se a senha vazou, quem a usou perde o acesso), mas quem trocou segue logado. Senha errada é `400` no campo, não `401`. |
| Suspensão separada da inativação | Pedido seu: o admin tira a conta do ar sem que a pessoa a reative com a senha. O Auth guarda o motivo e quem suspendeu. |
| Auditoria pelo contrato `IAuditTrail` | Pedido seu. Os handlers registram depois de gravar; a trilha nunca derruba o request (ver `modulo-audit.md`). |
| Claim `iat_ms` no access token | O `iat` é em segundos; sem a claim, a sessão nova devolvida pela troca de senha cairia junto com as antigas quando emitida no mesmo segundo. |
| `Location` do `201` aponta para `/me` | A conta e o perfil de tutor são lidos pelo token em `/users/me` e `/owners/me`; não há rota pública por id para o próprio usuário. Regra do §13. |
| Access token revogável por usuário | Pedido seu: inativar e trocar a senha cortam o acesso na hora, sem esperar o token expirar. |
| Contratos `IUserDirectory`, `IAccountRegistrar`, `IAccountProfileEditor` e `IAccountStatusManager` em `Shared.Contracts` | Portas do §5: o Owners lê a conta para as respostas, cria/desfaz a conta no cadastro, altera o perfil (com compensação) e inativa/reativa a conta junto com o perfil de tutor — sem referenciar o Auth nem fazer JOIN entre schemas. |
| Ids `uuid` v7 em vez de `int` identity | Decisão sua, vale para todas as tabelas. Gerado no domínio (`UserId.New()`), ordenado por tempo para não fragmentar o índice. A migration inicial `CreateUsersTable` foi recriada já com `uuid`. |
| "Esqueci a senha" responde `200` sempre | Não vira oráculo de quem tem cadastro. Pelo mesmo motivo o e-mail sai por fila em segundo plano: o tempo de resposta não muda com o envio. |
| Token de troca no Redis, opaco, só o hash guardado | Mesmo molde do refresh token; o TTL faz a expiração. Uso único e um pedido novo invalida o anterior. |
| `AUTH_PASSWORD_RESET_TOKEN_INVALID` responde `401` | Segue o sufixo `_TOKEN_INVALID` do `ErrorCodeStatusMapper`, igual ao refresh token. |
| Troca de senha encerra todas as sessões | Se a senha vazou, quem a usou perde o acesso na hora. |
| Senha forte só em senha nova | O login confere senhas antigas sem a regra; o admin do seed fica de fora por decisão sua. |
| E-mail com MailKit + Mailpit no compose | O `SmtpClient` do .NET é marcado como não recomendado. O Mailpit captura os e-mails em dev sem risco de mandar para gente de verdade. |
| Rotas `POST /password/forgot` e `POST /password/reset` | Mesmo estilo de `/sessions/login` e `/sessions/logout`, pedido seu. |
| Login exige `role` | O `escopo-mvp.md` define e-mail único **por papel**. |
| `SessionResponse` único para cadastro, login, refresh, reativação e troca | O §9 sugere um `...Response` por caso de uso; aqui o formato é idêntico e o cliente trata os três igual. |
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
| 4 | E-mail não é verificado. | É o que obriga a troca de conta a pedir senha. A verificação seria: token por e-mail no cadastro (mesmo molde do "esqueci a senha"), `email_verified_at` em `users`, e só contas verificadas trocariam sem senha. |
| 5 | Logout não revoga o access token da sessão. | Ele vale até expirar (15 min). Dá para revogar por `jti` se for preciso. |
| 2 | Rate limit só por IP/usuário, em memória. | Feito: limite geral e políticas por endpoint crítico (`PROJECT_STANDARDS` §13). Falta: limite por e-mail no login e no "esqueci a senha" (ataque distribuído em muitos IPs contra uma conta) e contadores no Redis quando houver mais de uma instância. |
| 3 | E-mail na fila se perde se a API reiniciar. | Aceitável para troca de senha (a pessoa pede de novo). Para e-mails que não podem se perder, trocar a fila em memória por uma persistente. |
| 6 | Seed dos tipos de pet (Cachorro, Gato, Pequenos animais). | É do módulo Pets, fora do escopo do Auth. |
| 8 | Anfitrião não inativa nem reativa a conta. | Quando houver o módulo do anfitrião, expor as rotas lá (como o tutor), pelo mesmo contrato `IAccountStatusManager`. |
| 7 | Data de nascimento do anfitrião sem trava. | Quando houver o Stripe Connect do anfitrião, travar a troca depois da verificação de identidade (mesma ideia do primeiro pagamento do tutor) e pedir a senha. |
