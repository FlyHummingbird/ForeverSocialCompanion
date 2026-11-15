using System.Text;
using System.Text.RegularExpressions;
using ForeverSocial.Companion;

// Read only a user-selected local invitation. Never include the code in settings or logs.
internal static partial class InvitationSource
{
    private const int MaxBytes = 16384;
    [GeneratedRegex(@"^[\t ]*(?:Einladungscode|Invitation code):[\t ]*([^\r\n]+?)[\t ]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvitationLine();

    internal static string Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("Invitation text too large");
        var matches = InvitationLine().Matches(text);
        var code = matches.Count == 1 ? matches[0].Groups[1].Value.Trim() : text.Trim();
        if (matches.Count > 1 || code.Length is < 24 or > 256 || code.Any(c => c < 33 || c > 126))
            throw new InvalidDataException("Invalid invitation code");
        return code;
    }

    internal static bool MatchesServer(CompanionSettings settings, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(settings.InvitationFile) || string.IsNullOrWhiteSpace(settings.InvitationEndpoint)) return false;
        try { return BridgeFiles.Endpoint(settings.InvitationEndpoint).AbsoluteUri == BridgeFiles.Endpoint(endpoint).AbsoluteUri; }
        catch (InvalidDataException) { return false; }
    }

    internal static string Read(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path).StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a local invitation file");
        BridgeFiles.RejectLink(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxBytes) throw new InvalidDataException("Invitation file too large");
        using var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return Parse(reader.ReadToEnd());
    }
}
