# PetHost

### O que o MVP tem, o que não tem, e por quê

_Documento de escopo · versão enxuta · outubro de 2026 · Dicionário de dados completo em [`dicionario-de-dados.md`](dicionario-de-dados.md)_

---

**Em uma frase.** O PetHost é o "deixa com o vizinho" que funciona: qualquer pessoa com espaço e jeito com bicho abre um **cantinho** na própria casa para hospedar pets, e quem vai viajar encontra alguém de confiança perto de casa, conversa, paga e fica tranquilo.

> **O MVP está pronto quando…**
>
> A Camila acha a Dona Cida no Jardim Alvorada, conversa com ela pelo chat, reserva 5 noites para a Pipoca, paga com Pix ou cartão, recebe foto da Pipoca durante a viagem, busca a Pipoca e avalia. E a Dona Cida recebe o dinheiro dela na conta.
>
> Tudo isso **sem ninguém sair do PetHost**. O que não for necessário para essa história acontecer, fica para depois.

*Como ler: as seções 1 a 3 são o essencial (10 minutos). O resto é consulta.*

## 1. Para quem é

Cada funcionalidade do PetHost existe para responder a um medo concreto de alguém. Se uma tela não responde a nenhum desses medos, ela provavelmente não precisa existir no MVP.

#### Camila, a tutora

31 anos, analista, mora num apartamento na Zona 7, em Maringá. Tem a Pipoca, vira-lata de 4 anos, porte médio, morre de medo de fogos. Vai passar o Natal com os pais em Curitiba. Hotel de pet é caro e a Pipoca fica numa baia.

**Medo:** "Quem é essa pessoa? Vão dar o remédio na hora? Vou ficar sem notícia?"

**Precisa:** ver quem é a pessoa e a casa, conversar antes de fechar, receber foto durante a viagem.

#### Dona Cida, a anfitriã

58 anos, aposentada, casa com quintal murado no Jardim Alvorada. Já criou três cachorros; hoje tem o Bóris, um labrador idoso e calmo. Quer uns R$ 800 a mais por mês. Usa WhatsApp, mas não é "da tecnologia".

**Medo:** "E se vier um cachorro bravo? E se destruir minha casa? E se não me pagarem?"

**Precisa:** saber exatamente que bicho vem, poder recusar, ter certeza de que vai receber, e telas simples com botões grandes.

#### Lucas, o anfitrião de apartamento

24 anos, estudante, trabalha de casa num apê pequeno perto da UEM. Só aceita gatos e pets pequenos. É por causa dele que tipo e porte aceitos importam tanto.

**Medo:** "Vão me mandar um pastor-alemão."

**Precisa:** filtrar quem pode pedir, antes do pedido chegar.

### Medo → o que o PetHost faz

| Medo                                 | Resposta no produto                                                                                                 |
|------------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| **"Não sei quem é essa pessoa" (tutor)** | Anfitrião com documento e selfie conferidos pelo admin + recebimento verificado pelo Stripe, avaliações públicas, selos e conversa antes de reservar. |
| **"Vou ficar sem notícia" (tutor)**      | Chat com foto durante a estadia. O chat é o diário.                                                                     |
| **"Vão esquecer o remédio" (tutor)**     | A ficha do pet vai junto com a reserva: remédio, horário, alimentação, manias.                                          |
| **"Vai vir um bicho bravo" (anfitrião)** | Anfitrião escolhe tipos e portes aceitos, lê a ficha antes e aceita ou recusa cada pedido. Aviso "Combina com seu pet". |
| **"Não vão me pagar" (anfitrião)**       | O tutor paga antes da estadia. O PetHost segura o dinheiro e repassa no check-out.                                      |
| **"Paguei e o anfitrião sumiu" (tutor)** | Se o anfitrião cancelar, o tutor recebe 100% de volta.                                                                  |

## 2. O jeito do PetHost

