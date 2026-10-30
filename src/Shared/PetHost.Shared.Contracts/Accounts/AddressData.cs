namespace PetHost.Shared.Contracts.Accounts;

/// <summary>Endereço como vem na request e como sai na resposta.</summary>
public sealed record AddressData(
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? Neighborhood,
    string? City,
    string? State);
