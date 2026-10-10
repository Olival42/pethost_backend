# PetHost — Padrões do Projeto

> Documento normativo. Tudo é obrigatório salvo onde marcado como *recomendado*.
> Em conflito com o código existente, este documento vence.

**Atualizado:** 2026-10-09 · **Escopo:** backend (API)

---

## 1. Stack

| Item | Escolha |
|------|---------|
| Runtime | **.NET 10** (`net10.0`) |
| Framework | **ASP.NET Core** com Controllers |
| Linguagem | C# 14, `Nullable: enable`, `TreatWarningsAsErrors: true` |
| ORM / Banco | **EF Core 10** + **PostgreSQL 17+** (Npgsql) |
| Casos de uso | **Handlers diretos** — sem MediatR/Wolverine |
| Validação | FluentValidation (via decorator) |
| Testes | **xUnit** + **Moq** + **FluentAssertions 7.x** + Testcontainers |
| Logs | Serilog (JSON estruturado) |

> ⚠️ `FluentAssertions` 8+ exige licença paga. Fixado em **7.x** (Apache-2.0) via `Directory.Packages.props`. Subir de versão exige decisão de licenciamento.

**Raiz obrigatória:** `Directory.Build.props` (propriedades comuns), `Directory.Packages.props` (Central Package Management — nenhum `.csproj` declara `Version`), `.editorconfig`, `PetHost.sln`.

---

## 2. Princípios

1. **Dependências apontam para dentro.** `Presentation → Application → Domain`. `Infrastructure` depende de `Application`/`Domain`, nunca o contrário.
2. **Domain é puro.** Sem EF Core, sem ASP.NET, sem I/O. Só C# e `Shared.Kernel`.
3. **Módulos são autônomos.** Nenhum módulo referencia projeto interno de outro. A fronteira é `Shared.Contracts`.
4. **Erro esperado não usa exceção.** Fluxo de negócio previsível retorna `Result`. Exceção só para bug ou infraestrutura fora.
5. **Toda regra de negócio tem teste unitário.** Sem exceção.

**Módulos** (bounded contexts): `Auth` (conta, sessão, perfil comum), `Owners` (perfil de tutor), `Hosts` (perfil de anfitrião, PF/PJ — por enquanto só a tabela), `Audit` (trilha de auditoria de todos os módulos), `Pets` (ficha do pet, do tutor ou do anfitrião), `Booking`, `Availability`, `Payments`, `Reviews`, `Notifications`. Criar módulo novo é decisão de arquitetura — discutir antes do PR.

---

## 3. Estrutura

Um **`.csproj` por camada por módulo** — o compilador vira o guardião do Clean Arch.

```
pethost_backend/
├── PetHost.sln · Directory.Build.props · Directory.Packages.props · .editorconfig
├── docs/PROJECT_STANDARDS.md
├── src/
│   ├── Host/PetHost.Api/                     ← único executável (composition root)
│   ├── Shared/
│   │   ├── PetHost.Shared.Kernel/            ← Result, Error, Entity, ValueObject, abstrações
│   │   ├── PetHost.Shared.Contracts/         ← ApiResponse, ErrorResponse, integration events
│   │   └── PetHost.Shared.Infrastructure/    ← cross-cutting: EF base, outbox, decorators
│   └── Modules/<Module>/
│       ├── PetHost.Modules.<Module>.Domain/
│       ├── PetHost.Modules.<Module>.Application/
│       ├── PetHost.Modules.<Module>.Infrastructure/
│       └── PetHost.Modules.<Module>.Presentation/
└── tests/
    ├── Shared/PetHost.Shared.{Kernel,Contracts}.UnitTests/
    ├── Modules/<Module>/
    │   ├── PetHost.Modules.<Module>.Domain.UnitTests/
    │   ├── PetHost.Modules.<Module>.Application.UnitTests/
    │   └── PetHost.Modules.<Module>.IntegrationTests/
    ├── PetHost.ArchitectureTests/            ← valida as regras do §4
    └── PetHost.TestKit/                      ← builders e fixtures compartilhados
```

A pasta física espelha o nome do projeto; solution folders espelham o disco.

### Organização interna — por feature, nunca por tipo técnico

❌ `Handlers/`, `Dtos/`, `Services/` na raiz.  ✅ `Bookings/CreateBooking/`.

| Camada | Pastas |
|--------|--------|
| `Domain` | `<Aggregates>/` (agregado, VOs, `I<X>Repository`, `Events/`), `Errors/<Module>Errors.cs` |
| `Application` | `<Aggregates>/<UseCase>/` (Command, Handler, Validator, Response), `Abstractions/`, `DependencyInjection.cs` |
| `Infrastructure` | `Persistence/` (DbContext, `Configurations/`, `Repositories/`, `Migrations/`), `Integrations/`, `DependencyInjection.cs` |
| `Presentation` | `<Aggregates>/<X>Controller.cs`, `DependencyInjection.cs` |

Pasta de agregado no **plural** (`Bookings/`); pasta de caso de uso no **imperativo** (`CreateBooking/`, `GetBookingById/`). Namespace espelha a pasta.

Cada camada com dependências expõe **um único** `DependencyInjection.cs` com `Add<Module><Layer>()`. O host apenas compõe.

---

## 4. Dependências entre camadas

| Projeto | **Pode** referenciar | **Nunca** |
|---------|----------------------|-----------|
| `<M>.Domain` | `Shared.Kernel` | EF Core, ASP.NET, outros módulos, outras camadas |
| `<M>.Application` | `<M>.Domain`, `Shared.Kernel`, `Shared.Contracts` | EF Core, ASP.NET, `Infrastructure`, outros módulos |
| `<M>.Infrastructure` | `<M>.Application`, `<M>.Domain`, `Shared.*` | `Presentation`, outros módulos |
| `<M>.Presentation` | `<M>.Application`, `Shared.Contracts` | `Domain`, `Infrastructure` |
| `PetHost.Api` | todos os `Infrastructure` + `Presentation` | — |
| `Shared.Kernel` | nada | tudo |

