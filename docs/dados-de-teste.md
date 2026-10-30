# Dados de teste

Para testar o app (front e API) sem cadastrar tudo à mão, a API cria na subida tutores com pets e anfitriões **já verificados**, com cantinho e os pets da casa. Tudo fictício, em Maringá (PR), perto o bastante para aparecer junto na busca por raio.

## 1. Como ligar

No `.env`:

```env
SEED_TEST_DATA_ENABLED=true
SEED_TEST_DATA_PASSWORD=Teste@1234
```

| Configuração | Variável | Padrão | |
|---|---|---|---|
| `Seed:TestData:Enabled` | `SEED_TEST_DATA_ENABLED` | `false` | Liga o seed. |
| `Seed:TestData:Password` | `SEED_TEST_DATA_PASSWORD` | — | A **mesma senha** para todas as contas. Precisa seguir a regra de senha forte do cadastro (senão a conta é recusada e fica só no log). Vazia com o seed ligado: nada é criado (aviso no log). |

Roda em toda subida, depois da inicialização de todos os módulos. **Idempotente pelo e-mail**: pessoa que já tem a conta fica como está (nem troca a senha), sem pets nem cantinho novos. Para recriar do zero, apague o volume do Postgres.

## 2. Contas

Login normal (`POST /api/v1/auth/sessions/login`) com a senha de `SEED_TEST_DATA_PASSWORD` e o `role` da tabela.

| Quem | E-mail | `role` | CPF | Endereço | Pets |
|---|---|---|---|---|---|
| Ana Souza | `tutora.ana@pethost.local` | `owner` | 529.982.247-25 | Av. Brasil, 1500 — Zona 1 | Thor (cão, Labrador, grande), Mel (gata, pequena) |
| Bruno Lima | `tutor.bruno@pethost.local` | `owner` | 111.444.777-35 | R. Néo Alves Martins, 2100 — Zona 3 | Pipoca (cã, Shih-tzu, pequena, com colírio), Kiwi (calopsita) |
| Carla Mendes | `anfitria.carla@pethost.local` | `host` | 390.533.447-05 | R. Pioneiro Antônio de Ganello, 450 — Zona 7 | Bob (cão da casa, médio) |
| Diego Rocha | `anfitriao.diego@pethost.local` | `host` | 935.411.347-80 | Av. Herval, 800 — Zona 1 | Luna (gata da casa) |
| Elisa Prado | `anfitria.elisa@pethost.local` | `host` | 286.754.031-35 | R. das Palmeiras, 250 — Jardim Alvorada | — |

## 3. Cantinhos

Um por anfitrião, no endereço da conta.

| Anfitrião | Cantinho | Casa | Aceita | Portes | Vagas | Diária | Dias |
|---|---|---|---|---|---|---|---|
| Carla | Casa com quintal na Zona 7 | casa com quintal | cães | P, M, G | 3 | R$ 80,00 | todos |
| Diego | Apartamento tranquilo para gatos | apartamento | gatos | P, M | 2 | R$ 55,00 | seg a sex |
| Elisa | Chácara urbana para cães, gatos e aves | casa com quintal, crianças | cães, gatos, calopsita, periquito, coelho | P, M | 5 | R$ 65,50 | todos |

Os pets foram escolhidos para testar a compatibilidade: o Thor (cão grande) só cabe no cantinho da Carla; a Mel (gata), no do Diego e no da Elisa; a Kiwi (calopsita), só no da Elisa.

## 4. Limites

- **Stripe de mentira.** Os anfitriões ficam com uma conta conectada inventada (`acct_seed` + CPF) e os repasses marcados como liberados, para os cantinhos aparecerem na busca sem passar pelo cadastro de recebimento. Pedir, aceitar e recusar reserva funciona; **pagar não** (o Stripe não conhece a conta). Para testar o pagamento, use o anfitrião de demonstração ([`modulo-hosts.md`](modulo-hosts.md) seção 8) com uma conta conectada de verdade. A releitura periódica no Stripe falha para essas contas e não muda nada (fica no log).
- **Sem fotos.** Cantinhos, pets e contas ficam sem foto; envie pelo app se precisar.
- **Coordenada fixa.** O cantinho já vem com a coordenada no catálogo: a subida não consulta o Nominatim. Mudar o endereço pelo PATCH geocodifica normalmente.
- **Só dados fictícios.** Os CPFs são inventados (com dígitos verificadores válidos) e os e-mails são `@pethost.local`. Não use em produção.

## 5. Onde está

| Peça | Onde | O que faz |
|---|---|---|
| `TestDataCatalog` | `PetHost.Api/Seed` | As pessoas, pets e cantinhos da seção 2 e 3. Mudar os dados é aqui. |
| `TestDataSeeder` | `PetHost.Api/Seed` | Cruza os módulos, por isso mora na API (a composição). Cada parte entra pelo fluxo do próprio módulo: tutor e pet pelos handlers da API (com a validação), anfitrião pelo `VerifiedHostSeeder`, cantinho pelo `ListingSeeder`. Falhar numa pessoa não impede as outras nem a subida. |
| `VerifiedHostSeeder` | `Hosts.Infrastructure/Persistence/Seed` | Conta + solicitação aprovada + perfil de anfitrião, pelo mesmo fluxo do domínio. Também usado pelo anfitrião de demonstração. Com `SimulatedPayouts`, liga a conta de mentira e libera os repasses. |
| `ListingSeeder` | `Listings.Infrastructure/Persistence/Seed` | Cantinho pelas regras do cadastro (anfitrião ativo e com repasses, limite, descrição e endereço pelo domínio), com a coordenada pronta. |

Testes: `TestDataSeedTests` (`Bookings.IntegrationTests`, que sobe todos os módulos envolvidos) — o tutor vê os pets dele, a busca acha os três cantinhos, o anfitrião vê o cantinho e os pets da casa; rodar de novo não duplica nada.
