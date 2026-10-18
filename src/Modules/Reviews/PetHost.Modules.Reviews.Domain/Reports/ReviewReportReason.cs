namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>Por que a avaliação foi denunciada.</summary>
public enum ReviewReportReason
{
    /// <summary>Ofensa, assédio ou discriminação.</summary>
    Offensive,

    /// <summary>O que está escrito não aconteceu (a estadia foi outra).</summary>
    FalseInformation,

    /// <summary>Expõe dado pessoal: telefone, endereço, documento.</summary>
    PersonalData,

    /// <summary>Propaganda ou texto que nada tem a ver com a estadia.</summary>
    Spam,

    /// <summary>Outro motivo: a descrição é obrigatória.</summary>
    Other,
}

/// <summary>Valores de <see cref="ReviewReportReason"/> no banco e no JSON.</summary>
public static class ReviewReportReasonValues
{
    public const string Offensive = "offensive";
    public const string FalseInformation = "false_information";
    public const string PersonalData = "personal_data";
    public const string Spam = "spam";
    public const string Other = "other";

    public static IReadOnlyList<string> All { get; } = [Offensive, FalseInformation, PersonalData, Spam, Other];

    public static string ToWire(ReviewReportReason reason) => reason switch
    {
        ReviewReportReason.Offensive => Offensive,
        ReviewReportReason.FalseInformation => FalseInformation,
        ReviewReportReason.PersonalData => PersonalData,
        ReviewReportReason.Spam => Spam,
        ReviewReportReason.Other => Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown report reason."),
    };

    public static ReviewReportReason FromWire(string value) =>
        TryFromWire(value, out var reason)
            ? reason
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown report reason.");

    /// <summary>O motivo do texto, exatamente como na lista (minúsculo). Para validar a entrada.</summary>
    public static bool TryFromWire(string? value, out ReviewReportReason reason)
    {
        (var known, reason) = value switch
        {
            Offensive => (true, ReviewReportReason.Offensive),
            FalseInformation => (true, ReviewReportReason.FalseInformation),
            PersonalData => (true, ReviewReportReason.PersonalData),
            Spam => (true, ReviewReportReason.Spam),
            Other => (true, ReviewReportReason.Other),
            _ => (false, default),
        };

        return known;
    }
}
