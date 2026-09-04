using System.Text.RegularExpressions;

namespace DiabloEnglishCoach;

internal sealed record SubtitleLine(string Text, double Top = 0, double Height = 0, double Width = 0);

internal static class DialogueText
{
    private static readonly string[] Speakers = ["Deckard Cain", "Cain", "Xul", "Charsi", "Akara", "Kashya", "Warriv", "Lethes", "Morgana", "Rodger", "Ottilie", "Hemlir", "Sister Lalia", "Rayek"];
    // Fix only recognizable English words, not every alphanumeric item code.
    private static readonly HashSet<string> Words = new(("you your yours our out of on or to do not no go for from follow " +
        "look good come close more power now down over world know soul souls door blood " +
        "corpse corpses cooldown bone armor command golem summon summons control protect " +
        "through before restore weapon equipment location objective collect floor forgotten").Split(' '), StringComparer.OrdinalIgnoreCase);

    public static string RepairZeroes(string text) => Regex.Replace(text, @"\b[A-Za-z0-9]+\b", match =>
    {
        var token = match.Value;
        if (!token.Contains('0') || token.Any(c => char.IsDigit(c) && c != '0')) return token;
        var repaired = token.Replace('0', 'o');
        if (!Words.Contains(repaired)) return token;
        return token.Where(char.IsLetter).All(char.IsUpper) && token.Any(char.IsLetter) ? repaired.ToUpperInvariant() : repaired;
    });

    public static string Normalize(IEnumerable<SubtitleLine> source, bool dialogue)
    {
        var lines = source.Where(l => !OcrService.IsChatOrAdvertising(l.Text))
            .Select(l => l with { Text = RepairZeroes(l.Text.Trim()) }).Where(l => l.Text.Length > 0).ToList();
        if (dialogue && lines.Count >= 2 && IsSpeaker(lines[0], lines[1])) lines.RemoveAt(0);
        var text = string.Join(" ", lines.Select(l => l.Text));
        if (dialogue)
            foreach (var name in Speakers.OrderByDescending(n => n.Length))
                text = Regex.Replace(text, @"^" + Regex.Escape(name) + @"\s*[:：]\s*", "", RegexOptions.IgnoreCase);
        return OcrService.Clean(text);
    }

    private static bool IsSpeaker(SubtitleLine first, SubtitleLine second)
    {
        var heading = first.Text.TrimEnd(':', '：');
        if (Speakers.Contains(heading, StringComparer.OrdinalIgnoreCase) &&
            (first.Text.EndsWith(':') || Regex.IsMatch(second.Text, @"^[A-Z]"))) return true;
        // Unknown names need typography AND a short, title-cased isolated heading.
        // Never strip a short sentence such as "We must" or "Stay close".
        return first.Height > 0 && second.Top - (first.Top + first.Height) >= first.Height * .45 &&
            first.Width < second.Width * .65 &&
            Regex.IsMatch(heading, @"^[A-Z][a-z]{2,}(?: [A-Z][a-z]{2,}){0,2}$") &&
            !Regex.IsMatch(heading, @"^(The|You|Your|Our|Please|Stay|Follow|Help|Look|Come|Wait|Leave|Return|Head|Search|This|That|There|Hello|Thanks|Thank|Good|Goodbye|Listen|What|Why|How|Where|When|Which|Yes|Well|Never)\b");
    }
}
