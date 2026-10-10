using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Pets.Application.Pets;

/// <summary>Registro na trilha das mudanças nas fotos do pet (adicionar, substituir, tirar).</summary>
internal static class PetPhotoTrail
{
    /// <param name="change"><c>photos.added</c>, <c>photos.replaced</c> ou <c>photos.removed</c>.</param>
    public static Task RecordAsync(IAuditTrail auditTrail, Pet pet, string change, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.PetUpdated,
                AuditTargets.Pet,
                pet.Id.Value,
                Details: new Dictionary<string, string?> { ["fields"] = change }),
            cancellationToken);
}
