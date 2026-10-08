using System.Globalization;

namespace PetHost.TestKit;

/// <summary>
/// Guids fixos e legíveis para teste: <c>TestIds.Of(42)</c> vira
/// <c>00000000-0000-0000-0000-000000000042</c>. Deixa a asserção comparar com um
/// valor conhecido sem espalhar Guids aleatórios pelos testes.
/// </summary>
public static class TestIds
{
    public static Guid Of(int number) =>
        Guid.Parse(number.ToString("D12", CultureInfo.InvariantCulture).PadLeft(32, '0'));
}
