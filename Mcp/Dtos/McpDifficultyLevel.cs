namespace MB.ComTools.Apps.Setup.Mcp.Dtos;

/// <summary>
/// Wire values for course difficulty. Mirrors <c>LearnCourse.DifficultyLevel</c>
/// (Unset=0, Beginner=1, Intermediate=2, Expert=3) as planned for Fachbereich.
/// </summary>
public static class McpDifficultyLevel
{
    public const string Unset = "Unset";
    public const string Beginner = "Beginner";
    public const string Intermediate = "Intermediate";
    public const string Expert = "Expert";

    /// <summary>
    /// Parses user/API filter text (de/en) into the integer stored on LearnCourse.
    /// </summary>
    public static bool TryParse(string? input, out int level)
    {
        level = 0;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim().ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal);

        if (normalized is "0" or "unset" or "none" or "null" or "unbekannt"
            or "k.a." or "k. a.")
        {
            level = 0;
            return true;
        }

        if (normalized is "3" or "expert" or "experte" or "experts" or "profi"
            or "professional"
            || normalized.Contains("expert", StringComparison.Ordinal)
            || normalized.Contains("experte", StringComparison.Ordinal))
        {
            level = 3;
            return true;
        }

        if (normalized is "1" or "beginner" or "anfaenger" or "einsteiger"
            or "basics" or "basic" or "grundlagen" or "entry" or "junior"
            || normalized.Contains("anfaenger", StringComparison.Ordinal)
            || normalized.Contains("einsteiger", StringComparison.Ordinal)
            || normalized.Contains("beginner", StringComparison.Ordinal))
        {
            level = 1;
            return true;
        }

        if (normalized is "2" or "intermediate" or "fortgeschritten" or "medium"
            or "mittel" or "advanced"
            || normalized.Contains("fortgeschritten", StringComparison.Ordinal)
            || normalized.Contains("intermediate", StringComparison.Ordinal))
        {
            level = 2;
            return true;
        }

        return false;
    }

    public static string? ToWireValue(int level) => level switch
    {
        1 => Beginner,
        2 => Intermediate,
        3 => Expert,
        _ => null
    };

    public static string? ToWireValue(Enum level) =>
        ToWireValue(Convert.ToInt32(level));

    /// <summary>Canonical MCP filter argument (includes Unset).</summary>
    public static string? ToFilterValue(int level) => level switch
    {
        0 => Unset,
        1 => Beginner,
        2 => Intermediate,
        3 => Expert,
        _ => null
    };

    public static string? NormalizeFilter(string? input) =>
        TryParse(input, out var level) ? ToFilterValue(level) : null;

    public static string? ToDisplayLabel(string? wireOrRaw, string language)
    {
        if (!TryParse(wireOrRaw, out var level) || level == 0)
        {
            return null;
        }

        var german = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

        return level switch
        {
            1 => german ? "Anfänger" : Beginner,
            2 => german ? "Fortgeschritten" : Intermediate,
            3 => german ? "Experte" : Expert,
            _ => null
        };
    }
}