A ideia é que o produto não pareça um sistema genérico. Ele tem quatro princípios, e eles valem para texto, tela e regra de negócio:
- **Pet tem nome.** Se sabemos o nome, usamos. "A Pipoca chegou na casa da Dona Cida", nunca "Check-in realizado".
- **Bairro, não coordenada.** Mostramos "Jardim Alvorada · 2 km de você". Confiança nasce da proximidade.
- **Capacidade honesta.** O anfitrião diz quantos pets cabem ao mesmo tempo, e o sistema nunca passa disso. Quem cuida de três não recebe o quarto.
- **Sem letra miúda.** O valor total aparece desde o primeiro clique. O tutor paga exatamente o que vê.

### Tom de voz: fala como vizinho, não como banco

| Em vez de…               | Escreva…                                                                           |
|------------------------------|----------------------------------------------------------------------------------------|
| Reserva confirmada.          | Fechado! A Dona Cida espera a Pipoca dia 20, a partir das 14h.                         |
| Nenhum resultado encontrado. | Ninguém livre por aqui nessas datas. Que tal aumentar a distância?                     |
| Check-in realizado.          | A Pipoca chegou!                                                                       |
| Pagamento aprovado.          | Pagamento guardado com a gente. A Dona Cida só recebe quando a Pipoca voltar pra casa. |
| Campo obrigatório.           | Falta dizer o porte da Pipoca.                                                         |

### As duas metáforas pedidas pela disciplina
- **Vizinhança** organiza a descoberta: busca por bairro, distância, selo "Vizinho", reputação local.
- **Hospedagem** organiza a estadia: cantinho, reserva, chegada, "voltou pra casa", avaliação.

## 3. O que tem e o que não tem

### 3.1 Entra no MVP