> `Presentation` **não** referencia `Domain`: o controller só conhece Commands/Queries e DTOs. Isso impede serializar entidade de domínio na resposta.

**Papéis:** `Domain` = agregados, VOs, domain events, interfaces de repositório, erros, invariantes. `Application` = orquestração (Commands, Queries, Handlers, DTOs, portas), sem regra de negócio. `Infrastructure` = DbContext, repositórios, migrations, clientes HTTP. `Presentation` = rota, autorização, `Result → IActionResult`. `Host` = `Program.cs`, pipeline, registro dos módulos.

Essas regras são verificadas por `PetHost.ArchitectureTests` (NetArchTest) — violação quebra o build.

---

## 5. Comunicação entre módulos

Proibido: referenciar projeto de outro módulo, compartilhar `DbContext`, fazer `JOIN` entre schemas.

1. **Porta + adaptador** (consulta síncrona) — a `Application` consumidora declara a interface no seu vocabulário (`IAvailabilityChecker`); a `Infrastructure` implementa via `Shared.Contracts`.
2. **Integration event** (assíncrono) — domain event fica dentro do módulo; para cruzar fronteira, publique um `record ...IntegrationEvent` declarado em `Shared.Contracts`, via **padrão Outbox** (tabela no schema do publicador, atômica com a transação).
3. **Read model próprio** — módulo que lê muito dado de outro mantém tabela desnormalizada alimentada por integration events. Duplicar dado é aceitável; acoplar schema não é.

---

## 6. Result Pattern

Em `PetHost.Shared.Kernel`. **Todo** método público de `Domain`/`Application` que pode falhar por regra de negócio retorna `Result` ou `Result<T>`.

```csharp
namespace PetHost.Shared.Kernel.Errors;

/// <param name="Code">Código estável, legível por máquina (SCREAMING_SNAKE_CASE).</param>
/// <param name="Message">Mensagem em inglês, frase completa.</param>
/// <param name="Field">Campo do payload em camelCase. Só em erro de validação.</param>
public sealed record Error(string Code, string Message, string? Field = null)
{
    public static Error Validation(string field, string message) => new(ErrorCodes.Validation, message, field);
    public static Error NotFound(string message)     => new(ErrorCodes.NotFound, message);
    public static Error Conflict(string message)     => new(ErrorCodes.Conflict, message);
    public static Error Unauthorized(string message) => new(ErrorCodes.Unauthorized, message);
    public static Error Forbidden(string message)    => new(ErrorCodes.Forbidden, message);
    public static Error Unexpected(string message = "An unexpected error occurred.") => new(ErrorCodes.Unexpected, message);
}

public static class ErrorCodes
{
    public const string Validation   = "VALIDATION_ERROR";
    public const string NotFound     = "NOT_FOUND";
    public const string Conflict     = "CONFLICT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden    = "FORBIDDEN";
    public const string BusinessRule = "BUSINESS_RULE_VIOLATION";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string Unexpected   = "UNEXPECTED_ERROR";
}
```

```csharp
namespace PetHost.Shared.Kernel.Results;

public class Result
{
    protected Result(bool isSuccess, List<Error>? errors)
    {
        if (isSuccess && errors is { Count: > 0 })
            throw new InvalidOperationException("A successful result cannot carry errors.");
        if (!isSuccess && errors is null or { Count: 0 })
            throw new InvalidOperationException("A failed result must carry at least one error.");

        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary><c>null</c> quando <see cref="IsSuccess"/>.</summary>
    public List<Error>? Errors { get; }
    public Error? FirstError => Errors is { Count: > 0 } ? Errors[0] : null;

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, [error]);
    public static Result Failure(IEnumerable<Error> errors) => new(false, [.. errors]);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, List<Error>? errors) : base(isSuccess, errors) => Value = value;

    /// <summary><c>default</c> quando <see cref="Result.IsFailure"/>.</summary>
    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(Error error) => new(false, default, [error]);
    public static new Result<T> Failure(IEnumerable<Error> errors) => new(false, default, [.. errors]);

    /// <summary>Propaga a falha de outro Result preservando todos os erros.</summary>
    public static Result<T> FromFailure(Result result) => new(false, default, result.Errors);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
```

Extensões de composição em `ResultExtensions`: `Map`, `BindAsync`.

### Regras

| Regra | Detalhe |
|-------|---------|
| Nunca exceção para regra de negócio | `throw` só para bug ou infra indisponível |
| Nunca ler `.Value` sem checar `IsSuccess` | Não use `!` para calar o analyzer |
| Propagar, não recriar | `Result<T>.FromFailure(other)` preserva os erros |
| Acumular só em validação | Validação retorna N erros; regra de domínio retorna 1 |
| `Field` em camelCase | Precisa casar com o campo no JSON da request |
| `Code` nunca muda | É contrato público. Mudar = breaking change |

Erros centralizados por módulo em `<Module>Errors` — **zero** literal inline:

```csharp
public static class BookingErrors
{
    public static readonly Error CheckInInThePast = Error.Validation("checkIn", "CheckIn cannot be in the past.");
    public static readonly Error OverlappingPeriod = new("BOOKING_OVERLAPPING_PERIOD", "The host already has a booking in this period.");
    public static Error NotFound(BookingId id) => new("BOOKING_NOT_FOUND", $"Booking '{id.Value}' was not found.");
}
```

---

## 7. Resposta da API

Em `PetHost.Shared.Contracts`. **Toda** resposta — inclusive 500 — usa `ApiResponse<T>`. Sem `ProblemDetails`.

