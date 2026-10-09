# PetHost --- Documentação do Projeto {#pethost-documentação-do-projeto}

Oct 7, 2026 · @Gu

## 1. Definição do problema {#definição-do-problema}

Tutores que precisam viajar ou se ausentar não encontram uma forma segura, acessível e personalizada de deixar seus animais de estimação, e muitos acabam desistindo de viajar ou deixando o pet com alguém sem garantias.

As alternativas atuais têm limitações claras:

- **Hotéis e creches para pets:** ambiente impessoal, com muitos animais juntos e pouca atenção individual.

- **Pessoas conhecidas:** dependem de favores, nem sempre estão disponíveis e não oferecem nenhum tipo de garantia.

- **Cuidadores sem plataforma:** sem verificação, sem pagamento seguro e sem forma de acompanhar o pet à distância.

O resultado é a **insegurança** do tutor ao confiar seu animal a pessoas que considera estranhas. Do outro lado, há pessoas que gostam de animais e têm tempo livre, mas não têm um canal confiável para oferecer esse cuidado e gerar renda com ele.

## 2. Objetivo do sistema {#objetivo-do-sistema}

O PetHost é uma plataforma que conecta tutores a anfitriões verificados para hospedagem de animais de estimação, com segurança, transparência e acompanhamento durante toda a estadia.

**Objetivo geral:** oferecer uma alternativa confiável aos hotéis tradicionais, em que o pet fique em um ambiente caseiro com atenção individual.

**Objetivos específicos:**

- Reduzir a insegurança do tutor com verificação de anfitriões, avaliações e comunicação constante.

- Permitir que o tutor encontre, reserve e pague a hospedagem em um único lugar.

- Dar ao anfitrião uma forma simples de oferecer o serviço, gerir reservas e receber pelo trabalho.

- Incluir o público mais velho (60 a 70 anos) como anfitrião, com uma interface simples e acessível.

- Criar uma comunidade de confiança baseada em avaliações reais.

## 3. Público-alvo {#público-alvo}

O público-alvo são pessoas que têm animais de estimação e já usam, ou consideram usar, serviços como hotel para cachorro. A plataforma tem dois perfis de usuário: quem contrata o serviço (tutor) e quem o presta (anfitrião).

### 3.1 Tutor (quem contrata o serviço) {#tutor-quem-contrata-o-serviço}

- Pessoas que possuem cães, gatos ou outros animais domésticos e precisam deixá-los ao viajar, trabalhar fora ou em imprevistos.

- Usuários atuais de hotéis, creches e hospedagem para pets, ou que hoje dependem de favores de amigos e familiares.

- Pessoas que valorizam atenção individual, segurança e informações frequentes sobre o pet.

- Perfil mais conectado ao celular, acostumado com aplicativos de reserva e pagamento online.

### 3.2 Anfitrião (quem presta o serviço) {#anfitrião-quem-presta-o-serviço}

- Pessoas que gostam de animais, têm espaço em casa e tempo disponível para cuidar deles.

- Em destaque, o público de **60 a 70 anos**, que tem afinidade com animais, tempo livre e interesse em renda extra e companhia.

- Também podem ser anfitriões estudantes, autônomos e pessoas que trabalham de casa.

- Perfil que precisa de uma interface simples, com poucos passos e linguagem clara.

## 4. Pesquisa com usuários {#pesquisa-com-usuários}

A pesquisa mostra que cerca de **70%** dos participantes viram potencial em usar o PetHost, mas mesmo entre eles a insegurança em deixar o pet com desconhecidos continua presente.

| Achado | O que significa para o projeto |
|----|----|
| Cerca de 70% viram potencial no sistema | Há demanda e interesse real pela proposta. |
| Insegurança em deixar o pet com pessoas \"estranhas\" | Confiança é o principal fator de adoção: verificação, avaliações e acompanhamento são essenciais. |
| Pessoas de 60 a 70 anos se interessam em ser anfitriãs | Esse é o perfil natural de oferta: gostam de animais e têm tempo livre. A interface precisa ser simples. |

**Conclusão:** o produto só vai converter o interesse em uso se reduzir a insegurança do tutor. Por isso, os recursos de confiança entram desde o MVP.

## 5. Principais necessidades {#principais-necessidades}

O tutor precisa de confiança e controle; o anfitrião precisa de simplicidade e segurança para prestar o serviço.

### Tutor

- Confiar no anfitrião: identidade verificada, avaliações e histórico.

- Acompanhar o pet durante a estadia com fotos, vídeos e mensagens.

- Encontrar o anfitrião certo por localização, datas, porte do pet e preço.