| Funcionalidade          | Como fica (versão simples)                                                                                                                                                                                                         |
|-----------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Contas separadas**        | Conta de **tutor** ou de **anfitrião**, escolhida no cadastro. Pode usar o mesmo e-mail nas duas e alternar entre elas; cada uma pode ser inativada sem afetar a outra. O cadastro de tutor cria a conta (dados comuns e endereço) e o perfil de tutor (CPF) num pedido só. Admin é criado direto no banco.                                                                                                      |
| **Tipos de pet**            | Lista fixa: cachorro, gato, calopsita, papagaio, periquito, canário, coelho, hamster, porquinho-da-índia, peixe, tartaruga/jabuti e **exótico** (o tutor descreve o animal; o anfitrião decide se aceita no pedido e na conversa). Porte (P, M, G): obrigatório para cachorro, opcional para gato. |
| **Cadastro de pet (ficha)** | Nome, foto, tipo, raça (texto livre), porte, idade, sexo, castrado, vacinas em dia, remédio e horário, alimentação, se dá bem com cães, gatos e crianças, veterinário, peso aproximado, microchip, alergias e "coisas que só quem convive sabe". O **anfitrião** também cadastra os pets que moram na casa dele: o tutor vê com quais animais o pet vai conviver. |
| **Cantinho**                | Fotos (até 5, em ordem escolhida pelo anfitrião), descrição, bairro e endereço (a coordenada é calculada pela API), tipo de casa, tem quintal, tem cães, gatos ou crianças em casa, tipos e portes aceitos, capacidade (quantos pets ao mesmo tempo, de tutores diferentes), diária por pet (em reais), regras da casa e **dias da semana em que recebe**. Pessoa física tem **um** cantinho (até 10 pets); empresa tem **um por filial** (até 200 pets cada, tipo de casa opcional). Detalhes em [`modulo-listings.md`](modulo-listings.md). |
| **Agenda**                  | Tudo disponível por padrão. O anfitrião bloqueia datas. Estadias pagas bloqueiam sozinhas.                                                                                                                                             |
| **Verificação**             | **Solicitação de cadastro de anfitrião:** CPF, tipo de documento (RG, CNH ou CIN), frente, verso e selfie segurando o documento, num bucket privado. O admin confere e aprova (aí nasce o perfil de anfitrião) ou recusa com motivo (pode tentar de novo até 3 vezes; fraude é definitiva). As fotos são apagadas depois da decisão. Depois da aprovação, o cadastro de recebimento no Stripe. Sem os dois, o cantinho não aparece na busca. Detalhes em [`modulo-hosts.md`](modulo-hosts.md). |
| **Busca**                   | **Por raio** (5 a 50 km) a partir do endereço do tutor ou de um ponto enviado (latitude e longitude); depois, datas + qual pet vai. Resultado em lista com foto, bairro, distância, preço, nota e selos. (Feito: raio, distância e ordem; filtros somados por estado, cidade, bairro, espécie, porte, dias da semana, faixa de diária, capacidade, quintal, tipo da casa e quem mora nela (cachorro, gato, criança) — com ou sem raio. Datas, vaga para os pets e selos dependem da agenda e das reservas.) |
| **Chat**                    | Botão "Puxar conversa" no cantinho. Texto e foto. Uma conversa por dupla tutor–anfitrião. Durante a estadia, é ali que chegam as fotos.                                                                                                |
| **Reserva**                 | Pedir → anfitrião aceita ou recusa (24h) → tutor paga (24h) → confirmada → "chegou" → "voltou pra casa".                                                                                                                               |
| **Pagamento**               | Stripe Connect com checkout transparente: cartão e Pix dentro do PetHost. Detalhes na seção 6.                                                                                                                                         |
| **Avaliação**               | Depois do check-out, cada lado avalia o outro: nota de 1 a 5 e comentário, em até 7 dias.                                                                                                                                              |
| **Selos automáticos**       | Calculados, não cadastrados: Vizinho, Responde rápido, Casa com quintal, Experiente, Bem avaliado (regras na seção 7).                                                                                                                 |
| **"Combina com seu pet"**   | Ao ver um cantinho, o sistema cruza a ficha do pet com a casa e com os outros hóspedes do período: "A Pipoca não se dá com gatos e aqui tem gato" ou "Nessas datas a Dona Cida também recebe o Thor, cão grande". Avisa, não bloqueia. |
| **Painel do anfitrião**     | Pedidos novos, próximas estadias e quanto recebeu no mês.                                                                                                                                                                              |
| **Admin mínimo**            | Aprovar anfitriões: fila de solicitações, dados da conta lado a lado com as fotos, aprovar ou recusar com motivo (cada foto aberta fica na trilha). Uma tela (tipos de pet são uma lista fixa). Suspender conta e liberar CPF em disputa; trilha de auditoria de tudo.                                                                                                                                                                               |

### 3.2 Fica de fora

| Item                                               | Por quê                                                         |
|--------------------------------------------------------|---------------------------------------------------------------------|
| **App nativo**                                         | A versão web responsiva já roda no celular.                         |
| **Mapa**                                               | Bairro e distância bastam para a vizinhança. Entra se sobrar tempo. |
| **Diário separado**                                    | O chat com foto faz esse papel.                                     |
| **Verificação automática de identidade**               | Stripe Identity não atende conta brasileira (out/2026); serviços nacionais (Serpro Datavalid, unico, idwall) são pagos. A conferência é manual, pelo admin. |
| **Notificação push e por e-mail**                      | Aviso dentro do app (contador de não lidas).                        |
| **Áudio, vídeo, "digitando…", confirmação de leitura** | Não muda a confiança; custa tempo.                                  |
| **Preço por temporada, desconto semanal, cupom**       | Diária fixa por pet resolve.                                        |
| **Seguro, cobertura veterinária, mediação de disputa** | Envolvem parceria e análise jurídica.                               |
| **Passeio, creche, banho**                             | Outro modelo de reserva (por hora).                                 |
| **Várias cidades e idiomas**                           | Piloto só em Maringá.                                               |

### 3.3 Se sobrar tempo (nessa ordem)
1. Recuperar senha por e-mail.
2. Mapa com Leaflet e OpenStreetMap (gratuito).
3. Avisos por e-mail (pedido novo, pagamento confirmado).
4. Filtro por faixa de preço.