```csharp
namespace PetHost.Shared.Contracts.Responses;

/// <summary>Envelope padrão de resposta da API PetHost.</summary>
public record ApiResponse<T>(bool Success, T? Data, ErrorResponse? Error, DateTimeOffset Timestamp)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null, DateTimeOffset.UtcNow);
    public static ApiResponse<T> Fail(ErrorResponse error) => new(false, default, error, DateTimeOffset.UtcNow);
}

/// <summary>Estrutura padronizada de erro.</summary>
public record ErrorResponse(string Code, string Message, object? Details = null);

/// <summary>Mensagens de validação agrupadas por campo.</summary>
public record DataErrors(string Field, List<string> Messages);
```

Único ponto de tradução `Result<T> → ApiResponse<T>` — nenhum controller monta envelope à mão:

```csharp
public static class ApiResponseMapper
{
    public static ApiResponse<T> ToApiResponse<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return ApiResponse<T>.Ok(result.Value!);

        var errors = result.Errors ?? [];

        var grouped = errors
            .Where(e => e.Code == ErrorCodes.Validation)
            .GroupBy(e => e.Field ?? "general")
            .Select(g => new DataErrors(g.Key, [.. g.Select(e => e.Message)]))
            .ToList();

        if (grouped.Count > 0)
            return ApiResponse<T>.Fail(new ErrorResponse(ErrorCodes.Validation, "Validation failed", grouped));

        var first = errors.FirstOrDefault();
        return ApiResponse<T>.Fail(new ErrorResponse(
            first?.Code ?? ErrorCodes.Unexpected,
            first?.Message ?? "Unknown error"));
    }
}
```

**Comportamento normativo:** havendo ≥1 `VALIDATION_ERROR`, a resposta é `VALIDATION_ERROR` com `Details` agrupado por campo (erros de outro tipo na lista são descartados). Senão, vence o **primeiro** erro e `Details` fica `null`. `Field` nulo cai no bucket `"general"`.

### Ponte com o ASP.NET Core

```csharp
public static IActionResult ToActionResult<T>(this Result<T> result, int successStatusCode = StatusCodes.Status200OK)
{
    var response = result.ToApiResponse();
    return new ObjectResult(response)
    {
        StatusCode = response.Success ? successStatusCode : response.Error!.Code.ToHttpStatusCode()
    };
}

public static int ToHttpStatusCode(this string errorCode) => errorCode switch
{
    ErrorCodes.Validation   => StatusCodes.Status400BadRequest,
    ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
    ErrorCodes.Forbidden    => StatusCodes.Status403Forbidden,
    ErrorCodes.NotFound     => StatusCodes.Status404NotFound,
    ErrorCodes.Conflict     => StatusCodes.Status409Conflict,
    ErrorCodes.BusinessRule => StatusCodes.Status422UnprocessableEntity,
    ErrorCodes.Unexpected   => StatusCodes.Status500InternalServerError,

    // Códigos de módulo herdam o status pelo sufixo.
    var c when c.EndsWith("_NOT_FOUND", StringComparison.Ordinal) => StatusCodes.Status404NotFound,
    var c when c.EndsWith("_CONFLICT",  StringComparison.Ordinal) => StatusCodes.Status409Conflict,

    _ => StatusCodes.Status422UnprocessableEntity,   // fallback: regra de negócio, não falha do servidor
};
```

**Obrigatório no `Program.cs`** — senão o MVC devolve `ProblemDetails` cru e quebra o contrato:

```csharp
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
{
    o.SuppressModelStateInvalidFilter = true;   // a validação é nossa
    o.SuppressMapClientErrors = true;
});
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
```

**Erro sem corpo também vai no envelope.** O ASP.NET Core responde sem corpo quando nenhuma rota casa — rota inexistente ou id fora do formato da rota (`/pets/abc` com `{petId:guid}`) —, quando o método não existe na rota (405) e quando o content-type é errado (415). `app.UseEmptyErrorEnvelope()` (em `Shared.Infrastructure/Http`, logo depois do `UseExceptionHandler`) completa essas respostas com o `ApiResponse`: `404 NOT_FOUND`, `405 METHOD_NOT_ALLOWED`, `415 UNSUPPORTED_MEDIA_TYPE`. Resposta que já tem corpo passa intacta. **Nenhuma** resposta de erro sai com corpo vazio.

### Payloads

Sucesso `200` · validação `400` · regra de negócio `422` · inesperado `500`:

```json
{ "success": true,  "data": { "bookingId": "0f8f…", "status": "Pending" }, "error": null, "timestamp": "2026-10-07T13:45:12.331+00:00" }

{ "success": false, "data": null, "error": {
    "code": "VALIDATION_ERROR", "message": "Validation failed",
    "details": [ { "field": "checkOut", "messages": ["CheckOut must be after CheckIn."] } ] },
  "timestamp": "2026-10-07T13:45:12.331+00:00" }

{ "success": false, "data": null, "error": {
    "code": "BOOKING_OVERLAPPING_PERIOD", "message": "The host already has a booking in this period.", "details": null },
  "timestamp": "2026-10-07T13:45:12.331+00:00" }

{ "success": false, "data": null, "error": {
    "code": "UNEXPECTED_ERROR", "message": "An unexpected error occurred.", "details": { "traceId": "0HN7G4K2P9QRS:00000001" } },
  "timestamp": "2026-10-07T13:45:12.331+00:00" }
```

> Em `UNEXPECTED_ERROR`, `Details` leva **apenas** o `traceId`. Nunca stack trace, mensagem de exceção ou nome de tabela.
> Operação sem retorno usa `ApiResponse<Unit>` — **nunca** `204 No Content`.

---

## 8. Catálogo de erros