- Pagar de forma segura e ter clareza sobre valores e cancelamento.

- Ter um plano claro para emergências, como problemas de saúde do animal.

- Comunicar a rotina, alimentação, medicação e comportamento do pet.

### Anfitrião

- Um cadastro simples e uma interface fácil de usar, mesmo com pouca experiência digital.

- Receber informações completas sobre o pet antes da estadia.

- Controlar a própria agenda e escolher quais reservas aceitar.

- Receber o pagamento de forma segura e previsível.

- Ter suporte e orientação para situações difíceis.

- Ser reconhecido por um bom trabalho, com avaliações e reputação.

## 6. Principais dificuldades {#principais-dificuldades}

As dificuldades do tutor estão ligadas à escolha e à confiança; as do anfitrião, ao uso da tecnologia e à gestão do serviço.

### Tutor

- Avaliar se um desconhecido é realmente confiável.

- Comparar opções de hospedagem com informações pouco padronizadas.

- Explicar todas as necessidades do pet de forma completa.

- Ficar sem notícias do animal durante a viagem.

- Saber o que fazer se algo der errado à distância.

### Anfitrião

- Usar aplicativos e plataformas digitais, especialmente entre o público de 60+.

- Organizar calendário, reservas e comunicação com vários tutores.

- Lidar com animais de comportamentos diferentes ou necessidades especiais.

- Definir preços e entender as regras da plataforma.

- Receber pagamentos e acompanhar os valores a receber.

## 7. Pontos de dor {#pontos-de-dor}

A dor central do tutor é o medo de que algo aconteça ao pet; a do anfitrião é a falta de segurança e de apoio ao assumir essa responsabilidade.

### Tutor

- **Medo de maus-tratos ou negligência** ao deixar o pet com alguém desconhecido.

- **Ansiedade pela falta de notícias** durante a viagem.

- **Dúvida sobre quem responde** por acidentes, fugas ou problemas de saúde.

- **Culpa e preocupação** com o bem-estar emocional do animal longe de casa.

- **Preço e qualidade incertos**, sem como comparar com segurança.

### Anfitrião

- **Receio de calotes** ou de cancelamentos de última hora.

- **Medo de ser responsabilizado** se o pet adoecer ou se machucar.

- **Insegurança com a tecnologia**, que pode afastar quem tem menos familiaridade digital.

- **Tutores que omitem informações** sobre o comportamento ou a saúde do animal.

- **Falta de reconhecimento** e de reputação construída pelo trabalho.

## 8. Personas {#personas}

Foram definidas quatro personas: duas primárias (tutores) e duas secundárias (anfitriões).

### Persona primária 1: Mariana, a tutora que viaja a trabalho

| Item | Descrição |
|----|----|
| Idade e perfil | 32 anos, analista de marketing, mora em apartamento. |
| Pet | Thor, um golden retriever de 3 anos. |
| Contexto | Viaja a trabalho cerca de uma vez por mês e hoje usa um hotel para cães. |
| Objetivos | Que o Thor tenha atenção individual e ela receba notícias todos os dias. |
| Frustrações | Sente culpa por deixá-lo em um local com muitos cães e não vê o que acontece. |
| Comportamento digital | Usa aplicativos de reserva e pagamento todos os dias. |
| O que a faria confiar | Perfil verificado, avaliações reais, fotos e vídeos durante a estadia. |

### Persona primária 2: Carlos, o tutor de primeira viagem e inseguro

| Item | Descrição |
|----|----|
| Idade e perfil | 45 anos, engenheiro, casado, mora em casa com quintal. |
| Pet | Mel, uma gata de 6 anos, tímida e que toma medicação. |
| Contexto | Precisa viajar com a família nas férias e nunca deixou a Mel com ninguém. |
| Objetivos | Garantir que a Mel receba a medicação e fique tranquila. |
| Frustrações | Não confia em desconhecidos e teme que ninguém dê a atenção que ela precisa. |
| Comportamento digital | Usa o celular para o essencial e pesquisa muito antes de decidir. |
| O que o faria confiar | Conversar antes com o anfitrião, ver a casa e saber como acionar ajuda em emergências. |

### Persona secundária 1: Dona Lúcia, a anfitriã

| Item | Descrição |
|----|----|
| Idade e perfil | 66 anos, aposentada, viúva, mora em casa com quintal. |
| Contexto | Sempre teve animais, tem tempo livre e sente falta de companhia. |
| Objetivos | Cuidar de animais, fazer uma renda extra e se sentir útil. |
| Frustrações | Teme não conseguir usar a tecnologia e ser responsabilizada por problemas. |
| Comportamento digital | Usa WhatsApp e redes sociais de forma básica. |
| O que a faria confiar | Cadastro simples, suporte de pessoas reais e pagamento garantido. |