## 4. Contas e o que cada uma vê

| Conta     | Menu                                                                 | Pode                                                                                                           |
|---------------|--------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------|
| **Tutor**     | Buscar · Meus pets · Minhas estadias · Conversas · Perfil                | Cadastrar pets, puxar conversa, pedir e pagar estadia, cancelar, avaliar o anfitrião.                              |
| **Anfitrião** | Minha verificação · Meu cantinho · Meus pets · Agenda · Pedidos · Estadias · Conversas · Ganhos · Perfil | Enviar a verificação (documento e selfie); depois de aprovado, montar o cantinho, cadastrar os pets da casa, conectar o Stripe, bloquear datas, aceitar ou recusar, marcar chegada e saída, avaliar o tutor. |
| **Admin**     | Anfitriões aguardando                                                    | Ver documento, selfie e dados da conta; aprovar ou recusar o anfitrião com motivo.                                  |

**Login com o mesmo e-mail:** a tela de entrar tem a escolha "Sou tutor / Sou anfitrião". O e-mail é único **dentro de cada tipo**, então a Dona Cida pode ter as duas contas com o mesmo e-mail, cada uma com sua senha.

## 5. Como uma estadia acontece
1. A Camila busca por "Zona 7", de 20 a 25 de dezembro, para a Pipoca. Aparecem só os cantinhos que aceitam cachorro médio e têm vaga nessas datas.
2. Ela abre o cantinho da Dona Cida e vê o aviso "A Pipoca combina com essa casa". Toca em **Puxar conversa** e pergunta sobre fogos.
3. Pede a estadia: datas, Pipoca, total de R$ 300 (R$ 60 × 5 noites × 1 pet). Status: **Solicitada**.
4. A Dona Cida lê a ficha e aceita. Status: **Aceita**. A Camila tem 24h para pagar.
5. A Camila paga com Pix dentro do PetHost. O Stripe confirma por webhook. Status: **Confirmada**. O endereço exato e o telefone são liberados.
6. No dia 20, a Dona Cida toca em **A Pipoca chegou**. Status: **Em andamento**. As fotos vão pelo chat.
7. No dia 25, toca em **A Pipoca voltou pra casa**. Status: **Concluída**. O repasse de 90% vai para a conta Stripe dela.
8. As duas se avaliam em até 7 dias.

| Status       | Quando                         | Para onde vai                                          |
|------------------|------------------------------------|------------------------------------------------------------|
| **Solicitada**   | Tutor pediu                        | Aceita, Recusada, Expirada (24h sem resposta) ou Cancelada |
| **Aceita**       | Anfitrião aceitou                  | Confirmada (pagou), Expirada (24h sem pagar) ou Cancelada  |
| **Confirmada**   | Pagamento confirmado pelo Stripe   | Em andamento ou Cancelada (com reembolso)                  |
| **Em andamento** | Anfitrião marcou "chegou"          | Concluída                                                  |
| **Concluída**    | Anfitrião marcou "voltou pra casa" | Fim. Libera repasse e avaliação.                           |

## 6. Pagamento com Stripe Connect

O PetHost é a plataforma no Stripe; cada anfitrião é uma **conta conectada**. Usamos o modelo de **cobrança e transferência separadas**: o tutor paga o PetHost, e o PetHost transfere para o anfitrião só no fim. Tudo em **modo de teste** (sem dinheiro de verdade).

| Etapa             | O que acontece                                                                                                                                                                                        |
|-----------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Anfitrião conecta** | Depois que o admin aprova a verificação, faz o cadastro de recebimento com o componente embutido do Stripe. Guardamos o hosts.stripe_account_id e só liberamos o cantinho quando o Stripe avisa que a conta pode receber (hosts.stripe_payouts_enabled). |
| **Tutor paga**        | A API cria a cobrança (valor total, BRL, ligada à reserva). A tela usa o Payment Element: cartão e Pix dentro do PetHost, QR code do Pix na própria página.                                               |
| **Confirmação**       | Só via webhook. A reserva vira Confirmada quando o Stripe avisa que o pagamento deu certo, nunca pelo retorno do navegador.                                                                               |
| **Repasse**           | Em "voltou pra casa", a API transfere **90%** para o anfitrião, amarrado à cobrança original. **10%** ficam com o PetHost, e é dessa parte que sai a taxa do Stripe.                                      |
| **Cancelamento**      | Como o repasse só acontece no fim, cancelar antes da chegada é só um reembolso.                                                                                                                           |

