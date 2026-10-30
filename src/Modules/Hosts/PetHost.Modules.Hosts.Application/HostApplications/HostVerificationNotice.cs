namespace PetHost.Modules.Hosts.Application.HostApplications;

/// <summary>
/// O aviso mostrado antes da verificação ("Usamos as fotos só para confirmar que você é
/// você..."). Quando o texto mudar, a versão muda: cada solicitação guarda a versão que a
/// pessoa aceitou.
/// </summary>
public static class HostVerificationNotice
{
    /// <summary>Versão vigente do aviso (data da redação).</summary>
    public const string CurrentVersion = "2026-10-10";
}
