namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Em que pé está a solicitação de cadastro de anfitrião.</summary>
public enum HostApplicationStatus
{
    /// <summary>Aberta: a pessoa ainda está enviando dados e fotos.</summary>
    Draft,

    /// <summary>Enviada: na fila do admin.</summary>
    Submitted,

    /// <summary>Aprovada: o perfil de anfitrião foi criado.</summary>
    Approved,

    /// <summary>Recusada com motivo; dá para abrir outra (até o limite de tentativas).</summary>
    Rejected,

    /// <summary>Recusada por suspeita de fraude: sem nova tentativa pelo app.</summary>
    RejectedFinal,

    /// <summary>A pessoa desistiu.</summary>
    Cancelled,

    /// <summary>Rascunho abandonado: fechado pela tarefa de limpeza.</summary>
    Expired,
}