| Quem cancela | Quando                          | Tutor recebe de volta                 |
|------------------|-------------------------------------|-------------------------------------------|
| Tutor            | 48h ou mais antes da chegada        | 100%                                      |
| Tutor            | Menos de 48h antes                  | 50% (os outros 50% seguem o 90/10 normal) |
| Anfitrião        | A qualquer momento antes da chegada | 100%                                      |

> **Para testar na semana 1**
>
> Confirmar como simular um Pix pago no modo de teste e usar a Stripe CLI para mandar webhooks para a máquina local. É a parte mais nova para o grupo; melhor descobrir cedo.

## 7. Regras que valem
1. Cantinho só aparece na busca com anfitrião verificado (ok do admin na solicitação), ativo e sem suspensão + Stripe liberado + cantinho ativo. O onboarding do Stripe só abre depois da aprovação.
2. **Capacidade:** em nenhum dia do período a soma dos pets de reservas Confirmadas ou Em andamento, mais os pets do novo pedido, pode passar da capacidade do cantinho. Tutores diferentes podem dividir o mesmo período.
3. Uma reserva é de um tutor só e leva 1 ou mais pets dele. Todos precisam ser de tipo e porte aceitos. A busca só mostra cantinhos com vaga para todos os pets escolhidos.
4. Valor = diária × noites × número de pets. Noites = data de saída − data de chegada.
5. Anfitrião tem 24h para responder; tutor tem 24h para pagar depois do aceite. Senão, Expirada.
6. Datas só ficam bloqueadas quando a reserva é paga (Confirmada).
7. Antes do pagamento, o tutor vê só o bairro. Endereço e telefone aparecem depois.
8. O tutor leva a ração e entrega o remédio. A tela de pedido lembra disso, e o tutor confirma.
9. Avaliação: só depois de Concluída, uma por lado, em até 7 dias, nota inteira de 1 a 5.
10. Selos: **Vizinho** = mesmo bairro do tutor · **Responde rápido** = média de resposta abaixo de 2h · **Casa com quintal** = marcado no cantinho · **Experiente** = 5+ estadias concluídas · **Bem avaliado** = nota 4,5+ com 3+ avaliações.
11. "Combina com seu pet": compara "se dá bem com cães, gatos e crianças" do pet com "tem cães, gatos e crianças" da casa. Só avisa.

## 8. Modelo entidade-relacionamento

Modelo lógico do MVP: 16 tabelas. Nomes em inglês e snake_case, no padrão de mercado; o app continua em português. O **dicionário de dados** ([`dicionario-de-dados.md`](dicionario-de-dados.md)) explica cada tabela e coluna.

| No app                    | No banco                      |
|-------------------------------|-----------------------------------|
| **Tutor · Anfitrião · Admin** | users.role = owner · host · admin |
| **Dados do tutor (CPF)**      | owners                            |
| **Anfitrião verificado**      | hosts                             |
| **Verificação (documento e selfie)** | host_applications · host_application_documents |
| **Cantinho**                  | listings                          |
| **Reserva / estadia**         | bookings                          |
| **Avaliação**                 | reviews                           |
| **Conversa · Mensagem**       | conversations · messages          |

Notação pé-de-galinha (no Mermaid): `||` exatamente um · `o{` zero ou muitos · `|{` um ou muitos · `o|` zero ou um. **PK** chave primária · **FK** chave estrangeira · **UK** único (mesmo número = único composto) · `"nullable"` aceita nulo.

