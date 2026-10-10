# PetHost — Módulo Owners (tutores)

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md) e o [`modulo-auth.md`](modulo-auth.md)_

O tutor inteiro: o perfil de tutor (o que só o tutor tem — hoje, o CPF, o status e o vínculo com o Customer do Stripe) **e** a conta dele, que vive no módulo Auth (nome, e-mail, telefone, nascimento, endereço). O Owners trata os dois juntos: cadastra, edita, inativa e reativa conta e perfil num pedido só, falando com o Auth pelos contratos de `Shared.Contracts`.

## Sumário

1. [Cadastro](#1-cadastro)
2. [Estrutura](#2-estrutura)
3. [Endpoints](#3-endpoints)
4. [Regras](#4-regras)
5. [Banco](#5-banco)
6. [Comunicação com o Auth](#6-comunicação-com-o-auth)
7. [Stripe](#7-stripe)
8. [Testes](#8-testes)
9. [Decisões e pendências](#9-decisões-e-pendências)

---

## 1. Cadastro

**Um pedido só:** `POST /api/v1/owners/register` recebe os dados da conta e o CPF, cria os dois e já devolve a sessão. É o **único** jeito de criar conta de tutor: `POST /api/v1/users/register` (Auth) cria só anfitrião.

Por dentro, conta e tutor ficam em módulos diferentes, então não há uma transação única:

1. O validador confere **tudo de uma vez** — o CPF aqui e a conta pelo contrato do Auth — e devolve todos os erros num só `400`.
2. O handler confere se o CPF está livre (`409`), pede ao Auth para criar a conta (`409` se o e-mail já é de outro tutor) e grava o tutor apontando para ela.
3. Se gravar o tutor falhar, a conta recém-criada é **apagada** (compensação). Como tudo foi validado antes, isso só acontece em corrida rara ou queda do banco.

O cadastro do primeiro pet é um passo à parte (módulo Pets, [`modulo-pets.md`](modulo-pets.md)), obrigatório só para pedir reserva. O pet guarda o **id do tutor** (`owners.id`), não o da conta.

## 2. Estrutura

```
src/Modules/Owners/
├── Domain/          Owner, OwnerId, Cpf, OwnersErrors, IOwnerRepository, IOwnersUnitOfWork
├── Application/     RegisterOwnerAccount, UpdateMyOwner, DeactivateMyOwner, ReactivateOwner,
│                    SuspendOwner (suspender, tirar a suspensão, liberar CPF — admin),
│                    GetMyOwner, GetOwnerById, ListOwners + porta IOwnerAccounts
├── Infrastructure/  OwnersDbContext (schema owner), migrations, repositório, adaptador OwnerAccounts
└── Presentation/    OwnersController
tests/Modules/Owners/  Domain.UnitTests, Application.UnitTests, IntegrationTests
```

Mesmas regras de camada do §4. `PetHost.ArchitectureTests` garante que Owners e Auth **não se referenciam**.

## 3. Endpoints

| Método | Rota | Acesso | O que faz | Sucesso |
|---|---|---|---|---|
| `POST` | `/api/v1/owners/register` | anônimo | Cadastro de tutor: conta + CPF, já com sessão | `201` |
| `GET` | `/api/v1/owners/me` | token de tutor | Tutor da conta logada | `200` |
| `PATCH` | `/api/v1/owners/me` | token de tutor | Altera conta, nascimento e/ou CPF — só o que vier | `200` |
| `PUT` | `/api/v1/owners/me/avatar` | token de tutor | Troca a foto de perfil (upload `multipart/form-data`, parte `file`) | `200` |
| `DELETE` | `/api/v1/owners/me/avatar` | token de tutor | Tira a foto de perfil | `200` |
| `POST` | `/api/v1/owners/me/deactivate` | token de tutor | Inativa perfil e conta, juntos | `200` |
| `POST` | `/api/v1/owners/reactivate` | anônimo | Reativa conta e perfil com e-mail e senha; já com sessão | `200` |
| `GET` | `/api/v1/owners/{ownerId}` | admin | Um tutor, pelo **id do tutor** | `200` |
| `GET` | `/api/v1/owners` | admin | Todos os tutores | `200` |
| `POST` | `/api/v1/owners/{ownerId}/suspension` | admin | Suspende perfil e conta, com motivo | `200` |
| `DELETE` | `/api/v1/owners/{ownerId}/suspension` | admin | Tira a suspensão | `200` |
| `POST` | `/api/v1/owners/{ownerId}/cpf-release` | admin | Libera o CPF de um tutor suspenso (disputa), com motivo | `200` |

Toda ação relevante vai para a trilha de auditoria ([`modulo-audit.md`](modulo-audit.md)): cadastro, troca de CPF/nascimento, inativar/reativar, as ações do admin com o motivo, e até a leitura do admin (`owner.viewed`, `owner.listed`), porque ele vê CPF completo de outra pessoa.

Rate limit (`PROJECT_STANDARDS` §13): `register` usa a política `registration`; `reactivate`, `credentials`; `me/deactivate`, `account-sensitive`; `PATCH me`, `account-update` (20 a cada 15 min por tutor, o que também limita tentativas de senha na troca de CPF). Estourou: `429 TOO_MANY_REQUESTS`.

A senha do tutor muda em `POST /api/v1/users/me/password` (Auth, [`modulo-auth.md`](modulo-auth.md) seção 2.9), igual para qualquer conta.

### 3.1 Formato do tutor

O mesmo em todas as rotas de tutor, inclusive as do admin:

```json
{
  "id": "0199d1f0-...",
  "userId": "01a11dc9-...",
  "cpf": "52998224725",
  "isActive": true,
  "suspendedAt": null,
  "createdAt": "2026-10-08T12:00:00+00:00",
  "user": {
    "id": "01a11dc9-...", "fullName": "Camila Souza", "email": "camila@exemplo.com", "role": "owner",
    "phone": "44999990000", "birthDate": "1990-05-10",
    "address": { "zipCode": "87020000", "street": "Rua das Flores", "number": "120", "complement": "Apto 3",
                 "neighborhood": "Zona 7", "city": "Maringá", "state": "PR" },
    "isActive": true, "createdAt": "2026-10-08T12:00:00+00:00"
  }
}
```

- `id` é o **id do tutor**; `userId` é o da conta (igual a `user.id`).
- `cpf` vem completo, só com dígitos. Some da resposta se o admin liberou o CPF.
- `isActive` é o status do perfil de tutor; anda junto com `user.isActive`.
- `suspendedAt` aparece quando o admin suspendeu; o motivo vem em `user.suspensionReason` (e `user.suspendedAt`).
- `user` são os dados da conta, lidos do Auth (seção 6). Campo nulo é omitido da resposta.

### 3.2 `POST /api/v1/owners/register`

```json
{
  "fullName": "Camila Souza",
  "email": "camila@exemplo.com",
  "password": "Nova@Senha123",
  "phone": "(44) 99999-0000",
  "birthDate": "1990-05-10",
  "cpf": "529.982.247-25",
  "address": {
    "zipCode": "87020-000", "street": "Rua das Flores", "number": "120", "complement": "Apto 3",
    "neighborhood": "Zona 7", "city": "Maringá", "state": "PR"
  }
}
```

Sem `role`: é sempre `owner`.

```json
{ "success": true, "data": { "accessToken": "...", "refreshToken": "...", "expiresAt": 1791461700, "owner": { "...": "formato da 3.1" } }, "timestamp": "..." }
```

| Status | Código | Quando |
|---|---|---|
| `201` | — | Conta e tutor criados. Header `Location: /api/v1/owners/me`. |
| `400` | `VALIDATION_ERROR` | Qualquer campo inválido — **todos de uma vez**, da conta e o CPF, com o endereço aninhado (`address.zipCode`...). |
| `409` | `AUTH_EMAIL_ALREADY_REGISTERED` | Esse e-mail já tem conta de tutor. Nada é criado. |
| `409` | `OWNER_CPF_ALREADY_REGISTERED` | O CPF já é de outro tutor. Nada é criado. |

### 3.3 `GET` e `PATCH /api/v1/owners/me`

`GET` devolve o formato da 3.1. `404 OWNER_NOT_FOUND` só para conta de tutor antiga, de antes do cadastro unificado, que nunca teve CPF.

`PATCH` muda conta, nascimento e CPF num pedido só, e **só o que vier**:

```json
{ "phone": "(44) 3222-1111", "address": { "number": "300" }, "birthDate": "1991-02-03", "cpf": "111.444.777-35", "currentPassword": "Tutora@123" }
```

- `fullName`, `phone`, `address`: regra do Auth (`ProfilePatch`, contrato `IAccountProfileEditor`) — ausente ou `null` não mexe; o endereço pode vir pela metade; texto vazio limpa o complemento.
- **A foto não muda por aqui** (`avatarUrl` no corpo é ignorado): ela tem rota própria, a 3.4.
- `cpf` e `birthDate` identificam o pagador (Stripe, verificação de documentos): trocar qualquer um pede **`currentPassword`** e só vale **até o primeiro pagamento**. A data segue as regras do cadastro (`yyyy-MM-dd`, não futura, 18+).
- **Valor igual ao atual não é troca:** mandar o mesmo CPF, a mesma data ou o mesmo nome passa sem erro, sem pedir senha e sem esbarrar na trava — mesmo depois do pagamento. O front pode mandar o formulário inteiro.
- E-mail e senha não mudam por aqui. Não existe `PATCH /users/me`: esta é a única rota que altera o perfil do tutor.

| Status | Código | Quando |
|---|---|---|
| `200` | — | Alterado. Corpo no formato da 3.1, já com os dados novos. |
| `400` | `VALIDATION_ERROR` | Campos da conta ou CPF inválidos — **todos de uma vez**. Também `currentPassword` ausente ou errada quando o CPF muda (campo `currentPassword`). |
| `404` | `OWNER_NOT_FOUND` | A conta não tem perfil de tutor. |
| `409` | `OWNER_CPF_ALREADY_REGISTERED` | O CPF novo é de outro tutor. |
| `422` | `OWNER_CPF_LOCKED` | Já houve pagamento: o CPF está travado. |
| `422` | `OWNER_BIRTH_DATE_LOCKED` | Já houve pagamento: a data de nascimento está travada. |

A ordem das recusas é: travado → senha → CPF de outro tutor. Assim quem não sabe a senha não descobre se um CPF está cadastrado. Nenhuma recusa grava nada.

Por dentro: tudo é conferido antes; a conta é gravada no Auth; o CPF é gravado aqui. Se gravar o CPF falhar, o perfil anterior da conta é aplicado de volta (compensação).

### 3.4 `PUT` e `DELETE /api/v1/owners/me/avatar`

Foto de perfil do tutor. O `PUT` recebe a imagem em `multipart/form-data`, na parte `file` (JPG/JPEG, PNG ou WebP, até 5 MB). A API envia a imagem ao bucket, grava a URL pública na conta (`user.avatarUrl`, no Auth, pelo `IAccountProfileEditor`) e apaga a foto anterior. O `DELETE` tira a foto e apaga a imagem; é idempotente. Os dois devolvem o tutor no formato da 3.1. A trilha fica com o Auth (`account.profile_updated`).

| Status | Código | Quando |
|---|---|---|
| `200` | — | Foto trocada ou removida. |
| `400` | `VALIDATION_ERROR` | Campo `file`: arquivo faltando, vazio, acima de 5 MB ou que não é JPEG/PNG/WebP (conferido pelo conteúdo). |
| `404` | `OWNER_NOT_FOUND` | Conta de tutor sem perfil. Nada é enviado ao bucket. |
| `413` | `PAYLOAD_TOO_LARGE` | Corpo acima de 8 MB. |

Regras do arquivo, R2 e desenvolvimento local: [armazenamento-de-imagens.md](armazenamento-de-imagens.md).

### 3.5 Inativar e reativar

- `POST /api/v1/owners/me/deactivate` (token de tutor, sem corpo): inativa o **perfil de tutor e a conta**. Todas as sessões caem na hora (o access token já emitido passa a dar `401`); login responde `403 AUTH_ACCOUNT_DEACTIVATED`. A conta de anfitrião da mesma pessoa não é afetada. Idempotente.
- `POST /api/v1/owners/reactivate` (anônimo): `{ "email", "password" }` da conta de tutor. Reativa conta e perfil e devolve a sessão no formato da 3.2 (`200`). Credenciais erradas: `401 AUTH_INVALID_CREDENTIALS`, e nada muda. Num tutor ativo, funciona como login.

Por dentro, os dois status nunca ficam diferentes: ao inativar, o perfil é gravado primeiro e volta a ativo se a conta não puder ser inativada; ao reativar, a conta vai primeiro (é ela que confere a senha) e é inativada de novo se o perfil não puder ser gravado.

Não existe inativar/reativar em `/users`: estas são as únicas rotas, e mudam conta e perfil juntos.

### 3.6 Admin: `GET /api/v1/owners` e `GET /api/v1/owners/{ownerId}`

Formato da 3.1 (a lista é um array dele). `{ownerId}` é o **id do tutor**, não o da conta. A lista vem do mais novo para o mais antigo, sem paginação nem filtro (lista administrativa, §13), e busca as contas no Auth numa consulta só. As duas leituras ficam na trilha.

### 3.7 Admin: suspender, tirar a suspensão, liberar o CPF

**Suspender ≠ inativar.** Inativar é decisão do tutor, e ele volta com a senha. Suspender é decisão do admin, e só o admin tira:

| | Inativado (pelo tutor) | Suspenso (pelo admin) |
|---|---|---|
| Login | `403 AUTH_ACCOUNT_DEACTIVATED` | `403 AUTH_ACCOUNT_SUSPENDED` |
| `POST /owners/reactivate` com a senha | Volta | **Não volta** (`403 AUTH_ACCOUNT_SUSPENDED`) |
| "Esqueci a senha" | Funciona | Responde `200`, mas não manda e-mail |
| Sessões | Caem na hora | Caem na hora |

- `POST /api/v1/owners/{ownerId}/suspension` com `{ "reason": "..." }` (obrigatório, até 500 caracteres): suspende perfil e conta. Idempotente — suspender de novo mantém a primeira. Resposta: formato da 3.1.
- `DELETE /api/v1/owners/{ownerId}/suspension`: tira a suspensão; o tutor volta ao status que tinha (ativo ou inativo) e entra de novo. Tutor cujo CPF foi liberado **não** volta: `422 OWNER_CPF_RELEASED`.
- `POST /api/v1/owners/{ownerId}/cpf-release` com `{ "reason": "..." }`: tira o CPF do tutor, para o dono de verdade poder se cadastrar. Só de tutor suspenso (`422 OWNER_SUSPENSION_REQUIRED`). A trilha guarda o CPF mascarado.

**Disputa de CPF, passo a passo:** a pessoa recebe `409 OWNER_CPF_ALREADY_REGISTERED` e procura o suporte → o admin confere o documento → suspende o tutor indevido → libera o CPF → a pessoa se cadastra normalmente. Cada passo fica na trilha com o motivo e quem fez.

Por dentro, perfil e conta mudam juntos, com compensação: o perfil é gravado primeiro e volta ao estado anterior se a conta não puder mudar. O admin não pode ser suspenso (`403 AUTH_ADMIN_SUSPENSION_FORBIDDEN`).

## 4. Regras

- **CPF:** só dígitos no banco; aceita máscara na entrada; confere os dois dígitos verificadores (módulo 11); recusa todos os dígitos iguais. Nas respostas vem completo; só o `ToString()` mascara (`***.***.247-25`), para não vazar em log (LGPD).
- **Um CPF por conta de tutor** e **um tutor por conta** (índices únicos em `cpf` e `user_id`).
- **CPF e data de nascimento editáveis até o primeiro pagamento, com a senha:** o marco é o `stripe_customer_id` (`Owner.HasPaid`), criado no primeiro pagamento. Depois dele, `OWNER_CPF_LOCKED` / `OWNER_BIRTH_DATE_LOCKED`. Antes, a troca pede `currentPassword`. A data fica na conta (Auth); quem aplica a trava é o caso de uso do Owners.
- **Status próprio, junto com a conta:** `Owner.Deactivate` / `Owner.Reactivate`, idempotentes. O caso de uso muda perfil e conta juntos.
- **Suspensão (admin):** `Owner.Suspend` / `Owner.LiftSuspension`, junto com a suspensão da conta. `Owner.ReleaseCpf` só em tutor suspenso; depois dele a suspensão não sai mais. CPF nulo só em tutor suspenso (constraint no banco).
- **Id próprio:** o tutor tem `id` (UUID v7 gerado pelo domínio); a conta entra só como referência (`user_id`).

| Código | HTTP | Mensagem |
|---|---|---|
| `VALIDATION_ERROR` (`currentPassword`) | 400 | Current password is required to change the CPF or the birth date. / Current password is incorrect. |
| `OWNER_CPF_ALREADY_REGISTERED` | 409 | An owner account with this CPF already exists. If this CPF is yours, contact support. |
| `OWNER_CPF_LOCKED` | 422 | The CPF cannot be changed after the first payment. |
| `OWNER_BIRTH_DATE_LOCKED` | 422 | The birth date cannot be changed after the first payment. |
| `VALIDATION_ERROR` (`reason`) | 400 | A reason is required. / Reason must be at most 500 characters. |
| `OWNER_SUSPENSION_REQUIRED` | 422 | The CPF can only be released from a suspended owner. |
| `OWNER_CPF_RELEASED` | 422 | This owner's CPF was released, so the suspension cannot be lifted. |
| `OWNER_NOT_FOUND` | 404 | Owner '{id}' was not found. / This account has no owner profile yet. |

## 5. Banco

Schema `owner`, histórico em `owner.__ef_migrations_history`. Migrations: `CreateOwnersTable`, `AddOwnIdToOwners`, `AddStatusToOwners`, `AddSuspensionToOwners`.

```sql
CREATE TABLE owner.owners (
    id                 uuid         NOT NULL,
    user_id            uuid         NOT NULL,
    cpf                char(11),
    stripe_customer_id varchar(255),
    is_active          boolean      NOT NULL DEFAULT true,
    deactivated_at     timestamptz,
    suspended_at       timestamptz,
    created_at         timestamptz  NOT NULL,
    updated_at         timestamptz  NOT NULL,
    CONSTRAINT pk_owners PRIMARY KEY (id),
    CONSTRAINT ck_owners_cpf_only_null_when_suspended CHECK (cpf IS NOT NULL OR suspended_at IS NOT NULL)
);
CREATE UNIQUE INDEX uq_owners_user_id ON owner.owners (user_id);
CREATE UNIQUE INDEX uq_owners_cpf ON owner.owners (cpf);
CREATE UNIQUE INDEX uq_owners_stripe_customer_id ON owner.owners (stripe_customer_id);
```

**`user_id` é FK lógica para `auth.users.id`, sem constraint física:** cada módulo tem o seu schema, e o §5/§11 proíbe restrição e JOIN entre schemas. A ligação é garantida pela aplicação — o `user_id` sai sempre da conta criada no cadastro ou do token.

```bash
dotnet dotnet-ef migrations add <Nome> --project src/Modules/Owners/PetHost.Modules.Owners.Infrastructure --startup-project src/Host/PetHost.Api --context OwnersDbContext --output-dir Persistence/Migrations
```

Em Development a API aplica as migrations dos dois módulos na subida.

## 6. Comunicação com o Auth

Pelo §5 (porta + adaptador), sem referência entre os módulos:

| Owners pede | Porta (`IOwnerAccounts`) | Contrato (`Shared.Contracts`) | Implementação (Auth.Infrastructure) |
|---|---|---|---|
| Dados da conta para a resposta | `GetByIdsAsync` | `IUserDirectory` | `UserDirectory` |
| Validar / criar a conta no cadastro | `ValidateAsync` / `CreateAsync` | `IAccountRegistrar` | `AccountRegistrar` |
| Desfazer a conta (compensação do cadastro) | `DeleteAsync` | `IAccountRegistrar.DeleteAsync` | `AccountRegistrar` |
| Validar / aplicar o patch da conta | `ValidateProfileAsync` / `UpdateProfileAsync` | `IAccountProfileEditor` | `AccountProfileEditor` |
| Conferir a senha (troca de CPF) | `VerifyPasswordAsync` | `IAccountProfileEditor.VerifyPasswordAsync` | `AccountProfileEditor` |
| Inativar / reativar a conta | `DeactivateAsync` / `ReactivateAsync` | `IAccountStatusManager` | `AccountStatusManager` |
| Suspender / tirar a suspensão (admin) | `SuspendAsync` / `LiftSuspensionAsync` | `IAccountStatusManager` | `AccountStatusManager` |

O Owners também **oferece** um contrato de leitura, para quem guarda o id do tutor:

| Outro módulo pede | Contrato (`Shared.Contracts`) | Implementação (Owners.Infrastructure) |
|---|---|---|
| O tutor da conta logada / tutores pelos ids (sem CPF) | `IOwnerDirectory` | `OwnerDirectory` |

Hoje quem usa é o Pets, para ligar o pet ao tutor da conta logada e mostrar o dono.

A trilha de auditoria é outro contrato (`IAuditTrail`, módulo Audit), usado direto pelos handlers do Owners.

O Auth usa por dentro o mesmo validador e handler do `/users/register`, a regra `ProfilePatch` para o perfil e o agregado `User` (`Deactivate`/`Reactivate`) para o status, então as regras não se duplicam. Nenhum desses casos tem rota própria em `/users`. `UpdateProfileAsync` devolve o perfil como era antes, pronto para ser aplicado de volta na compensação.

## 7. Stripe

O tutor é o pagador. No primeiro pagamento, o módulo de pagamentos cria o **Customer** com:

| Stripe | Vem de |
|---|---|
| `name`, `email` | `users.full_name`, `users.email` |
| `phone` | `users.phone` em E.164 (`+5544999990000`) |
| `address.line1` / `line2` | `street` + `street_number` / `complement` |
| `address.postal_code`, `city`, `state` | `zip_code`, `city`, `state` |
| `address.country` | sempre `BR` |
| tax ID `br_cpf` | `owners.cpf` |

O `cus_...` volta para `owners.stripe_customer_id` (`Owner.LinkStripeCustomer`) e trava o CPF.

## 8. Testes

| Projeto | O que cobre | Resultado |
|---|---|---|
| `Owners.Domain.UnitTests` | `Cpf` (válidos, inválidos, máscara no `ToString`), `Owner` (id próprio, referência à conta, troca do CPF antes e depois do primeiro pagamento, inativar/reativar idempotentes, suspender, liberar CPF só de suspenso, suspensão que não sai depois do CPF liberado) | 28 ✅ |
| `Owners.Application.UnitTests` | Cadastro unificado (sucesso, CPF de outro, e-mail de outro, **compensação**), PATCH (só conta, só CPF, data de nascimento, senha ausente/errada, travado, CPF de outro, valor igual sem senha nem trava, **compensação**), validadores que juntam erros do CPF e da conta, inativar/reativar (com **compensação** nos dois sentidos), suspender/tirar/liberar CPF (com **compensação**, motivo e CPF mascarado na trilha), consultas do admin, trocar/tirar a foto (só a URL vai no patch, foto antiga apagada, imagem nova apagada se a conta recusar, nada enviado sem perfil de tutor) | 44 ✅ |
| `Owners.IntegrationTests` | Os fluxos pela API com Postgres e Redis em container: cadastro de anfitrião em `/users/register` (papel sempre `host`), cadastro unificado de tutor, PATCH parcial, com CPF e com data de nascimento (senha, trava no pagamento, reenvio do mesmo valor), `400` da data pelo `/users/me` para tutor, recusas sem gravar nada, inativar/reativar (sessões caem, login `403`, sessão nova vale), `/me` e rotas de admin. `OwnerAdminTests`: suspensão (sessões caem, login e reativação `403`), tirar a suspensão, disputa de CPF ponta a ponta, `422` das regras, `400` sem motivo, `403` para não admin, e a trilha (quem suspendeu e por quê, login recusado, leitura do admin, CPF mascarado). `OwnerAvatarTests`: foto no bucket e link público, troca apaga a anterior, `400` sem arquivo ou com arquivo que não é imagem, remoção idempotente, `avatarUrl` do PATCH ignorado | 45 ✅ |

## 9. Decisões e pendências

| Decisão | Por quê |
|---|---|
| Módulo próprio, schema `owner` | Decisão sua: o Auth fica com identidade e conta; pagamentos vão falar com o Owners. |
| Tutor tratado inteiro pelo Owners (cadastro, PATCH, status) | Decisão sua. Custa a compensação entre módulos (seções 1, 3.3 e 3.5), coberta por teste. `/users/register` não cria conta de tutor, e não há inativar/reativar em `/users`. |
| `PATCH` em vez de `PUT` | Pedido seu: o tutor manda só o que quer trocar. |
| Troca de CPF e de data de nascimento pede a senha atual | Dados sensíveis (Stripe, verificação de documentos): exigem reautenticação. Senha errada é `400` no campo, não `401`. |
| Data de nascimento travada no primeiro pagamento | Pedido seu: ela é usada para validar documentos, como o CPF. |
| Valor igual ao atual não é troca | O front pode mandar o formulário inteiro sem pedir senha nem esbarrar na trava. |
| Perfil de tutor com status próprio | Pedido seu: inativar/reativar o tutor muda perfil e conta juntos. |
| Tutor com id próprio; `user_id` só como referência | Decisão sua. Um tutor por conta continua garantido pelo índice único. |
| `user_id` sem FK física | §5/§11: sem constraint entre schemas de módulos. Se quiser a constraint, abrir exceção no padrão. |
| CPF editável só até o primeiro pagamento | Decisão sua. Corrige erro de digitação sem quebrar o vínculo com o Stripe. |
| CPF completo e dados da conta em toda resposta | Decisão sua: o front tem tudo do tutor numa chamada só. Em log o CPF continua mascarado. |
| Lista do admin sem paginação | Permitido pelo §13 para listas administrativas pequenas. |
| Suspensão separada da inativação | Pedido seu: o admin precisa tirar uma conta do ar sem que a pessoa a reative com a senha. |
| Liberar o CPF só de tutor suspenso, sem volta | Na disputa, o CPF vai para o dono de verdade; a conta indevida não pode voltar ao ar sem CPF. |
| Toda ação do admin exige motivo | Vai para a trilha: quem fez, por quê, quando. |

| # | Pendência | Impacto |
|---|---|---|
| 1 | Troca de e-mail. | Precisa da verificação de e-mail: código para o e-mail novo, troca só depois de confirmado, aviso para o antigo. |
| 2 | Anfitrião sem perfil próprio. | O cantinho e o Stripe Connect farão esse papel no módulo do anfitrião. |
| 3 | CPF único só entre tutores. | Alguém com outro e-mail pode usar o CPF de um anfitrião (ou de quem ainda não tem conta) para criar um tutor. Regra decidida (escopo, restrição 9): o mesmo CPF só pode estar num tutor e num anfitrião do **mesmo e-mail**. Entra junto com o CPF do anfitrião: o módulo do anfitrião guarda o CPF, e os dois lados conferem um ao outro por um contrato em `Shared.Contracts` antes de gravar. |
| 4 | Titularidade do CPF do tutor não é verificada. | O dígito confere, mas não prova que o CPF é de quem digitou — e quem cadastra primeiro fica com ele. Opções (decisão de custo): Serpro Datavalid (CPF + nome + nascimento) no cadastro, ou Stripe Identity (documento + selfie) no primeiro pagamento. |
| 5 | Disputa de CPF sem tela. | As rotas existem (seção 3.7) e tudo fica na trilha; falta a tela no admin e o canal de suporte por onde a pessoa abre o pedido. |
| 6 | Suspender anfitrião. | Mesmo contrato (`IAccountStatusManager.SuspendAsync`); as rotas entram no módulo do anfitrião. |
