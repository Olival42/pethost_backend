# Confirmação confiável de uploads diretos

Implementado em 29/10/2026. Complementa [upload-direto.md](upload-direto.md), que descreve os tickets, finalidades e limites.

## Plano e execução

1. Tornar atômicos o recurso e o recibo do upload: implementado por transação PostgreSQL compartilhada e trava da linha.
2. Separar efeitos externos do commit: implementado para exclusões de imagens e notificações de mensagens.
3. Renovar URLs privadas ao repetir confirmações: implementado para `chat-photo`.
4. Migrar os seis consumidores frontend para PUT direto: implementado no frontend; documentos usam confirmação em lote para preservar tudo ou nada.
5. Validar falhas, concorrência, links e consumidores: suítes e resultados abaixo. Publicação e migração dos arquivos antigos não foram executadas.

## Recursos e interfaces

| Recurso | Localização | Interface, propósito e consumidores |
|---|---|---|
| `UploadService` | `src/Shared/PetHost.Shared.Infrastructure/Uploads/UploadService.cs` | `RequestAsync`, `ConfirmAsync`, `ConfirmDocumentsAsync`. Reutiliza `IUploadPurpose` e os casos de uso existentes. Nenhum destino/chave é escolhido pelo navegador. |
| `IUploadPurpose.RestoreAsync` | `src/Shared/PetHost.Shared.Infrastructure/Uploads/IUploadPurpose.cs` | Restaura JSON de um recibo concluído sem executar novamente a alteração. Implementação padrão devolve o resultado persistido; chat confere acesso e renova a URL. |
| `ChatPhotoUploadPurpose` | `src/Modules/Conversations/PetHost.Modules.Conversations.Presentation/Uploads/ChatPhotoUploadPurpose.cs` | Usa `GetMessagePhotoQuery` na restauração. Uma mensagem apagada ou inacessível não ganha outra URL. |
| `ICommitEffects` | `src/Shared/PetHost.Shared.Contracts/Storage/ICommitEffects.cs` | `IsActive`, `AfterCommit`, `AfterRollback`. Contrato entre armazenamento e notificações; não contém tipos EF/Npgsql. Registrar callbacks somente quando ativo. |
| `UploadEffects` | `src/Shared/PetHost.Shared.Infrastructure/Uploads/UploadEffects.cs` | Scoped: `Begin`, `CommitAsync`, `RollbackAsync`, `Discard`. Uma confirmação por escopo. Descarta callbacks depois de cada resultado; erro externo é registrado sem converter um commit bem-sucedido em HTTP 500. |
| `S3ImageStorage` / `S3PrivateImageStorage` | `src/Shared/PetHost.Shared.Infrastructure/Storage/` | Agora scoped, com os clientes S3 ainda singleton. Arquivo novo é compensado no rollback; exclusão da imagem anterior ocorre após commit. Fora de confirmação, preservam comportamento anterior. |
| `ConversationNotifications` | `src/Modules/Conversations/PetHost.Modules.Conversations.Application/Conversations/ConversationNotifications.cs` | Adia `MessageSentAsync` enquanto há confirmação ativa; demais chamadas mantêm comportamento. |
| `ConversationsUnitOfWork` | `src/Modules/Conversations/PetHost.Modules.Conversations.Infrastructure/Persistence/ConversationsUnitOfWork.cs` | Reutiliza a transação ambiente na confirmação, preservando a trava da conversa; fora dela continua iniciando sua própria transação. |
| `UploadCleanupService` / `UploadStaging` | `src/Shared/PetHost.Shared.Infrastructure/Uploads/` | Limpeza usa `FOR UPDATE SKIP LOCKED`, marca expirados antes de excluir objetos e retém recibos pelo horário de conclusão. Leitura preliminar de tamanho redundante removida; conteúdo continua limitado e validado. |

## Transação e concorrência

`ConfirmAsync` usa `TransactionScope` com fluxo assíncrono, isolamento ReadCommitted e limite de cinco minutos. A linha do upload é lida com `FOR UPDATE NOWAIT`, filtrada por usuário e papel. Quem disputar recebe `409 UPLOAD_CONFIRMATION_CONFLICT`, inclusive quando o EF encapsula a exceção PostgreSQL.

