# PetHost — API (backend)

> O "deixa com o vizinho" que funciona: quem tem espaço e jeito com bicho abre um **cantinho** na própria casa para hospedar pets, e quem vai viajar encontra alguém de confiança perto de casa, conversa, paga e fica tranquilo.

Projeto acadêmico. Todos os dados de exemplo são fictícios e os pagamentos rodam sempre em **modo de teste** do Stripe.

## Sumário

1. [Descrição da solução](#1-descrição-da-solução)
2. [Tecnologias utilizadas](#2-tecnologias-utilizadas)
3. [Estrutura do projeto](#3-estrutura-do-projeto)
4. [Configuração do ambiente](#4-configuração-do-ambiente)
5. [Como executar o projeto](#5-como-executar-o-projeto)
6. [Instruções para execução (passo a passo e testes)](#6-instruções-para-execução)
7. [Autenticação](#7-autenticação)
8. [Endpoints](#8-endpoints)
9. [Banco de dados](#9-banco-de-dados)
10. [Informações necessárias para utilização da aplicação](#10-informações-necessárias-para-utilização-da-aplicação)
11. [Documentação complementar](#11-documentação-complementar)

---

## 1. Descrição da solução

O PetHost é uma plataforma de hospedagem de pets entre vizinhos. Este repositório é a **API REST** que atende o front (Next.js). Ela cobre a jornada inteira:

| Etapa | O que a API faz |
|---|---|
| **Cadastro** | Conta de **tutor** (dono do pet) ou de **anfitrião** (quem hospeda). A mesma pessoa (CPF) pode ter as duas e alternar entre elas. |
| **Verificação do anfitrião** | O anfitrião envia documento e selfie; o **admin** aprova ou recusa. Fotos ficam em bucket privado e são apagadas depois da decisão (LGPD). |
| **Recebimento** | O anfitrião conecta uma conta **Stripe Connect** para receber os repasses. |
| **Pets** | Ficha do pet com tipo, porte, remédios, alimentação e manias, mais fotos. |
| **Cantinhos** | O espaço do anfitrião: tipos e portes aceitos, vagas, diária, fotos, agenda e **busca por raio** (geocodificação via Nominatim/OpenStreetMap). |
| **Reservas** | Pedido do tutor, aceite/recusa do anfitrião, cancelamento, check-in e check-out, respeitando a capacidade do cantinho. |
| **Pagamentos** | Pix ou cartão em até 6 parcelas, checkout transparente, webhooks, reembolso, repasse 90/10 no check-out e contestação. |
| **Chat** | Uma conversa por dupla tutor–anfitrião, com texto e foto, contador de não lidas, bloqueio, denúncia e aviso em **tempo real** (SignalR). |
| **Avaliações** | Depois do check-out cada lado avalia o outro (nota 1 a 5, às cegas, em até 7 dias), com resposta, denúncia e moderação. |
| **Administração** | Fila de verificação, suspensão de contas, moderação de chat e avaliações, pendências de pagamento e **trilha de auditoria**. |

O escopo completo do MVP está em [`docs/escopo-mvp.md`](docs/escopo-mvp.md).

---

## 2. Tecnologias utilizadas

| Área | Tecnologia |
|---|---|
| Runtime / linguagem | **.NET 10**, C# 14 (`Nullable` ligado, avisos viram erro) |
| Framework web | **ASP.NET Core** com Controllers |
| Banco de dados | **PostgreSQL 17** com **EF Core 10** (Npgsql, `EFCore.NamingConventions`) |
| Cache / sessões | **Redis 8** (refresh tokens, revogação de tokens, backplane do SignalR) |
| Autenticação | **JWT** (HS256) + refresh token opaco rotativo; senhas com **Argon2id** |
| Validação | **FluentValidation** |
| Tempo real | **SignalR** com backplane no Redis |
| Pagamentos | **Stripe Connect** (`Stripe.net`), modo de teste |
| Armazenamento de imagens | Bucket **S3 compatível** (`AWSSDK.S3`): **RustFS** no ambiente local, Cloudflare R2 / Supabase em produção |
| E-mail | **MailKit** (SMTP) com caixa de saída persistente; **Mailpit** no ambiente local |
| Logs | **Serilog** (JSON estruturado) |
| Documentação da API | OpenAPI + **Swagger UI** |
| Testes | **xUnit v3**, **Moq**, **FluentAssertions 7**, **Testcontainers**, **Respawn**, **NetArchTest** |
| Infraestrutura | **Docker** / **Docker Compose**, **ngrok** (expor os webhooks do Stripe), **GitHub Actions** (CI) |

As versões dos pacotes ficam centralizadas em [`Directory.Packages.props`](Directory.Packages.props) e a versão do SDK em [`global.json`](global.json).

---

## 3. Estrutura do projeto

Arquitetura de **monólito modular** com **Clean Architecture**: cada módulo (bounded context) tem quatro projetos, um por camada, e o compilador garante as dependências permitidas (`Presentation → Application → Domain`; `Infrastructure` implementa as portas). Módulos não se referenciam entre si; conversam por contratos em `Shared.Contracts`.

```
pethost_backend/
├── PetHost.slnx                      ← solution (formato do SDK 10)
├── Directory.Build.props             ← propriedades comuns de build
├── Directory.Packages.props          ← versões centralizadas dos pacotes NuGet
├── docker-compose.yml · Dockerfile   ← ambiente completo em containers
├── .env.example                      ← modelo das variáveis de ambiente
├── docs/                             ← documentação de cada módulo, padrões e dicionário de dados
├── scripts/                          ← relatório de cobertura (coverage.ps1 / coverage.sh)
├── src/
│   ├── Host/PetHost.Api/             ← único executável: Program.cs, pipeline, health checks, Swagger, seeds
│   ├── Shared/
│   │   ├── PetHost.Shared.Kernel/          ← Result, Error, Entity, ValueObject
│   │   ├── PetHost.Shared.Contracts/       ← envelope da API, contratos entre módulos
│   │   └── PetHost.Shared.Infrastructure/  ← e-mail, storage S3, Stripe, Redis, SignalR, CORS, rate limit
│   └── Modules/<Módulo>/
│       ├── PetHost.Modules.<Módulo>.Domain/          ← agregados, value objects, regras
│       ├── PetHost.Modules.<Módulo>.Application/     ← casos de uso (commands/queries + handlers)
│       ├── PetHost.Modules.<Módulo>.Infrastructure/  ← DbContext, migrations, repositórios, integrações
│       └── PetHost.Modules.<Módulo>.Presentation/    ← controllers
├── tests/
│   ├── Modules/<Módulo>/             ← testes unitários (Domain, Application) e de integração
│   ├── Shared/                       ← testes do Kernel e Contracts
│   ├── PetHost.ArchitectureTests/    ← valida as regras de dependência entre camadas
│   └── PetHost.TestKit/              ← builders e fixtures compartilhados
└── tools/PetHost.ExploratoryQa/      ← roteiro exploratório contra a API no ar (HTTP e SignalR)
```

Módulos: `Auth`, `Owners`, `Hosts`, `Pets`, `Listings`, `Bookings`, `Payments`, `Conversations`, `Reviews` e `Audit` (o papel de cada um está no [`PROJECT_STANDARDS.md`](docs/PROJECT_STANDARDS.md) §2).

Dentro de cada camada a organização é **por feature** (ex.: `Bookings/CreateBooking/`), nunca por tipo técnico. Os padrões completos estão em [`docs/PROJECT_STANDARDS.md`](docs/PROJECT_STANDARDS.md).

---

## 4. Configuração do ambiente

### 4.1 Pré-requisitos

| Ferramenta | Para quê | Obrigatório? |
|---|---|---|
| **Docker** + **Docker Compose** | Subir API, Postgres, Redis, Mailpit, RustFS e ngrok | Sim |
| **.NET SDK 10.0.100+** | Rodar a API fora do Docker e rodar os testes | Só para desenvolvimento/testes |
| **Conta Stripe** (modo de teste) | Pagamentos e cadastro de recebimento do anfitrião | Só para testar pagamentos |
| **Stripe CLI** ou **conta ngrok** | Receber os webhooks do Stripe localmente | Só para testar pagamentos |
| **OpenSSL** (ou similar) | Gerar as chaves secretas | Recomendado |

### 4.2 Arquivo `.env`

Toda a configuração vem de variáveis de ambiente. Copie o modelo:

```bash
cp .env.example .env
```

O Docker Compose lê o `.env` automaticamente (ele **nunca** vai para o git). Os valores do modelo já funcionam para desenvolvimento local; o que você **precisa** trocar:

| Variável | O que colocar |
|---|---|
| `JWT_KEY` | Chave de no mínimo 32 bytes. Gere com `openssl rand -base64 48` |
| `HOST_CPF_FINGERPRINT_KEY` | Segredo de no mínimo 32 caracteres. Gere com `openssl rand -hex 32` |
| `SEED_ADMIN_PASSWORD` | Senha do administrador criado na subida |

Variáveis opcionais mais importantes:

| Grupo | Variáveis | Observação |
|---|---|---|
| Portas | `API_HOST_PORT` (8080), `POSTGRES_HOST_PORT` (5432), `REDIS_HOST_PORT` (6379), `MAILPIT_UI_HOST_PORT` (8025), `RUSTFS_HOST_PORT` (9000), `RUSTFS_CONSOLE_HOST_PORT` (9001) | Mude se a porta já estiver em uso |
| Banco / cache | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `REDIS_PASSWORD` | Usados pelo compose para criar os serviços e montar a connection string |
| JWT | `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_ACCESS_TOKEN_LIFETIME_MINUTES` (15), `JWT_REFRESH_TOKEN_LIFETIME_DAYS` (30) | |
| CORS | `CORS_ALLOWED_ORIGIN` | Origem do front (padrão `http://localhost:3000`) |
| Stripe | `STRIPE_SECRET_KEY`, `STRIPE_PUBLISHABLE_KEY`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_CONNECT_WEBHOOK_SECRET`, `STRIPE_CONNECT_RETURN_URL`, `STRIPE_CONNECT_REFRESH_URL` | Use **sempre** chaves de teste (`sk_test_`, `pk_test_`) |
| ngrok | `NGROK_AUTHTOKEN`, `NGROK_URL` | Expõe a API para os webhooks do Stripe |
| E-mail | `EMAIL_SMTP_*`, `EMAIL_FROM_*` | Padrão: Mailpit (captura tudo, não entrega) |
| Imagens | `STORAGE_*`, `RUSTFS_*` | Padrão: RustFS local; veja [`docs/armazenamento-de-imagens.md`](docs/armazenamento-de-imagens.md) |
| Seeds | `SEED_ADMIN_*`, `SEED_DEMO_HOST_*`, `SEED_TEST_DATA_*` | Veja a [seção 10](#10-informações-necessárias-para-utilização-da-aplicação) |

Cada variável está comentada no [`.env.example`](.env.example).

### 4.3 Configuração fora do Docker

Rodando a API com `dotnet run`, a configuração vem de `appsettings.json` / `appsettings.Development.json` e de variáveis de ambiente no formato do ASP.NET Core (`__` separa seções), por exemplo:

```
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=pethost;Username=pethost;Password=pethost_local_dev
ConnectionStrings__Redis=localhost:6379,password=pethost_local_dev,abortConnect=false
Jwt__Issuer=https://api.pethost.local
Jwt__Audience=pethost-app
Jwt__Key=<sua chave de 32+ bytes>
HostVerification__CpfFingerprintKey=<seu segredo de 32+ caracteres>
Storage__ServiceUrl=http://localhost:9000
```

O mapeamento completo entre as variáveis do `.env` e as chaves do ASP.NET Core está no bloco `environment` do serviço `api` em [`docker-compose.yml`](docker-compose.yml). Configuração obrigatória inválida (chave JWT curta, e-mail sem host etc.) **derruba a subida** com uma mensagem clara.

---

## 5. Como executar o projeto

### Opção A — tudo no Docker (recomendado)

```bash
cp .env.example .env
```

Edite o `.env` (seção 4.2) e suba:

```bash
docker compose up -d --build
```

O compose sobe, nesta ordem: **Postgres** e **Redis** (com health check), **Mailpit**, **RustFS** + criação dos buckets, a **API** e o **ngrok**. Em `Development` a API aplica as migrations de todos os módulos e roda os seeds automaticamente.

| Serviço | Endereço |
|---|---|
| API | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| Documento OpenAPI | http://localhost:8080/openapi/v1.json |
| Health (processo) | http://localhost:8080/health/live |
| Health (Postgres + Redis) | http://localhost:8080/health/ready |
| Caixa de e-mails (Mailpit) | http://localhost:8025 |
| Console do bucket (RustFS) | http://localhost:9001 (login `RUSTFS_ACCESS_KEY` / `RUSTFS_SECRET_KEY`) |
| Inspector do ngrok | http://localhost:4040 |

> O ngrok sem `NGROK_AUTHTOKEN` não sobe; o resto funciona normalmente. Ele só é necessário para os webhooks do Stripe.

Comandos úteis:

```bash
docker compose logs -f api
```

```bash
docker compose down
```

```bash
docker compose down -v
```

O último apaga também os volumes (banco, Redis e imagens), útil para recriar os dados de teste do zero.

### Opção B — API local com `dotnet run`

Suba só as dependências no Docker:

```bash
docker compose up -d postgres redis mailpit rustfs rustfs-init
```

Configure as variáveis da seção 4.3 e rode:

```bash
dotnet run --project src/Host/PetHost.Api
```

A API fica em http://localhost:5038 (perfil `http` do `launchSettings.json`) e o Swagger em http://localhost:5038/swagger.

---

## 6. Instruções para execução

### 6.1 Primeiro uso, passo a passo

1. Suba o ambiente (seção 5).
2. Confira que a API está pronta: `GET http://localhost:8080/health/ready` deve devolver `200`.
3. Abra o **Swagger** em http://localhost:8080/swagger.
4. Faça login em `POST /api/v1/auth/sessions/login` com o admin (`SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`, `"role": "admin"`) ou com uma conta dos [dados de teste](#103-dados-de-teste).
5. Copie o `accessToken` da resposta, clique em **Authorize** e cole o token (sem a palavra `Bearer`). O token fica salvo no navegador.
6. Chame as rotas protegidas. E-mails enviados (esqueci a senha, avisos do chat, lembretes) aparecem no **Mailpit** (http://localhost:8025).

### 6.2 Testando pagamentos (opcional)

1. Preencha `STRIPE_SECRET_KEY` e `STRIPE_PUBLISHABLE_KEY` com as chaves de teste do Dashboard do Stripe.
2. Encaminhe os webhooks para a API com a Stripe CLI (tudo numa linha):

   ```bash
   stripe listen --events account.updated,payment_intent.succeeded,payment_intent.processing,payment_intent.payment_failed,payment_intent.canceled,charge.refunded,charge.refund.updated,refund.updated,refund.failed,charge.dispute.created,charge.dispute.updated,charge.dispute.closed --forward-to localhost:8080/api/v1/stripe/webhooks --forward-connect-to localhost:8080/api/v1/stripe/webhooks
   ```

   Coloque o `whsec_...` que ela imprime em `STRIPE_WEBHOOK_SECRET`. Como alternativa, use a URL pública do ngrok (`<URL>/api/v1/stripe/webhooks`) como destino no Dashboard.
3. Reinicie a API para ler as chaves novas.
4. Use o **anfitrião de demonstração** (`SEED_DEMO_HOST_*`) ligado a uma conta conectada de teste real (`acct_...`): os anfitriões dos dados de teste usam uma conta Stripe fictícia e **não** recebem pagamento.

### 6.3 Testes automatizados

O projeto usa o **Microsoft Testing Platform** (xUnit v3). Os testes de integração sobem Postgres, Redis e RustFS com **Testcontainers**, então precisam do Docker rodando.

Todos os testes (unitários, de arquitetura e de integração):

```bash
dotnet test
```

Só os que não precisam de Docker:

```bash
dotnet test --filter-not-namespace "*.IntegrationTests*" --ignore-exit-code 8
```

Um projeto específico:

```bash
dotnet test --project tests/Modules/Listings/PetHost.Modules.Listings.Application.UnitTests
```

Relatório de cobertura (HTML):

```bash
./scripts/coverage.sh
```

No Windows, use `scripts/coverage.ps1`. O **CI** ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) compila e roda todos os testes em todo push e em toda PR para `main` e `dev`.

O resultado da última execução completa, com cada caso de teste planejado na Entrega 2 ligado aos testes que o verificam, as falhas encontradas, as correções e as evidências, está no [**relatório de testes**](docs/relatorio-de-testes.md).

Além dos testes automatizados, o **roteiro exploratório** (`tools/PetHost.ExploratoryQa`) roda contra a API no ar: casos de borda, segurança, chat em tempo real, carga leve e logs. Com a API no Docker e os dados de teste ligados (`SEED_TEST_DATA_ENABLED=true`):

```bash
PETHOST_QA_TEST_DATA_PASSWORD=<SEED_TEST_DATA_PASSWORD> PETHOST_QA_ADMIN_EMAIL=<SEED_ADMIN_EMAIL> PETHOST_QA_ADMIN_PASSWORD=<SEED_ADMIN_PASSWORD> dotnet run --project tools/PetHost.ExploratoryQa -- --base-url http://localhost:8080 --docker-logs pethost-api
```

O resultado sai em `docs/evidencias/exploratorio-<data>.md`. O roteiro estoura de propósito o limite de login por IP, então espere 5 minutos entre duas execuções. Detalhes no [relatório de testes](docs/relatorio-de-testes.md), seção 11.

### 6.4 Migrations

Em `Development` as migrations rodam sozinhas na subida. Para criar uma nova (cada módulo tem o seu `DbContext`):

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add CreateBookingsTables --project src/Modules/Bookings/PetHost.Modules.Bookings.Infrastructure --startup-project src/Host/PetHost.Api --context BookingsDbContext --output-dir Persistence/Migrations
```

---

## 7. Autenticação

A API usa **JWT Bearer** com **refresh token** rotativo.

```
POST /api/v1/auth/sessions/login   { email, password, role }
        │
        ▼
{ accessToken, refreshToken, expiresAt, user }
        │
        ├─► chamadas protegidas:  Authorization: Bearer <accessToken>
        │
        └─► antes de expirar:     POST /api/v1/auth/sessions/refresh { refreshToken }
                                  → par novo (o refresh anterior deixa de valer)
```

Exemplo de login:

```json
{
  "email": "tutora.ana@pethost.local",
  "password": "Teste@1234",
  "role": "owner"
}
```

O campo `role` é obrigatório porque a mesma pessoa pode ter conta de tutor e de anfitrião com o mesmo e-mail. O access token vale 15 min e o refresh token, 30 dias (configuráveis no `.env`). Senhas são guardadas com **Argon2id**.

| Papel (`role`) | Quem é | Como é criado |
|---|---|---|
| `owner` | Tutor do pet | `POST /api/v1/owners/register` |
| `host` | Anfitrião | `POST /api/v1/users/register`, depois verificação aprovada pelo admin |
| `admin` | Administrador | Criado pelo seed na subida (não se cadastra pela API) |

Claims do token, rotação e revogação, esqueci a senha, troca entre contas e rate limit: [`docs/modulo-auth.md`](docs/modulo-auth.md) e [`docs/PROJECT_STANDARDS.md`](docs/PROJECT_STANDARDS.md) §13. O hub de tempo real do chat recebe o token pela query `access_token`: [`docs/modulo-conversations.md`](docs/modulo-conversations.md).

---

## 8. Endpoints

Todas as rotas usam o prefixo `/api/v1` e trabalham com **JSON em camelCase** (uploads de imagem usam `multipart/form-data`, com o arquivo na parte `file`). A referência completa e sempre atualizada, com corpo, respostas e exemplos de cada rota, é o **Swagger** em `http://localhost:8080/swagger` (só em `Development`).

### 8.1 Envelope de resposta

Toda resposta, inclusive erro, segue o mesmo formato:

```json
{ "success": true, "data": { "...": "..." }, "timestamp": "2026-10-08T12:00:00.000+00:00" }
```

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validation failed",
    "details": [
      { "field": "email", "messages": ["Email is required."], "errors": [{ "code": "AUTH_EMAIL_REQUIRED" }] }
    ]
  },
  "timestamp": "2026-10-08T12:00:00.000+00:00"
}
```

Trate os erros pelo `error.code` (estável), nunca pela `message`.

### 8.2 Rotas por módulo

| Módulo | Prefixos | Detalhes |
|---|---|---|
| Auth | `/auth/sessions`, `/auth/password`, `/users` | [`modulo-auth.md`](docs/modulo-auth.md) |
| Owners | `/owners` | [`modulo-owners.md`](docs/modulo-owners.md) |
| Hosts | `/hosts`, `/host-applications` | [`modulo-hosts.md`](docs/modulo-hosts.md) |
| Pets | `/pets`, `/hosts/{hostId}/pets` | [`modulo-pets.md`](docs/modulo-pets.md) |
| Listings | `/listings` | [`modulo-listings.md`](docs/modulo-listings.md) |
| Bookings | `/bookings` | [`modulo-bookings.md`](docs/modulo-bookings.md) |
| Payments | `/bookings/{bookingId}/payment`, `/payments` | [`modulo-payments.md`](docs/modulo-payments.md) |
| Conversations | `/conversations`, hub `/hubs/conversations` | [`modulo-conversations.md`](docs/modulo-conversations.md) |
| Reviews | `/reviews`, `/owners/{ownerId}/reviews`, `/pets/{petId}/reviews` | [`modulo-reviews.md`](docs/modulo-reviews.md) |
| Audit | `/audit-entries` | [`modulo-audit.md`](docs/modulo-audit.md) |

Fora do prefixo de módulo: `GET /health/live` e `GET /health/ready` (saúde da API), `GET /api/v1/support` (contato do suporte) e `POST /api/v1/stripe/webhooks` (eventos do Stripe).

---

## 9. Banco de dados

**PostgreSQL 17** com **EF Core 10**. Cada módulo tem o **seu `DbContext`, o seu schema e as suas migrations**, e não há `JOIN` entre schemas: `auth`, `owner`, `host`, `pet`, `listing`, `booking`, `payment`, `conversation`, `review`, `audit` e `email` (caixa de saída de e-mails).

- Nomes em inglês e `snake_case`; chave primária `id` do tipo **UUID v7**, gerado pela aplicação.
- Datas em `timestamptz` (UTC); dinheiro nunca em ponto flutuante (`numeric` em reais, ou centavos inteiros no módulo de pagamentos).
- Em `Development` as migrations rodam sozinhas na subida.
- O **Redis** guarda refresh tokens e revogações; as imagens ficam em um bucket S3 público, e os documentos da verificação do anfitrião, em um bucket privado.

Diagrama, tabelas, colunas e enums: [**dicionário de dados**](docs/dicionario-de-dados.md).

---

## 10. Informações necessárias para utilização da aplicação

### 10.1 Endereços e credenciais locais

| Item | Valor padrão |
|---|---|
| URL base da API | `http://localhost:8080/api/v1` |
| Swagger | `http://localhost:8080/swagger` |
| Postgres | `localhost:5432`, banco `pethost`, usuário `pethost`, senha `POSTGRES_PASSWORD` |
| Redis | `localhost:6379`, senha `REDIS_PASSWORD` |
| Mailpit (e-mails) | `http://localhost:8025` |
| RustFS (imagens) | `http://localhost:9001` |

### 10.2 Administrador

Criado na subida quando `SEED_ADMIN_ENABLED=true` (padrão no `.env.example`):

| Campo | Valor |
|---|---|
| E-mail | `SEED_ADMIN_EMAIL` (padrão `admin@pethost.local`) |
| Senha | `SEED_ADMIN_PASSWORD` |
| `role` no login | `admin` |

O seed é idempotente e **nunca** altera a senha de um admin que já existe.

### 10.3 Dados de teste

Com `SEED_TEST_DATA_ENABLED=true` e `SEED_TEST_DATA_PASSWORD=Teste@1234` no `.env`, a API cria contas fictícias em Maringá (PR), todas com a mesma senha:

| Quem | E-mail | `role` | Tem |
|---|---|---|---|
| Ana Souza | `tutora.ana@pethost.local` | `owner` | Thor (cão grande), Mel (gata) |
| Bruno Lima | `tutor.bruno@pethost.local` | `owner` | Pipoca (cã pequena), Kiwi (calopsita) |
| Carla Mendes | `anfitria.carla@pethost.local` | `host` | Cantinho "Casa com quintal na Zona 7" (cães) |
| Diego Rocha | `anfitriao.diego@pethost.local` | `host` | Cantinho "Apartamento tranquilo para gatos" |
| Elisa Prado | `anfitria.elisa@pethost.local` | `host` | Cantinho "Chácara urbana" (cães, gatos, aves, coelho) |

Os anfitriões já vêm verificados e com cantinho na busca. Dá para pedir, aceitar e recusar reservas, mas **não pagar** (a conta Stripe deles é fictícia). Para testar pagamento, use o **anfitrião de demonstração** (`SEED_DEMO_HOST_ENABLED=true` + `SEED_DEMO_HOST_STRIPE_ACCOUNT_ID` com uma conta conectada de teste). Lista completa em [`docs/dados-de-teste.md`](docs/dados-de-teste.md).

### 10.4 Regras que o cliente precisa conhecer

- **Login exige `role`**: `owner`, `host` ou `admin`.
- **Anfitrião precisa ser aprovado** pelo admin e ter a conta Stripe liberada antes do cantinho aparecer na busca.
- **Header de autenticação:** `Authorization: Bearer <accessToken>`; renove pelo `refresh` antes de `expiresAt` (Unix, segundos).
- **Imagens:** JPEG, PNG ou WebP, até 5 MB, em `multipart/form-data` na parte `file`. URLs de imagem nunca são aceitas no JSON; metadados (EXIF/GPS) são removidos.
- **CORS:** só as origens em `CORS_ALLOWED_ORIGIN` podem chamar a API pelo navegador.
- **Cartões de teste do Stripe:** use os números de teste publicados pelo Stripe (ex.: `4242 4242 4242 4242`, qualquer data futura e CVC).
- **Erros:** trate pelo `error.code` (estável), nunca pela `message`.

---

## 11. Documentação complementar

| Documento | Conteúdo |
|---|---|
| [`docs/escopo-mvp.md`](docs/escopo-mvp.md) | O que o MVP tem, o que não tem e por quê |
| [`docs/PROJECT_STANDARDS.md`](docs/PROJECT_STANDARDS.md) | Padrões obrigatórios de arquitetura, código, API e testes |
| [`docs/dicionario-de-dados.md`](docs/dicionario-de-dados.md) | Diagrama, tabelas, colunas, relacionamentos e enums |
| [`docs/dados-de-teste.md`](docs/dados-de-teste.md) | Contas, pets e cantinhos criados pelo seed |
| [`docs/relatorio-de-testes.md`](docs/relatorio-de-testes.md) | Casos de teste executados, resultado, cobertura, falhas, correções e evidências |
| [`docs/armazenamento-de-imagens.md`](docs/armazenamento-de-imagens.md) | Buckets, RustFS, R2/Supabase e regras de upload |
| [`docs/upload-direto.md`](docs/upload-direto.md) | Upload direto do front ao bucket (URL assinada): contrato, erros, CORS e limpeza |
| `docs/modulo-*.md` | Endpoints, regras, erros e configuração de cada módulo (Auth, Owners, Hosts, Pets, Listings, Bookings, Payments, Conversations, Reviews, Audit) |

## Design e links dos e-mails

Todos os avisos transacionais usam o template Quintal da outbox. Configure `EMAIL_APP_URL` com a URL pública do frontend para habilitar os botões; mantenha os valores SMTP existentes. Destinos, configuração, compatibilidade e validação: [docs/email-design.md](docs/email-design.md).
