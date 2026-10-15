using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

internal static partial class TableNamingConvention
{
    public static void Apply(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is not { } table || table.StartsWith("t_", StringComparison.Ordinal))
                continue;
            entity.SetTableName(Prefixed(table));
        }
    }

    public static string Prefixed(string table)
    {
        var words = AcronymBoundary().Replace(table, "$1_$2");
        words = WordBoundary().Replace(words, "$1_$2");
        return "t_" + words.ToLowerInvariant();
    }

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundary();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundary();
}
