# PetHost — Módulo Pets (e a tabela de anfitriões)

_Documento do que foi implementado · outubro de 2026 · complementa o [`PROJECT_STANDARDS.md`](PROJECT_STANDARDS.md), o [`modulo-owners.md`](modulo-owners.md) e o [`dicionario-de-dados.md`](dicionario-de-dados.md)_

A ficha do pet. O pet pertence a **um tutor** — o pet que vai se hospedar — **ou a um anfitrião** — o pet que mora na casa dele. O anfitrião nem sempre é uma empresa: muitas vezes é alguém com bichos em casa, e o tutor precisa saber com quais animais o pet dele vai conviver na estadia.

## Sumário

1. [Dono do pet](#1-dono-do-pet)
2. [Estrutura](#2-estrutura)
3. [Endpoints](#3-endpoints)
4. [Regras da ficha](#4-regras-da-ficha)
5. [Banco](#5-banco)
6. [Comunicação com outros módulos](#6-comunicação-com-outros-módulos)
7. [Módulo Hosts (só a tabela)](#7-módulo-hosts-só-a-tabela)
8. [Testes](#8-testes)
9. [Decisões e pendências](#9-decisões-e-pendências)

---

## 1. Dono do pet

O pet guarda o **id do tutor** (`owner_id` → `owner.owners.id`) **ou** o **id do anfitrião** (`host_id` → `host.hosts.id`) — nunca o id da conta, e nunca os dois. São FKs lógicas, sem constraint física (§5/§11: sem restrição nem JOIN entre schemas); um CHECK no banco garante que exatamente um dos dois está preenchido.

Um tutor ou anfitrião tem **muitos** pets (1:N). O dono sai **sempre do token**:

- token de `owner` → o tutor da conta (contrato `IOwnerDirectory`);
- token de `host` → o anfitrião da conta (contrato `IHostDirectory`);
- conta sem esse perfil → `404 PET_KEEPER_NOT_FOUND`. Hoje isso vale para **todo anfitrião**, porque o cadastro do perfil de anfitrião ainda não existe (seção 7).

A mesma pessoa com conta de tutor e de anfitrião tem dois donos diferentes: o pet cadastrado como tutor não aparece nem é editável pela conta de anfitrião, e vice-versa.

## 2. Estrutura

```
src/Modules/Pets/
├── Domain/          Pet, PetId, PetProfile (+ PetProfileData), PetKeeper, KeeperType,
│                    PetSpecies/PetSize/PetSex (+ *Values), PetsErrors, IPetRepository, IPetsUnitOfWork
├── Application/     RegisterPet, UpdatePet (+ PetPatch), DeactivatePet, ReactivatePet,
│                    GetPetById, ListMyPets, ListHostPets, PetAccess + porta IPetKeepers
├── Infrastructure/  PetsDbContext (schema pet), migration, repositório, adaptador PetKeepers
└── Presentation/    PetsController, HostPetsController
src/Modules/Hosts/   Domain (Host, Cpf, Cnpj, CompanyAddress, PersonType) + Infrastructure (HostsDbContext, HostDirectory)
tests/Modules/Pets/  Domain.UnitTests, Application.UnitTests, IntegrationTests
tests/Modules/Hosts/ Domain.UnitTests
```

`PetHost.ArchitectureTests` garante que Pets e Hosts não referenciam nenhum outro módulo e que nenhum módulo os referencia.

## 3. Endpoints

| Método | Rota | Acesso | O que faz | Sucesso |
|---|---|---|---|---|
| `POST` | `/api/v1/pets` | token de tutor ou anfitrião | Cadastra um pet da conta logada | `201` |
| `GET` | `/api/v1/pets/me` | token de tutor ou anfitrião | Todos os pets da conta logada (ativos e inativos) | `200` |
| `GET` | `/api/v1/pets/{petId}` | qualquer token | Um pet (regras de quem vê abaixo) | `200` |
| `PATCH` | `/api/v1/pets/{petId}` | dono do pet | Altera a ficha — só o que vier | `200` |
| `POST` | `/api/v1/pets/{petId}/deactivation` | dono do pet | Desativa | `200` |
| `DELETE` | `/api/v1/pets/{petId}/deactivation` | dono do pet | Reativa | `200` |
| `POST` | `/api/v1/pets/{petId}/photos` | dono do pet | Adiciona uma foto (até 3; upload `multipart/form-data`, parte `file`) | `200` |
| `PATCH` | `/api/v1/pets/{petId}/photos/{photoId}` | dono do pet | Substitui a imagem de uma foto (mesmo id e posição) | `200` |
| `DELETE` | `/api/v1/pets/{petId}/photos/{photoId}` | dono do pet | Tira uma foto | `200` |
| `GET` | `/api/v1/hosts/{hostId}/pets` | qualquer token | Pets **ativos** da casa do anfitrião | `200` |

As listas não têm paginação nem filtro (pedido do produto; exceção registrada no §13 dos padrões). Ordem: do mais novo para o mais antigo.

Desativar e reativar seguem o §13: a ação não é sobre a conta, então vira sub-recurso (`/deactivation`), como a suspensão do tutor (`/owners/{id}/suspension`).

**Quem vê um pet (`GET /pets/{petId}`):**

| Quem pede | Pet do tutor | Pet do anfitrião, ativo | Pet do anfitrião, inativo |
|---|---|---|---|
| O próprio dono | ✅ (inclusive inativo) | ✅ | ✅ |
| Outro tutor ou anfitrião | `404` | ✅ | `404` |
| Admin | ✅ | ✅ | ✅ |

Pet de outro tutor responde `404` (e não `403`): quem não pode ver não descobre que existe. O mesmo vale para **alterar** (`PATCH`, desativar, reativar): pet de outra conta responde `404 PET_NOT_FOUND`, igual a pet inexistente — um `403` confirmaria que aquele id existe.

### 3.1 Formato do pet

O mesmo em todas as rotas de um pet (cadastro, `GET /pets/{petId}`, `PATCH`, desativar/reativar):

```json
{
  "id": "0199d2a0-...",
  "ownerId": "0199d1f0-...",
  "keeper": { "id": "0199d1f0-...", "type": "owner", "name": "Camila Souza", "avatarUrl": "https://..." },
  "species": "dog",
  "name": "Pipoca",
  "breed": "SRD",
  "size": "medium",
  "birthDate": "2022-03-15",
  "sex": "female",
  "isNeutered": true,
  "isVaccinated": true,
  "feedingNotes": "Ração 2x ao dia",
  "goodWithDogs": true,
  "goodWithCats": false,
  "goodWithKids": true,
  "vetContact": "Dra. Ana — (44) 3222-0000",
  "notes": "Morre de medo de fogos.",
  "weightKg": 14.5,
  "microchip": "985112004567890",
  "allergies": "Frango",
  "photos": [
    { "id": "0199d2b1-...", "url": "https://<bucket>/pets/0199d2b1....jpg" },
    { "id": "0199d2b2-...", "url": "https://<bucket>/pets/0199d2b2....png" }
  ],
  "isActive": true,
  "createdAt": "2026-10-09T12:00:00+00:00",
  "updatedAt": "2026-10-09T12:00:00+00:00"
}
```

- `ownerId` **ou** `hostId` — o outro é omitido (nulo some da resposta).
- `keeper` é o dono para exibição: `id` do tutor/anfitrião (o mesmo de `ownerId`/`hostId`), `type` (`owner`/`host`), nome e foto. Anfitrião empresa aparece pelo **nome fantasia**. Sem e-mail, telefone nem endereço: o contato só é liberado depois do pagamento (escopo, regra 7).
- **Nas listas** (`GET /pets/me` e `GET /hosts/{hostId}/pets`) o dono vem **uma vez**, no topo, e cada pet sai sem `ownerId`, `hostId` e `keeper` — todos os pets da lista são do mesmo dono:

  ```json
  {
    "keeper": { "id": "0199d1f0-...", "type": "host", "name": "Dona Cida", "avatarUrl": "https://..." },
    "pets": [ { "id": "0199d2a0-...", "species": "dog", "name": "Thor", "...": "..." } ]
  }
  ```

  Sem pets, `pets` vem `[]` e o `keeper` vem do mesmo jeito.
- `photos`: até 3 fotos, em ordem de posição. A **primeira é a capa**. Sem foto, vem `[]`. Cada foto tem `id` (para substituir ou tirar) e `url` (pública, abre direto no navegador).
- `speciesDescription` aparece só em `exotic`; `size`, só em cachorro e gato (no gato, se informado); `deactivatedAt`, só em pet inativo.

### 3.2 `POST /api/v1/pets`

Corpo: os campos da ficha (seção 4), sem dono — ele vem do token.

| Status | Código | Quando |
|---|---|---|
| `201` | — | Criado. Header `Location: /api/v1/pets/{id}`. |
| `400` | `VALIDATION_ERROR` | Qualquer campo inválido — **todos de uma vez**, cada um no seu campo. |
| `401` | `UNAUTHORIZED` | Sem token. |
| `403` | `FORBIDDEN` | Token de admin (admin não tem pets). |
| `404` | `PET_KEEPER_NOT_FOUND` | A conta não tem perfil de tutor/anfitrião. |

### 3.3 `PATCH /api/v1/pets/{petId}`

Regra do §13: campo ausente ou `null` não mexe; `""` limpa um campo opcional de texto (`breed`, `birthDate`, `medicationNotes`, `feedingNotes`, `vetContact`, `notes`, `microchip`, `allergies`). A ficha que resulta da mescla é **validada inteira** e todos os erros voltam juntos.

```json
{ "name": "Paçoca", "breed": "", "goodWithCats": true }
```

- **Trocar a espécie** limpa o porte e a descrição que não vierem junto **quando a nova espécie não os tem**: de `dog` para `rabbit` não precisa mandar `"size": ""`. Entre cachorro e gato o porte é mantido. Para virar cachorro (vindo de um gato sem porte ou de outra espécie), mande o porte junto; para virar exótico, a descrição.
- Ficha igual à atual não grava nada nem mexe no `updatedAt`.
- Pet **desativado também pode ser editado** (o dono corrige a ficha sem reativar); editar não reativa.

| Status | Código | Quando |
|---|---|---|
| `200` | — | Alterado (ou nada mudou). Corpo no formato da 3.1. |
| `400` | `VALIDATION_ERROR` | Ficha resultante inválida. |
| `404` | `PET_NOT_FOUND` | Pet não existe **ou é de outra conta** (as duas respondem igual). |
| `409` | `PET_MICROCHIP_ALREADY_REGISTERED` | O microchip novo é de outro pet ativo. |

### 3.4 Desativar e reativar

Pet com histórico não é apagado (escopo, restrição 9): é desativado — faleceu, foi doado. Idempotente nos dois sentidos; devolve o pet no formato da 3.1. Pet inativo some para quem não é o dono (inclusive da lista da casa do anfitrião) e continua em `GET /pets/me` do dono.

### 3.5 Fotos (até 3)

As fotos ficam na tabela `pet.pet_photos`. As três rotas recebem e devolvem o pet no formato da 3.1, com `photos` atualizado; só o dono mexe (pet de outra conta: `404 PET_NOT_FOUND`). Valem também para pet desativado. O upload é `multipart/form-data` com a imagem na parte **`file`**: JPEG, PNG ou WebP, até 5 MB. Regras do arquivo e do bucket em [armazenamento-de-imagens.md](armazenamento-de-imagens.md).

| Rota | O que faz |
|---|---|
| `POST /pets/{petId}/photos` | Adiciona uma foto na **primeira vaga livre** (1 a 3). A vaga 1 é a capa; se a capa for tirada, a próxima foto adicionada vira a capa. Com 3 fotos: `422 PET_PHOTO_LIMIT_REACHED`. |
| `PATCH /pets/{petId}/photos/{photoId}` | **Só substitui**: troca a imagem daquela foto, mantendo o `id` e a posição, e apaga a imagem antiga do bucket. |
| `DELETE /pets/{petId}/photos/{photoId}` | Tira a foto e apaga a imagem. As outras ficam onde estavam. Tirar de novo: `404 PET_PHOTO_NOT_FOUND`. |

A posse do pet, a vaga (no `POST`) e a existência da foto (no `PATCH`) são conferidas **antes** do envio: uma recusa não deixa arquivo no bucket.

**Foto repetida:** o pet não pode ter a mesma imagem duas vezes. Na hora do upload a API calcula o SHA-256 dos bytes e guarda em `content_hash`; se o pet já tem uma foto com o mesmo hash, a resposta é `409 PET_PHOTO_ALREADY_EXISTS` e a cópia enviada é apagada do bucket. Vale no `POST` e no `PATCH`, inclusive substituir uma foto pela mesma imagem. A regra é **por pet**: a mesma imagem pode estar em pets diferentes. Ela pega o **mesmo arquivo**; a mesma foto recortada, comprimida ou reexportada tem outros bytes e passa como imagem nova.

```bash
curl -X POST  http://localhost:8080/api/v1/pets/{petId}/photos           -H "Authorization: Bearer <token>" -F "file=@pipoca.jpg"
curl -X PATCH http://localhost:8080/api/v1/pets/{petId}/photos/{photoId} -H "Authorization: Bearer <token>" -F "file=@pipoca-nova.jpg"
```

| Status | Código | Quando |
|---|---|---|
| `400` | `VALIDATION_ERROR` | Campo `file`: faltando, vazio, acima de 5 MB ou que não é JPEG/PNG/WebP. |
| `404` | `PET_NOT_FOUND` | Pet não existe ou é de outra conta. |
| `404` | `PET_PHOTO_NOT_FOUND` | A foto não é deste pet (`PATCH`, `DELETE`). |
| `413` | `PAYLOAD_TOO_LARGE` | Corpo acima de 8 MB. |
| `415` | `UNSUPPORTED_MEDIA_TYPE` | Corpo que não é `multipart/form-data`. |
| `409` | `PET_PHOTO_ALREADY_EXISTS` | O pet já tem essa imagem (mesmo arquivo) em uma das fotos. |
| `422` | `PET_PHOTO_LIMIT_REACHED` | O pet já tem 3 fotos (`POST`). |

## 4. Regras da ficha

Tudo no value object `PetProfile` (domínio), usado no cadastro (o validador chama a mesma regra) e no PATCH (depois da mescla).

| Campo | Regra |
|---|---|
| `species` | Obrigatório. `dog`, `cat`, `cockatiel` (calopsita), `parrot` (papagaio), `parakeet` (periquito), `canary` (canário), `rabbit`, `hamster`, `guinea_pig` (porquinho-da-índia), `fish`, `turtle` (tartaruga/jabuti) ou `exotic`. Maiúscula/minúscula tanto faz. |
| `speciesDescription` | Obrigatória **só** em `exotic` (até 60): o que é o animal ("Iguana verde"). Proibida nas outras espécies. |
| `name` | Obrigatório, até 60. |
| fotos | **Não vêm no JSON** (cadastro e PATCH ignoram `photoUrl`/`photos`). Entram por upload, nas rotas `/photos` (seção 3.5). |
| `breed` | Opcional, texto livre, até 60. Vazio = sem raça definida. |
| `size` | `small`, `medium`, `large`. **Obrigatório** em `dog`, **opcional** em `cat`, proibido nas outras espécies. |
| `birthDate` | Opcional, `yyyy-MM-dd`, aproximada, não no futuro. |
| `sex` | Obrigatório: `male`, `female` ou `unknown`. |
| `isNeutered`, `isVaccinated`, `goodWithDogs`, `goodWithCats`, `goodWithKids` | Obrigatórios (`true`/`false`) no cadastro. |
| `medicationNotes`, `feedingNotes` | Opcionais, até 1000. |
| `vetContact` | Opcional, até 160. |
| `notes` | Opcional ("coisas que só quem convive sabe"), até 2000. |
| `weightKg` | Opcional, número em kg, maior que 0 e até 150, guardado com 3 casas (gramas: `0.035` = 35 g). Ajuda onde não há porte (coelho, ave). No PATCH pode ser trocado, mas não apagado (número não tem "texto vazio"). |
| `microchip` | Opcional, 15 dígitos (ISO 11784/11785). Aceita espaços e hífens; guarda só os dígitos. **Único entre os pets ativos** (ver abaixo). |
| `allergies` | Opcional, até 1000: alergias e restrições (alimentos, remédios, produtos). |

Texto é aparado; texto vazio em campo opcional vira nulo.

| Código | HTTP | Mensagem |
|---|---|---|
| `VALIDATION_ERROR` | 400 | Uma por campo (ex.: `Size is required for dogs.`, `Describe the animal when the species is 'exotic'.`). |
| `PET_NOT_FOUND` | 404 | Pet '{id}' was not found. Também para pet de outra conta. |
| `PET_KEEPER_NOT_FOUND` | 404 | This account has no owner or host profile yet. |
| `PET_HOST_NOT_FOUND` | 404 | Host '{id}' was not found. |
| `PET_PHOTO_NOT_FOUND` | 404 | Photo '{id}' was not found. |
| `PET_PHOTO_ALREADY_EXISTS` | 409 | This pet already has this photo. |
| `PET_PHOTO_LIMIT_REACHED` | 422 | A pet can have at most 3 photos. Remove one before adding another. |
| `PET_MICROCHIP_ALREADY_REGISTERED` | 409 | Another active pet already has this microchip number. If this pet is yours, contact support. |

**Microchip único:** o número identifica um animal só, então dois pets **ativos** não podem ter o mesmo microchip, nem entre contas diferentes. Pet desativado não conta: se o animal mudou de tutor, o antigo desativa e o novo cadastra com o mesmo número. A regra é conferida no cadastro, no PATCH (só quando o número muda) e ao reativar: se outro pet ativo ficou com o número nesse meio-tempo, a reativação devolve `409`. No banco, o índice parcial `uq_pets_microchip_active` garante a mesma coisa.

**Exótico:** a lista cobre os pets mais comuns; o resto é `exotic` com a descrição em texto. A descrição deixa o anfitrião saber o que é o animal já na ficha; aceitar ou não continua sendo decisão dele no pedido de reserva e na conversa.

## 5. Banco

Schema `pet`, histórico em `pet.__ef_migrations_history`. Migrations: `CreatePetsTable`, `AddHealthDetailsToPets`, `AllowSizeForCats`, `AddUniqueMicrochipToPets`, `AddPetPhotos` (cria `pet_photos`, copia a `photo_url` de cada pet como a foto 1 e remove a coluna), `AddContentHashToPetPhotos`.

```sql
CREATE TABLE pet.pets (
    id                  uuid         NOT NULL,
    owner_id            uuid,                       -- FK lógica → owner.owners.id
    host_id             uuid,                       -- FK lógica → host.hosts.id
    species             varchar(20)  NOT NULL,
    species_description varchar(60),
    name                varchar(60)  NOT NULL,
    breed               varchar(60),
    size                varchar(10),
    birth_date          date,
    sex                 varchar(10)  NOT NULL,
    is_neutered         boolean      NOT NULL,
    is_vaccinated       boolean      NOT NULL,
    medication_notes    text,
    feeding_notes       text,
    good_with_dogs      boolean      NOT NULL,
    good_with_cats      boolean      NOT NULL,
    good_with_kids      boolean      NOT NULL,
    vet_contact         varchar(160),
    notes               text,
    weight_kg           numeric(6,3),
    microchip           char(15),
    allergies           text,
    is_active           boolean      NOT NULL DEFAULT true,
    deactivated_at      timestamptz,
    created_at          timestamptz  NOT NULL,
    updated_at          timestamptz  NOT NULL,
    CONSTRAINT pk_pets PRIMARY KEY (id),
    CONSTRAINT ck_pets_one_keeper CHECK (num_nonnulls(owner_id, host_id) = 1),
    CONSTRAINT ck_pets_size_required_for_dogs CHECK (species <> 'dog' OR size IS NOT NULL),
    CONSTRAINT ck_pets_size_only_for_dogs_and_cats CHECK (size IS NULL OR species IN ('dog', 'cat')),
    CONSTRAINT ck_pets_description_only_for_exotic CHECK ((species = 'exotic') = (species_description IS NOT NULL))
    -- + ck_pets_species, ck_pets_size, ck_pets_sex com os valores do enum
);
CREATE INDEX ix_pets_owner_id ON pet.pets (owner_id);
CREATE INDEX ix_pets_host_id ON pet.pets (host_id);
-- Em SQL na migration AddUniqueMicrochipToPets: o EF não indexa coluna de tipo complexo.
CREATE UNIQUE INDEX uq_pets_microchip_active ON pet.pets (microchip) WHERE is_active AND microchip IS NOT NULL;

CREATE TABLE pet.pet_photos (
    id          uuid          NOT NULL,
    pet_id      uuid          NOT NULL,   -- FK física: mesma tabela-mãe, mesmo módulo
    url         varchar(500)  NOT NULL,
    content_hash char(64),                -- SHA-256 da imagem; nulo só nas fotos de antes da regra
    position    smallint      NOT NULL,   -- vaga 1..3; a menor é a capa
    created_at  timestamptz   NOT NULL,
    CONSTRAINT pk_pet_photos PRIMARY KEY (id),
    CONSTRAINT fk_pet_photos_pets FOREIGN KEY (pet_id) REFERENCES pet.pets (id) ON DELETE CASCADE,
    CONSTRAINT ck_pet_photos_position CHECK (position BETWEEN 1 AND 3)
);
-- Limite de 3 também no banco: dois uploads ao mesmo tempo não criam a quarta foto.
CREATE UNIQUE INDEX uq_pet_photos_pet_id_position ON pet.pet_photos (pet_id, position);
-- A mesma imagem não entra duas vezes no mesmo pet.
CREATE UNIQUE INDEX uq_pet_photos_pet_id_content_hash ON pet.pet_photos (pet_id, content_hash) WHERE content_hash IS NOT NULL;
```

```bash
dotnet dotnet-ef migrations add <Nome> --project src/Modules/Pets/PetHost.Modules.Pets.Infrastructure --startup-project src/Host/PetHost.Api --context PetsDbContext --output-dir Persistence/Migrations
```

Em design time o `dotnet ef` monta a API inteira: defina `ConnectionStrings__Postgres`, `ConnectionStrings__Redis` e a seção `Jwt` (variáveis de ambiente ou `appsettings`). Em Development a API aplica as migrations de todos os módulos na subida.

## 6. Comunicação com outros módulos

Pelo §5 (porta + adaptador), sem referência entre módulos. A porta é `IPetKeepers` (Application); o adaptador `PetKeepers` (Infrastructure) usa:

| Pets pede | Contrato (`Shared.Contracts`) | Implementação |
|---|---|---|
| O tutor da conta logada; tutores pelos ids | `IOwnerDirectory` | `OwnerDirectory` (Owners.Infrastructure) |
| O anfitrião da conta logada; anfitriões pelos ids | `IHostDirectory` | `HostDirectory` (Hosts.Infrastructure) |
| Nome e foto da conta do dono | `IUserDirectory` | `UserDirectory` (Auth.Infrastructure) |

As listas buscam os donos numa consulta por módulo, não uma por pet.

**Trilha de auditoria** (`IAuditTrail`, alvo `pet`): `pet.registered` (com o tipo do dono e a espécie), `pet.updated` (com os **nomes** dos campos alterados, nunca os valores), `pet.deactivated`, `pet.reactivated`. Leitura não vai para a trilha (§15).

## 7. Módulo Hosts (só a tabela)

O pet do anfitrião precisa de um `host_id`, então o perfil de anfitrião ganhou módulo e tabela próprios — **sem rotas por enquanto**. Só o domínio (`Host` e os value objects), a tabela `host.hosts` (migration `CreateHostsTable`) e o contrato de leitura `IHostDirectory`.

O anfitrião pode ser **pessoa física** ou **pessoa jurídica** (empresa de hospedagem). É o `business_type` da conta conectada no Stripe:

| | Pessoa física (`individual`) | Pessoa jurídica (`company`) |
|---|---|---|
| CPF | Do anfitrião (único entre PFs) | Do **representante legal** (o Stripe exige) |
| CNPJ | — | Obrigatório, único. Aceita o **CNPJ alfanumérico** (Receita, julho de 2026) |
| Razão social / nome fantasia | — | Obrigatórios. O tutor vê o nome fantasia |
| Endereço | O da conta | O da conta (do representante) **e** o da empresa (`company_*`) |
| `stripe_account_id` | Conta conectada (sai do cantinho e vem para cá) | idem |

O que o Stripe pede de uma empresa e **não** guardamos (cargo do representante, sócios com 25% ou mais, site ou descrição do negócio, MCC, conta bancária, documentos) é coletado no onboarding hospedado do Stripe. Detalhes no [dicionário](dicionario-de-dados.md), tabela `hosts`.

## 8. Testes

| Projeto | O que cobre | Resultado |
|---|---|---|
| `Hosts.Domain.UnitTests` | `Cnpj` (numérico e alfanumérico, máscara, dígitos), `Cpf`, `CompanyAddress` (todos os erros juntos), `Host` (PF sem dados de empresa, PJ com tudo, nomes obrigatórios) | 36 ✅ |
| `Pets.Domain.UnitTests` | `PetProfile` (cada regra e cada erro, porte obrigatório no cachorro e opcional no gato, descrição só em exótico, peso/microchip/alergias, texto vazio vira nulo, ida e volta), `Pet` (fotos: vagas em ordem, limite de 3, vaga liberada reaproveitada, substituir mantém id e posição, imagem repetida ao adicionar e ao substituir, URL inválida, foto de outro pet; um dono só, tutor ≠ anfitrião com o mesmo id, ficha igual não mexe no `updatedAt`, desativar/reativar idempotentes), enums | 73 ✅ |
| `Pets.Application.UnitTests` | Cadastro (tutor, anfitrião, sem perfil, trilha, microchip repetido), validador, PATCH (parcial, limpar com `""`, troca de espécie, nada mudou, pet inativo, todos os erros, pet de outra conta = 404, mesma pessoa em outro papel, microchip repetido), `PetPatch`, desativar/reativar (reativar com microchip já em uso), quem vê o quê, listas com o dono uma vez no topo, fotos: adicionar (limite e posse conferidos antes do upload, compensação se gravar falhar), substituir (imagem antiga apagada, foto de outro pet sem upload), tirar | 60 ✅ |
| `Pets.IntegrationTests` | Pela API com Postgres e Redis em container: cadastro (`201` + `Location`, `400` com todos os campos, exótico, anfitrião sem perfil `404`, anfitrião com perfil, `401`, admin `403`), PATCH (inclusive de pet inativo), pet de outra conta `404`, desativar/reativar, `/pets/me` e a lista do anfitrião com o dono uma vez no topo, rota inexistente/id fora do formato `404` e método errado `405` no envelope, visibilidade entre tutores, pets da casa do anfitrião vistos pelo tutor, peso e microchip, gato com e sem porte, microchip repetido (`409` no cadastro, PATCH e reativação; liberado quando o pet antigo é desativado), CHECK de dono único e índice do microchip no banco. `PetPhotoTests`: adicionar (bucket e link público, `422` na quarta foto sem upload, `404` sem upload para pet de outra conta, `400` de arquivo grande ou que não é imagem, `413` acima do teto), imagem repetida (`409` sem deixar arquivo; liberada em outro pet), substituir (mesmo id e posição, imagem antiga apagada, `404` de foto de outro pet, `409` com imagem de outra foto), tirar (só aquela foto; de novo `404`), `photoUrl` do JSON ignorado, limite de 3 no banco (vaga única, CHECK) | 44 ✅ |

## 9. Decisões e pendências

| Decisão | Por quê |
|---|---|
| Pet de tutor **ou** de anfitrião, com `owner_id` / `host_id` | Pedido seu: o anfitrião nem sempre é empresa, e o tutor precisa ver com quais animais o pet vai conviver. Guarda o id do perfil (tutor/anfitrião), não o da conta. |
| FKs lógicas + CHECK de dono único | §5/§11: sem constraint entre schemas. O CHECK garante no banco o "exatamente um". |
| Tipo do pet como enum, não tabela `pet_types` | Pedido seu: lista fixa por enquanto, com os pets mais comuns e `exotic` para o resto. O admin não gerencia tipos. |
| `exotic` exige descrição em texto | Pedido seu (opção recomendada): o anfitrião sabe o que é o animal antes da conversa; a decisão continua dele. |
| `sex = unknown` | Aves, peixes e vários exóticos só se sexam com exame: melhor admitir do que forçar um chute. |
| Troca de espécie limpa porte/descrição que não vieram | O cliente não precisa mandar `""` para campos que deixaram de fazer sentido. Cachorro ↔ gato mantém o porte. |
| Porte também no gato, opcional | Pedido seu. Opcional para não invalidar os gatos já cadastrados sem porte; o cantinho que filtra por porte trata gato sem porte como "não informado". |
| `deactivated_at` | Mesmo padrão de `users` e `owners`: quando foi desativado. |
| Peso, microchip e alergias | Sugestão aceita por você: peso ajuda onde não há porte; microchip identifica o animal se fugir; alergias evitam erro do anfitrião com comida e remédio. |
| Listas sem paginação | Pedido seu; uma pessoa ou casa tem poucos pets. Exceção registrada no §13. |
| Pet de outro tutor é `404` no GET e `403` no PATCH | Quem não pode ver não descobre que existe; quem tenta alterar algo que vê (pet de anfitrião) recebe o motivo. |
| Módulo Hosts só com a tabela | Pedido seu: cadastro e rotas do anfitrião ficam para depois. |

| # | Pendência | Impacto |
|---|---|---|
| 1 | Cadastro do perfil de anfitrião (PF/PJ). | Até lá, **toda** conta de anfitrião recebe `404 PET_KEEPER_NOT_FOUND` ao cadastrar pet. A ideia é um cadastro unificado como o do tutor (`POST /hosts/register`: conta + CPF/CNPJ num pedido só), substituindo o `/users/register`. |
| 2 | Pet em reserva ativa. | Quando o módulo Booking existir, desativar um pet com estadia confirmada ou em andamento deve ser recusado (ou cancelar a estadia). |
| 3 | Ver a ficha do pet do tutor pelo anfitrião. | Hoje só o dono e o admin veem o pet de um tutor. Com o Booking, o anfitrião passa a ver os pets do pedido que recebeu. |
| 4 | Tutor/anfitrião inativado ou suspenso. | Os pets dele continuam ativos e o pet do anfitrião continua visível na casa. Quando o anfitrião tiver status, a lista da casa pode esconder anfitrião inativo. |
| 5 | Fotos do pet: resolvido. | Até 3 por pet, em `pet.pet_photos` (seção 3.5). Reordenar ou escolher a capa ainda não tem rota: a capa é a foto da menor vaga. |
