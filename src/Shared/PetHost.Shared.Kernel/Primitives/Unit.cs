namespace PetHost.Shared.Kernel.Primitives;

/// <summary>Ausência de valor de retorno. O envelope acompanha toda resposta — nunca 204 (§7).</summary>
public sealed record Unit
{
    public static readonly Unit Value = new();

    private Unit() { }
}
