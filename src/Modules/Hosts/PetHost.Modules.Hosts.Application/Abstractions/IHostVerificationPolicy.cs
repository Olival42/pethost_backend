namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>Prazos da verificação, vindos da configuração (<c>HostVerification:*</c>).</summary>
public interface IHostVerificationPolicy
{
    /// <summary>
    /// Por quanto tempo as fotos ficam depois que a solicitação fecha. <see cref="TimeSpan.Zero"/>
    /// (o padrão) apaga na hora da decisão.
    /// </summary>
    TimeSpan DocumentRetention { get; }

    /// <summary>Quanto tempo um rascunho fica aberto antes de expirar.</summary>
    TimeSpan DraftLifetime { get; }
}
