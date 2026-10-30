# PetHost — Relatório de testes (API)

_Execução dos testes planejados na Entrega 2 (seção 6 da Documentação Técnica) · backend `pethost_backend`_

## Sumário

1. [Resumo](#1-resumo)
2. [Ambiente e como reproduzir](#2-ambiente-e-como-reproduzir)
3. [Resultado por projeto](#3-resultado-por-projeto)
4. [Cobertura de código](#4-cobertura-de-código)
5. [Casos de teste executados](#5-casos-de-teste-executados)
6. [Regras de negócio (seção 6.3)](#6-regras-de-negócio-seção-63)
7. [Validações esperadas (seção 6.4)](#7-validações-esperadas-seção-64)
8. [Falhas encontradas e correções](#8-falhas-encontradas-e-correções)
9. [Pendências](#9-pendências)
10. [Evidências](#10-evidências)
11. [Roteiro exploratório (seção 6.1)](#11-roteiro-exploratório-seção-61)

---

## 1. Resumo

| Item | Resultado |
|---|---|
| Data da execução | 2026-10-25 (início 14:59, horário de Brasília) |
| Versão testada | Branch `docs/readme-and-api-docs`, a partir da `dev` (`c19640c`), com a correção do limite de denúncias do chat (seção 8, F-01), os testes que fecharam as pendências (seção 9) e as correções F-09 e F-10, achadas pelo roteiro exploratório (seção 11) |
| Testes automatizados executados | **2904** |
| Aprovados / reprovados | **2904 / 0** |
| Duração | 6m 09s 531ms |
| Cobertura de linha / branch (solução) | **95,7% / 86,1%** (meta: 85% / 80%) |
| Casos de teste da Entrega 2 (backend) | 156 (CT-01 a CT-156) |
| Cobertos por teste automatizado que passou | **156** |
| Cobertos em parte | **0** |
| Sem teste automatizado | **0** |
| Roteiro exploratório contra a API no ar (seção 11) | **60 cenários, 60 aprovados** (na primeira execução, 3 falharam: F-09 e F-10) |

Nenhum teste falhou na execução. A seção 8 registra as falhas encontradas no desenvolvimento e nesta conferência, com a correção de cada uma; a seção 9, as pendências da primeira execução e o teste que fechou cada uma.

## 2. Ambiente e como reproduzir

| Item | Valor |
|---|---|
| Sistema | Windows 10 Pro, Docker Desktop 28.3 |
| SDK | .NET 10 (`global.json`), Microsoft Testing Platform + xUnit v3 |
| Dependências dos testes de integração | PostgreSQL 17, Redis 8 e RustFS em container (Testcontainers), banco limpo com Respawn |
| Serviços externos | Stripe, Nominatim e SMTP simulados nos testes (o Stripe por um servidor HTTP falso com a mesma API) |

Suíte completa (precisa do Docker), com o relatório de cobertura:

```bash
./scripts/coverage.sh
```

No Windows: `scripts/coverage.ps1`. Só unidade e arquitetura, sem Docker:

```bash
dotnet test --filter-not-namespace "*.IntegrationTests*" --ignore-exit-code 8
```

Com o resultado de cada teste na saída (a execução deste relatório):

```bash
dotnet test --output Detailed
```

O CI (`.github/workflows/ci.yml`) roda a suíte inteira em todo push e em toda PR para `main` e `dev`; o check `build-and-test` é obrigatório para o merge.

## 3. Resultado por projeto

| Projeto de teste | Tipo | Testes | Resultado |
|---|---|---:|---|
| `Audit.Application.UnitTests` | unitário | 3 | ✅ 3 aprovados |
| `Audit.Domain.UnitTests` | domínio | 5 | ✅ 5 aprovados |
| `Auth.Application.UnitTests` | unitário | 98 | ✅ 98 aprovados |
| `Auth.Domain.UnitTests` | domínio | 137 | ✅ 137 aprovados |
| `Auth.Infrastructure.UnitTests` | unitário | 32 | ✅ 32 aprovados |
| `Auth.IntegrationTests` | integração | 73 | ✅ 73 aprovados |
| `Bookings.Application.UnitTests` | unitário | 130 | ✅ 130 aprovados |
| `Bookings.Domain.UnitTests` | domínio | 134 | ✅ 134 aprovados |
| `Bookings.IntegrationTests` | integração | 59 | ✅ 59 aprovados |
| `Conversations.Application.UnitTests` | unitário | 209 | ✅ 209 aprovados |
| `Conversations.Domain.UnitTests` | domínio | 148 | ✅ 148 aprovados |
| `Conversations.IntegrationTests` | integração | 62 | ✅ 62 aprovados |
| `Hosts.Application.UnitTests` | unitário | 155 | ✅ 155 aprovados |
| `Hosts.Domain.UnitTests` | domínio | 183 | ✅ 183 aprovados |
| `Hosts.IntegrationTests` | integração | 66 | ✅ 66 aprovados |
| `Listings.Application.UnitTests` | unitário | 108 | ✅ 108 aprovados |
| `Listings.Domain.UnitTests` | domínio | 124 | ✅ 124 aprovados |
| `Listings.IntegrationTests` | integração | 38 | ✅ 38 aprovados |
| `Owners.Application.UnitTests` | unitário | 67 | ✅ 67 aprovados |
| `Owners.Domain.UnitTests` | domínio | 32 | ✅ 32 aprovados |
| `Owners.IntegrationTests` | integração | 52 | ✅ 52 aprovados |
| `Payments.Application.UnitTests` | unitário | 91 | ✅ 91 aprovados |
| `Payments.Domain.UnitTests` | domínio | 93 | ✅ 93 aprovados |
| `Payments.IntegrationTests` | integração | 61 | ✅ 61 aprovados |
| `Pets.Application.UnitTests` | unitário | 75 | ✅ 75 aprovados |
| `Pets.Domain.UnitTests` | domínio | 81 | ✅ 81 aprovados |
| `Pets.IntegrationTests` | integração | 46 | ✅ 46 aprovados |
| `Reviews.Application.UnitTests` | unitário | 166 | ✅ 166 aprovados |
| `Reviews.Domain.UnitTests` | domínio | 104 | ✅ 104 aprovados |
| `Reviews.IntegrationTests` | integração | 33 | ✅ 33 aprovados |
| `Shared.Contracts.UnitTests` | unitário | 14 | ✅ 14 aprovados |
| `Shared.Infrastructure.UnitTests` | unitário | 107 | ✅ 107 aprovados |
| `Shared.Kernel.UnitTests` | unitário | 34 | ✅ 34 aprovados |
| `ArchitectureTests` | arquitetura | 84 | ✅ 84 aprovados |
| **Total** | | **2904** | **✅ 2904 aprovados, 0 reprovados** |

## 4. Cobertura de código

Metas da Entrega 2 (seção 6.1): Domain 95% de linha e 90% de branch; Application 90% e 85%; Infrastructure 70% de linha; solução 85% e 80%.

| Camada | Projeto | Linha | Branch | Meta |
|---|---|---:|---:|---|
| Domain | `Audit.Domain` | 98,2% | 100% | ✅ 95% / 90% |
| Domain | `Auth.Domain` | 97% | 97,5% | ✅ 95% / 90% |
| Domain | `Bookings.Domain` | 99,1% | 95,8% | ✅ 95% / 90% |
| Domain | `Conversations.Domain` | 99,4% | 97,5% | ✅ 95% / 90% |
| Domain | `Hosts.Domain` | 98,3% | 96,1% | ✅ 95% / 90% |
| Domain | `Listings.Domain` | 99% | 93,5% | ✅ 95% / 90% |
| Domain | `Owners.Domain` | 98,5% | 95,6% | ✅ 95% / 90% |
| Domain | `Payments.Domain` | 99% | 95% | ✅ 95% / 90% |
| Domain | `Pets.Domain` | 99,2% | 94,5% | ✅ 95% / 90% |
| Domain | `Reviews.Domain` | 99,3% | 99,5% | ✅ 95% / 90% |
| Application | `Audit.Application` | 100% | 95,4% | ✅ 90% / 85% |
| Application | `Auth.Application` | 98,3% | 87,5% | ✅ 90% / 85% |
| Application | `Bookings.Application` | 98,9% | 91,2% | ✅ 90% / 85% |
| Application | `Conversations.Application` | 99,6% | 97,2% | ✅ 90% / 85% |
| Application | `Hosts.Application` | 95,6% | 86,3% | ✅ 90% / 85% |
| Application | `Listings.Application` | 98,8% | 91,2% | ✅ 90% / 85% |
| Application | `Owners.Application` | 97% | 87% | ✅ 90% / 85% |
| Application | `Payments.Application` | 97,1% | 89,1% | ✅ 90% / 85% |
| Application | `Pets.Application` | 99,6% | 93,6% | ✅ 90% / 85% |
| Application | `Reviews.Application` | 100% | 98,9% | ✅ 90% / 85% |
| Infrastructure | `Audit.Infrastructure` | 94,2% | 82,1% | ✅ 70% (linha) |
| Infrastructure | `Auth.Infrastructure` | 92,4% | 64,9% | ✅ 70% (linha) |
| Infrastructure | `Bookings.Infrastructure` | 96,1% | 78,1% | ✅ 70% (linha) |
| Infrastructure | `Conversations.Infrastructure` | 92,3% | 77,6% | ✅ 70% (linha) |
| Infrastructure | `Hosts.Infrastructure` | 87,4% | 45,4% | ✅ 70% (linha) |
| Infrastructure | `Listings.Infrastructure` | 80,1% | 67% | ✅ 70% (linha) |
| Infrastructure | `Owners.Infrastructure` | 95,5% | 63,6% | ✅ 70% (linha) |
| Infrastructure | `Payments.Infrastructure` | 93,3% | 73,5% | ✅ 70% (linha) |
| Infrastructure | `Pets.Infrastructure` | 99,1% | 84,6% | ✅ 70% (linha) |
| Infrastructure | `Reviews.Infrastructure` | 92,6% | 78,9% | ✅ 70% (linha) |
| Compartilhado | `Shared.Kernel` | 88,8% | 92,3% | — |
| Compartilhado | `Shared.Contracts` | 97,8% | 81,5% | — |
| Compartilhado | `Shared.Infrastructure` | 91,2% | 78,8% | — |
| Compartilhado | `Api` | 94,2% | 75% | — |
| **Solução** | | **95,7%** | **86,1%** | ✅ 85% / 80% |

Todos os projetos atingiram a meta da camada. Na primeira execução (seção 9) três estavam abaixo: `Auth.Application` (82,1% de branch), `Owners.Application` (82,3%) e `Payments.Infrastructure` (60,4% de linha). O relatório completo, com a cobertura de cada classe e de cada linha, é gerado em `artifacts/coverage/report/index.html` pelo `scripts/coverage.sh`.

## 5. Casos de teste executados

Cada caso planejado na Entrega 2 (seção 6.2) com os testes automatizados que o verificam e o resultado deles nesta execução. **Tipo do teste:** integração (rota HTTP de ponta a ponta, com banco, Redis e bucket reais), unitário (caso de uso, com as portas simuladas), domínio (regra da entidade) e arquitetura.

**Resultado:** ✅ passou e cobre o caso · ⚠️ passou, mas parte do caso não tem teste (o que falta está na observação) · ❌ sem teste automatizado.

### Contas, sessão e perfil (Auth e Owners)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-01 | Cadastro de tutor com dados válidos | 201, sessão devolvida, Location: /owners/me | `OwnerRegistrationTests.RegisterOwnerAccount_Should_Return201WithSessionAndOwner_When_DataIsValid` (integração)<br>`AccountEndpointsTests.Register_Should_PointLocationToMe_When_Created` (integração) | ✅ Passou |
| CT-02 | Cadastro com senha fraca, CPF inválido, menor de 18 e CEP errado | 400 com todos os campos de uma vez | `OwnerRegistrationTests.RegisterOwnerAccount_Should_Return400WithAccountAndCpfFields_When_PayloadIsEmpty` (integração)<br>`OwnerRegistrationTests.RegisterHost_Should_Return400WithNestedAddressFields_When_AddressIsInvalid` (integração)<br>`OwnerRegistrationTests.RegisterHost_Should_Return400_When_PersonIsUnder18` (integração)<br>`AccountEndpointsTests.Register_Should_ReturnEveryInvalidField_When_ManyFieldsAreEmpty` (integração) | ✅ Passou |
| CT-03 | Cadastro com e-mail já usado por outro tutor | 409 AUTH_EMAIL_ALREADY_REGISTERED | `OwnerRegistrationTests.RegisterOwnerAccount_Should_Return409WithoutCreatingOwner_When_EmailIsAlreadyAnOwner` (integração) | ✅ Passou |
| CT-04 | Cadastro com CPF de conta de outro e-mail | 409 OWNER_CPF_ALREADY_REGISTERED | `OwnerRegistrationTests.RegisterOwnerAccount_Should_Return409_When_CpfIsAlreadyRegistered` (integração) | ✅ Passou |
| CT-05 | Login com credenciais certas e o papel certo | 200 com access e refresh token | `AuthEndpointsTests.Login_Should_Return200WithSession_When_CredentialsAreValid` (integração) | ✅ Passou |
| CT-06 | Login com senha errada ou e-mail inexistente | O mesmo 401 AUTH_INVALID_CREDENTIALS nos dois casos | `AuthEndpointsTests.Login_Should_Return401_When_PasswordIsWrong` (integração)<br>`AuthEndpointsTests.Login_Should_Return401_When_AccountDoesNotExist` (integração) | ✅ Passou |
| CT-07 | Login de conta suspensa ou inativa | 403 AUTH_ACCOUNT_SUSPENDED / AUTH_ACCOUNT_DEACTIVATED | `OwnerAdminTests.Suspend_Should_BlockTheOwnerImmediately_When_AdminSuspends` (integração)<br>`AccountEndpointsTests.Deactivate_Should_BlockLoginAndRefreshOfThatAccountOnly_When_Called` (integração)<br>`CreateSessionCommandHandlerTests.HandleAsync_Should_ReturnSuspended_When_AdminSuspendedTheAccount` (unitário) | ✅ Passou |
| CT-08 | 11ª tentativa de login em 5 minutos | 429 com Retry-After | `RateLimitingTests.Login_Should_Return429InTheEnvelope_When_CredentialsLimitIsExceeded` (integração) | ✅ Passou |
| CT-09 | Refresh válido; depois o mesmo refresh de novo | Par novo; na segunda vez, 401 AUTH_REFRESH_TOKEN_INVALID | `AuthEndpointsTests.Refresh_Should_Return200WithNewPair_When_TokenIsValid` (integração)<br>`AuthEndpointsTests.Refresh_Should_Return401_When_TokenIsReused` (integração) | ✅ Passou |
| CT-10 | Cinco refresh simultâneos com o mesmo token | Só um vence | `AuthEndpointsTests.Refresh_Should_AllowOnlyOneWinner_When_SameTokenIsUsedConcurrently` (integração) | ✅ Passou |
| CT-11 | Logout e depois refresh com o mesmo token | Logout 200 (idempotente); o refresh dá 401; o access token ainda vale até vencer | `AuthEndpointsTests.Revoke_Should_InvalidateRefreshToken_When_LoggingOut` (integração)<br>`AuthEndpointsTests.Revoke_Should_Return200_When_TokenWasAlreadyInvalid` (integração)<br>`AccountEndpointsTests.Logout_Should_KeepTheAccessTokenValidUntilItExpires_When_TheRefreshTokenIsRevoked` (integração) | ✅ Passou |
| CT-12 | Troca de senha logado com outra sessão aberta | Sessão nova devolvida; a antiga recebe 401 na hora; e-mail "senha alterada" | `AccountEndpointsTests.ChangePassword_Should_EndOldSessionsAndReturnANewOne_When_CurrentPasswordIsRight` (integração)<br>`ChangePasswordCommandHandlerTests.HandleAsync_Should_ChangePasswordRevokeEverythingAndOpenNewSession_When_CurrentPasswordIsRight` (unitário)<br>_O e-mail "senha alterada" é conferido no teste unitário (notifier)._ | ✅ Passou |
| CT-13 | Troca de senha com a senha atual errada | 400 em currentPassword, não 401 | `AccountEndpointsTests.ChangePassword_Should_Return400OnCurrentPasswordAndKeepSession_When_PasswordIsWrong` (integração) | ✅ Passou |
| CT-14 | Esqueci a senha para e-mail existente, inexistente e de conta suspensa | 200 nos três; e-mail só para a conta existente e não suspensa | `PasswordEndpointsTests.Forgot_Should_EmailATokenAndLink_When_AccountExists` (integração)<br>`PasswordEndpointsTests.Forgot_Should_Return200WithoutEmail_When_AccountDoesNotExist` (integração)<br>`ForgotPasswordCommandHandlerTests.HandleAsync_Should_SucceedWithoutNotifying_When_AccountIsSuspended` (unitário)<br>_Conta suspensa: coberta por teste unitário._ | ✅ Passou |
| CT-15 | Reset com token válido; o mesmo token de novo; token antigo depois de um pedido novo | Senha trocada e sessões encerradas; depois, 401 AUTH_PASSWORD_RESET_TOKEN_INVALID | `PasswordEndpointsTests.Reset_Should_ChangePassword_When_TokenAndPasswordAreValid` (integração)<br>`PasswordEndpointsTests.Reset_Should_RevokeEveryOpenSession_When_PasswordChanges` (integração)<br>`PasswordEndpointsTests.Reset_Should_Return401_When_TokenIsReused` (integração)<br>`PasswordEndpointsTests.Reset_Should_InvalidateOlderToken_When_ANewOneIsRequested` (integração) | ✅ Passou |
| CT-16 | Troca de conta com a senha certa da outra conta | Sessão da outra conta; a atual continua valendo; cada uma vê só os próprios dados | `AccountEndpointsTests.Switch_Should_OpenSessionOnTheOtherAccount_When_PasswordIsRight` (integração) | ✅ Passou |
| CT-17 | Troca de conta com senha errada, sem a outra conta ou para o mesmo papel | 404 AUTH_LINKED_ACCOUNT_NOT_FOUND nos três casos | `AccountEndpointsTests.Switch_Should_Return404_When_PasswordIsWrong` (integração)<br>`AccountEndpointsTests.Switch_Should_Return404_When_OtherAccountDoesNotExist` (integração)<br>`AccountEndpointsTests.Switch_Should_Return404_When_TheTargetIsTheSameRole` (integração) | ✅ Passou |
| CT-18 | PATCH do tutor trocando o CPF sem currentPassword; trocando depois do primeiro pagamento | 400 em currentPassword; 422 OWNER_CPF_LOCKED | `OwnerRegistrationTests.UpdateMe_Should_Return400OnCurrentPassword_When_CpfChangesWithoutPassword` (integração)<br>`OwnerRegistrationTests.UpdateMe_Should_Return422_When_FirstPaymentHappened` (integração) | ✅ Passou |
| CT-19 | Tutor inativa o perfil com reserva em aberto | 422 OWNER_HAS_OPEN_BOOKING, nada muda | `BookingEndpointsTests.OwnerDeactivation_Should_Return422_While_TheOwnerHasAnOpenBooking_And_WorkAfterCancelling` (integração)<br>`DeactivateMyOwnerCommandHandlerTests.HandleAsync_Should_RefuseWithoutTouchingAnything_When_OwnerHasAnOpenBooking` (unitário) | ✅ Passou |
| CT-20 | Tutor inativa e depois reativa com e-mail e senha | Sessões caem; login dá 403; a reativação devolve sessão nova | `OwnerRegistrationTests.DeactivateMe_Should_DeactivateOwnerAndAccount_When_Called` (integração)<br>`OwnerRegistrationTests.Reactivate_Should_ReactivateOwnerAndAccountAndReturnSession_When_CredentialsAreRight` (integração)<br>`AccountEndpointsTests.Deactivate_Should_RejectTheOldAccessTokenImmediately_When_Called` (integração) | ✅ Passou |
| CT-21 | Admin libera o CPF de tutor não suspenso; tira a suspensão depois de liberar | 422 OWNER_SUSPENSION_REQUIRED; 422 OWNER_CPF_RELEASED | `OwnerAdminTests.ReleaseCpf_Should_Return422_When_OwnerIsNotSuspended` (integração)<br>`OwnerAdminTests.Lift_Should_RefuseWithoutTouchingTheAccount_When_CpfWasReleased` (unitário)<br>_O segundo caso é coberto por teste unitário._ | ✅ Passou |
| CT-22 | Tutor exclui a conta com a senha certa | 200; sessões caem; nome vira "Conta excluída"; pets inativos e sem fotos; conversas apagadas; reservas e pagamentos ficam; trilha com owner.erased e account.erased | `OwnerDeletionTests.DeleteMe_Should_AnonymizeTheAccountAndDropTheCpf_When_PasswordMatches` (integração)<br>`OwnerDeletionTests.DeleteMe_Should_EndTheSessionsAndRefuseTheOldLogin_When_Deleted` (integração)<br>`DeleteMyOwnerCommandHandlerTests.HandleAsync_Should_EraseEverythingInOrder_When_PasswordMatches` (unitário)<br>`ErasePetsOfKeeperTests.HandleAsync_Should_EraseEveryPetAndThenThePhotos_When_KeeperHasPets` (unitário)<br>`EraseConversationsOfPartyCommandHandlerTests.HandleAsync_Should_DeleteEveryConversationOfTheParty_When_Called` (unitário)<br>_Pets e conversas apagados: cobertos nos testes unitários de cada módulo._ | ✅ Passou |
| CT-23 | Exclusão com reserva em aberto; com a senha errada | 422 OWNER_HAS_OPEN_BOOKING, nada muda; 400 em currentPassword | `DeleteMyOwnerCommandHandlerTests.HandleAsync_Should_RefuseWithoutTouchingAnything_When_OwnerHasAnOpenBooking` (unitário)<br>`OwnerDeletionTests.DeleteMe_Should_Return400AndKeepTheAccount_When_PasswordIsWrong` (integração)<br>_Reserva em aberto: coberta por teste unitário._ | ✅ Passou |
| CT-24 | Login e reativação com o e-mail da conta excluída; cadastro novo com o mesmo e-mail e CPF | 401; o cadastro novo passa (e-mail e CPF ficaram livres) | `OwnerDeletionTests.DeleteMe_Should_FreeTheEmailAndTheCpf_When_Deleted` (integração)<br>`OwnerDeletionTests.DeleteMe_Should_EndTheSessionsAndRefuseTheOldLogin_When_Deleted` (integração) | ✅ Passou |

### Verificação e perfil do anfitrião (Hosts)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-25 | Rascunho, três fotos, envio e aprovação | Perfil de anfitrião criado; fotos apagadas; e-mail "aprovado" | `HostApplicationFlowTests.Flow_Should_CreateTheVerifiedHost_When_AdminApprovesTheApplication` (integração)<br>`HostApplicationFlowTests.Upload_Should_StoreTheThreePhotos_When_TheyComeInOneRequest` (integração) | ✅ Passou |
| CT-26 | Abertura como empresa sem CNPJ; pessoa física com dado de empresa | 400 nos campos da empresa; 400 em cnpj | `CompanyAndCpfRuleTests.Company_Should_Return400WithEveryCompanyField_When_DataIsMissing` (integração)<br>`CompanyAndCpfRuleTests.Individual_Should_Return400_When_CompanyDataIsSent` (integração) | ✅ Passou |
| CT-27 | Uma das fotos não é imagem | 400 no campo da foto; nenhuma foto gravada (tudo ou nada) | `HostApplicationFlowTests.Upload_Should_KeepNothing_When_OneOfThePhotosIsInvalid` (integração)<br>`HostApplicationFlowTests.Upload_Should_Return400_When_TheFileIsNotAnImage` (integração) | ✅ Passou |
| CT-28 | A mesma imagem em duas vagas | 409 HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS | `HostApplicationFlowTests.Upload_Should_Return409_When_TheSameImageGoesToTwoSlots` (integração) | ✅ Passou |
| CT-29 | Enviar sem as três fotos; trocar foto depois de enviada | 422 HOST_APPLICATION_DOCUMENTS_MISSING ; 422 HOST_APPLICATION_NOT_EDITABLE | `HostApplicationFlowTests.Submit_Should_Return422WithTheMissingPhotos_When_SomeAreMissing` (integração)<br>`StartHostApplicationTests.Update_Should_ReturnNotEditable_When_AlreadySubmitted` (unitário)<br>_Foto depois de enviada: coberto por teste unitário._ | ✅ Passou |
| CT-30 | Quarta tentativa depois de 3 recusas; nova solicitação depois de recusa por fraude | 422 HOST_APPLICATION_ATTEMPTS_EXCEEDED ; 422 HOST_APPLICATION_BLOCKED | `HostApplicationTests.Start_Should_ReturnAttemptsExceeded_When_MaxRejectionsWereReached` (domínio)<br>`HostApplicationFlowTests.Reject_Should_BlockNewApplications_When_FraudIsSuspected` (integração)<br>_Quarta tentativa: coberta por teste de domínio._ | ✅ Passou |
| CT-31 | Outra conta abre verificação com um CPF recusado por fraude | 422 HOST_IDENTITY_BLOCKED; o banco guarda só o HMAC do CPF | `IdentityBlockApiTests.Fraud_Should_BlockTheCpfEvenAfterTheAccountIsDeleted_When_ThePersonComesBack` (integração)<br>`StartHostApplicationTests.Start_Should_ReturnIdentityBlocked_When_TheCpfWasBlockedByAnotherAccount` (unitário) | ✅ Passou |
| CT-32 | Admin libera o CPF bloqueado; libera um CPF sem bloqueio | A outra conta volta a abrir a verificação; 404 HOST_IDENTITY_BLOCK_NOT_FOUND | `IdentityBlockApiTests.Release_Should_LetTheRealOwnerStart_When_TheAdminReleasesTheCpf` (integração)<br>`ReleaseIdentityBlockTests.HandleAsync_Should_ReturnNotFound_When_TheCpfIsNotBlocked` (unitário) | ✅ Passou |
| CT-33 | Dois admins decidem a mesma solicitação ao mesmo tempo | Um vence; o outro recebe 409 HOST_APPLICATION_CONFLICT | `HostApplicationFlowTests.Decision_Should_LetOnlyOneAdminWin_When_TwoDecideTheSameApplicationAtTheSameTime` (integração)<br>`HostApplicationFlowTests.Decision_Should_Return409ToTheSecondWrite_When_TwoAdminsLoadedTheSameApplication` (integração)<br>_Pela API, quem chega depois recebe 409 (perdeu a gravação) ou 422 (já decidida); contra o banco, a segunda gravação recebe 409._ | ✅ Passou |
| CT-34 | Rascunho parado há 7 dias | A tarefa marca expired e apaga as fotos | `HostApplicationFlowTests.Purge_Should_ExpireAbandonedDrafts_When_TheyPassTheLimit` (integração) | ✅ Passou |
| CT-35 | Admin abre uma foto da verificação | Link assinado de 5 min com no-store; abertura registrada na trilha | `HostApplicationFlowTests.DocumentLink_Should_ExpireInFiveMinutes_And_StopWorking_When_Tampered` (integração)<br>`HostApplicationFlowTests.Flow_Should_CreateTheVerifiedHost_When_AdminApprovesTheApplication` (integração)<br>_O no-store e o host_application.document_viewed são conferidos no fluxo._ | ✅ Passou |
| CT-36 | Anfitrião tenta mudar o nome ou o CPF no PATCH | 422 HOST_IDENTITY_LOCKED; telefone e endereço mudam normalmente | `HostLifecycleApiTests.Patch_Should_Return422_When_TheVerifiedNameChanges` (integração)<br>`HostLifecycleApiTests.Patch_Should_ChangeThePhoneAndKeepTheIdentity_When_TheFormComesWhole` (integração) | ✅ Passou |
| CT-37 | Cadastro no Stripe antes da aprovação; anfitrião suspenso | 404 HOST_NOT_FOUND; 422 HOST_STRIPE_ONBOARDING_NOT_ALLOWED | `HostStripeOnboardingApiTests.Onboarding_Should_Return404_When_TheAdminHasNotApprovedTheHostYet` (integração)<br>`StripeOnboardingTests.Start_Should_RefuseWithoutCallingStripe_When_HostIsSuspended` (unitário)<br>_Suspenso: coberto por teste unitário._ | ✅ Passou |
| CT-38 | Webhook account.updated com os repasses liberados | stripeStatus = enabled; o cantinho passa a aparecer; e-mail ao anfitrião | `HostStripeOnboardingApiTests.Webhook_Should_MakeTheHostListable_When_StripeEnablesPayouts` (integração) | ✅ Passou |
| CT-39 | Cadastro no Stripe parado | Lembretes nos dias 3 e 7, uma vez cada | `HostStripeOnboardingApiTests.Maintenance_Should_RemindByEmail_When_TheHostStoppedHalfway` (integração) | ✅ Passou |
| CT-40 | Admin suspende o anfitrião com reservas confirmadas e uma em andamento | As que não começaram são canceladas com 100% ao tutor; a em andamento continua, e o admin faz o check-out com motivo | `BookingEndpointsTests.HostSuspension_Should_CancelWhatHasNotStarted_And_LetTheAdminCheckOutTheStayInProgress` (integração)<br>`PlatformActionsTests.CancelForSuspension_Should_CancelEveryBookingNotStarted_When_TheHostIsSuspended` (unitário) | ✅ Passou |
| CT-41 | Painel com pedidos, estadias e repasses no mês | Cada grupo com count e até 5 itens; recebido do mês pela data do repasse; guardado e previsto de agora; ?month= muda só os ganhos | `PaymentEndpointsTests.Dashboard_Should_ShowRequestsStays_AndCountEarningsByTransferDate` (integração)<br>`HostDashboardTests.Handle_Should_MapBookingsAndEarnings_When_HostExists` (unitário)<br>`HostDashboardTests.Handle_Should_UseRequestedMonth_When_MonthIsInThePast` (unitário) | ✅ Passou |
| CT-42 | Painel com month no futuro ou mal formado; antes da aprovação | 400 em month; 404 HOST_NOT_FOUND | `PaymentEndpointsTests.Dashboard_Should_Return400ForBadMonth_And403ForOwner` (integração)<br>`HostStripeOnboardingApiTests.Dashboard_Should_Return404BeforeApproval_AndEmptyPanelAfter` (integração)<br>`HostDashboardTests.Handle_Should_ReturnMonthInFuture_When_MonthIsAfterTheCurrentOne` (unitário) | ✅ Passou |
| CT-43 | Anfitrião exclui a conta | Solicitações e documentos apagados do bucket privado; cantinhos inativos, sem fotos e sem o endereço exato; o bloqueio do CPF continua; conta do Stripe e dados da empresa ficam | `HostDeletionApiTests.DeleteMe_Should_DropTheCpfAndEndTheSessions_When_TheApprovedHostLeaves` (integração)<br>`HostDeletionApiTests.DeleteMe_Should_EraseTheApplication_When_TheAccountIsStillInVerification` (integração)<br>`DeleteMyHostTests.HandleAsync_Should_EraseEverythingInOrder_When_PasswordMatches` (unitário)<br>`EraseListingsOfHostTests.HandleAsync_Should_EraseTheListingsAndThenTheirPhotos_When_HostHasListings` (unitário)<br>`IdentityBlockApiTests.Fraud_Should_BlockTheCpfEvenAfterTheAccountIsDeleted_When_ThePersonComesBack` (integração) | ✅ Passou |

### Pets

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-44 | Cachorro sem porte; exótico sem descrição; peixe com porte | 400 no campo | `PetEndpointsTests.Register_Should_Return400_When_DogHasNoSizeAndExoticHasNoDescription` (integração)<br>`PetProfileTests.Create_Should_RejectSize_When_PetIsNeitherDogNorCat` (domínio)<br>_Peixe com porte: coberto por teste de domínio._ | ✅ Passou |
| CT-45 | Admin tenta cadastrar pet | 403 FORBIDDEN | `PetEndpointsTests.Register_Should_Return403_When_AccountIsAdmin` (integração) | ✅ Passou |
| CT-46 | Anfitrião ainda não aprovado cadastra pet da casa | 404 PET_KEEPER_NOT_FOUND | `PetEndpointsTests.Register_Should_Return404KeeperNotFound_When_HostAccountHasNoHostProfile` (integração) | ✅ Passou |
| CT-47 | Microchip igual ao de outro pet ativo; igual ao de um pet desativado | 409 PET_MICROCHIP_ALREADY_REGISTERED; passa | `PetEndpointsTests.Register_Should_Return409_When_AnotherActivePetHasTheMicrochip` (integração)<br>`PetEndpointsTests.Register_Should_AcceptTheMicrochip_When_ThePetThatHadItIsDeactivated` (integração) | ✅ Passou |
| CT-48 | Quarta foto; a mesma imagem duas vezes | 422 PET_PHOTO_LIMIT_REACHED; 409 PET_PHOTO_ALREADY_EXISTS | `PetPhotoTests.AddPhoto_Should_Return422AndStoreNothing_When_PetAlreadyHasThreePhotos` (integração)<br>`PetPhotoTests.AddPhoto_Should_Return409AndLeaveNoFileBehind_When_ThePetAlreadyHasThatImage` (integração) | ✅ Passou |
| CT-49 | Ler ou alterar pet de outro tutor | 404 PET_NOT_FOUND | `PetEndpointsTests.GetById_Should_HideTheOwnersPet_When_AnotherOwnerAsks` (integração)<br>`PetEndpointsTests.Update_Should_Return404_When_PetBelongsToAnotherOwner` (integração) | ✅ Passou |
| CT-50 | Anfitrião lê a ficha do pet de um pedido no seu cantinho; outro anfitrião tenta | 200 com a ficha completa; | `PetBookingRulesTests.GetById_Should_ShowTheOwnersPetToTheHost_When_ThePetWasBookedWithThem` (unitário)<br>`PetBookingRulesTests.GetById_Should_HideTheOwnersPetFromAHost_When_ThereIsNoBooking` (unitário)<br>_Coberto por teste unitário (a consulta à reserva é uma porta)._ | ✅ Passou |
| CT-51 | Desativar pet com reserva em aberto | 422 PET_HAS_OPEN_BOOKING | `BookingEndpointsTests.PetDeactivation_Should_Return422_While_ThePetHasAnOpenBooking_And_WorkAfterCancelling` (integração) | ✅ Passou |
| CT-52 | Trocar a espécie de cachorro para coelho | O porte é limpo sozinho | `PetEndpointsTests.Update_Should_KeepTheSizeForACatAndDropItForARabbit_When_SpeciesChanges` (integração) | ✅ Passou |

### Cantinho e busca (Listings)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-53 | Anfitrião verificado com Stripe liberado cadastra o cantinho | 201, coordenada e ponto aproximado calculados pela API | `ListingEndpointsTests.Create_Should_Return201WithTheFullListing_When_IndividualHostSendsAValidListing` (integração)<br>`ListingTests.Create_Should_DrawAnApproximateLocationNearTheHome` (domínio) | ✅ Passou |
| CT-54 | Sem repasses liberados; segundo cantinho de pessoa física | 422 LISTING_HOST_PAYOUTS_NOT_ENABLED; 422 LISTING_LIMIT_REACHED | `ListingEndpointsTests.Create_Should_Return422_When_StripeHasNotApprovedThePayoutSetupYet` (integração)<br>`ListingEndpointsTests.Create_Should_Return422LimitReached_When_IndividualHostAlreadyHasAListing` (integração) | ✅ Passou |
| CT-55 | Capacidade 11 em pessoa física; diária de R$ 5 | 400 em capacity e nightlyRate | `ListingEndpointsTests.Create_Should_Return400_When_IndividualSendsMoreThanTenPetsOrThreeDecimals` (integração)<br>`ListingDetailsTests.Create_Should_KeepTheNightlyRateInRange` (domínio)<br>_Diária abaixo de R$ 10: coberta por teste de domínio._ | ✅ Passou |
| CT-56 | Endereço que o mapa não acha; mapa fora do ar | 422 LISTING_ADDRESS_NOT_LOCATED; 503 LISTING_GEOCODER_UNAVAILABLE, nada gravado | `ListingEndpointsTests.Create_Should_FailWithoutSaving_When_TheMapDoesNotLocateTheAddress` (integração)<br>`CreateListingCommandHandlerTests.HandleAsync_Should_NotSave_When_TheMapDoesNotLocateTheAddress` (unitário)<br>_Mapa fora do ar (503): coberto pelo teste unitário parametrizado._ | ✅ Passou |
| CT-57 | O mesmo endereço geocodificado duas vezes | Uma consulta ao Nominatim; a segunda vem do cache no Redis | `CachedGeocoderTests.LocateAsync_Should_CallTheMapOnce_When_TheSameAddressIsAskedTwice` (integração) | ✅ Passou |
| CT-58 | Inativar com reserva em aberto | 422 LISTING_HAS_OPEN_BOOKING | `BookingEndpointsTests.ListingDeactivation_Should_Return422_While_ThereIsAnOpenBooking_And_WorkAfterItCloses` (integração) | ✅ Passou |
| CT-59 | Outra conta abre o detalhe do cantinho | Só bairro, cidade e UF, sem location; o dono e o admin veem tudo | `ListingEndpointsTests.GetById_Should_HideStreetZipAndLocation_When_ViewerIsAnOwner` (integração) | ✅ Passou |
| CT-60 | Bloqueio de agenda sobreposto; 51º bloqueio ativo | 422 LISTING_AVAILABILITY_BLOCK_OVERLAP; LISTING_AVAILABILITY_BLOCK_LIMIT_REACHED | `AvailabilityBlocksTests.Block_Should_FailWithOverlapWithoutSaving_When_TheDatesAreAlreadyBlocked` (unitário)<br>`AvailabilityBlockTests.Create_Should_FailWithLimitReached_When_TheListingHasTheMaximumOfOpenBlocks` (domínio)<br>_Coberto por testes unitários e de domínio; não há teste de integração da agenda._ | ✅ Passou |
| CT-61 | Busca de 10 km com filtros de espécie e porte | Só os que atendem a todos, do mais perto ao mais longe, com selos | `ListingEndpointsTests.Search_Should_ReturnOnlyListingsInsideTheRadius_OrderedByDistance` (integração)<br>`ListingEndpointsTests.Search_Should_SumEveryFilter_When_FiltersAreCombinedWithTheRadius` (integração) | ✅ Passou |
| CT-62 | Busca com datas e 2 pets | Some o cantinho sem vaga, com dia bloqueado ou que não recebe no dia | `BookingEndpointsTests.Search_Should_HideAFullListingOnlyOnTheBookedDates_And_ShowBadges` (integração)<br>`ListingAvailabilityTests.Search_Should_RequireEveryWeekdayOfTheStayAndRoomForThePets_When_DatesAreSent` (unitário) | ✅ Passou |
| CT-63 | Anfitrião suspenso ou sem Stripe liberado | O cantinho dele não aparece na busca nem no detalhe para os outros | `ListingEndpointsTests.Search_Should_HideInactiveListingsAndSuspendedHosts` (integração)<br>`ListingEndpointsTests.Search_Should_HideTheListingButKeepItForTheOwner_When_StripeBlocksPayoutsLater` (integração) | ✅ Passou |
| CT-64 | Busca só por cidade, escrita sem acento | maringa acha Maringá; ordem do mais novo, sem distanceKm | `ListingEndpointsTests.Search_Should_IgnoreDistance_When_OnlyPlaceFiltersAreSent` (integração) | ✅ Passou |
| CT-65 | Conta sem endereço busca sem ponto; radiusKm=60; só latitude | 422 LISTING_SEARCH_ADDRESS_REQUIRED; 400; 400 | `SearchListingsQueryHandlerTests.HandleAsync_Should_ReturnAddressRequired_When_AccountHasNoAddress` (unitário)<br>`ListingEndpointsTests.Search_Should_Return400_When_RadiusIsOutOfRange` (integração)<br>`ListingEndpointsTests.Search_Should_Return400WithEveryField_When_FiltersAreInvalid` (integração) | ✅ Passou |
| CT-66 | Empresa logada como anfitrião busca sem ponto | A origem é o endereço da empresa | `ListingEndpointsTests.Search_Should_StartFromTheCompanyAddress_When_ACompanyHostSearchesWithoutCoordinates` (integração) | ✅ Passou |
| CT-67 | Busca sem login, sem coordenada nem lugar; sem login com radiusKm | Todos os cantinhos visíveis, do mais novo, sem o selo neighbor; 422 LISTING_SEARCH_ADDRESS_REQUIRED | `SearchListingsQueryHandlerTests.HandleAsync_Should_ListEverythingWithoutRadius_When_AnonymousSendsNoPlace` (unitário)<br>`SearchListingsQueryHandlerTests.HandleAsync_Should_ReturnAddressRequired_When_AnonymousSendsRadiusWithoutCoordinates` (unitário)<br>_Coberto por teste unitário._ | ✅ Passou |
| CT-68 | Detalhe sem login | Visão pública com approximateLocation, badges e rating, sem location | `ListingEndpointsTests.GetById_Should_ShowThePublicView_When_ViewerIsNotLoggedIn` (integração) | ✅ Passou |
| CT-69 | Ponto aproximado de um cantinho | A casa fica a menos de 500 m do ponto; o ponto não muda entre leituras e só é sorteado de novo quando o endereço muda; a distância da busca usa o ponto | `ListingTests.Create_Should_DrawAnApproximateLocationNearTheHome` (domínio)<br>`ListingTests.Relocate_Should_KeepTheApproximateLocation_When_OnlyTheAddressTextChanges` (domínio)<br>`ListingEndpointsTests.Search_Should_SendTheSameApproximateLocation_On_EverySearch` (integração) | ✅ Passou |
| CT-70 | Selo e nota | well_rated com média 4,5 e 3 avaliações, sem ele com 2; rating omitido sem avaliação; empresa usa a nota da filial e pessoa física a do anfitrião | `ReviewsBothSidesTests.Search_Should_ShowTheRatingAndTheWellRatedBadge_When_TheHostHasThreeGoodReviews` (integração)<br>`ReviewsBothSidesTests.Search_Should_LeaveTheRatingOut_When_NobodyReviewed` (integração)<br>`ListingRatingsTests.ForAsync_Should_UseTheHostRatingForIndividualsAndTheListingRatingForCompanies_When_BothAreOnThePage` (unitário) | ✅ Passou |
| CT-71 | "Combina com seu pet" para um pet que não se dá com gatos, com um gato hospedado nas datas | matches: false com guest_is_cat e o nome do hóspede, sem dados do outro tutor | `BookingEndpointsTests.Compatibility_Should_WarnAboutTheCatStayingInThePeriod_When_PetDoesNotGetAlongWithCats` (integração) | ✅ Passou |
| CT-72 | "Combina" com pet de outro tutor; pedido feito por anfitrião | 404 LISTING_PET_NOT_FOUND; 403 | `BookingEndpointsTests.Compatibility_Should_Return404_When_PetIsFromAnotherOwner` (integração)<br>`BookingEndpointsTests.Compatibility_Should_Return403_When_CallerIsAHost` (integração) | ✅ Passou |

### Reservas (Bookings)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-73 | Pedido válido com 72h de antecedência | 201, requested, total = diária × noites × pets | `BookingEndpointsTests.Booking_Should_GoFromRequestToCompleted_When_HostAcceptsChecksInAndChecksOut` (integração) | ✅ Passou |
| CT-74 | Chegada em menos de 72h; mais de 30 noites | 400 no período | `BookingEndpointsTests.Create_Should_Return400CheckInTooSoon_When_TheStayStartsInLessThan72Hours` (integração)<br>`BookingEndpointsTests.Create_Should_Return400_When_PeriodIsInvalid` (integração)<br>`StayPeriodTests.Create_Should_ReturnValidationError_When_StayHasMoreThanMaxNights` (domínio) | ✅ Passou |
| CT-75 | Pet de espécie ou porte não aceito | 422 BOOKING_PET_SPECIES_NOT_ACCEPTED / SIZE_NOT_ACCEPTED | `BookingEndpointsTests.Create_Should_Return422SizeNotAccepted_When_ListingDoesNotTakeLargeDogs` (integração)<br>`BookingRequestTests.Request_Should_ReturnSpeciesNotAccepted_When_ListingDoesNotTakeTheSpecies` (domínio)<br>_Espécie não aceita: coberta por teste de domínio._ | ✅ Passou |
| CT-76 | Pet já em outra reserva no período | 409 BOOKING_PET_PERIOD_CONFLICT | `BookingEndpointsTests.Create_Should_Return409_When_PetAlreadyHasABookingInThePeriod` (integração) | ✅ Passou |
| CT-77 | Capacidade esgotada numa noite; dia bloqueado | 422 BOOKING_CAPACITY_EXCEEDED; 422 BOOKING_DATES_BLOCKED | `BookingEndpointsTests.Create_Should_Return422CapacityExceeded_When_AnAcceptedStayHoldsTheOnlySpot` (integração)<br>`BookingEndpointsTests.Calendar_Should_BlockRequestsAndSearch_On_TheClosedDays_And_ReopenThem` (integração) | ✅ Passou |
| CT-78 | Tutor com parcela em atraso pede estadia | 422 BOOKING_OWNER_PAYMENT_OVERDUE | `BookingEndpointsTests.Create_Should_Return422_When_TheOwnerHasAnOverdueInstallment` (integração) | ✅ Passou |
| CT-79 | Dois aceites ao mesmo tempo que juntos passam da capacidade | Só um passa; o outro recebe 422 BOOKING_CAPACITY_EXCEEDED | `BookingEndpointsTests.Acceptance_Should_LetOnlyOneOfTwoSimultaneousAcceptsTakeTheLastSpot` (integração) | ✅ Passou |
| CT-80 | Aceite depois do prazo de resposta | 422 BOOKING_RESPONSE_EXPIRED, e a reserva expira | `AcceptBookingCommandHandlerTests.HandleAsync_Should_ExpireAndReturnResponseExpired_When_TheHostAcceptsAfterTheDeadline` (unitário)<br>`BookingTransitionsTests.Accept_Should_ReturnResponseExpired_When_MoreThan24HoursPassed` (domínio) | ✅ Passou |
| CT-81 | Tutor suspenso depois do pedido; o anfitrião tenta aceitar | 422 BOOKING_OWNER_INACTIVE, sem gravar nem mandar e-mail | `BookingEndpointsTests.Accept_Should_Return422AndSendNoEmail_When_TheOwnerWasSuspendedAfterTheRequest` (integração) | ✅ Passou |
| CT-82 | Sem resposta em 24h; aceita e não paga em 24h | A tarefa marca expired e libera a vaga | `BookingEndpointsTests.Expiration_Should_CloseRequestsWithoutAnswerAfter24Hours` (integração)<br>`BookingEndpointsTests.Expiration_Should_ExpireAnAcceptedBooking_When_TheOwnerDoesNotPayIn24Hours` (integração) | ✅ Passou |
| CT-83 | Tutor cancela com 48h ou mais; com menos de 48h | Reembolso de 100%; de 50% do pago | `BookingEndpointsTests.Cancellation_Should_RefundHalf_When_OwnerCancelsAConfirmedStayOnTheDay` (integração)<br>`PaymentEndpointsTests.Cancellation_Should_RefundHalfAndTransferTheHostShareOfTheRest_When_TheOwnerCancelsOnTheDay` (integração)<br>_100% com 48h ou mais: coberto nos testes de domínio do reembolso._ | ✅ Passou |
| CT-84 | Anfitrião cancela; admin cancela sem motivo | 100% ao tutor; 400 em reason | `BookingEndpointsTests.Cancellation_Should_RefundEverything_When_HostCancels` (integração)<br>`BookingEndpointsTests.AdminCancellation_Should_RequireAReason_And_RefundInFull_When_TheStayStartsToday` (integração) | ✅ Passou |
| CT-85 | Cancelar depois do check-in | 422 BOOKING_CANNOT_CANCEL_AFTER_CHECK_IN | `BookingTransitionsTests.Cancel_Should_ReturnCannotCancelAfterCheckIn_When_PetAlreadyArrived` (domínio)<br>`BookingTransitionsTests.Cancel_Should_ReturnCannotCancelAfterCheckIn_When_StayIsCompleted` (domínio)<br>_Coberto por teste de domínio._ | ✅ Passou |
| CT-86 | Check-in antes do dia de chegada; no dia de saída | 422 BOOKING_CHECK_IN_TOO_EARLY; BOOKING_CHECK_IN_WINDOW_CLOSED | `BookingEndpointsTests.CheckIn_Should_Return422TooEarly_When_CheckInIsTomorrow` (integração)<br>`BookingTransitionsTests.CheckIn_Should_ReturnWindowClosed_When_CheckOutDayArrived` (domínio) | ✅ Passou |
| CT-87 | Reserva paga chega ao dia de saída sem check-in | Cancelada pela plataforma com 50% de reembolso | `BookingEndpointsTests.OverdueStays_Should_CloseTheNoShow_RemindTheHost_And_CheckOutAfterTheGrace` (integração) | ✅ Passou |
| CT-88 | Estadia sem check-out | Lembrete no dia de saída; conclusão automática 3 dias depois, com o repasse liberado | `BookingEndpointsTests.OverdueStays_Should_CloseTheNoShow_RemindTheHost_And_CheckOutAfterTheGrace` (integração)<br>`OverdueStayTests.CloseOverdueStay_Should_RemindOnceAndThenCheckOut_When_TheHostForgetsTheCheckOut` (domínio) | ✅ Passou |
| CT-89 | Contato antes e depois do pagamento | Endereço e telefone só a partir de confirmed; o admin não vê | `BookingEndpointsTests.Contact_Should_AppearOnlyAfterConfirmation_WithAddressForTheOwnerAndPhoneForBoth` (integração) | ✅ Passou |
| CT-90 | Tutor e anfitrião mexem na mesma reserva ao mesmo tempo | Quem grava depois recebe 409 BOOKING_CONFLICT | `BookingsUnitOfWorkTests.SaveChangesAsync_Should_ReturnConflict_When_TheOwnerAndTheHostChangeTheSameBookingAtTheSameTime` (integração)<br>`AcceptBookingCommandHandlerTests.HandleAsync_Should_ReturnConflict_When_SomeoneChangedTheBookingMeanwhile` (unitário) | ✅ Passou |
| CT-91 | Pedido sem careTermsAccepted ou com false | 400 em careTermsAccepted | `CreateBookingCommandValidatorTests.Validate_Should_RequireTheCareTerms_When_TheOwnerDidNotConfirm` (unitário)<br>`BookingEndpointsTests.Create_Should_RecordTheCareTerms_When_TheOwnerConfirms` (integração) | ✅ Passou |
| CT-92 | Tutor pede estadia no cantinho da própria conta de anfitrião (mesmo e-mail) | 422 BOOKING_OWN_LISTING | `BookingEndpointsTests.Create_Should_Return422_When_TheListingBelongsToTheOwnersOwnHostAccount` (integração) | ✅ Passou |
| CT-93 | Pedido parado a 4h do fim do prazo; aceite sem pagamento a 6h do fim | Um lembrete ao anfitrião e um ao tutor, uma vez cada | `BookingEndpointsTests.Deadlines_Should_RemindTheHostToAnswerAndThenTheOwnerToPay_OnceEach` (integração) | ✅ Passou |
| CT-94 | Admin lista reservas por tutor e status; anfitrião tenta a lista do admin | Só as que atendem aos filtros, sem o contato; 403 | `BookingEndpointsTests.AdminList_Should_FilterBookings_AndBeOnlyForTheAdmin` (integração) | ✅ Passou |
| CT-95 | Nota do tutor no pedido | ownerRating aparece para o anfitrião e o admin, nunca para o tutor | `BlindReviewsAndHistoryTests.Booking_Should_ShowTheOwnerRatingToTheHostOnly_When_TheOwnerWasReviewed` (integração)<br>`BookingOwnerRatingTests.BuildAsync_Should_LeaveTheRatingOutWithoutAskingReviews_When_TheOwnerReads` (unitário) | ✅ Passou |

### Pagamento (Payments)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-96 | Escolher 3 parcelas e pagar a 1ª com Pix | Webhook confirma; a reserva vira confirmed | `PaymentEndpointsTests.Payment_Should_ConfirmByWebhook_TransferOnCheckOut_AndTransferLaterInstallmentsWhenPaid` (integração) | ✅ Passou |
| CT-97 | Pagar com o cartão aprovado; com o cartão recusado | Parcela paga; parcela continua a pagar, e o app pode tentar de novo | `PaymentEndpointsTests.Payment_Should_ConfirmByWebhook_TransferOnCheckOut_AndTransferLaterInstallmentsWhenPaid` (integração)<br>`PaymentLifecycleTests.ApplyCharge_Should_ReturnToPendingWithTheFailure_When_ProcessingPaymentFails` (domínio)<br>_Cartão recusado: coberto por teste de domínio._ | ✅ Passou |
| CT-98 | Reserva de R$ 99 em 2x; de R$ 121 em 3x | 422 PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED (mínimo de R$ 50 por parcela) | `PaymentEndpointsTests.Plan_Should_Return422_When_InstallmentsWouldBeBelowFiftyReais` (integração) | ✅ Passou |
| CT-99 | O mesmo webhook entregue duas vezes; webhook com assinatura errada | Tratado uma vez só; 400 | `PaymentEndpointsTests.Payment_Should_ConfirmByWebhook_TransferOnCheckOut_AndTransferLaterInstallmentsWhenPaid` (integração)<br>`PaymentEndpointsTests.Webhook_Should_Return400_When_TheSignatureIsWrong` (integração)<br>_O evento repetido é conferido dentro do teste do fluxo._ | ✅ Passou |
| CT-100 | Trocar o plano depois de pagar; pagar fora de ordem | 422 PAYMENT_PLAN_LOCKED; 422 PAYMENT_INSTALLMENT_OUT_OF_ORDER | `ChoosePaymentPlanCommandHandlerTests.HandleAsync_Should_ReturnPlanLocked_When_AnInstallmentWasPaid` (unitário)<br>`PaymentEndpointsTests.Session_Should_Return422_When_AnEarlierInstallmentIsOpen` (integração) | ✅ Passou |
| CT-101 | Pagar a 1ª parcela depois do prazo | 422 PAYMENT_WINDOW_CLOSED, e a reserva expira | `PaymentPlanTests.EnsurePayable_Should_ReturnWindowClosed_When_FirstInstallmentIsPastTheDeadline` (domínio)<br>`PaymentEndpointsTests.Expiration_Should_CancelTheOpenCharge_When_TheOwnerDoesNotPayIn24Hours` (integração) | ✅ Passou |
| CT-102 | Parcela vencida antes do check-in, passadas as 24h de tolerância | Reserva cancelada pela plataforma, com 100% de volta | `PaymentEndpointsTests.Maintenance_Should_CancelTheBookingAndRefund_When_AnInstallmentIsMissedBeforeCheckIn` (integração) | ✅ Passou |
| CT-103 | Parcela vencida depois do check-in | Multa de 2% e juros de 1% ao mês; lembrete semanal por 4 semanas; o tutor não pede outra estadia até pagar | `PaymentEndpointsTests.Maintenance_Should_MarkOverdueAndBlockNewStays_When_AnInstallmentIsMissedAfterCheckIn_UntilItIsPaid` (integração)<br>`PaymentEndpointsTests.OverduePayment_Should_ChargeTheFineAndInterest_AndTransferNinetyPercentOfWhatWasPaid` (integração)<br>`PaymentLateFeeTests.TakeOverdueReminders_Should_RemindEverySevenDays_When_TheInstallmentStaysOverdue` (domínio)<br>`PaymentLateFeeTests.TakeOverdueEscalations_Should_EscalateOnceAndStopTheReminders_When_FourWeeksHavePassed` (domínio) | ✅ Passou |
| CT-104 | Check-out com parcelas pagas | 90% repassado ao anfitrião, 10% retidos | `PaymentEndpointsTests.Payment_Should_ConfirmByWebhook_TransferOnCheckOut_AndTransferLaterInstallmentsWhenPaid` (integração) | ✅ Passou |
| CT-105 | Cartão contestado, e a contestação perdida depois do repasse | Repasse estornado do anfitrião; pendência dispute_lost e alerta ao admin | `PaymentEndpointsTests.Dispute_Should_HoldThenReverseTheHostTransfer_When_TheOwnerWinsTheChargeback` (integração)<br>`PaymentReconcilerTests.ReconcileAsync_Should_ReverseTheTransfer_When_DisputeIsLostAfterThePayout` (unitário) | ✅ Passou |
| CT-106 | Stripe bloqueia os repasses do anfitrião com reserva em aberto | Repasse retido (payout_blocked); sai sozinho quando o Stripe libera | `PaymentReconcilerTests.ReconcileAsync_Should_HoldThePayoutAndAlertOnce_When_StripeBlockedTheHostPayouts` (unitário)<br>`PaymentReconcilerTests.ReconcileAsync_Should_TransferTheHeldPayout_When_StripeReleasesTheHostAgain` (unitário)<br>_Coberto por testes unitários._ | ✅ Passou |
| CT-107 | Webhook perdido | A sincronização pedida pelo app ou a manutenção (até 15 min) relê a cobrança e confirma | `PaymentEndpointsTests.Synchronization_Should_ConfirmTheBookingWithoutTheWebhook_When_TheOwnerComesBackFromTheCheckout` (integração)<br>`PaymentEndpointsTests.Maintenance_Should_ConfirmTheBooking_When_TheOwnerPaidButTheWebhookNeverArrived` (integração) | ✅ Passou |

### Chat (Conversations)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-108 | Tutor puxa conversa duas vezes; dez pedidos simultâneos | 201 e depois 200; uma conversa só | `ConversationsControllerTests.Start_Should_Return201AndThenTheSameConversationWith200_When_TheOwnerTapsTwice` (integração)<br>`ConversationsControllerTests.Start_Should_CreateASingleConversation_When_TwoRequestsArriveAtTheSameTime` (integração) | ✅ Passou |
| CT-109 | Anfitrião ou admin tenta puxar conversa; cantinho fora da busca | 403 FORBIDDEN; 404 CONVERSATION_LISTING_NOT_FOUND | `ConversationsControllerTests.Start_Should_Return403_When_TheTokenIsFromAHost` (integração)<br>`ConversationsControllerTests.Start_Should_Return404_When_TheListingDoesNotExist` (integração) | ✅ Passou |
| CT-110 | Tutor puxa conversa no cantinho da própria conta de anfitrião | 422 CONVERSATION_OWN_LISTING | `StartConversationCommandHandlerTests.HandleAsync_Should_ReturnOwnListing_When_TheHostAccountIsTheSamePerson` (unitário)<br>_Coberto por teste unitário._ | ✅ Passou |
| CT-111 | Mensagem de texto | 201; o outro lado recebe messageCreated e o contador sobe | `ConversationsControllerTests.Messages_Should_ReachTheOtherSideAndClearTheUnreadCount_When_TheHostReads` (integração)<br>`ConversationsHubTests.Hub_Should_PushTheMessageAndTheUnreadCountToTheHost_When_TheOwnerSends` (integração) | ✅ Passou |
| CT-112 | Texto vazio, com 2001 caracteres ou com caractere nulo | 400 no campo body | `ConversationsControllerTests.Send_Should_Return400WithTheBodyField_When_TheTextIsBlank` (integração)<br>`ConversationsControllerTests.Send_Should_Return400OnTheBodyField_When_TheTextHasANullCharacter` (integração)<br>`SendMessageCommandValidatorTests.Validate_Should_ReturnBodyTooLong_When_TheTextIsLongerThanTheLimit` (unitário) | ✅ Passou |
| CT-113 | Conversa de outra pessoa | 404 CONVERSATION_NOT_FOUND; nenhum evento chega a quem não participa | `ConversationsControllerTests.Conversation_Should_Return404ToEverybodyElse_When_AnotherOwnerTriesToUseIt` (integração) | ✅ Passou |
| CT-114 | Escrever para quem foi suspenso; remetente suspenso | 403 CONVERSATION_RECIPIENT_SUSPENDED, nada vai ao bucket; 403 CONVERSATION_OWNER_DEACTIVATED ou HOST_DEACTIVATED, mas o histórico continua legível | `ConversationsControllerTests.Send_Should_Return403RecipientSuspended_When_TheHostWritesToASuspendedOwner` (integração)<br>`ConversationsControllerTests.SendPhoto_Should_Return403RecipientSuspendedWithoutUploading_When_TheOwnerWritesToASuspendedHost` (integração)<br>`ConversationsControllerTests.Send_Should_Return403_When_TheOwnerIsSuspended` (integração) | ✅ Passou |
| CT-115 | Foto com GPS no EXIF; GIF; arquivo de 9 MB | Metadados removidos; 400; 413 | `ImageMetadataStripperTests.Strip_Should_RemoveExifAndKeepOnlyTheOrientation_When_JpegHasGpsData` (unitário)<br>`ConversationsControllerTests.SendPhoto_Should_Return400OnTheFileField_When_TheFileIsNotAnImage` (integração)<br>`PetPhotoTests.AddPhoto_Should_Return413InTheEnvelope_When_BodyIsOverTheLimit` (integração)<br>_O upload é a mesma peça compartilhada (ImageUploadEndpoint) em todas as rotas de imagem._ | ✅ Passou |
| CT-116 | 31ª mensagem no mesmo minuto | 429 TOO_MANY_REQUESTS | `ConversationsRateLimitingTests.Send_Should_Return429InTheEnvelope_When_TextAndPhotosGoOverTheChatLimit` (integração) | ✅ Passou |
| CT-117 | Envio durante estadia em andamento | A mensagem leva o bookingId | `ConversationsControllerTests.Send_Should_LinkTheMessageToTheStay_When_TheBookingIsInProgress` (integração) | ✅ Passou |
| CT-118 | Página com before; before de outra conversa | Anteriores ao cursor, sem repetir nem pular; 400 em before | `ConversationsControllerTests.Messages_Should_PageFromTheNewestWithTheCursor_When_ThereAreMoreThanAPage` (integração)<br>`ConversationsControllerTests.Messages_Should_Return400OnBefore_When_TheCursorIsNotAMessageOfTheConversation` (integração) | ✅ Passou |
| CT-119 | Marcar como lida duas vezes | { total } atualizado; a segunda chamada não muda nada | `ConversationsHubTests.Hub_Should_PushTheNewTotal_When_TheReaderMarksTheConversationAsRead` (integração)<br>`ConversationsControllerTests.Messages_Should_ReachTheOtherSideAndClearTheUnreadCount_When_TheHostReads` (integração) | ✅ Passou |
| CT-120 | Anfitrião ainda não aprovado abre a lista e o contador | Lista vazia e 0 | `ConversationsControllerTests.Inbox_Should_ReturnAnEmptyListAndZero_When_TheHostIsNotApprovedYet` (integração) | ✅ Passou |
| CT-121 | 20 envios simultâneos na mesma conversa | Todas as mensagens gravadas; last_message_at termina na mais nova | `ConversationsControllerTests.Send_Should_KeepTheLastActivityAtTheNewestMessage_When_BothSidesSendAtTheSameTime` (integração)<br>`ConversationsUnitOfWorkTests.RunLockedOnConversationAsync_Should_MakeTheSecondRequestWait_When_TheFirstHoldsTheLock` (integração) | ✅ Passou |
| CT-122 | Um lado bloqueia; o outro tenta enviar, desbloquear e bloquear por cima | 422 CONVERSATION_BLOCKED (os dois continuam lendo); 403 CONVERSATION_UNBLOCK_FORBIDDEN; CONVERSATION_ALREADY_BLOCKED; os dois recebem conversationBlockChanged | `ConversationModerationTests.Block_Should_StopBothSidesFromSendingButLetThemRead_When_TheHostBlocks` (integração)<br>`ConversationModerationTests.Unblock_Should_BeOnlyForWhoBlocked_When_TheOtherSideTries` (integração)<br>`ConversationsHubTests.Hub_Should_PushTheBlockToTheOtherSide_When_TheHostBlocksAndUnblocks` (integração) | ✅ Passou |
| CT-123 | Quem recebeu denuncia a mensagem; o remetente denuncia a própria; a mesma de novo | 201 e e-mail ao admin sem o texto; CONVERSATION_REPORT_OWN_MESSAGE; 409 CONVERSATION_REPORT_ALREADY_EXISTS | `ConversationModerationTests.Report_Should_EnterTheAdminQueue_When_TheOwnerReportsAMessageFromTheHost` (integração)<br>`ConversationModerationTests.Report_Should_BeRefused_When_ItIsTheOwnMessageARepeatAnotherConversationOrMissingTheDescription` (integração)<br>`ConversationModerationTests.Report_Should_EmailTheAdminWithoutTheMessage_When_ANewReportArrives` (integração) | ✅ Passou |
| CT-124 | Admin julga a denúncia procedente; um lado tenta desbloquear | Conversa bloqueada pela moderação (422 CONVERSATION_BLOCKED_BY_MODERATION); o desbloqueio do lado não tira; o admin tira com DELETE .../moderation-block | `ConversationModerationTests.Upheld_Should_BlockTheConversationForBothSidesForGood_When_TheAdminUpholdsAReport` (integração)<br>`ConversationModerationTests.LiftModerationBlock_Should_LetBothSidesSendAgain_When_TheAdminLiftsIt` (integração) | ✅ Passou |
| CT-125 | Mensagem sem ler há 15 minutos; outra 30 minutos depois | Um resumo por e-mail, sem o texto; o segundo só 60 minutos depois do primeiro | `UnreadEmailTests.Notify_Should_EmailTheHostASummaryWithoutTheText_When_AMessageIsUnreadFor15Minutes` (integração)<br>`UnreadEmailTests.Notify_Should_NotRepeat_When_NothingNewArrivedOrTheCooldownHasNotPassed` (integração) | ✅ Passou |
| CT-126 | Conta que desligou o aviso; perfil suspenso | Nenhum e-mail; a pendência sai | `UnreadEmailTests.NotificationSettings_Should_TurnTheEmailOffForThatAccountOnly_When_TheHostDisablesIt` (integração)<br>`UnreadEmailTests.Notify_Should_NotEmail_When_TheHostIsSuspended` (integração) | ✅ Passou |
| CT-127 | Conversa parada há 91 dias sem reserva em aberto; outra bloqueada ou com denúncia pendente | A primeira é apagada com mensagens e fotos; a de 89 dias e as bloqueadas ou denunciadas ficam | `ConversationRetentionTests.Purge_Should_DeleteTheConversationItsMessagesAndItsPhotos_When_ItIsInactiveForMoreThan90Days` (integração)<br>`ConversationRetentionTests.Purge_Should_KeepTheConversation_When_ThereWasActivityInsideThePeriod` (integração)<br>`ConversationRetentionTests.Purge_Should_KeepTheConversation_When_ItIsBlocked` (integração)<br>`ConversationRetentionTests.Purge_Should_KeepTheConversationUntilTheAdminAnalysesTheReport_When_AReportIsPending` (integração) | ✅ Passou |
| CT-128 | Hub: conectar sem token, com token adulterado ou de admin | 401 / 401 / 403 | `ConversationsHubTests.Hub_Should_RefuseTheConnection_When_ThereIsNoToken` (integração)<br>`ConversationsHubTests.Hub_Should_Return401_When_TheTokenWasTampered` (integração)<br>`ConversationsHubTests.Hub_Should_Return403_When_TheTokenIsFromAnAdmin` (integração) | ✅ Passou |
| CT-129 | Hub: o token vence com o socket aberto | O servidor fecha a conexão | `ConversationsHubTests.Hub_Should_CloseTheConnection_When_TheAccessTokenExpires` (integração) | ✅ Passou |
| CT-130 | Hub: tutor conectado numa instância da API e anfitrião em outra | A mensagem chega pelo backplane no Redis | `ConversationsHubTests.Hub_Should_PushTheMessageThroughTheRedisBackplane_When_TheHostIsConnectedToAnotherInstance` (integração) | ✅ Passou |

### Avaliações (Reviews)

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-131 | Tutor avalia a estadia concluída | 201; a avaliação fica guardada e só o autor a lê | `ReviewsControllerTests.Reviews_Should_FollowTheWholeLifecycle_When_BothSidesReviewACompletedStay` (integração) | ✅ Passou |
| CT-132 | O anfitrião também avalia, com nota de cada pet | As duas são publicadas na hora; cada avaliado recebe o e-mail sem o texto | `ReviewsControllerTests.Reviews_Should_FollowTheWholeLifecycle_When_BothSidesReviewACompletedStay` (integração)<br>`BlindReviewsAndHistoryTests.PetHistory_Should_ShowThePetRatings_When_TheHostRatedThePetAndBothReviewsArePublished` (integração) | ✅ Passou |
| CT-133 | Só um lado avalia, e o prazo de 7 dias acaba | Publicada sozinha; a tarefa avisa o avaliado uma vez | `BlindReviewsAndHistoryTests.BlindReview_Should_BePublishedAndNotifiedByTheTask_When_TheWindowEndsWithOnlyOneSide` (integração) | ✅ Passou |
| CT-134 | Reserva sem check-out; depois de 7 dias; segunda avaliação do mesmo lado, mesmo com a primeira apagada | 422 REVIEW_BOOKING_NOT_COMPLETED; 422 REVIEW_WINDOW_CLOSED; 409 REVIEW_ALREADY_EXISTS | `ReviewsControllerTests.Create_Should_Return422_When_TheStayHasNotEndedYet` (integração)<br>`ReviewsControllerTests.Create_Should_Return422_When_TheCheckOutWasMoreThanSevenDaysAgo` (integração)<br>`ReviewsControllerTests.Create_Should_Return409_When_TheBookingIsReviewedTwice` (integração) | ✅ Passou |
| CT-135 | Nota 0 ou 6; tutor manda pets; pet fora da reserva | 400 em rating; 400 em pets | `ReviewsControllerTests.Create_Should_Return400WithTheFields_When_ThePayloadIsInvalid` (integração)<br>`ReviewsBothSidesTests.HostReview_Should_Return400_When_ThePetIsNotFromTheStay` (integração) | ✅ Passou |
| CT-136 | Editar depois da publicação | 422 REVIEW_ALREADY_PUBLISHED | `ReviewPublicationTests.Update_Should_ReturnAlreadyPublished_When_TheReviewWasPublished` (domínio)<br>_Coberto por teste de domínio._ | ✅ Passou |
| CT-137 | Avaliação ainda não publicada, lida por quem não é o autor | 404 REVIEW_NOT_FOUND; fora da lista, da média e do selo | `ReviewQueriesHandlersTests.GetReviewById_Should_ReturnNotFound_When_TheReviewIsNotPublishedYet` (unitário)<br>`ReviewsControllerTests.Reviews_Should_FollowTheWholeLifecycle_When_BothSidesReviewACompletedStay` (integração) | ✅ Passou |
| CT-138 | Outro tutor abre a avaliação que um anfitrião escreveu de um tutor; tutor chama /owners/{id}/reviews | 404 REVIEW_NOT_FOUND; 403 | `ReviewsBothSidesTests.HostReview_Should_BeVisibleOnlyToHostsAndTheOwner_When_TheHostReviewsTheOwnerAndThePet` (integração)<br>`ReviewQueriesHandlersTests.GetReviewById_Should_ReturnNotFound_When_AnonymousReadsTheHostsReview` (unitário) | ✅ Passou |
| CT-139 | Lista e nota de um anfitrião sem login | Só avaliações publicadas, com o autor pelo primeiro nome; { average, count } | `ReviewsControllerTests.Reviews_Should_FollowTheWholeLifecycle_When_BothSidesReviewACompletedStay` (integração)<br>`ReviewQueriesHandlersTests.GetReviewById_Should_ReturnThePublicView_When_AnonymousReadsTheOwnersReview` (unitário) | ✅ Passou |
| CT-140 | O avaliado responde; o autor tenta responder | Resposta pública; 404 REVIEW_NOT_FOUND | `ReviewsBothSidesTests.Reply_Should_ShowPublicly_When_TheHostAnswersTheOwnersReview` (integração)<br>`ReplyHandlersTests.Reply_Should_ReturnNotFound_When_TheAuthorTriesToAnswerHerOwnReview` (unitário) | ✅ Passou |
| CT-141 | Denúncia julgada procedente | Avaliação oculta: some da lista, da média e do selo; o autor vê o motivo; as outras denúncias pendentes fecham juntas | `ReviewsBothSidesTests.Moderation_Should_HideAndShowTheReview_When_TheAdminUpholdsAReportAndThenUndoesIt` (integração) | ✅ Passou |
| CT-142 | Admin oculta direto e depois volta a mostrar | Some e volta a contar na média | `ReviewsBothSidesTests.Moderation_Should_HideDirectlyUpholdingEveryPendingReport_When_TheAdminHidesWithAReason` (integração)<br>`ReviewsBothSidesTests.Moderation_Should_HideAndShowTheReview_When_TheAdminUpholdsAReportAndThenUndoesIt` (integração) | ✅ Passou |
| CT-143 | O autor apaga uma avaliação denunciada | Denúncias pendentes fecham como closed; esse lado não avalia a reserva de novo | `ReviewsBothSidesTests.Moderation_Should_CloseThePendingReports_When_TheAuthorDeletesTheReview` (integração) | ✅ Passou |
| CT-144 | Ninguém avaliou 24h depois do check-out | Um lembrete por lado, uma vez só | `ReviewsBothSidesTests.Reminders_Should_EmailBothSidesOnce_When_NobodyReviewedADayAfterCheckOut` (integração)<br>`ReviewsBothSidesTests.Reminders_Should_SkipTheSideThatReviewed_When_OneSideAlreadyReviewed` (integração) | ✅ Passou |
| CT-145 | 21ª escrita em 15 minutos; 11ª denúncia na mesma hora | 429 nos dois; ler continua liberado | `ReviewsRateLimitingTests.Writes_Should_Return429PerAccountAndLeaveReadingFree_When_TheAuthorGoesOverTheLimit` (integração)<br>`ReviewsRateLimitingTests.Reports_Should_Return429_When_TheAccountGoesOverTheReportLimit` (integração) | ✅ Passou |

### Auditoria, admin e segurança

| ID | Cenário | Resultado esperado | Testes automatizados | Resultado |
|---|---|---|---|---|
| CT-146 | Admin suspende um tutor | Sessões caem na hora; reservas que não começaram são canceladas sem reembolso ao tutor; trilha com o motivo | `BookingEndpointsTests.OwnerSuspension_Should_CancelThePaidBookingWithoutRefund_When_AdminSuspendsTheOwner` (integração)<br>`OwnerAdminTests.Suspend_Should_BlockTheOwnerImmediately_When_AdminSuspends` (integração)<br>`OwnerAdminTests.Audit_Should_RecordWhoSuspendedAndWhy_When_AdminSuspends` (integração) | ✅ Passou |
| CT-147 | Admin consulta a trilha por action e período; tutor tenta consultar | Página filtrada e audit.searched registrado; 403 | `OwnerAdminTests.Audit_Should_RecordLoginsAndAdminReads_When_TheyHappen` (integração)<br>`OwnerAdminTests.AdminRoutes_Should_Return403_When_CalledByAnOwner` (integração)<br>`SearchAuditEntriesQueryHandlerTests.HandleAsync_Should_RecordTheSearchItself_When_Called` (unitário) | ✅ Passou |
| CT-148 | Trilha com from depois de to e pageSize=101 | 400 com os dois campos | `OwnerAdminTests.Audit_Should_Return400_When_PageSizeIsTooBig` (integração)<br>`SearchAuditEntriesQueryHandlerTests.HandleAsync_Should_ReturnEveryError_When_PagingAndPeriodAreInvalid` (unitário) | ✅ Passou |
| CT-149 | Rota sem token; papel errado; id de outra pessoa | 401; 403; 404 | `AccountEndpointsTests.GetMe_Should_Return401_When_NotLoggedIn` (integração)<br>`PetEndpointsTests.Register_Should_Return403_When_AccountIsAdmin` (integração)<br>`BookingEndpointsTests.GetById_Should_Return404_When_CallerIsAnotherOwner` (integração)<br>_Repetido em todos os módulos (401, 403 e 404 por rota)._ | ✅ Passou |
| CT-150 | Rota inexistente, verbo ou content-type errado | 404, 405, 415, sempre no envelope | `PetEndpointsTests.UnknownRoute_Should_Return404InTheEnvelope_When_RouteDoesNotMatch` (integração)<br>`PetEndpointsTests.WrongMethod_Should_Return405InTheEnvelope_When_RouteExistsWithAnotherMethod` (integração)<br>`OwnerAvatarTests.ChangeAvatar_Should_Return415SayingMultipart_When_ImageIsSentAsRawBody` (integração) | ✅ Passou |
| CT-151 | JSON malformado; valor de tipo errado | 400 em body; 400 no campo | `AuthEndpointsTests.AnyEndpoint_Should_Return400_When_BodyIsMalformedJson` (integração)<br>`AccountEndpointsTests.Register_Should_PointToTheField_When_ValueHasWrongType` (integração) | ✅ Passou |
| CT-152 | Falha inesperada | 500 com só o traceId | `GlobalExceptionHandlerTests.TryHandleAsync_Should_Return500WithOnlyTheTraceId_When_AnExceptionEscapes` (unitário)<br>`HostStripeOnboardingApiTests.Webhook_Should_Return500SoStripeRetries_When_StripeIsDownOnTheReread` (integração) | ✅ Passou |
| CT-153 | Logs depois de um fluxo completo | Nenhum token, senha, CPF ou texto de mensagem nos logs | `LogPrivacyTests.Logs_Should_NotContainPasswordsTokensCpfOrMessageText_When_AFullFlowRuns` (integração)<br>_Nos níveis configurados da API. Abaixo deles (Trace do ASP.NET Core) a senha e o CPF aparecem: ver F-07._ | ✅ Passou |
| CT-154 | Um módulo referencia outro, ou o Domain depende de outra camada | O teste de arquitetura falha e o CI bloqueia o merge | `ConversationsModuleBoundaryTests.ConversationsModule_Should_NotReferenceOtherModules` (arquitetura)<br>`BookingsModuleBoundaryTests.BookingsDomain_Should_NotDependOnFrameworksOrOuterLayers` (arquitetura)<br>_84 testes de arquitetura (NetArchTest), um conjunto por módulo; o CI exige o check verde para o merge._ | ✅ Passou |
| CT-155 | Preflight OPTIONS vindo da origem do front; de uma origem fora da lista | Headers de CORS com credenciais e Location/Retry-After expostos; sem os headers, e o navegador bloqueia | `CorsTests.Preflight_Should_AllowTheFrontWithCredentials_When_OriginIsConfigured` (integração)<br>`CorsTests.Request_Should_CarryCorsHeadersAndExposeLocation_When_OriginIsConfigured` (integração)<br>`CorsTests.Request_Should_NotCarryCorsHeaders_When_OriginIsNotConfigured` (integração) | ✅ Passou |
| CT-156 | GET /support sem login | { email } da configuração | `SupportEndpointTests.Support_Should_ReturnTheConfiguredEmailWithoutLogin_When_ItIsSet` (integração) | ✅ Passou |

## 6. Regras de negócio (seção 6.3)

As 17 regras que a Entrega 2 diz que os testes precisam garantir, com os casos que as verificam. Todas têm teste automatizado aprovado; além dos casos listados, cada regra tem testes de domínio próprios.

| # | Regra | Casos de teste | Resultado |
|---:|---|---|---|
| 1 | Capacidade: em nenhuma noite a soma dos pets passa da capacidade | CT-77, CT-79 | ✅ |
| 2 | Valor = diária × noites × pets, com a diária copiada no pedido | CT-73 | ✅ |
| 3 | Prazos de pedido, resposta e pagamento, com os lembretes | CT-74, CT-80, CT-82, CT-93, CT-101 | ✅ |
| 4 | Reembolso de 100%, 50% ou 0% conforme quem cancela e quando | CT-40, CT-83, CT-84, CT-87, CT-102, CT-146 | ✅ |
| 5 | Parcelas de 1 a 6, mínimo de R$ 50, plano travado depois de pagar, multa e juros | CT-98, CT-100, CT-103 | ✅ |
| 6 | Repasse de 90% ao anfitrião e 10% à plataforma | CT-103, CT-104, CT-105 | ✅ |
| 7 | Só aparece na busca o cantinho de anfitrião verificado, ativo e com repasses; ninguém reserva nem conversa no próprio cantinho | CT-54, CT-63, CT-92, CT-110 | ✅ |
| 8 | Endereço exato e telefone só depois do pagamento; ponto aproximado a menos de 500 m | CT-59, CT-68, CT-69, CT-89 | ✅ |
| 9 | Recurso de outra pessoa responde 404 | CT-49, CT-50, CT-113, CT-149 | ✅ |
| 10 | Um CPF só em contas do mesmo e-mail; CPF bloqueado guardado só como HMAC | CT-04, CT-31, CT-32 | ✅ |
| 11 | Troca de senha, inativação, exclusão e suspensão derrubam as sessões | CT-12, CT-15, CT-20, CT-22, CT-146 | ✅ |
| 12 | Chat: uma conversa por dupla, suspenso não envia, bloqueio dos dois lados | CT-108, CT-114, CT-122 | ✅ |
| 13 | Retenção das fotos da verificação e das conversas paradas | CT-25, CT-34, CT-127 | ✅ |
| 14 | Avaliação às cegas, publicada não muda, uma por lado | CT-131 a CT-137 | ✅ |
| 15 | Moderação: procedente bloqueia a conversa ou oculta a avaliação | CT-124, CT-141, CT-142 | ✅ |
| 16 | Exclusão da conta: anonimiza, apaga fotos e conversas, mantém reservas e trilha | CT-22, CT-23, CT-43 | ✅ |
| 17 | E-mails sem o texto de mensagem, avaliação ou denúncia; lembretes uma vez só | CT-123, CT-125, CT-132, CT-144 | ✅ |

## 7. Validações esperadas (seção 6.4)

| Validação | Como é verificada | Resultado |
|---|---|---|
| Erros de formato juntos num `400 VALIDATION_ERROR`, por campo em camelCase | CT-02, CT-26, CT-112, CT-135; validadores de cada caso de uso | ✅ |
| JSON malformado ou corpo vazio volta 400 em `body`; tipo errado aponta o campo | CT-151 (`AuthEndpointsTests`, `AccountEndpointsTests`) | ✅ |
| Status pelo sufixo do código de erro | `ErrorCodeStatusMapperTests` (Shared.Infrastructure) e as rotas de cada módulo | ✅ |
| Todo código de erro existe no OpenAPI e cada regra de validação tem código próprio | `ErrorCodeCoverageTests` (arquitetura) e `OpenApiErrorCodesTests` (integração) | ✅ |
| Datas `yyyy-MM-dd` e "hoje" em Brasília | CT-74, `StayPeriodTests`, `HostDashboardTests.Handle_Should_UseBrasiliaMonth_When_UtcIsAlreadyNextMonth` | ✅ |
| Imagens conferidas pelo conteúdo (JPEG, PNG, WebP até 5 MB), erro em `file` | CT-27, CT-48, CT-115 e os testes de foto de cada módulo | ✅ |
| Toda resposta no envelope `{ success, data, error, timestamp }`, sem nulos | `AuthEndpointsTests.EveryResponse_Should_UseTheEnvelope_When_RequestFails`, CT-150 | ✅ |

## 8. Falhas encontradas e correções

Falhas achadas pelos testes, pelo CI ou pela conferência com a Entrega 2, e como foram corrigidas.

| # | Data | Falha | Como foi encontrada | Correção | Teste que garante |
|---|---|---|---|---|---|
| F-01 | 25/10/2026 | A denúncia de mensagem do chat não tinha o limite de 10 por hora por conta planejado na Entrega 2 (seção 4.5); só valia o limite geral de 120 por minuto, e cada denúncia manda e-mail ao admin | Conferência da documentação com o código | Política `reports` na rota `POST /conversations/{id}/messages/{messageId}/report` (`ConversationsController`), documentada no Swagger e nos docs | `ConversationsRateLimitingTests.Report_Should_Return429InTheEnvelope_When_TheAccountGoesOverTheReportLimit` |
| F-02 | 16/10/2026 | Teste instável de recusa de reserva: um e-mail deixado na fila por um teste chegava depois da limpeza do teste seguinte e quebrava as asserções dele | Teste instável: falhava de vez em quando | `ResetAsync` da fixture espera a caixa de saída de e-mails esvaziar antes de limpar (commit `8d7243a`). Conferido com o envio 300 ms mais lento: sem a correção 2 testes falham, com ela 32/32 | Os testes de e-mail de `BookingEndpointsTests` |
| F-03 | 15/10/2026 | Dependência indireta `System.Security.Cryptography.Pkcs` 10.0.0 (via MailKit) com duas vulnerabilidades de negação de serviço (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx); com avisos como erro, o restore do CI quebrava | CI (NU1902/NU1903) | Versão corrigida fixada no `Directory.Packages.props` (commit `7f3a4f3`) | Build do CI |
| F-04 | 21/10/2026 | O Swagger descrevia sucesso e erro com o mesmo schema (`success` sem valor fixo) e sem exemplo por status | Revisão do Swagger | Schemas separados (`ApiResponse` só sucesso, `ApiErrorResponse` nos 4xx/5xx) com exemplo por status (commit `56d4400`) | — (conferido no Swagger) |
| F-05 | 23/10/2026 | Erro de validação só trazia a frase em inglês, sem código estável por regra; o front não tinha como traduzir | O front precisava traduzir os erros pelo código, não pela frase (PROJECT_STANDARDS §7) | Código `MODULO_CAMPO_REGRA` e `params` por regra em `details[].errors` (commit `db16491`) | `ErrorCodeCoverageTests` (falha se uma regra ficar sem código ou se um código for reusado) |
| F-06 | 25/10/2026 | Documentação desatualizada: contagens de testes (Bookings 55→58, Owners 51→52, Hosts no doc de Pets 171→183), tabela de rate limit incompleta e códigos de erro de exemplo que não existem | Conferência da documentação com o código | Docs corrigidos (`PROJECT_STANDARDS.md` e `modulo-*.md`) | — |
| F-07 | 25/10/2026 | No nível Trace, o próprio ASP.NET Core registra os argumentos de cada ação, e com eles a senha e o CPF do cadastro e do login (o `ToString` dos comandos). Nos níveis configurados da API (`Microsoft.AspNetCore` em Information no Development e Warning em produção) esse log não é emitido | Teste novo dos logs (CT-153), rodado primeiro no nível mais baixo (Verbose) | Nenhuma mudança no código: a configuração já impede. O teste passou a usar os níveis do `appsettings` e documenta que `Microsoft.AspNetCore` não pode ir para Trace. Se um dia for preciso, esconder a senha no `ToString` dos comandos | `LogPrivacyTests.Logs_Should_NotContainPasswordsTokensCpfOrMessageText_When_AFullFlowRuns` |
| F-08 | 25/10/2026 | O value object `Password` devolve o erro de senha fraca no campo `password`, mesmo quando o campo da troca de senha é `newPassword`. Na API não aparece, porque o validador barra antes, já no campo certo | Teste novo do handler de troca de senha | Nenhuma: o teste confere o erro de validação sem depender do nome do campo | `ChangePasswordCommandHandlerTests.HandleAsync_Should_ReturnTheRulesWithoutSaving_When_TheNewPasswordIsWeak` |
| F-09 | 25/10/2026 | Corpo acima do limite do Kestrel (30 MB) derrubava a API com `500 UNEXPECTED_ERROR`: a `BadHttpRequestException` do servidor caía no `GlobalExceptionHandler` como exceção inesperada | Roteiro exploratório (B-15 e S-21), contra a API no ar | O `GlobalExceptionHandler` responde com o status da `BadHttpRequestException` no envelope: 413 `PAYLOAD_TOO_LARGE` (os outros, `VALIDATION_ERROR`), com log Warning. | `GlobalExceptionHandlerTests.TryHandleAsync_Should_ReturnTheRequestStatus_When_TheServerRejectedTheRequest` (413 e 400); B-15 do roteiro |
| F-10 | 25/10/2026 | Com a F-09 corrigida, o log da requisição do Serilog ainda registrava o pedido recusado como Error, porque a exceção passa pelo `UseSerilogRequestLogging` antes do handler. Um erro do cliente apareceria como falha do servidor | Roteiro exploratório (L-03), lendo o log da API | `GetLevel` do `UseSerilogRequestLogging` (`Program.cs`): `BadHttpRequestException` vira Warning; outras exceções e 5xx continuam Error | L-03 do roteiro |

## 9. Pendências

A primeira execução (25/10/2026, 10:58) deixou 14 pendências: partes do plano sem teste automatizado e três metas de cobertura não atingidas. Todas foram fechadas nesta rodada:

| # | Pendência | Caso / meta | Como foi fechada |
|---|---|---|---|
| P-01 | `Auth.Application` com 82,1% de branch | meta 85% | Testes unitários da suspensão de conta, da troca de senha (conta inexistente, suspensa, senha fraca), do cadastro (sem endereço, papel e data inválidos, menor de idade), do patch de perfil e da retirada da suspensão |
| P-02 | `Owners.Application` com 82,3% de branch | meta 85% | Testes unitários de suspender e tirar a suspensão (inexistente, falha e exceção na conta, já suspenso), de inativar com exceção na conta e de "meu perfil" |
| P-03 | `Payments.Infrastructure` com 60,4% de linha | meta 70% | `StripePaymentsGatewayTests`: a classe que chama o Stripe, com o SDK de verdade falando com um Stripe de teste por HTTP (cada operação, os parâmetros enviados, a chave de idempotência e a tradução dos erros) |
| P-04 | Access token continua valendo depois do logout | CT-11 | `AccountEndpointsTests.Logout_Should_KeepTheAccessTokenValidUntilItExpires_When_TheRefreshTokenIsRevoked` |
| P-05 | "Esqueci a senha" de conta suspensa | CT-14 | `ForgotPasswordCommandHandlerTests.HandleAsync_Should_SucceedWithoutNotifying_When_AccountIsSuspended` |
| P-06 | Troca de conta para o mesmo papel | CT-17 | `AccountEndpointsTests.Switch_Should_Return404_When_TheTargetIsTheSameRole` |
| P-07 | Dois admins decidindo a mesma solicitação | CT-33 | `HostApplicationFlowTests.Decision_Should_LetOnlyOneAdminWin_When_TwoDecideTheSameApplicationAtTheSameTime` e `Decision_Should_Return409ToTheSecondWrite_When_TwoAdminsLoadedTheSameApplication` |
| P-08 | Expiração da reserva no aceite fora do prazo | CT-80 | `AcceptBookingCommandHandlerTests.HandleAsync_Should_ExpireAndReturnResponseExpired_When_TheHostAcceptsAfterTheDeadline` |
| P-09 | Tutor e anfitrião gravando a mesma reserva | CT-90 | `BookingsUnitOfWorkTests.SaveChangesAsync_Should_ReturnConflict_When_TheOwnerAndTheHostChangeTheSameBookingAtTheSameTime` |
| P-10 | Liberação do repasse quando o Stripe volta a liberar | CT-106 | O teste já existia e não tinha sido ligado ao caso: `PaymentReconcilerTests.ReconcileAsync_Should_TransferTheHeldPayout_When_StripeReleasesTheHostAgain` |
| P-11 | Hub com token adulterado e com token de admin | CT-128 | `ConversationsHubTests.Hub_Should_Return401_When_TheTokenWasTampered` e `Hub_Should_Return403_When_TheTokenIsFromAnAdmin` |
| P-12 | Hub fecha a conexão quando o token vence | CT-129 | `ConversationsHubTests.Hub_Should_CloseTheConnection_When_TheAccessTokenExpires` |
| P-13 | Erro 500 só com o traceId | CT-152 | `GlobalExceptionHandlerTests.TryHandleAsync_Should_Return500WithOnlyTheTraceId_When_AnExceptionEscapes` |
| P-14 | Segredos fora dos logs | CT-153 | `LogPrivacyTests.Logs_Should_NotContainPasswordsTokensCpfOrMessageText_When_AFullFlowRuns` (e a falha F-07) |

**Pendências abertas:** nenhuma.

## 10. Evidências

| Evidência | Onde |
|---|---|
| Resultado de cada um dos 2904 testes desta execução (nome completo e resultado) | [`evidencias/execucao-testes-2026-10-25.txt`](evidencias/execucao-testes-2026-10-25.txt) |
| Resumo da cobertura por projeto e por classe | [`evidencias/cobertura-2026-10-25.txt`](evidencias/cobertura-2026-10-25.txt) |
| Relatório de cobertura em HTML (linha a linha) | `artifacts/coverage/report/index.html`, gerado por `scripts/coverage.sh` (fora do git) |
| Execuções do CI em cada push e PR | Aba **Actions** do repositório no GitHub (`build-and-test`) |
| Histórico das correções | Commits citados na seção 8 (`git show <hash>`) |
| Roteiro exploratório contra a API no ar: cada cenário, o esperado e o obtido | [`evidencias/exploratorio-2026-10-25.md`](evidencias/exploratorio-2026-10-25.md) |

## 11. Roteiro exploratório (seção 6.1)

A Entrega 2 planeja, além dos testes automatizados, um roteiro contra a API rodando: casos de borda, segurança, carga leve e logs, num script C# com cliente HTTP e SignalR. É o `tools/PetHost.ExploratoryQa`: não referencia nenhum projeto da API, vê só o que um cliente vê, e grava cada cenário com o esperado, o obtido e o veredito.

| Área | Cenários | O que confere |
|---|---|---|
| Preparação | P-01 a P-03 | API no ar (`/health/ready`) e login das contas dos dados de teste e do admin |
| Casos de borda | B-01 a B-16 | JSON quebrado, campo faltando e tipo errado; `text/plain`; rota, id e método que não existem; `pageSize` e `page` fora da faixa; datas invertidas e no passado; SQL injection, emoji e HTML na busca; corpo de 31 MB; cadastro com 11 campos inválidos de uma vez |
| Segurança | S-01 a S-21 | Sem token, token com papel trocado, `alg: none` e lixo; papel errado (tutor em rota de admin, anfitrião em rota de tutor); pet de outro tutor (ler e alterar: 404, e o pet não muda); login e "esqueci a senha" iguais para conta existente e inexistente; papel que não bate; SQL injection no login; refresh de uso único; logout; CORS do front e de outra origem; força bruta no login (429 com `Retry-After`); nenhuma resposta com stack trace; nenhum 500 e tudo no envelope |
| Chat e tempo real | R-01 a R-14 | Tutora puxa conversa; a anfitriã conectada no hub (WebSocket) recebe `messageCreated` e `unreadCountChanged`; acentos e emoji chegam iguais; histórico; mensagem vazia, longa demais e com `\u0000`; quem não é da conversa não lê nem escreve; hub sem token, com token adulterado e de admin |
| Carga leve | C-01, C-02 | 100 buscas, 10 ao mesmo tempo (p50 127 ms, p95 244 ms, máx. 291 ms na execução registrada); rajada sem login até o limite geral (429 com `Retry-After`) |
| Logs | L-01 a L-04 | Cada requisição com método, rota, status e tempo; rate limit como Warning; nenhum Error durante o roteiro; nenhuma senha, token, CPF ou texto do chat no log |

**Execução registrada:** 25/10/2026, 60 cenários, 60 aprovados ([evidência](evidencias/exploratorio-2026-10-25.md)). A primeira execução achou duas falhas, F-09 (corpo grande demais dava 500) e F-10 (o mesmo pedido saía como Error no log), corrigidas na seção 8.

**Ambiente da execução:** código desta branch rodando com `dotnet run` em Development, com os limites de produção (rate limit ligado) e os dados de teste do seed, contra Postgres, Redis e Mailpit em containers descartáveis. Os logs conferidos são o JSON do Serilog que a API escreveu durante o roteiro.

**Como rodar:** com a API no ar e os dados de teste ligados (`SEED_TEST_DATA_ENABLED=true`):

```bash
PETHOST_QA_TEST_DATA_PASSWORD=<SEED_TEST_DATA_PASSWORD> PETHOST_QA_ADMIN_EMAIL=<SEED_ADMIN_EMAIL> PETHOST_QA_ADMIN_PASSWORD=<SEED_ADMIN_PASSWORD> \
  dotnet run --project tools/PetHost.ExploratoryQa -- --base-url http://localhost:8080 --docker-logs pethost-api
```

O relatório sai em `docs/evidencias/exploratorio-<data>.md` (`--out` muda o caminho) e o processo termina com código 1 se algum cenário falhar. Sem as variáveis do admin, os cenários de admin aparecem como "não rodou". Entre duas execuções, espere 5 minutos: o roteiro estoura de propósito o limite de login por IP (10 a cada 5 minutos) e, por último, o limite geral sem login. `--help` lista as opções.

