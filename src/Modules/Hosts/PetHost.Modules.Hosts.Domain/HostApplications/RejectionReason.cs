namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>
/// Por que o admin recusou. É o que a pessoa vê (o front traduz em uma mensagem no tom do
/// PetHost); a nota do admin é interna.
/// </summary>
public enum RejectionReason
{
    /// <summary>Foto tremida, escura ou cortada: não dá para ler.</summary>
    DocumentIllegible,

    /// <summary>Documento vencido (CNH).</summary>
    DocumentExpired,

    /// <summary>A selfie não mostra a mesma pessoa do documento, ou não mostra o documento.</summary>
    SelfieMismatch,

    /// <summary>Nome, nascimento ou CPF do documento não batem com a conta.</summary>
    DataMismatch,

    /// <summary>Uma das fotos não é o que devia (ex.: frente enviada como verso).</summary>
    MissingDocument,

    /// <summary>Suspeita de fraude. É a única recusa <b>definitiva</b>.</summary>
    SuspectedFraud,

    /// <summary>Outro motivo, explicado na nota.</summary>
    Other,
}
