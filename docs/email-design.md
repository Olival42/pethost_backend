# E-mails transacionais — identidade Quintal

Atualizado em 28/10/2026. O HTML e o texto puro são finalizados por `src/Shared/PetHost.Shared.Infrastructure/Email/EmailTemplate.cs`, chamado por `EmailOutbox.EnqueueAsync` antes de persistir. Todos os notifiers continuam responsáveis por seus textos, prazos, regras financeiras, privacidade e destinatários; não há um segundo mecanismo de envio.

## Aparência e compatibilidade

Fundo papel `#FEFAF3`, cartão branco com borda tinta `#1F1A17`, botão coral `#F26B3A`, faixa grama `#C5DB4F` e texto secundário `#5A4E45`, conforme tokens do frontend. Títulos usam **Caprasimo 400**; texto e botões, **Atkinson Hyperlegible Next 400/700**, mesmos WOFF2 do site, agora servidos em `public/fonts/`. O template declara `@font-face` com URLs públicas e exclui essas regras do Outlook clássico via condicional MSO. Georgia/Arial são apenas fallback: Gmail não carrega webfonts personalizadas, portanto não é possível garantir a fonte exata no corpo em todo cliente. Referência de compatibilidade: [Can I email](https://www.caniemail.com/features/css-at-font-face/).

A assinatura porta + pethost usa `/brand/email-logo.png`, PNG transparente em 3x gerado da marca e Caprasimo existentes; assim preserva sua tipografia também em clientes sem webfont. Possui tamanho explícito e `alt="PetHost"`; sem `Email:AppUrl` mantém assinatura textual sem inventar domínio. Sem JavaScript ou SVG no e-mail. Layout fluido até 600 px, tabelas de apresentação, CSS inline, texto de prévia oculto, título semântico e link alternativo visível. Arredondamento e sombra degradam para cartão com borda quando não suportados. O conteúdo de HTML dos notifiers é confiável e já escapa dados dinâmicos; o renderer não é sanitizador de HTML arbitrário.

## Configuração e redirecionamento

No `.env` remoto, acrescentar **somente** `EMAIL_APP_URL=https://URL-PUBLICA-DO-FRONTEND`, preservando SMTP, banco e demais segredos. O Compose mapeia para `Email__AppUrl`. Usar o endereço do site/Vercel, nunca a API ou Swagger. A configuração rejeita esquema diferente de HTTP(S), credenciais, query e fragmento. Sem esse valor, a aparência continua aplicada, mas ações relativas não inventam um domínio.

Endereço confirmado pelo usuário: `https://pethost.vercel.app`. Configuração concreta:

```dotenv
EMAIL_APP_URL=https://pethost.vercel.app
PASSWORD_RESET_URL='https://pethost.vercel.app/reset-password#token={token}'
```

`PASSWORD_RESET_URL` já existia no backend antes deste trabalho e pode ser configurada em versões anteriores; a variável explícita tem precedência. Alternativamente deixá-la vazia usa o fallback de `EMAIL_APP_URL` no código atual. O fragmento `#token=` é lido e removido pelo frontend (`ResetPasswordFlow`/`useUrlSecret`); a pessoa escolhe a senha sem login prévio. Não usar a rota antiga `/redefinir-senha`, ausente no frontend atual. O reset chama a API existente, que consome o token uma vez; novo pedido invalida o anterior.

Quando existe link, o HTML não mostra uma caixa de código manual: a tela atual do frontend não tem esse campo. Mensagem e validade se referem ao link. Sem ambas as URLs permanece o fallback técnico de token, para integrações legadas; ele não é um fluxo completo para o site atual. Configurar o domínio é necessário para recuperação pelo site. SMTP, tokens, TTL e fila não foram substituídos.

`EmailMessage` mantém os cinco argumentos existentes e aceita `EmailAction? Action = null` como sexto. `EmailAction(Label, Url)` aceita HTTP(S) absoluto ou caminho iniciado por `/`, resolvido contra a origem de `Email:AppUrl`. Esquemas executáveis, destinos `//`, backslashes e credenciais não viram links. O botão, a URL alternativa e a versão texto apontam para o mesmo destino. O corpo final é persistido; nenhuma migration é necessária. Itens já enfileirados antes da atualização mantêm o corpo anterior para não reescrever notificações pendentes ou tokens.

| Aviso | Destino |
|---|---|
| Recuperação de senha | `PasswordReset:ResetUrl`, se definido; senão `/reset-password#token=...` usando `Email:AppUrl`; sem ambos, código manual |
| Senha alterada | `/forgot-password` |
| Suspensão retirada | `/login` |
| Verificação aprovada / recusada | `/host/payments` / `/host/application` |
| Recebimentos | `/host/payments` |
| Reservas, em todos os estados e lembretes | `/owner/bookings/{id}` ou `/host/bookings/{id}`, conforme destinatário |
| Pagamentos, atraso e reembolso | `/owner/bookings/{id}` |
| Repasse e estorno | `/host/bookings/{id}` |
| Mensagens não lidas | `ConversationUnreadEmail:AppUrl` legado, quando preenchido; senão `/conversations` |
| Avaliações e lembrete | `ReviewEmail:AppUrl` legado, quando preenchido; senão início do site (o contrato atual não informa a reserva) |
| Alertas administrativos de pagamentos, recebimentos, avaliações e chat | `/admin/users`; os caminhos REST de investigação permanecem no corpo, pois não há tela dedicada para todas essas pendências |

Os caminhos foram conferidos contra `pethost_frontend/src/app`. Links não substituem autenticação nem concedem permissões. Não são links de redirecionamento arbitrário recebidos do usuário. O token padrão fica em fragmento, lido e removido pelo frontend; uma `PASSWORD_RESET_URL` legada explícita continua tendo precedência, inclusive se usar query.

Árvore dos recursos específicos desta entrega:

```text
src/Shared/PetHost.Shared.Infrastructure/Email/
  EmailTemplate.cs                  layout final, URL e alternativas HTML/texto
  EmailMessage.cs                   mensagem e ação opcional
  EmailOptions.cs                   SMTP e URL pública do frontend
  EmailOptionsValidator.cs          validação da configuração
  EmailOutbox.cs                    renderização antes da persistência
tests/Shared/PetHost.Shared.Infrastructure.UnitTests/Email/EmailTemplateTests.cs
tests/Modules/Auth/PetHost.Modules.Auth.Infrastructure.UnitTests/Notifications/
  PasswordResetEmailNotifierTests.cs
```

Exemplo de consumo em um notifier: `new EmailMessage(email, name, subject, text, html, new EmailAction("Ler minhas mensagens", "/conversations"))`. Não chamar o renderer em cada notifier: a outbox já aplica uma vez, inclusive aos avisos administrativos. As implementações por módulo permanecem em `Notifications/`, `UnreadEmail/` e `Moderation/`; não há novo provider, endpoint ou serviço hospedado.

## Validação e limites

- Unitários/funcionais: `tests/Shared/PetHost.Shared.Infrastructure.UnitTests/Email/EmailTemplateTests.cs` cobre HTML/texto equivalentes, preservação do conteúdo, escape de metadados, URLs inválidas, ausência de endereço e URLs de logo/fontes sem vazamento de token: **16 casos aprovados**.
- Unitários do fluxo de senha: `tests/Modules/Auth/PetHost.Modules.Auth.Infrastructure.UnitTests/Notifications/PasswordResetEmailNotifierTests.cs` cobre URL legada, fallback com fragmento, código técnico sem URL, validade, escape e ausência da caixa de código quando há link: **3 casos aprovados**. A classe privada `CapturingOutbox` é um fake local dessa suíte, sem reuso de produção.
- Integração: atualizados `EmailOutboxTests`, `PasswordEndpointsTests` e configuração de `AuthApiFixture` para verificar HTML e links após a outbox. Projeto compilado; execução com PostgreSQL/Redis bloqueada por acesso negado ao Docker local. Não declarar integração aprovada.
- Build da API e projeto de integração aprovados sem warnings/erros. Comandos: `dotnet build src/Host/PetHost.Api/PetHost.Api.csproj --no-restore` e `dotnet build tests/Modules/Auth/PetHost.Modules.Auth.IntegrationTests/PetHost.Modules.Auth.IntegrationTests.csproj --no-restore`.
- Testes executados com `dotnet test --project <projeto> --no-restore -- --filter-class '*EmailTemplateTests'` e filtro `'*PasswordResetEmailNotifierTests'` no projeto Auth Infrastructure.UnitTests.
- Prévias geradas com o renderer real e dados fictícios; inspeção Chromium desktop e celular, cinco mensagens verificadas sem overflow horizontal em 390 px. Isso não equivale a renderização real em Gmail/Outlook. Entrega SMTP real, comportamento entre clientes e clique ponta a ponta após deploy permanecem pendentes; não é necessária jornada Playwright de aplicação para o renderer sem UI própria.

Para publicar é necessário reconstruir a API; somente mudar o `.env` não instala o novo template. Não houve push, deploy remoto nem alteração de credenciais nesta entrega.

Publicar também os assets do frontend antes do novo template: [email-brand-assets.md](../../pethost_frontend/docs/email-brand-assets.md). A pasta pública substitui os arquivos privados antigos, sem duplicar fontes. Geração da logo real e testes dos assets executados; 3 casos aprovados. Nenhum teste enviou e-mail real. Prévias Chromium carregaram logo e ambas as fontes dos arquivos locais interceptados, sem overflow em 390 px; isso não valida o deploy Vercel nem o Gmail real.