| `Code` | HTTP | Quando |
|--------|------|--------|
| `VALIDATION_ERROR` | 400 | Payload inválido. Sempre com `Field` |
| `UNAUTHORIZED` | 401 | Token ausente, inválido ou expirado |
| `FORBIDDEN` | 403 | Autenticado, sem permissão |
| `NOT_FOUND` | 404 | Recurso inexistente |
| `CONFLICT` | 409 | Duplicidade, concorrência otimista |
| `BUSINESS_RULE_VIOLATION` | 422 | Payload válido, regra impede a operação |
| `METHOD_NOT_ALLOWED` | 405 | A rota existe, mas não com esse método |
| `UNSUPPORTED_MEDIA_TYPE` | 415 | Corpo num content-type que a rota não aceita (use `application/json`) |
| `TOO_MANY_REQUESTS` | 429 | Rate limit estourado. Vem com o header `Retry-After` (segundos) |
| `UNEXPECTED_ERROR` | 500 | Exceção não tratada |

**Códigos de módulo:** `<MODULE>_<REASON>` em `SCREAMING_SNAKE_CASE` — `AUTH_INVALID_CREDENTIALS`, `AUTH_EMAIL_ALREADY_REGISTERED`, `BOOKING_NOT_FOUND`, `BOOKING_OVERLAPPING_PERIOD`, `BOOKING_ALREADY_CANCELLED`, `PET_MICROCHIP_ALREADY_REGISTERED`, `PAYMENT_GATEWAY_REJECTED`.

**Recurso de outra conta responde `404`, não `403`.** Ler ou alterar pelo id um recurso que pertence a outro usuário (pet, reserva, mensagem) devolve o mesmo `<MODULE>_NOT_FOUND` de um id inexistente: um `403` confirmaria que o id existe. O `403` fica para o que não depende do recurso — papel errado no token (`FORBIDDEN`), conta desativada ou suspensa. O sufixo `_NOT_OWNED_BY_REQUESTER` → 403 continua no mapeador, mas não deve ser usado para recurso que se busca pelo id.

Toda constante em `<Module>Errors`. `Message` em inglês, frase completa, sem expor infraestrutura nem dado de outro usuário. Adicionar código é retrocompatível; **renomear ou remover é breaking change**.

---

## 9. Casos de uso

Sem mediator. Abstrações no `Shared.Kernel`:

```csharp
public interface ICommand<TResponse>;
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
```

| Regra | Detalhe |
|-------|---------|
| Nome | `<Verb><Aggregate>Command`/`Query` + `...Handler` + `...Validator` + `...Response` |
| Tipo | `sealed record` com construtor primário |
| Um handler, um caso de uso | Nada de `ProcessBookingHandler` genérico |
| Retorno | **Sempre** `Task<Result<T>>`. Nunca `void`, nunca entidade de domínio |
| `CancellationToken` | Último parâmetro, propagado até o EF Core |
| Sem regra de negócio | Regra vive no agregado; handler orquestra |
| Sem `DbContext` | Handler usa interface de repositório |
| **`TimeProvider` injetado** | Nunca `DateTime.UtcNow` em `Domain`/`Application` — quebra determinismo do teste. Nos testes, `FakeTimeProvider` |

Esqueleto de handler — note a propagação de falha e a regra dentro do agregado:

```csharp
public sealed class CreateBookingCommandHandler(
    IBookingRepository bookingRepository,
    IAvailabilityChecker availabilityChecker,
    IBookingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CreateBookingCommand, CreateBookingResponse>
{
    public async Task<Result<CreateBookingResponse>> HandleAsync(
        CreateBookingCommand command, CancellationToken cancellationToken)
    {
        var period = BookingDateRange.Create(command.CheckIn, command.CheckOut);
        if (period.IsFailure)
            return Result<CreateBookingResponse>.FromFailure(period);

        var available = await availabilityChecker.IsHostAvailableAsync(
            command.HostId, command.CheckIn, command.CheckOut, cancellationToken);
        if (available.IsFailure)
            return Result<CreateBookingResponse>.FromFailure(available);
        if (!available.Value)
            return Result<CreateBookingResponse>.Failure(BookingErrors.OverlappingPeriod);

        var booking = Booking.Create(command.PetId, command.HostId, period.Value!, timeProvider.GetUtcNow());
        if (booking.IsFailure)
            return Result<CreateBookingResponse>.FromFailure(booking);

        bookingRepository.Add(booking.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateBookingResponse(booking.Value!.Id.Value, booking.Value.Status.ToString());
    }
}
```

No agregado, **toda** regra retorna `Result`, o construtor é privado e há um `private Booking() { }` só para o EF Core:

```csharp
public Result Cancel(DateTimeOffset now)
{
    if (Status is BookingStatus.Cancelled) return Result.Failure(BookingErrors.AlreadyCancelled);
    if (Status is BookingStatus.Completed) return Result.Failure(BookingErrors.CannotCancelCompleted);

    Status = BookingStatus.Cancelled;
    CancelledAt = now;
    RaiseDomainEvent(new BookingCancelledDomainEvent(Id, now));
    return Result.Success();
}
```

O controller tem **três** responsabilidades: receber, delegar, converter com `ToActionResult()`.

```csharp
[ApiController]
[Route("api/v1/bookings")]
public sealed class BookingsController(
    ICommandHandler<CreateBookingCommand, CreateBookingResponse> createBooking) : ControllerBase
{
    /// <summary>Cria uma reserva de hospedagem.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<CreateBookingResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<CreateBookingResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<CreateBookingResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateBookingCommand command, CancellationToken cancellationToken)
    {
        var result = await createBooking.HandleAsync(command, cancellationToken);
        return result.ToActionResult(StatusCodes.Status201Created);
    }
}
```

---

## 10. Validação

| Nível | Onde | Valida | Erro |
|-------|------|--------|------|
| **Entrada** | FluentValidation na `Application` | Formato, obrigatoriedade, range | `VALIDATION_ERROR` + `Field`, N erros acumulados |
| **Domínio** | Agregado / value object | Invariante, transição de estado | `<MODULE>_<REASON>`, 1 erro |

