namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Um aviso do "Combina com seu pet". O texto ("A Pipoca não se dá com gatos e aqui tem gato") fica com o app.</summary>
/// <param name="Code">Um dos códigos de <see cref="PetCompatibility"/>.</param>
/// <param name="Guest">O outro hóspede do período que gerou o aviso; nulo nos avisos sobre o cantinho e a casa.</param>
public sealed record CompatibilityWarning(string Code, CompatibilityPet? Guest = null);
