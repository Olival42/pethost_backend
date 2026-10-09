# PetHost — Módulo Audit (trilha de auditoria)

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md)_

A trilha de auditoria guarda **quem fez o quê, sobre o quê, quando e de onde**. Os outros módulos registram fatos pelo contrato `IAuditTrail` de `Shared.Contracts`, sem referenciar o Audit. O admin consulta a trilha numa rota paginada.

## Sumário

1. [Estrutura](#1-estrutura)
2. [O que é registrado](#2-o-que-é-registrado)
3. [Como registrar](#3-como-registrar)
4. [Consulta](#4-consulta)
5. [Banco](#5-banco)
6. [Testes](#6-testes)
7. [Decisões e pendências](#7-decisões-e-pendências)

---

## 1. Estrutura

```
src/Modules/Audit/
├── Domain/          AuditEntry (imutável), AuditEntryId, IAuditEntryRepository, AuditErrors
├── Application/     SearchAuditEntries (consulta do admin), AuditEntryResponse
├── Infrastructure/  AuditDbContext (schema audit), migration, repositório,
│                    AuditTrail (adaptador do contrato IAuditTrail)
└── Presentation/    AuditEntriesController
src/Shared/PetHost.Shared.Contracts/Audit/IAuditTrail.cs   contrato + AuditActions + AuditTargets
tests/Modules/Audit/  Domain.UnitTests, Application.UnitTests (os fluxos HTTP estão em Owners.IntegrationTests)
```

`PetHost.ArchitectureTests` garante que nenhum módulo referencia o Audit e que o Audit não referencia ninguém.

## 2. O que é registrado

Cada registro tem: `occurredAt`, `action`, `targetType` + `targetId`, `actorId` + `actorRole` (do token, ou informado), `reason` (ações do admin), `details` (texto curto), `ipAddress`, `traceId`.

| Ação | Alvo | Quando | Detalhes |
|---|---|---|---|
| `account.registered` | conta | Conta criada (anfitrião ou tutor) | `role` |
| `account.deleted` | conta | Conta desfeita na compensação do cadastro de tutor | `cause` |
| `account.profile_updated` | conta | PATCH de perfil que mudou algo | `fields` (nomes, nunca valores) |
| `account.deactivated` / `account.reactivated` | conta | Status da conta mudou | — |
| `account.suspended` / `account.suspension_lifted` | conta | Admin suspendeu / tirou a suspensão | motivo em `reason` |
| `session.login_succeeded` | conta | Login | `role` |
| `session.login_failed` | conta (se existir) | Login recusado | `role`, `reason` (`invalid_credentials`, `deactivated`, `suspended`) |
| `session.switched` | conta de destino | Troca entre tutor e anfitrião | `fromAccountId`, `toRole` |
| `password.reset_requested` / `password.reset_completed` | conta | "Esqueci a senha" (só conta existente) | — |
| `password.changed` | conta | Troca de senha logada | — |
| `owner.registered` | tutor | Cadastro de tutor | — |
| `owner.updated` | tutor | CPF ou nascimento trocados | `cpf` **mascarado**, `birthDateChanged` |
| `owner.deactivated` / `owner.reactivated` | tutor | Status do tutor mudou | — |
| `owner.suspended` / `owner.suspension_lifted` | tutor | Admin suspendeu / tirou | motivo em `reason` |
| `owner.cpf_released` | tutor | Admin liberou o CPF (disputa) | motivo; `cpf` **mascarado** |
| `owner.viewed` | tutor | Admin abriu um tutor (CPF completo) | — |
| `owner.listed` | — | Admin listou os tutores | `count` |
| `audit.searched` | — | Admin consultou a trilha | filtros usados |

**Nunca vai para a trilha:** senha, token, hash, CPF completo, e-mail digitado no login. Campo alterado vai pelo **nome**, não pelo valor. A leitura de dado pessoal pelo admin (`owner.viewed`, `owner.listed`) é registrada porque a LGPD pede rastreio de quem acessou.

**Fora da trilha (de propósito):** refresh e logout. São frequentes demais e não mudam nada além da sessão. O log da API já mostra cada request.

## 3. Como registrar

No handler, **depois** que a operação deu certo:

```csharp
await auditTrail.RecordAsync(
    new AuditRecord(AuditActions.OwnerSuspended, AuditTargets.Owner, owner.Id.Value, command.Reason),
    cancellationToken);
```

- `ActorId` só quando não é o usuário do token — login e cadastro ainda não têm token. Senão, o adaptador pega `sub` e `role` do request, além do IP (já resolvido pelo `X-Forwarded-For`) e do trace id.
- **Nunca derruba o request.** O adaptador grava num `AuditDbContext` próprio, criado pela factory: uma falha não contamina a transação de ninguém. Se gravar falhar, o registro inteiro vai para o log como `Error`. Todo registro também sai no log como `Information`.
- Textos longos demais são cortados (motivo: 500, detalhe: 256, até 20 detalhes) em vez de recusados: a trilha não pode perder um fato por causa do tamanho de um campo.
- Ação nova vira constante em `AuditActions` (`alvo.ação`). Renomear quebra filtros já usados.

## 4. Consulta

`GET /api/v1/audit-entries` — só admin. Do mais novo para o mais antigo, paginado (§13).

| Parâmetro | O quê |
|---|---|
| `page`, `pageSize` | Página (≥ 1) e tamanho (1 a 100, padrão 20) |
| `action` | Ex.: `owner.suspended` |
| `targetType`, `targetId` | Ex.: `owner` + id do tutor; `account` + id da conta |
| `actorId` | Tudo o que uma pessoa (ou um admin) fez |
| `from`, `to` | Período, ISO 8601 |

```json
{ "success": true, "data": {
    "items": [ { "id": "...", "occurredAt": "2026-10-09T12:00:00+00:00", "action": "owner.suspended",
                 "targetType": "owner", "targetId": "...", "actorId": "...", "actorRole": "admin",
                 "reason": "Uso de CPF de terceiro", "details": {}, "ipAddress": "203.0.113.7", "traceId": "..." } ],
    "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1 }, "timestamp": "..." }
```

`400` com todos os erros juntos (`page`, `pageSize`, `from` depois de `to`). A consulta também fica registrada (`audit.searched`).

## 5. Banco

Schema `audit`, migration `CreateAuditEntriesTable`.

```sql
CREATE TABLE audit.audit_entries (
    id           uuid         NOT NULL,
    occurred_at  timestamptz  NOT NULL,
    action       varchar(64)  NOT NULL,
    target_type  varchar(32)  NOT NULL,
    target_id    uuid,
    actor_id     uuid,
    actor_role   varchar(16),
    reason       varchar(500),
    details      jsonb        NOT NULL,
    ip_address   varchar(45),
    trace_id     varchar(64),
    CONSTRAINT pk_audit_entries PRIMARY KEY (id)
);
CREATE INDEX ix_audit_entries_target      ON audit.audit_entries (target_type, target_id, occurred_at);
CREATE INDEX ix_audit_entries_actor       ON audit.audit_entries (actor_id, occurred_at);
CREATE INDEX ix_audit_entries_action      ON audit.audit_entries (action, occurred_at);
CREATE INDEX ix_audit_entries_occurred_at ON audit.audit_entries (occurred_at);
```

Sem FK para as tabelas dos outros módulos: o registro sobrevive mesmo se a conta ou o tutor forem apagados. A aplicação só insere e consulta, nunca altera nem apaga.

## 6. Testes

| Projeto | O que cobre | Resultado |
|---|---|---|
| `Audit.Domain.UnitTests` | `AuditEntry`: campos, corte de textos longos, limite de detalhes, motivo e chave em branco, ação obrigatória | 5 ✅ |
| `Audit.Application.UnitTests` | Consulta: página com totais e filtros, a própria consulta registrada, todos os erros de paginação e período juntos | 3 ✅ |
| `Owners.IntegrationTests` (`OwnerAdminTests`) | Pela API: quem suspendeu e o motivo, login recusado com a conta, `owner.viewed` do admin, CPF sempre mascarado, `400` de `pageSize`, `403` para não admin | ver [`modulo-owners.md`](modulo-owners.md) |

## 7. Decisões e pendências

| Decisão | Por quê |
|---|---|
| Módulo próprio, schema `audit` | Pedido seu. A trilha é de todos os módulos e não pertence a nenhum; contrato em `Shared.Contracts` (§5). |
| Gravar depois da operação, em contexto próprio, sem derrubar o request | A operação já aconteceu; falhar a trilha não pode desfazê-la nem virar `500`. O log guarda o registro se o banco falhar. |
| Registrar leitura do admin (`owner.viewed`, `owner.listed`, `audit.searched`) | LGPD: rastreio de acesso a dado pessoal. |
| IP do cliente na trilha | Investigar fraude (vários logins recusados do mesmo IP). É dado pessoal: ver pendência 1. |

| # | Pendência | Impacto |
|---|---|---|
| 1 | Sem política de retenção. | Definir por quanto tempo guardar (ex.: 5 anos para ação do admin, 6 meses para login) e um job que apaga o que venceu. |
| 2 | Imutabilidade só na aplicação. | Em produção, o usuário do banco da API deve ter só `INSERT` e `SELECT` em `audit.audit_entries` (sem `UPDATE`/`DELETE`). |
| 3 | Sem tela no admin. | Hoje é só a rota; a tela de disputa de CPF pode mostrar a trilha do tutor. |
