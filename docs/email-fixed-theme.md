# Tema claro fixo dos emails

Status: implementado em 30/10/2026. Substitui a adaptação ao modo escuro por decisão do usuário. Sem publicação ou envio real nesta entrega.

## Contrato e propósito

`src/Shared/PetHost.Shared.Infrastructure/Email/EmailTemplate.cs`, método interno `Render(EmailMessage, appUrl)`, mantém assunto, destinatários, texto puro, links e outbox. Todos os notifiers continuam usando o template central. Conteúdo de usuário segue escapado pelos notifiers e assunto/URL/alt pelo template. Nenhum endpoint, configuração SMTP, .env, provider ou dependência nova.

O documento declara `<meta name="color-scheme" content="light only">`, `supported-color-schemes: light` e CSS `color-scheme: only light`. Não contém tema escuro, media query de esquema de cor ou seletores Outlook de adaptação. Fundo externo, cartão e imagens usam o papel Quintal #FEFAF3; texto principal #1F1A17, auxiliar #5A4E45. Botão coral, bordas tinta, sombra e faixa grama continuam os elementos de marca. A cor fixa de papel usa também `bgcolor`, estilo inline e gradiente de duas cores iguais para reduzir recoloração automática de fundos. Preenchimento de texto WebKit complementa `color`, sem depender somente de CSS do head.

Logo e títulos permanecem PNGs com Caprasimo real; não houve a migração para títulos HTML proposta antes da instrução de manter o tema fixo. `pethost_frontend/scripts/generate-email-headings.mjs` lê papel do token existente em `src/shared/theme/tokens/colors.ts` e incorpora o mesmo fundo da logo. Os 34 PNGs foram regenerados em `public/brand/email-headings/`, sem mudar nomes/dimensões. O gerador da logo já usava papel e não precisou mudar. As placas claras adicionais do HTML foram removidas. `?v=quintal-light-3` evita o cache das versões anteriores. Nenhuma versão paralela clara/escura fica sem consumidor.

Uso: publicar assets atualizados do frontend antes do backend. Regenerar títulos com `npm run brand:email-headings` se os assuntos/fontes/tokens mudarem; não editar dados gerados à mão. Sem mudanças nas interfaces dos geradores, catálogo `EmailHeadingAssets` ou fixture de títulos.

## Compatibilidade e limites

O Gmail passou a reconhecer `light only` para contas Google em atualização de 2026: [EMC, Gmail app dark mode update](https://emailmarkup.org/en/blog/2026/gmail-app-dark-mode-update). Versão do aplicativo e contas externas no Gmail podem ter comportamento diferente. Fundos com gradiente são uma defesa adicional documentada pela [Litmus](https://www.litmus.com/blog/background-colors-html-email). Nenhum HTML de email controla integralmente um cliente que remove regras ou força sua própria recoloração. Não aplicar o truque screen/difference de texto branco ao texto tinta: sua matemática não preserva essas cores. Confirmar com email novo no Gmail real após publicar. A interface externa do Gmail continua no tema escolhido pelo usuário; somente a mensagem pede tema claro.

Corpo continua texto selecionável com Atkinson e fallback do cliente; não se promete webfont no Gmail. Mensagens já prontas na outbox não são reescritas. O parâmetro de versão de assets não contém dados pessoais/token.

## Validação

`dotnet test tests/Shared/PetHost.Shared.Infrastructure.UnitTests --no-restore --filter 'FullyQualifiedName~EmailTemplateTests'`: 53 testes aprovados, incluindo os 34 títulos, URLs, escape, texto puro, tema claro fixo, ausência de tema escuro/placas e parâmetros novos de cache.

Frontend `npx vitest run scripts/email-brand-assets.test.ts --maxWorkers=1`: cinco testes aprovados. Verifica pixels RGB papel exatos nos cantos de logo e títulos, opacidade de todos os pixels e presença da tinta; dimensões, fontes e ausência de órfãos continuam cobertas. Gerador executado. Chromium usa cinco exemplos renderizados pelo template C# real, 390/1000px, esquema claro/escuro e CSS do head removido, com webfonts bloqueadas; não substitui o Gmail nativo. Resultados e capturas privados em `.agent-tools/email-fixed-light-browser.log` e `.agent-tools/email-preview/fixed-*.png`. SMTP/produção não executados. Unitários de composição e pixels são pertinentes; integração externa/end to end Gmail real permanece pendente.
