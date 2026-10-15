using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public readonly record struct ClientVersion(int Major, int Minor, int Patch) : IComparable<ClientVersion>
{
    public static bool TryParse(string? value, out ClientVersion version)
    {
        version = default;
        if (value is null || value.Length is < 5 or > 14 || !Regex.IsMatch(value, @"\A(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z", RegexOptions.CultureInvariant))
            return false;
        var parts = value.Split('.');
        version = new(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture));
        return true;
    }

    public int CompareTo(ClientVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }
}
