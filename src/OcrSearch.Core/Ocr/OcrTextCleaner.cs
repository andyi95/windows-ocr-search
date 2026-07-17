namespace OcrSearch.Core.Ocr;

public static class OcrTextCleaner
{
    public const string Version = "c1";

    public static string Clean(string text)
    {
        var lines = text.Split('\n')
            .Select(CleanLine)
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Drops tokens with no letters or digits ("|", "—", "()"), then the whole line
    /// if fewer than two letters/digits remain (lone "O", "-0", "»" are icon/UI debris).
    /// Digit runs (dates, coordinates) are kept — they are real, searchable text.
    /// </summary>
    private static string CleanLine(string line)
    {
        var tokens = line
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Any(char.IsLetterOrDigit));
        var cleaned = string.Join(' ', tokens);
        return cleaned.Count(char.IsLetterOrDigit) < 2 ? "" : cleaned;
    }
}