- Toda `ICommand` tem validador, mesmo trivial. `IQuery` só com filtro/paginação.
- O handler **nunca** chama o validador: um `ValidationCommandHandlerDecorator` registrado no DI intercepta, acumula as falhas como `Error.Validation(...)` e retorna antes de executar.
- `PropertyName` do FluentValidation é convertido para **camelCase** (`CheckOut` → `checkOut`) para casar com o JSON.
- Validador **não** acessa banco. Unicidade (e-mail duplicado, período sobreposto) é regra de domínio → `CONFLICT` ou código de módulo.
- Mensagem em inglês, frase completa, terminada em ponto.

---

## 11. Persistência

| Regra | Detalhe |
|-------|---------|
| Um `DbContext` por módulo | `BookingDbContext`, `AuthDbContext` |
| Um schema por módulo | `auth`, `owner`, `host`, `pet`, `audit` (singular) |
| Migrations e histórico isolados | `Persistence/Migrations`, `__ef_migrations_history` no schema do módulo |
| Sem `JOIN` entre schemas | Cruzar dado só pelo §5 |
| `DbContext` nunca sai da `Infrastructure` | `Application` só conhece interfaces |

**Convenções de banco:** schema `snake_case` singular · tabela `snake_case` **plural** · coluna `snake_case` · PK `id` do tipo `uuid` (v7, gerado pela aplicação com `Guid.CreateVersion7()`, **nunca** auto-incremento/identity) · FK `<singular>_id` também `uuid` · índice `ix_<table>_<cols>` · unique `uq_<table>_<cols>` · timestamp `timestamptz` sempre UTC · dinheiro `numeric(18,2)` + coluna de moeda (**nunca** `float`/`double`).

Mapeamento `PascalCase → snake_case` via `UseSnakeCaseNamingConvention()`; configuração explícita por entidade em `IEntityTypeConfiguration<T>`.

**Queries:** `AsNoTracking()` sempre na leitura · toda lista **paginada** (exceção: lista administrativa, ver §13) (sem coleção ilimitada) · `Include` explícito, lazy loading off · projetar direto no DTO via `Select` · `enum` como **string** · concorrência otimista com `xmin` → `CONFLICT`.

**Migrations:** nome em inglês, `PascalCase`, imperativo (`CreateBookingsTable`, `AddCancellationReasonToBookings`). Migration já aplicada em produção **nunca** é editada — crie outra. Em produção não rodam no startup: step dedicado do pipeline.

```bash
dotnet ef migrations add CreateBookingsTable \
  --project src/Modules/Booking/PetHost.Modules.Booking.Infrastructure \
  --startup-project src/Host/PetHost.Api \
  --context BookingDbContext --output-dir Persistence/Migrations
```

---

## 12. Nomenclatura

> **Todo identificador é em inglês:** arquivo, pasta, projeto, namespace, classe, método, variável, endpoint, tabela, coluna, código de erro, nome de teste e mensagem de erro da API.
> **Português só em:** comentário `///`, documentos em `docs/` e mensagem de commit.

| Elemento | Convenção | Exemplo |
|----------|-----------|---------|
| Classe / record / enum / namespace | `PascalCase` | `BookingDateRange` |
| Interface | `I` + `PascalCase` | `IBookingRepository` |
| Método | `PascalCase`, sufixo `Async` se assíncrono | `HandleAsync` |
| Parâmetro / variável | `camelCase` | `checkIn` |
| Campo privado | `_camelCase` | `_bookings` |
| Constante | `PascalCase` (valor em `SCREAMING_SNAKE_CASE`) | `Validation = "VALIDATION_ERROR"` |
| Genérico | `T` + descrição | `TCommand`, `TResponse` |
| Arquivo | Exatamente o nome do tipo público | `Booking.cs` |

**Estilo obrigatório:** `namespace` file-scoped · um tipo público por arquivo · `sealed` por padrão · `record` para DTO/Command/VO/evento, `class` para entidade e serviço · sem `#region` · `using` fora do namespace · nunca `!` para silenciar warning (exceto após checar `IsSuccess`) · `async`/`await` ponta a ponta, nunca `.Result`/`.Wait()`.

**JSON:** propriedade `camelCase` · nulo omitido · data ISO 8601 (`DateOnly` → `"2026-11-02"`) · `enum` como string.

### Glossário do domínio

O time pensa em português, o código fala inglês. Esta é a tradução oficial — **não** inventar sinônimo.

| PT | EN | PT | EN |
|----|----|----|----|
| Reserva | `Booking` *(❌ Reservation)* | Avaliação | `Review` |
| Hospedagem (período) | `Stay` | Nota | `Rating` |
| Anfitrião / hospedeiro | `Host` | Pagamento | `Payment` |
| Tutor / dono | `Owner` *(❌ Tutor, Client)* | Repasse | `Payout` |
| Pet / animal | `Pet` *(❌ Animal)* | Reembolso | `Refund` |
| Espécie / porte | `Species` / `Size` | Cancelamento | `Cancellation` |
| Cuidador | `Sitter` | Check-in / check-out | `CheckIn` / `CheckOut` |
| Disponibilidade | `Availability` | Diária | `NightlyRate` |
| Usuário / perfil | `User` / `Profile` | Vacina | `Vaccination` |
| Inativar (pela pessoa) | `Deactivate` | Suspender (pelo admin) | `Suspend` |
| Trilha de auditoria | `AuditTrail` / `AuditEntry` | Motivo | `Reason` |
| Dono do pet (tutor **ou** anfitrião) | `Keeper` *(❌ Owner, que é só o tutor)* | Animal fora da lista | `Exotic` (+ `SpeciesDescription`) |
| Pessoa física / jurídica | `Individual` / `Company` (`PersonType`) | Razão social / nome fantasia | `LegalName` / `TradeName` |

