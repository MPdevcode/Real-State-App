using System.Globalization;

namespace RealEstateApp.WebApp.Support;

public static class Presentation
{
    public static string Money(decimal value) => $"DOP {value.ToString("N2", CultureInfo.GetCultureInfo("es-DO"))}";
    public static string Date(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " (RD)";
    public static string Image(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "/images/placeholder.svg";
        if (key.Length != 36 || !key.EndsWith(".jpg", StringComparison.Ordinal)
            || !Guid.TryParseExact(key[..32], "N", out _)) return "/images/placeholder.svg";
        return "/media/" + key;
    }
}
