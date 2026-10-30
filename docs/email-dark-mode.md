# Contraste dos emails em modo escuro

Implementado em 30/10/2026, no template central da outbox. Publicação e recebimento real ainda não executados.

## Diagnóstico e solução

Logo e títulos eram PNGs transparentes com tinta escura. Clientes que escurecem o fundo HTML sem recolorir os pixels das imagens deixavam as letras invisíveis. Pedir `color-scheme: light` não resolve essa transformação em todos os aplicativos. Referências: [Litmus](https://www.litmus.com/blog/the-ultimate-guide-to-dark-mode-for-email-marketers), [suporte de prefers-color-scheme](https://www.caniemail.com/features/css-at-media-prefers-color-scheme/).

A defesa principal independe de CSS: todos os títulos e a logo agora incorporam uma superfície clara opaca nos próprios PNGs, preservando Caprasimo, cores e dimensões. No escuro aparecem como placas da marca com cantos arredondados no HTML; no claro integram-se às superfícies existentes. Um cliente pode ignorar a cor CSS do contêiner, mas os pixels claros sob a tinta continuam presentes. Não há imagem alternativa escondida nem dependência de troca por media query.

## Recursos e interfaces

- `src/Shared/PetHost.Shared.Infrastructure/Email/EmailTemplate.cs`: `Render(EmailMessage, appUrl)` mantém assinatura/contrato. Novos `class` de superfície, texto, legenda e links; `color-scheme`/`supported-color-schemes` light dark; media query e seletores Outlook `[data-ogsc]` aplicam fundo tinta, cartão café, corpo papel e links luz. Paleta clara inline e bgcolor continuam para clientes sem CSS. Logo/títulos têm placas claras; assunto desconhecido ou frontend ausente continuam texto acessível, com estilo escuro onde suportado.
- URLs dos assets incluem `?v=contrast-2`, evitando reutilização da versão transparente em caches/proxies dos novos emails. O parâmetro é fixo, sem token, destinatário ou dado pessoal. Links de ação e versão texto não mudam.
- `pethost_frontend/scripts/generate-email-logo.mjs` e `scripts/generate-email-headings.mjs`: comandos existentes `brand:email-logo`/`brand:email-headings` agora capturam superfícies opacas. Fontes e DoorMark permanecem fontes da marca. Produzem `public/brand/email-logo.png`, os 34 títulos em `public/brand/email-headings/`, catálogo C# e fixture já existentes. Sem novos arquivos/fontes/providers em runtime ou instalação de dependências. Caminhos/dimensões preservados; assets antigos não são removidos por esta correção.
- `scripts/email-brand-assets.test.ts` no frontend verifica o alpha de todos os pixels dos 35 PNGs, fundos claros, presença de tinta e dimensões; não apenas assinatura do arquivo.

## Publicação e limites

Publicar os assets do frontend antes do backend atualizado. Não é necessário alterar SMTP ou .env. O backend persiste HTML já renderizado na outbox: mensagens prontas/enviadas não são reescritas. O corpo continua texto selecionável e os PNGs têm alt; bloquear imagens ainda permite ler o título alternativo, sujeito ao cliente. Webfonts não são garantidas no Gmail. Recoloração agressiva, imagens desabilitadas e comportamento real de Gmail/Outlook exigem conferência após receber um email novo. Não prometer reprodução idêntica por todos os clientes nem confundir emulação Chromium com Gmail nativo.

## Validação

`dotnet test tests/Shared/PetHost.Shared.Infrastructure.UnitTests --no-restore --filter 'FullyQualifiedName~EmailTemplateTests'`: **53 testes aprovados**, cobrindo os 34 assuntos reais, estilos escuros, cache, escape, URLs/tokens, assunto desconhecido, texto e invariantes da notificação.

Frontend `npx vitest run scripts/email-brand-assets.test.ts --maxWorkers=1`: **5 testes aprovados**, incluindo opacidade efetiva dos 35 PNGs. Ambos os geradores executados. Chromium: cinco exemplos do template C# real em 390/1000px × claro/escuro/recoloração sintética com CSS original removido e webfonts bloqueadas: **30 verificações aprovadas**, sem overflow, imagens decodificadas e corpo adaptado. Um exemplo usa título desconhecido textual. Evidências privadas `.agent-tools/email-preview/contrast-*.png`. Unitários e navegador cobrem composição e assets; SMTP/API externa e envio real N/A para validação local, permanecem pendentes. Sem push/deploy nesta entrega.