---

## 13. Endpoints

```
/api/v{version}/{resource}[/{id}][/{sub-resource}]
```

Prefixo `/api` sempre · versão obrigatória desde o primeiro endpoint · recurso substantivo **plural** em `kebab-case` · ID com constraint tipada (`{bookingId:guid}`) · query string `camelCase`.

**Ação não-CRUD:** prefira sub-recurso substantivado (`POST /bookings/{id}/cancellation`). Verbo curto no fim da rota é **permitido** quando a ação é sobre a sessão ou a própria conta e o substantivo ficaria artificial — sempre `POST`, sempre no último segmento:

| ❌ | ✅ |
|----|----|
| `POST /bookings/{id}/cancel` | `POST /bookings/{id}/cancellation` |
| `POST /auth/do-login` | `POST /auth/sessions/login` |
| `POST /auth/refresh-token` | `POST /auth/sessions/refresh` |
| `POST /users/create` | `POST /users/register` |
| `POST /users/{id}/disable` (id vindo do cliente) | `POST /owners/me/deactivate` |
| `POST /pets/{id}/deactivate` (recurso que não é a conta) | `POST /pets/{id}/deactivation` · reativar: `DELETE /pets/{id}/deactivation` |

Verbos em uso: `register`, `login`, `refresh`, `logout`, `switch`, `deactivate`, `reactivate`, `forgot`, `reset`. Sub-recurso da conta: `POST /users/me/password` (troca de senha logada).

**`me`:** rota sobre a conta do usuário autenticado usa `me` (`GET /users/me`, `GET /owners/me`); o id sai do token (`sub`), **nunca** do corpo ou da rota. Ninguém lê nem altera dado de outra pessoa por um id que ela mesma enviou.

| Verbo | Uso | Sucesso |
|-------|-----|---------|
| `GET` | Leitura, sem efeito colateral | `200` |
| `POST` | Cria recurso ou dispara ação | `201` (com `Location`) / `200` |
| `PUT` / `PATCH` | Substitui / atualiza parcialmente | `200` |
| `DELETE` | Remove (preferir soft delete) | `200` |

`Location` aponta para onde o recurso criado é lido: `/{resource}/{id}` ou, quando o recurso é do próprio usuário, a rota `me` (`Location: /api/v1/users/me`). No controller: `result.ToCreatedResult(location)`.

**Edição de perfil é `PATCH`:** só o que vier no corpo muda. Campo ausente ou `null` = não mexer; texto vazio (`""`) limpa um campo opcional, porque `null` não distingue "não mexer" de "apagar". Objeto aninhado (endereço) também é parcial e é validado inteiro depois de completado com o valor atual. Todos os erros de validação voltam juntos.

**Dado sensível fora do `PATCH`:** senha e e-mail têm rota própria e pedem a senha atual (`POST /users/me/password`). Dado sensível que fica no `PATCH` do papel (CPF do tutor) pede `currentPassword` quando muda. Senha atual errada é `400` no campo `currentPassword`, não `401` — o token é válido.

> Nunca `204` — o envelope acompanha toda resposta.

```
POST   /api/v1/users/register      ·  GET  /api/v1/users/me
POST   /api/v1/auth/sessions/login    ·  POST /api/v1/auth/sessions/refresh
POST   /api/v1/pets               ·  GET  /api/v1/pets/me  ·  PATCH /api/v1/pets/{petId:guid}
POST   /api/v1/pets/{petId:guid}/deactivation  ·  DELETE /api/v1/pets/{petId:guid}/deactivation
GET    /api/v1/hosts/{hostId:guid}/pets
POST   /api/v1/bookings               ·  GET  /api/v1/bookings/{bookingId:guid}
POST   /api/v1/bookings/{bookingId:guid}/cancellation
GET    /api/v1/hosts/{hostId:guid}/availability?from=2026-11-01&to=2026-11-30
```

**Paginação:** `?page=1&pageSize=20` — `page` inicia em 1, `pageSize` default 20 e máximo 100. Resposta em `Data` via `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`, `totalPages`).

Exceções, sem paginação nem filtro enquanto o volume for pequeno:

- **Lista administrativa** (só `admin`) — ex.: `GET /owners`.
- **Pets de uma pessoa** — `GET /pets/me` e `GET /hosts/{hostId}/pets`: poucos itens por definição (os pets de um tutor ou de uma casa). Pedido do produto. Como todos os itens são do mesmo dono, a resposta traz o dono **uma vez** no topo (`{ keeper, pets }`) em vez de repeti-lo em cada item — regra para qualquer lista "de uma pessoa".

Fora isso, lista que o usuário final vê é sempre paginada.

Todo endpoint tem `/// <summary>`, `[ProducesResponseType<ApiResponse<T>>]` para **cada** status possível e `[Authorize]`/`[AllowAnonymous]` explícito — é o que aparece no Swagger (`/swagger`, só em Development, documento do `Microsoft.AspNetCore.OpenApi` em `/openapi/v1.json`, com o JWT declarado para o botão Authorize). O `429` do limite geral vale para todos e não é repetido em cada um; endpoint com política própria declara o `429`.

### Rate limit

Rate limiting nativo do ASP.NET Core (`AddPetHostRateLimiting`, em `Shared.Infrastructure/RateLimiting`), janela fixa, sem fila:

- **Limite geral** em todo endpoint: por usuário logado (`sub`) ou, sem token, por IP.
- **Endpoint crítico** ganha política própria, mais rígida, com `[EnableRateLimiting(RateLimitPolicies.X)]` — conta à parte do limite geral:

