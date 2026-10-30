using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Hosts.Application.HostApplications.CancelMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.GetMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Application.HostApplications.StartHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.SubmitMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UpdateMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;
using PetHost.Modules.Hosts.Application.Hosts.GetMyHost;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Presentation.Hosts;

/// <summary>
/// O anfitrião logado: o perfil verificado e a solicitação de cadastro (verificação de
/// identidade com documento e selfie, aprovada pelo admin).
/// </summary>
[ApiController]
[Route("api/v1/hosts/me")]
public sealed class HostsController(
    IQueryHandler<GetMyHostQuery, HostResponse> getMyHost,
    IQueryHandler<GetMyHostApplicationQuery, MyHostApplicationResponse> getMyApplication,
    ICommandHandler<StartHostApplicationCommand, HostApplicationResponse> startApplication,
    ICommandHandler<UpdateMyHostApplicationCommand, HostApplicationResponse> updateApplication,
    ICommandHandler<UploadHostApplicationDocumentsCommand, HostApplicationResponse> uploadDocuments,
    ICommandHandler<SubmitMyHostApplicationCommand, HostApplicationResponse> submitApplication,
    ICommandHandler<CancelMyHostApplicationCommand, HostApplicationResponse> cancelApplication) : ControllerBase
{
    /// <summary>Onde a solicitação da conta é lida: vai no <c>Location</c> de quando ela é aberta.</summary>
    public const string ApplicationRoute = "/api/v1/hosts/me/application";

    /// <summary>Perfil de anfitrião da conta logada. Só existe depois da aprovação do admin.</summary>
    /// <remarks>404 <c>HOST_NOT_FOUND</c> enquanto a verificação não foi aprovada.</remarks>
    [HttpGet]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        var result = await getMyHost.HandleAsync(new GetMyHostQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>A tela "Minha verificação": a solicitação mais recente e se dá para abrir outra.</summary>
    /// <remarks>
    /// Nunca 404: quem nunca pediu recebe <c>application</c> nulo e <c>canStartNew: true</c>.
    /// Traz também a versão vigente do aviso de uso das fotos.
    /// </remarks>
    [HttpGet("application")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<MyHostApplicationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MyHostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MyHostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyApplicationAsync(CancellationToken cancellationToken)
    {
        var result = await getMyApplication.HandleAsync(new GetMyHostApplicationQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Abre a solicitação de cadastro de anfitrião (rascunho).</summary>
    /// <remarks>
    /// Corpo: <c>cpf</c>, <c>documentType</c> (<c>rg</c>, <c>cnh</c> ou <c>cin</c>) e
    /// <c>acceptNotice: true</c> (a pessoa leu como as fotos são usadas). Depois, envie as três
    /// fotos e a solicitação. Já aberta: 409 <c>HOST_APPLICATION_ALREADY_EXISTS</c>; CPF de outro
    /// anfitrião: 409 <c>HOST_CPF_ALREADY_REGISTERED</c>; já aprovada, bloqueada ou sem
    /// tentativas: 422.
    /// </remarks>
    [HttpPost("application")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> StartApplicationAsync(
        [FromBody] StartHostApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new StartHostApplicationCommand(
            User.GetUserId(),
            request.Cpf,
            request.DocumentType,
            request.AcceptNotice,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            request.PersonType,
            request.Cnpj,
            request.LegalName,
            request.TradeName,
            request.CompanyAddress);

        var result = await startApplication.HandleAsync(command, cancellationToken);

        return result.ToCreatedResult(ApplicationRoute);
    }

    /// <summary>Corrige CPF e/ou tipo de documento do rascunho. Só o que vier muda.</summary>
    /// <remarks>Depois de enviada, nada muda: 422 <c>HOST_APPLICATION_NOT_EDITABLE</c>.</remarks>
    [HttpPatch("application")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateApplicationAsync(
        [FromBody] UpdateMyHostApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await updateApplication.HandleAsync(
            new UpdateMyHostApplicationCommand(
                User.GetUserId(),
                request.Cpf,
                request.DocumentType,
                request.Cnpj,
                request.LegalName,
                request.TradeName,
                request.CompanyAddress),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Envia as fotos do rascunho num pedido só: frente, verso e selfie com o documento.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com as partes <c>front</c>, <c>back</c> e <c>selfie</c>
    /// (JPG/JPEG, PNG ou WebP, até 5 MB cada, conferido pelo conteúdo). O normal é mandar as
    /// três; mandar só algumas troca só essas (as outras ficam). Tudo ou nada: se uma foto for
    /// recusada, nenhuma entra, e o erro aponta a parte (<c>field: "back"</c>). As fotos vão
    /// para um bucket <b>privado</b>: não existe link público, e a resposta não devolve a
    /// imagem. Trocar uma foto apaga a anterior. A mesma imagem em duas vagas: 409.
    /// </remarks>
    [HttpPut("application/documents")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ImageUploadEndpoint(MaxImages = 3, Fields = "front, back, selfie")]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadDocumentsAsync(
        IFormFile? front,
        IFormFile? back,
        IFormFile? selfie,
        CancellationToken cancellationToken)
    {
        var result = await uploadDocuments.HandleAsync(
            new UploadHostApplicationDocumentsCommand(
                User.GetUserId(),
                front.ToImageUpload(),
                back.ToImageUpload(),
                selfie.ToImageUpload()),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Envia o rascunho para a análise do admin.</summary>
    /// <remarks>Faltando foto: 422 <c>HOST_APPLICATION_DOCUMENTS_MISSING</c>, dizendo quais.</remarks>
    [HttpPost("application/submission")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitApplicationAsync(CancellationToken cancellationToken)
    {
        var result = await submitApplication.HandleAsync(new SubmitMyHostApplicationCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Desiste da solicitação aberta (rascunho ou em análise). As fotos são apagadas.</summary>
    [HttpDelete("application")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CancelApplicationAsync(CancellationToken cancellationToken)
    {
        var result = await cancelApplication.HandleAsync(new CancelMyHostApplicationCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo de <c>POST /hosts/me/application</c>. O id vem do token; o IP, do servidor.</summary>
/// <param name="DocumentType"><c>rg</c>, <c>cnh</c> ou <c>cin</c>.</param>
/// <param name="AcceptNotice">Precisa ser <c>true</c>: a pessoa leu como as fotos são usadas.</param>
/// <param name="PersonType"><c>pf</c> (padrão, pessoa física) ou <c>pj</c> (pessoa jurídica); maiúsculas também valem.</param>
/// <param name="Cpf">Pessoa física: o próprio. Empresa: o do representante legal (dono da conta).</param>
/// <param name="Cnpj">Só empresa, com os outros três campos de empresa.</param>
/// <param name="LegalName">Razão social. Só empresa.</param>
/// <param name="TradeName">Nome fantasia. Só empresa.</param>
/// <param name="CompanyAddress">Endereço da empresa. Só empresa.</param>
public sealed record StartHostApplicationRequest(
    string? Cpf,
    string? DocumentType,
    bool? AcceptNotice,
    string? PersonType = null,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null);

/// <summary>Corpo de <c>PATCH /hosts/me/application</c>: só o que vier muda.</summary>
/// <remarks>Os dados da empresa, se vierem, vêm inteiros (só em solicitação de empresa).</remarks>
public sealed record UpdateMyHostApplicationRequest(
    string? Cpf,
    string? DocumentType,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null);