### Persona secundária 2: Lucas, o anfitrião universitário

| Item | Descrição |
|----|----|
| Idade e perfil | 20 anos, estudante de graduação, mora com a família em uma casa com quintal. |
| Contexto | Paga parte da faculdade por conta própria e precisa de uma renda extra com horários flexíveis, que caibam entre as aulas. |
| Objetivos | Juntar dinheiro para a mensalidade e fazer da hospedagem de pets uma fonte de renda regular. |
| Frustrações | Poucas opções de renda flexível e medo de não ser levado a sério pelos tutores por ser jovem. |
| Comportamento digital | Usa aplicativos todos os dias e se adapta rápido a qualquer plataforma. |
| O que o faria confiar | Pagamento garantido e rápido, controle total da agenda e avaliações que construam sua reputação. |

Em contraste com Dona Lúcia, que precisa de suporte e simplicidade, Lucas valoriza agilidade, flexibilidade de agenda e um repasse previsível.

## 9. Jornada do usuário {#jornada-do-usuário}

A jornada do tutor vai da busca à avaliação da estadia; a do anfitrião vai do cadastro ao recebimento e à reputação construída.

### 9.1 Jornada do tutor {#jornada-do-tutor}

<table>
<colgroup>
<col style="width: 25%" />
<col style="width: 25%" />
<col style="width: 25%" />
<col style="width: 25%" />
</colgroup>
<thead>
<tr>
<th>Etapa</th>
<th>O que o tutor faz</th>
<th>O que sente</th>
<th>Oportunidade para o PetHost</th>
</tr>
</thead>
<tbody>
<tr>
<td>1. Descoberta</td>
<td>Percebe que vai viajar e procura onde deixar o pet.</td>
<td>Preocupação</td>
<td>Mostrar claramente a proposta de segurança e cuidado caseiro.</td>
</tr>
<tr>
<td>2. Cadastro</td>
<td>Cria a conta e o perfil do pet com porte, vacinas, rotina e saúde.</td>
<td>Cautela</td>
<td>Formulário simples e guiado.</td>
</tr>
<tr>
<td>3. Busca</td>
<td>Pesquisa anfitriões por local, datas, porte e preço.</td>
<td>Curiosidade e dúvida</td>
<td>Filtros claros e perfis completos com fotos e avaliações.</td>
</tr>
<tr>
<td>4. Avaliação</td>
<td>Lê avaliações e conversa com o anfitrião no chat.</td>
<td>Insegurança</td>
<td>Selo de verificação e chat em tempo real para tirar dúvidas.</td>
</tr>
<tr>
<td>5. Reserva e pagamento</td>
<td>Escolhe as datas e paga online.</td>
<td>Cuidado com o dinheiro</td>
<td>Pagamento seguro, retido até o fim da estadia.</td>
</tr>
<tr>
<td>6. Check-in</td>
<td><p>Leva o pet e entrega i</p>
<p>nformações e rotina.</p></td>
<td>Ansiedade</td>
<td>Checklist de entrada e confirmação pelo aplicativo.</td>
</tr>
<tr>
<td>7. Estadia</td>
<td>Recebe fotos, vídeos e mensagens.</td>
<td>Alívio e tranquilidade</td>
<td>Atualizações frequentes e contato de emergência.</td>
</tr>
<tr>
<td>8. Check-out</td>
<td>Busca o pet e confirma a saída.</td>
<td>Satisfação</td>
<td>Registro de saída e relatório da estadia.</td>
</tr>
<tr>
<td>9. Avaliação final</td>
<td>Avalia o anfitrião e a experiência.</td>
<td>Gratidão</td>
<td>Incentivo à avaliação e à reserva futura.</td>
</tr>
</tbody>
</table>

### 9.2 Jornada do anfitrião {#jornada-do-anfitrião}