```mermaid
erDiagram
    users ||--o| owners : "is owner"
    users ||--o| hosts : "is host"
    users ||--o{ host_applications : "applies"
    host_applications ||--o{ host_application_documents : "has"
    host_applications ||--o| hosts : "approved as"
    owners ||--o{ pets : "owns"
    hosts ||--o{ pets : "lives with"
    hosts ||--o| listings : "hosts"
    listings ||--o{ listing_photos : "has"
    listings ||--|{ listing_species : "accepts"
    listings ||--o{ availability_blocks : "blocks"
    users ||--o{ bookings : "requests"
    listings ||--o{ bookings : "receives"
    bookings ||--|{ booking_pets : "includes"
    pets ||--o{ booking_pets : ""
    bookings ||--o| payments : "paid by"
    bookings ||--o{ reviews : "reviewed in"
    users ||--o{ reviews : "writes / receives"
    users ||--o{ conversations : "participates"
    conversations ||--o{ messages : "contains"
    users ||--o{ messages : "sends"
    users {
        uuid id PK
        varchar email UK
        enum role UK
    }
    owners {
        uuid id PK
        uuid user_id FK, UK
        char cpf UK
    }
    hosts {
        uuid id PK
        uuid user_id FK, UK
        enum person_type
        char cpf
        char cnpj UK "nullable"
        varchar stripe_account_id UK "nullable"
        uuid application_id UK
    }
    host_applications {
        uuid id PK
        uuid user_id FK
        enum status
        char cpf
    }
    host_application_documents {
        uuid id PK
        uuid application_id FK, UK
        enum kind UK
    }
    pets {
        uuid id PK
        uuid owner_id FK "nullable"
        uuid host_id FK "nullable"
        enum species
    }
    listings {
        uuid id PK
        uuid host_id FK, UK
    }
    listing_photos {
        uuid id PK
        uuid listing_id FK
    }
    listing_species {
        uuid listing_id PK, FK
        enum species PK
    }
    availability_blocks {
        uuid id PK
        uuid listing_id FK
    }
    bookings {
        uuid id PK
        uuid owner_id FK
        uuid listing_id FK
        uuid cancelled_by_id FK "nullable"
    }
    booking_pets {
        uuid booking_id PK, FK
        uuid pet_id PK, FK
    }
    payments {
        uuid id PK
        uuid booking_id FK, UK
        varchar stripe_payment_intent_id UK
    }
    stripe_events {
        uuid id PK
        varchar stripe_event_id UK
    }
    conversations {
        uuid id PK
        uuid owner_id FK, UK
        uuid host_id FK, UK
    }
    messages {
        uuid id PK
        uuid conversation_id FK
        uuid sender_id FK
    }
    reviews {
        uuid id PK
        uuid booking_id FK, UK
        uuid author_id FK, UK
        uuid reviewee_id FK
    }
```

_Aqui só as chaves. Todas as colunas estão no dicionário de dados e na versão em imagem: [`er.png`](er.png)._

### Relacionamentos e cardinalidade

| Relacionamento                 | Cardinalidade | Observação                                   |
|------------------------------------|-------------------|--------------------------------------------------|
| **users → owners**                 | 1 : 0..1          | Só contas owner. Criado junto no cadastro.       |
| **users → hosts**                  | 1 : 0..1          | Só contas host. Pessoa física ou jurídica.       |
| **owners → pets**                  | 1 : N             | Pets do tutor.                                   |
| **hosts → pets**                   | 1 : N             | Pets da casa do anfitrião. Cada pet tem um dono. |
| **users → host_applications**      | 1 : N             | Verificações da conta; no máximo uma aberta.     |
| **host_applications → documents**  | 1 : 0..3          | Frente, verso e selfie; apagadas após a decisão. |
| **host_applications → hosts**      | 1 : 0..1          | A aprovada cria o anfitrião.                     |
| **hosts → listings**               | 1 : N             | Pessoa física: no máximo um. Empresa: um por filial. |
| **listings → listing_photos**      | 1 : N             | Até 5 fotos.                                     |
| **listings → availability_blocks** | 1 : N             | Períodos fechados pelo anfitrião.                |
| **users → bookings**               | 1 : N             | O tutor faz várias reservas.                     |
| **listings → bookings**            | 1 : N             | Várias no mesmo período, limitadas por capacity. |
| **bookings → pets**                | N : N             | Com pets, via booking_pets.                      |
| **bookings → payments**            | 1 : 0..1          | Criado quando o tutor vai pagar.                 |
| **bookings → reviews**             | 1 : 0..2          | Uma por lado.                                    |
| **users → reviews**                | 1 : N             | author_id e reviewee_id.                         |
| **users → conversations**          | 1 : N             | owner_id e host_id; uma por dupla.               |
| **conversations → messages**       | 1 : N             |                                                  |
| **users → messages**               | 1 : N             | sender_id.                                       |