| Política | Para | Chave | Padrão |
|---|---|---|---|
| `credentials` | Senha conferida sem sessão: login, troca de conta, reativação | IP | 10 / 5 min |
| `registration` | Criar conta | IP | 10 / 1 h |
| `password-reset` | Esqueci a senha (pedido e troca com o token) | IP | 5 / 15 min |
| `session` | Refresh e logout | IP | 30 / 1 min |
| `account-sensitive` | Logado, pede a senha ou derruba a conta: trocar senha, inativar | usuário | 5 / 15 min |
| `account-update` | `PATCH` de perfil (inclui troca de CPF) | usuário | 20 / 15 min |
| geral | Todo endpoint | usuário ou IP | 120 / 1 min |

- Estourou: `429 TOO_MANY_REQUESTS` no envelope + `Retry-After`. Log `Warning` com rota e IP.
- Health checks ficam fora (`DisableRateLimiting`).
- Endpoint novo que confere senha, cria conta ou manda e-mail **precisa** de política própria.
- Atrás de proxy, o IP vem do `X-Forwarded-For` (`ForwardedHeaders:Enabled`), confiando só em proxy de rede privada e loopback.
- Os contadores ficam em memória: valem por instância. Com mais de uma instância, mover para o Redis.

---

## 14. Testes

> **Nenhum PR é aprovado sem teste.** Todo arquivo com lógica tem teste unitário; todo arquivo que cruza fronteira de processo tem teste de integração.

**Stack:** `xunit.v3` · `Moq` · `FluentAssertions 7.x` · `Microsoft.Extensions.TimeProvider.Testing` · `Testcontainers.PostgreSql` · `Microsoft.AspNetCore.Mvc.Testing` · `Bogus` · `NetArchTest` · `coverlet.collector`.

### Exige teste unitário

| Alvo | Cobrir |
|------|--------|
| Agregado / entidade | Cada fábrica e transição, **cada ramo de falha** |
| Value object | `Create` válido/inválido, igualdade, normalização |
| Command/Query handler | Caminho feliz + **cada** `Result` de falha + interação com dublês |
| Validador | Uma asserção por regra |
| `Result` / `Result<T>` | Fábricas, invariantes, `Map`, `BindAsync`, conversões implícitas |
| `ApiResponseMapper` | Sucesso, 1 erro, N erros por campo, mistura de tipos, `Field` nulo |
| Mapeamento de status HTTP | Cada código do catálogo + fallback |
| Decorator / extension | Curto-circuito na falha; toda função pública pura |

### Exige teste de integração

Repositório · configuração EF (`IEntityTypeConfiguration`) · migration em banco limpo · controller/endpoint (rota, binding, autorização, **formato do envelope**) · pipeline de auth (401 vs 403 vs 200) · Unit of Work (commit e rollback) · outbox e integration events · cliente HTTP externo (contra WireMock) · concorrência otimista → `CONFLICT`.

**Não precisa:** DTO sem lógica, enum, constante, `DependencyInjection.cs`.

### Estrutura e nomes

O caminho do teste espelha o do código; arquivo `<TypeUnderTest>Tests.cs`, classe `sealed`.

Nome do teste, em inglês: **`MethodName_Should_ExpectedOutcome_When_Condition`**

```
Create_Should_ReturnValidationError_When_CheckInIsInThePast
Cancel_Should_RaiseBookingCancelledDomainEvent_When_BookingIsPending
HandleAsync_Should_NotCallSaveChanges_When_DomainValidationFails
ToApiResponse_Should_GroupMessagesByField_When_MultipleErrorsShareTheSameField
```

| Regra | Detalhe |
|-------|---------|
| **AAA** | Blocos `// Arrange`, `// Act`, `// Assert` explícitos |
| Um comportamento por teste | Múltiplas asserções só para o **mesmo** comportamento |
| **Zero lógica no teste** | Sem `if`/`for`/`switch`/`try`. Variação → `[Theory]` + `[InlineData]` |
| Campo sob teste `_sut` | *System Under Test* |
| `MockBehavior.Strict` | Padrão do projeto: chamada não configurada falha o teste |
| Dado irrelevante vem de builder | Builders em `PetHost.TestKit` — evita quebrar teste ao adicionar campo |
| Determinístico | Sem `Thread.Sleep`, sem random sem seed, sem `DateTime.Now` → `FakeTimeProvider` |
| Verificar ausência de efeito | Em falha, afirmar `Times.Never` em repositório/UoW |

```csharp
[Fact]
public async Task HandleAsync_Should_ReturnOverlappingPeriodError_When_HostIsNotAvailable()
{
    // Arrange
    var command = ValidCommand();
    _availabilityChecker
        .Setup(x => x.IsHostAvailableAsync(command.HostId, command.CheckIn, command.CheckOut, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result<bool>.Success(false));

    // Act
    var result = await _sut.HandleAsync(command, CancellationToken.None);

    // Assert
    result.IsFailure.Should().BeTrue();
    result.FirstError.Should().BeEquivalentTo(BookingErrors.OverlappingPeriod);
    _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
}
```

Integração: `WebApplicationFactory<Program>` + `PostgreSqlContainer` (`postgres:17-alpine`) por módulo, container compartilhado pela collection, banco limpo entre testes com `Respawn`. Asserção obrigatória sobre o **envelope**, não só o status.

### Cobertura

| Camada | Linha | Branch |
|--------|-------|--------|
| `Domain`, `Shared.Kernel`, `Shared.Contracts` | **95 %** | 90 % |
| `Application` | **90 %** | 85 % |
| `Infrastructure` | 70 % | — *(coberta por integração)* |
| **Solution** | **85 %** | 80 % |

Pipeline falha abaixo do mínimo. `[ExcludeFromCodeCoverage]` exige justificativa em comentário — permitido só em `Migrations/` e `DependencyInjection.cs`.

```bash
dotnet test --collect:"XPlat Code Coverage" --results-directory ./artifacts/coverage
```

---

## 15. Exceções e observabilidade