O recibo e as gravações do módulo usam o mesmo banco/pool Npgsql, com operações sequenciais e conexões fechadas entre comandos. Não abrir duas conexões simultâneas, não usar `Task.WhenAll` para essas consultas e não introduzir outro banco no fluxo: transação distribuída não é suportada por esta composição. Há verificação explícita contra promoção. Referência: [documentação Npgsql](https://www.npgsql.org/doc/basic-usage.html#systemtransactions-and-distributed-transactions).

A trava permanece até commit/rollback; não há nova reserva que expire no meio do trabalho. `processing`/`lease_until` continuam lidos apenas para compatibilidade com recibos deixados pela versão anterior durante a atualização. Uma queda antes do commit reverte tanto a alteração do módulo quanto a conclusão do recibo, permitindo repetir o mesmo upload. A limpeza pula confirmações ativas.

Após commit, ocorrem exclusões e avisos. No rollback confirmado, são apagadas as imagens novas. Se a conexão cair durante COMMIT e o resultado ficar incerto, são preservadas as duas imagens: uma nova tentativa lê o recibo. Banco e S3 não têm transação única; queda de processo ou resultado incerto ainda pode deixar objeto final órfão para reconciliação. A varredura automática cobre a área `uploads/pending/`, não promete remover qualquer órfão final.

Avisos SignalR após commit são de melhor esforço e têm log em caso de falha; não foi criada uma outbox durável de eventos. Auditoria permanece no caso de uso existente e participa da transação quando executada nesse contexto.

## Confirmação conjunta de documentos

`POST /api/v1/uploads/documents/confirm`, somente host:

```json
{ "uploadIds": ["0199f0a0-0000-7000-8000-000000000011", "0199f0a0-0000-7000-8000-000000000012"] }
```

Aceita um a três uploads da própria conta/papel, finalidade `host-document`, com vagas diferentes. IDs e vagas repetidos, uploads de outra finalidade ou de outra conta são recusados com validação `UPLOAD_DOCUMENT_BATCH_INVALID`. A ordem de trava é determinística. Todas as vagas e recibos são gravados numa transação; qualquer recusa reverte o lote, mantém arquivos pendentes válidos e não apaga documentos anteriores.

Resposta: envelope de `UploadConfirmationResponse`; `result` é o `HostApplicationResponse` após a última vaga aplicada. Repetir o mesmo lote concluído não substitui arquivos novamente. Sem bytes no corpo dessa chamada. Os `PUT` de cada arquivo já ocorreram diretamente no bucket privado.

## Migração e publicação

- Backend mantém os endpoints multipart como contrato de compatibilidade, ainda usados pelos testes/consumidores REST anteriores. O frontend atualizado não os usa nem os permite no BFF.
- Novas mídias do chat são privadas. As fotos antigas continuam com `image_url` pública e precisam de migração própria de objetos e referências; nenhuma exclusão ou cópia de produção foi feita aqui.
- Produção precisa de endpoint S3 **HTTPS acessível pelo navegador** em `STORAGE_SIGNED_URL_SERVICE_URL`, preservando host/path usados na assinatura. `rustfs:9000` ou `localhost:9000` não servem como URL pública. `STORAGE_SERVICE_URL` pode continuar interno.
- CORS do bucket privado deve permitir `PUT` e `Content-Type` da origem real, por exemplo `https://pethost.cfd`. Nunca publicar credenciais S3 no frontend. GET por `img` não exige CORS; downloads via `fetch` exigem permitir GET também.
- Publicar backend com o endpoint de lote e migrations de uploads/conversas antes do frontend. Nenhuma migration adicional foi necessária nesta correção.

## Validação

Integração real usa PostgreSQL, Redis e RustFS em containers isolados. `PetPhotoDirectUploadTests` cobre fluxo, posse, assinatura, arquivos inválidos, compensação, falha provocada no recibo após salvar foto, substituição revertida, disputa real entre duas confirmações, retenção e limpeza. `HostApplicationFlowTests.DirectDocuments*` cobre lote feliz/repetição, lote parcialmente inválido e vagas inválidas. `ConversationsControllerTests.DirectUpload*` cobre criação privada única e restauração de recibo com link expirado. `UploadEffectsTests` cobre commit, rollback, falha externa e resultado incerto. Testes de notificações existentes continuam aplicáveis; frontend documenta seus testes em `docs/direct-media-uploads.md`.

Validação executada: PetPhotoDirectUploadTests (18), HostApplicationFlowTests.DirectDocuments (3), UploadEffectsTests (4), notificações/envio (16) e exportação OpenAPI (1) aprovados. A regressão DirectUpload cobre assinatura renovada e falha SQL traduzida pelo módulo. SAVEPOINT reverte gravações parciais antes de persistir a rejeição; checkpoint dos efeitos compensa somente o recurso recusado, preservando os efeitos anteriores do lote. Não há validação SMTP, Stripe live ou deploy nesta entrega. E2E do navegador com backend simulado não equivale à integração completa Next + API publicada.
