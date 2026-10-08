namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Base64 seguro para URL e header: sem <c>+</c>, <c>/</c> nem <c>=</c>, que
/// exigiriam escape ao trafegar o token.
/// </summary>
internal static class Base64Url
{
    public static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
