# E-mails e fidelidade ao modal Quintal — 29/10/2026

## Recursos e comportamento

`src/Shared/PetHost.Shared.Infrastructure/Email/EmailTemplate.cs` continua sendo o ponto único de composição antes da persistência na outbox. A mudança alcança todos os notifiers de conta, senha, reservas, pagamentos, verificação, recebimentos, avaliações, chat e alertas administrativos, sem alterar destinatários, regras de negócio, tokens, validade, permissões ou links de ação.

A frase abaixo da logo foi removida. Cartão usa borda tinta de 2 px, raio de 24 px e sombra deslocada em 5 px, conforme `surfaceStyles` do frontend. Botão usa raio de 18 px e sombra de 3 px, conforme `buttonStyles`. A sombra agora é formada por tabelas com fundo escuro e padding deslocado; não depende de `box-shadow`, frequentemente descartado por clientes de e-mail. Clientes sem border-radius ainda podem mostrar cantos retos.

## Fonte dos títulos sem depender de webfont

`EmailHeadingAssets.g.cs` é um catálogo interno gerado, consumido somente por `EmailTemplate`: `Images` associa o título de produção a `EmailHeadingImage(Path, Width, Height)`. Existem 34 entradas, extraídas dos assuntos dos notifiers implementados. Para cada título conhecido com `Email:AppUrl` configurada, o `<h1>` contém uma imagem PNG pública de Caprasimo real. Tem texto alternativo com o título completo, dimensões explícitas e largura responsiva. Não inclui dados pessoais nem token em URL de imagem.

O alerta de pagamento com assunto dinâmico usa o título visual comum “Pagamento precisa de atenção”; a pendência continua no assunto original e no corpo. Assunto desconhecido ou domínio ausente mantém `<h1>` textual, sem gerar referência para imagem inexistente. A versão texto puro, o conteúdo da mensagem e os links reais continuam disponíveis. O assunto original não muda.

Corpo, parágrafos, botão e assinatura usam Atkinson Hyperlegible Next explícita. `@font-face` continua para clientes compatíveis, com fallback sans-serif. **Não há como forçar Atkinson no corpo do Gmail quando ele remove webfonts.** Rasterizar todo o texto prejudicaria seleção, adaptação de tamanho e acessibilidade; apenas logo e títulos fixos são imagem. Essa entrega preserva Caprasimo no título mesmo com as fontes externas bloqueadas, mas não promete renderização idêntica do corpo em todo cliente. Referência: [suporte a @font-face](https://www.caniemail.com/features/css-at-font-face/).

## Geração, dependências e atualização

No workspace com ambos os repositórios, executar no frontend `npm run brand:email-headings`. `scripts/generate-email-headings.mjs` lê apenas os assuntos reais dos arquivos `*EmailNotifier.cs`/`ModerationEmailAlerts.cs`, usa Caprasimo local via Playwright/Chromium existente e gera:

- `pethost_frontend/public/brand/email-headings/<hash-do-titulo>.png`: imagem de 340 px, fonte de 36 px, PNG em 2x, sem conteúdo privado;
- `pethost_backend/src/Shared/PetHost.Shared.Infrastructure/Email/EmailHeadingAssets.g.cs`: índice compilado com caminhos e dimensões;
- `pethost_frontend/src/test/fixtures/emailHeadingAssets.json`: metadados de teste, para o CI do frontend não precisar clonar o backend.

Os arquivos são versionados. Nenhum navegador, pacote de imagem ou renderização adicional roda na VM Oracle durante o envio. O gerador remove PNGs obsoletos da pasta específica e rejeita assuntos dinâmicos desconhecidos, evitando catálogo parcial silencioso. Reexecutar ao alterar assuntos ou Caprasimo; revisar diffs dos dois repositórios e publicar assets do frontend antes do backend. Não editar o catálogo gerado manualmente. Aproximadamente 20–30 KB por título; cada e-mail pede somente o título usado, não todos os 34.

## Validação

- `EmailTemplateTests`: 52 casos aprovados, incluindo os 34 títulos implementados, escape, metadados, fallback sem domínio, título desconhecido, aviso administrativo dinâmico, borda/sombra, retirada da frase e preservação de texto/links.
- `scripts/email-brand-assets.test.ts`: 4 casos aprovados, incluindo integridade/dimensões de todos os PNGs gerados e ausência de órfãos, logo, fontes e CORS público. A fixture JSON é produzida pelo mesmo gerador do catálogo; serve apenas aos testes.
- API compilada sem avisos/erros; typecheck, ESLint e Prettier do frontend aprovados.
- Prévias Chromium desktop/celular e quatro tipos de e-mail com downloads de WOFF2 bloqueados: títulos carregados como PNG, frase ausente, sem overflow horizontal em 390 px. Isso simula ausência de fontes, não é teste real do Gmail/Outlook.
- Integração SMTP/Postgres, chegada de novo e-mail real, imagens bloqueadas pelo destinatário e comportamento de todos os clientes permanecem pendentes; não foram enviados e-mails reais nem feito deploy. Não exige migration ou mudança de credenciais.

Comandos: `dotnet test --project tests/Shared/PetHost.Shared.Infrastructure.UnitTests/PetHost.Shared.Infrastructure.UnitTests.csproj --no-restore -- --filter-class '*EmailTemplateTests'`; frontend `node node_modules/vitest/vitest.mjs run --project=unit --maxWorkers=1 scripts/email-brand-assets.test.ts`.