### Restrições que o modelo precisa garantir
1. users: (email, role) único. O mesmo e-mail pode existir uma vez como owner e uma vez como host. owners: cpf único; só pode ser trocado até o primeiro pagamento.
2. pets.owner_id aponta para owners.id e pets.host_id para hosts.id — exatamente um dos dois (CHECK). bookings.owner_id e conversations.owner_id apontam para users com role = owner; listings.host_id para hosts.id; conversations.host_id para role = host. FKs lógicas, validadas na aplicação.
3. booking_pets: todo pet precisa ser do mesmo tutor da reserva (pet de anfitrião não se hospeda), e de tipo e porte aceitos pelo cantinho.
4. bookings: check_out_date \> check_in_date; pet_count = linhas em booking_pets; nightly_rate_cents é cópia do preço no momento do pedido.
5. Capacidade: em nenhum dia, a soma de pet_count das reservas confirmed ou in_progress do cantinho passa de listings.capacity.
6. Dinheiro sempre em centavos inteiros (\*\_cents), como o Stripe. payments: platform_fee_cents + host_payout_cents = amount_cents − refunded_cents.
7. reviews.rating entre 1 e 5; só para bookings com status completed.
8. stripe_events.stripe_event_id único: webhook repetido não é processado duas vezes.
9. **CPF identifica a pessoa; o e-mail identifica o login.** Um CPF aparece em no máximo uma conta de tutor e uma de anfitrião, e só se as duas forem do **mesmo e-mail** (a mesma pessoa). CPF já usado por outro e-mail, em qualquer papel, é recusado (`409`). Vale entre tutores e anfitriões (inclusive o representante de empresa e as solicitações abertas), pelo contrato `ICpfOwnership` ([`modulo-hosts.md`](modulo-hosts.md) seção 4.1).
10. **Titularidade do CPF:** validar o dígito não prova que o CPF é de quem digitou. O anfitrião é verificado pelo admin (documento com foto e selfie conferidos contra os dados da conta) e, depois, pelo Stripe Connect (CPF, nome e nascimento conferidos na Receita) antes de receber. O tutor não é verificado no MVP — opções: consulta CPF + nome + nascimento (Serpro Datavalid) ou Stripe Identity no primeiro pagamento. Quem tem o CPF recusado porque outra conta já o usa fala com o suporte; o admin confere o documento, libera o CPF e inativa a conta indevida.
9. Nada com histórico é apagado: pets e cantinhos são desativados (is_active).

## 9. Telas

| Quem      | Telas                                                                                                                                                                                        |
|---------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Todos**     | Entrar · Criar conta · Conversas · Conversa · Perfil                                                                                                                                             |
| **Tutor**     | Buscar · Resultados · Cantinho (detalhe) · Meus pets · Ficha do pet · Pedir estadia · Pagar · Minhas estadias · Estadia · Avaliar                                                                |
| **Anfitrião** | Minha verificação (aviso de uso das fotos, documento, frente, verso, selfie, status e motivo da recusa) · Montar meu cantinho (passo a passo, com simulação de quanto pode ganhar) · Pets da casa · Conectar Stripe · Agenda · Pedidos · Pedido (com a ficha do pet) · Estadias · Estadia em andamento · Ganhos · Avaliar |
| **Admin**     | Anfitriões aguardando                                                                                                                                                                            |

