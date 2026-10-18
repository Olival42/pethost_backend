namespace PetHost.Modules.Hosts.Domain.IdentityBlocks;

/// <summary>Por que o CPF foi bloqueado.</summary>
public enum IdentityBlockReason
{
    /// <summary>Recusa por suspeita de fraude (<c>rejected_final</c>).</summary>
    Fraud,

    /// <summary>A conta usou as tentativas sem ser aprovada.</summary>
    AttemptsExhausted,
}

/// <summary>Texto do motivo no banco e na trilha.</summary>
public static class IdentityBlockReasonValues
{
    public const string Fraud = "fraud";
    public const string AttemptsExhausted = "attempts_exhausted";

    public static string ToWire(IdentityBlockReason reason) => reason switch
    {
        IdentityBlockReason.Fraud => Fraud,
        IdentityBlockReason.AttemptsExhausted => AttemptsExhausted,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static IdentityBlockReason FromWire(string value) => value switch
    {
        Fraud => IdentityBlockReason.Fraud,
        AttemptsExhausted => IdentityBlockReason.AttemptsExhausted,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