Exceção é **falha inesperada**, nunca controle de fluxo.

| Regra | Detalhe |
|-------|---------|
| Nunca `catch` vazio | Engolir exceção é proibido |
| Nunca `throw ex` | Use `throw;` para preservar o stack trace |
| Nunca expor detalhe interno | A resposta leva só `code`, mensagem genérica e `traceId` |
| `catch` específico na `Infrastructure` | Traduza para `Error`: `UniqueViolation` → código de módulo, `DbUpdateConcurrencyException` → `CONFLICT` |
| Log no ponto de tratamento | Não logar e relançar |
| Toda exceção 500 gera alerta | Monitoramento obrigatório |

Um `IExceptionHandler` global no host é o **único** lugar que captura exceção não tratada: loga com `TraceId` e devolve `ApiResponse` com `UNEXPECTED_ERROR` + 500.

| Item | Padrão |
|------|--------|
| Logging | Serilog, JSON, *message template* — `logger.LogInformation("Booking {BookingId} created.", id)`, nunca interpolação |
| Nível | `Information` para caso de uso concluído, `Warning` para falha de negócio relevante, `Error` só para exceção |
| Correlação | `TraceId` em toda requisição e log; devolvido no erro 500 |
| Dado sensível | **Nunca** logar senha, token, hash, CPF, cartão. Mascarar e-mail |
| Tracing / métricas | OpenTelemetry (ASP.NET Core, `HttpClient`, Npgsql); contador de caso de uso por resultado |
| Health checks | `/health/live` e `/health/ready` (inclui Postgres) |

### Trilha de auditoria

Log é para operar o sistema; a **trilha** (módulo Audit, `audit.audit_entries`) é o registro de negócio de quem fez o quê. Todo módulo registra pelo contrato `IAuditTrail` (`Shared.Contracts/Audit`), nunca referenciando o Audit.

| Regra | Detalhe |
|-------|---------|
| O que registrar | Toda mudança de estado relevante (criar, alterar dado sensível, mudar status), toda ação do admin sobre dado de outra pessoa, login (sucesso e falha) e leitura de dado pessoal pelo admin (LGPD) |
| O que não registrar | Leitura do próprio usuário, refresh, logout — o log do request basta |
| Quando | No handler, **depois** que a operação gravou. A trilha nunca derruba o request |
| Ação do admin | Sempre com `reason` (obrigatório na API) |
| Conteúdo | `AuditActions.X` (`alvo.ação`) + `AuditTargets.X` + id. Campo alterado pelo **nome**, nunca pelo valor. CPF só mascarado. Nunca senha, token, hash, e-mail digitado |
| Quem fez | Sai do token; informe `ActorId` só quando não há token (login, cadastro) |

---

## 16. Git

```
main                                    ← protegida, sempre deployável
feat|fix|refactor|chore/<module>-<short-description>
```

Commits em **Conventional Commits** — `<type>(<scope>): <descrição no imperativo>`, com `scope` = módulo. Tipo e escopo em inglês; descrição pode ser em português.

```
feat(booking): add cancellation endpoint
fix(auth): expire refresh token after rotation
test(booking): cover overlapping period rule
```

**PR:** título em Conventional Commit · descrição com o que/por quê/como testar · CI verde (build + testes + cobertura + arquitetura) · ≥1 approve · alteração de contrato público (`Code`, formato de resposta, rota) exige nota explícita de *breaking change*.

---

## 17. Definition of Done

- [ ] Compila com `TreatWarningsAsErrors` — **zero** warning
- [ ] Identificadores em **inglês**; termo conferido no [glossário](#glossário-do-domínio)
- [ ] Camadas respeitadas (§4); nenhuma referência a outro módulo fora de `Shared.Contracts`
- [ ] Regra de negócio no agregado, não no handler
- [ ] Todo retorno que pode falhar usa `Result`; nenhuma exceção como controle de fluxo
- [ ] Toda `ICommand` tem validador
- [ ] `TimeProvider` injetado — nenhum `DateTime.UtcNow` em `Domain`/`Application`
- [ ] `CancellationToken` propagado até o EF Core
- [ ] Código de erro novo declarado em `<Module>Errors` e mapeado para status HTTP
- [ ] Endpoint com `[Authorize]`/`[AllowAnonymous]` e `[ProducesResponseType]` por status
- [ ] Resposta via `ToActionResult()` — nada montado à mão
- [ ] **Teste unitário** para cada ramo novo, inclusive os de falha
- [ ] **Teste de integração** se cruza fronteira (§14)
- [ ] Testes nomeados `MethodName_Should_X_When_Y`, em inglês; cobertura dentro do mínimo
- [ ] Testes de arquitetura passando
- [ ] Migration com nome descritivo, aplicada em banco limpo
- [ ] Nenhum dado sensível em log; `/// <summary>` em todo membro público
- [ ] Mudança de estado relevante e ação do admin registradas na trilha (`IAuditTrail`), sem dado sensível
- [ ] Este documento atualizado, se um padrão mudou

---

## 18. Novo módulo

1. Criar os quatro projetos em `src/Modules/<Module>/` (`Domain`, `Application`, `Infrastructure`, `Presentation`) e ligar as referências conforme §4.
2. Criar `Domain.UnitTests`, `Application.UnitTests` e `IntegrationTests` em `tests/Modules/<Module>/`.
3. Implementar nesta ordem, **cada etapa com seus testes**:
   `Domain` (agregado, VOs, `<Module>Errors`, interface de repositório) → `Application` (Command/Query + Handler + Validator + Response) → `Infrastructure` (DbContext com schema próprio, configurações, repositório, migration) → `Presentation` (controller) → `IntegrationTests`.
4. Registrar no `Program.cs` e adicionar a asserção do módulo em `PetHost.ArchitectureTests`.
5. Atualizar este documento: módulo em §2 e termos novos no glossário.
