namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Qual das três fotos da verificação.</summary>
public enum DocumentKind
{
    /// <summary>Frente do documento.</summary>
    Front,

    /// <summary>Verso do documento.</summary>
    Back,

    /// <summary>Selfie da pessoa segurando o documento, com o rosto à mostra.</summary>
    Selfie,
}
