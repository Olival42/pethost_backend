namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Documento de identidade com foto aceito na verificação.</summary>
public enum IdentityDocumentType
{
    /// <summary>RG (Registro Geral), o modelo antigo.</summary>
    Rg,

    /// <summary>CNH (Carteira Nacional de Habilitação) física. Traz o CPF e a validade.</summary>
    Cnh,

    /// <summary>CIN (Carteira de Identidade Nacional), a nova: o número é o próprio CPF.</summary>
    Cin,
}