| Etapa | O que o anfitrião faz | O que sente | Oportunidade para o PetHost |
|----|----|----|----|
| 1\. Descoberta | Conhece a plataforma e vê a chance de cuidar de pets e ganhar renda. | Interesse | Comunicar a renda extra e o prazer de cuidar. |
| 2\. Cadastro e verificação | Cria o perfil, envia documentos e fotos do espaço. | Receio com a tecnologia | Cadastro passo a passo, com suporte humano. |
| 3\. Configuração | Define agenda, preços, tipos de animais aceitos e regras. | Dúvida | Sugestões de preço e modelos prontos. |
| 4\. Solicitação | Recebe o pedido de reserva e vê o perfil do pet. | Expectativa | Informações completas do pet antes de aceitar. |
| 5\. Conversa | Conversa com o tutor no chat e combina detalhes. | Segurança | Chat simples, com mensagens rápidas. |
| 6\. Check-in | Recebe o pet e confirma a entrada. | Responsabilidade | Checklist e confirmação de entrada. |
| 7\. Estadia | Cuida do pet e envia fotos e atualizações. | Satisfação | Envio de fotos em poucos toques e lembretes. |
| 8\. Check-out | Entrega o pet e finaliza a reserva. | Dever cumprido | Registro de saída. |
| 9\. Pagamento e avaliação | Recebe o pagamento e a avaliação do tutor. | Reconhecimento | Repasse rápido e reputação visível. |

## 10. Descrição da solução proposta {#descrição-da-solução-proposta}

O PetHost é um aplicativo e site que conecta tutores a anfitriões verificados, oferecendo hospedagem de pets em ambiente caseiro, com pagamento seguro e acompanhamento durante a estadia.

A solução responde diretamente às dores identificadas na pesquisa:

| Dor identificada | Como o PetHost responde |
|----|----|
| Insegurança com desconhecidos | Verificação de identidade e do local, avaliações reais e perfis completos. |
| Falta de notícias do pet | Fotos, vídeos e mensagens durante a estadia, além do chat em tempo real. |
| Medo de problemas com pagamento | Pagamento online com retenção do valor até o fim da estadia. |
| Dificuldade tecnológica do anfitrião | Interface simples, cadastro guiado e suporte humano. |
| Dúvida sobre emergências | Contato de emergência e ficha de saúde do pet, acessíveis ao anfitrião. |

O tutor cadastra o pet, busca anfitriões, conversa, reserva e paga. O anfitrião cadastra o espaço, define agenda e preço, recebe reservas e envia atualizações. Ao final, os dois se avaliam, o que constrói a reputação e a confiança da comunidade.

## 11. Levantamento inicial das funcionalidades  {#levantamento-inicial-das-funcionalidades}

O tutor consegue encontrar um anfitrião confiável, reservar, pagar e acompanhar o pet, e o anfitrião consegue receber a reserva, cuidar do animal e ser pago.

### 11.1 Cadastro e login {#cadastro-e-login}

Tutor e anfitrião têm **contas separadas**. O cadastro pede nome, e-mail, telefone e senha, e o usuário aceita os termos de uso e a política de privacidade (LGPD). O e-mail é confirmado por um link, e o login é feito com e-mail e senha, com opção de recuperar a senha.

- **Conta do tutor:** logo após o cadastro, ele é levado a criar o perfil do primeiro pet, para já poder buscar e reservar.

- **Conta do anfitrião:** depois do cadastro, ele envia documento com foto, comprovante de endereço e fotos do espaço. A conta fica \"em análise\" até a aprovação da equipe do PetHost, e só então o perfil aparece na busca.

- Como parte do público anfitrião tem de 60 a 70 anos, as telas de cadastro usam letras grandes, poucos passos por tela e textos simples.

### 11.2 Perfil do pet (conta do tutor) {#perfil-do-pet-conta-do-tutor}

O tutor cadastra cada animal com nome, espécie, raça, idade, porte, foto, vacinas, alergias, medicações, rotina de alimentação e comportamento (por exemplo, se convive bem com outros animais). Essa ficha é enviada ao anfitrião na reserva, para que ele saiba tudo antes de receber o pet.

### 11.3 Perfil do espaço, agenda e preços (conta do anfitrião) {#perfil-do-espaço-agenda-e-preços-conta-do-anfitrião}

O anfitrião descreve o local (casa ou apartamento, quintal, tipos e portes de animais aceitos, regras da casa) e envia fotos. Ele também marca no calendário os dias disponíveis e define o preço da diária. Esse perfil é o \"quarto\" que o tutor vê na busca, conforme a metáfora do hotel.

### 11.4 Busca e reserva {#busca-e-reserva}

O tutor busca anfitriões por localização, datas, porte do pet e faixa de preço, e abre o perfil para ver fotos, regras e avaliações. Ao escolher, ele envia uma solicitação de reserva para o pet e as datas desejadas. O anfitrião aceita ou recusa, e a reserva passa pelos estados: solicitada, confirmada, em andamento (check-in feito), concluída (check-out feito) ou cancelada.

### 11.5 Pagamento online {#pagamento-online}

