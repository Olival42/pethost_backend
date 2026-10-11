using FluentValidation;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;

/// <summary>
/// Envia (ou troca) as fotos da solicitação em rascunho num pedido só. O normal é mandar as
/// três; mandar só uma ou duas troca só essas vagas.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Front">A parte <c>front</c> do multipart (frente do documento). Nula se não veio.</param>
/// <param name="Back">A parte <c>back</c> (verso). Nula se não veio.</param>
/// <param name="Selfie">A parte <c>selfie</c> (a pessoa segurando o documento). Nula se não veio.</param>
public sealed record UploadHostApplicationDocumentsCommand(
    Guid UserId,
    ImageUpload? Front,
    ImageUpload? Back,
    ImageUpload? Selfie) : ICommand<HostApplicationResponse>;

/// <summary>
/// Só confere que veio alguma foto. Cada arquivo (vazio, grande, formato) é conferido pelo
/// bucket, depois de saber que a conta tem rascunho aberto.
/// </summary>
public sealed class UploadHostApplicationDocumentsCommandValidator : AbstractValidator<UploadHostApplicationDocumentsCommand>
{
    public UploadHostApplicationDocumentsCommandValidator() =>
        RuleFor(x => x)
            .Must(x => x.Front is not null || x.Back is not null || x.Selfie is not null)
            .OverridePropertyName(HostsErrors.DocumentsRequired.Field!)
            .WithMessage(HostsErrors.DocumentsRequired.Message);
}