O protótipo HTML atual serve como **mapa de telas**, não como visual final. O visual deve seguir a seção 2: pouca coisa por tela, botões grandes (pensando na Dona Cida), textos com o nome do pet e a ficha do pet com cara de carteirinha.

## 10. Plano de 4 semanas

| Semana | Entrega                                                                                                                                                              |
|------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **1**      | Base: projeto .NET, banco, login com dois tipos de conta, ficha do pet (tutor e anfitrião). Decidir o front. Teste isolado do Stripe (Pix em modo de teste + webhook local).   |
| **2**      | Verificação do anfitrião (solicitação, fotos no bucket privado, fila e decisão do admin — feito), cantinho + conexão com Stripe, agenda, busca, tela do cantinho com selos e "Combina com seu pet".                                                           |
| **3**      | Reserva completa (pedir, aceitar, recusar, expirar), pagamento com Payment Element, webhook, chegada e saída, repasse, chat.                                             |
| **4**      | Avaliação, cancelamento com reembolso, painel do anfitrião, revisão de todos os textos no tom da seção 2, dados de demonstração com bairros de Maringá, testes e ensaio. |

## 11. Em aberto e o que mudou

### Decisões em aberto
- **Front-end.** Recomendação: React, porque o Payment Element e o cadastro embutido do Stripe têm versão oficial para React. Blazor ou Razor funcionam, mas precisam de ponte com JavaScript.
- **Banco.** PostgreSQL ou SQL Server. Escolher o que mais gente do grupo já conhece.
- **Chat em tempo real.** SignalR (já vem no ASP.NET Core) ou consulta a cada poucos segundos. Começar pela consulta e trocar se der tempo.
- **Fotos.** ~~Pasta local no servidor no MVP.~~ Decidido: bucket S3 compatível (RustFS no compose; R2 ou Supabase em produção, ver [`armazenamento-de-imagens.md`](armazenamento-de-imagens.md)). Documentos e selfies da verificação ficam num **bucket privado** separado.

### O que muda em relação à versão anterior

| Antes                                            | Agora                                             |
|------------------------------------------------------|-------------------------------------------------------|
| Uma conta com modo tutor e modo anfitrião            | Duas contas separadas (pode usar o mesmo e-mail)      |
| Sem pagamento; tutor e anfitrião combinavam por fora | Stripe Connect com cartão e Pix, repasse no check-out |
| Sem chat; telefone liberado após confirmação         | Chat com texto e foto                                 |
| Diário da estadia como módulo próprio                | Fotos pelo chat                                       |
| Verificação com selfie, documento, CPF e comprovante | Documento (RG, CNH ou CIN) + selfie em bucket privado, conferidos pelo admin, depois o cadastro do Stripe. Sem comprovante de endereço. |
| Busca com mapa e raio                                | Busca por bairro, mapa só se sobrar tempo             |
| Raças cadastradas pelo admin, recusa por raça        | Raça em texto livre; filtro por tipo e porte          |

**Mantido da versão anterior:** API em ASP.NET Core, JWT, testes com xUnit, Git e GitHub, ração e remédio por conta do tutor, taxa de 10%.

### Ideias guardadas para depois
- **Boletim do dia:** chips "Comeu bem / Passeou / Tomou remédio / Dormiu bem" + foto, viram um card no chat.
- **Checklist de chegada:** ao marcar "chegou", confirmar ração, remédio e coleira.
- **Botão de emergência:** veterinário do pet e contato do tutor em um toque.
- **Currículo do pet:** o anfitrião também avalia o pet; o histórico ajuda nas próximas estadias.
- **Cartão-postal da estadia:** as fotos do chat viram um álbum no fim.
- **Visita de reconhecimento:** propor pelo chat uma visita antes da estadia.