Depois que o anfitrião aceita, o tutor paga dentro do aplicativo. O valor fica retido pela plataforma até o fim da estadia e só então é repassado ao anfitrião, já com a taxa do PetHost descontada. Cada conta vê o histórico de pagamentos, e as regras de cancelamento e reembolso aparecem antes da confirmação.

### 11.6 Chat em tempo real {#chat-em-tempo-real}

Tutor e anfitrião conversam por um chat dentro do aplicativo, ligado a cada reserva, antes, durante e depois da estadia. O chat permite enviar texto, fotos e vídeos e avisa o usuário por notificação quando chega uma nova mensagem. A conversa fica registrada no histórico da reserva.

### 11.7 Check-in, acompanhamento e check-out {#check-in-acompanhamento-e-check-out}

No dia da entrada, os dois confirmam o check-in pelo aplicativo. Durante a estadia, o anfitrião envia fotos, vídeos e atualizações, que o tutor recebe por notificação. Na saída, os dois confirmam o check-out, o que libera o repasse do pagamento.

### 11.8 Avaliações {#avaliações}

Ao fim da estadia, o tutor avalia o anfitrião e o espaço, e o anfitrião avalia o tutor e o comportamento do pet, com nota de 1 a 5 estrelas e comentário. As avaliações aparecem no perfil e são a base da confiança na plataforma.

**Comum às duas contas**

- Cadastro, login e recuperação de senha

- Edição do perfil pessoal (nome, foto, telefone)

- Chat em tempo real

- Notificações

- Histórico de reservas

- Histórico de pagamentos

- Avaliações (cada um avalia o outro)

- Suporte e denúncia

**Só na conta do tutor**

- Perfil do pet (um ou mais animais)

- Busca com filtros e visualização do perfil do anfitrião

- Solicitação de reserva e pagamento online

- Acompanhamento da estadia (fotos, vídeos e atualizações)

**Só na conta do anfitrião**

- Envio de documentos e verificação de identidade e do local

- Perfil do espaço, calendário de disponibilidade e preços

- Aceite ou recusa de reservas

- Envio de fotos, vídeos e atualizações do pet

- Recebimento dos repasses e histórico de ganhos

## 12. Metáforas do projeto {#metáforas-do-projeto}

O projeto usa duas metáforas que o usuário já conhece, para que ele entenda o sistema sem precisar aprender conceitos novos: **Hotel** e **Viagem**.

### Metáfora 1: Hotel

O PetHost funciona como um hotel em que o \"hóspede\" é o pet. As pessoas já sabem como reservar, entrar, ficar e sair de um hotel, e essa familiaridade reduz a curva de aprendizado e transmite organização e profissionalismo.

| Conceito de hotel | Como aparece no PetHost | Exemplo na interface |
|----|----|----|
| Quarto | Espaço do anfitrião disponível para o pet | Cartão do anfitrião com fotos do espaço |
| Reserva | Período de hospedagem do pet | Tela \"Minhas reservas\" |
| Avaliação | Nota do espaço e do anfitrião | Estrelas e comentários no perfil |
| Check-in | Entrada do pet na hospedagem | Botão \"Confirmar chegada\" |
| Check-out | Saída do pet | Botão \"Confirmar saída\" |

**Benefício:** o tutor entende de imediato o que está contratando e o que esperar de cada etapa, o que reforça a sensação de segurança.

### Metáfora 2: Viagem

A experiência é apresentada como uma viagem, em que o pet \"vai para um destino\". Isso torna a jornada mais leve e afetiva, e ajuda a comunicar a hospedagem como uma experiência boa para o animal e não apenas como um serviço.

| Conceito de viagem | Como aparece no PetHost | Exemplo na interface |
|----|----|----|
| Procurar destino | Buscar cidade ou espaço do anfitrião | Campo de busca \"Para onde seu pet vai?\" |
| Localização | Onde o espaço está | Mapa com os anfitriões próximos |
| Reserva | Preparar a viagem | Fluxo de reserva e pagamento |
| Datas | Período da viagem do pet | Seletor de data de ida e volta |
| Chegada | Check-in | Notificação \"Seu pet chegou\" |
| Finalização | Check-out | Resumo da estadia e avaliação |

**Benefício:** a linguagem de viagem torna a experiência mais envolvente e prepara o tutor para acompanhar o pet \"em viagem\", com fotos e atualizações.

### Como as duas metáforas se combinam

A metáfora do **Hotel** organiza as regras e a estrutura do serviço (reserva, entrada, saída, avaliação). A metáfora da **Viagem** dá o tom e a linguagem da experiência (destino, chegada, aventura). Juntas, tornam a plataforma familiar, clara e acolhedora para tutores e anfitriões.
