using System;
using System.IO;
using System.Text.RegularExpressions;

namespace LampaWeb;

/// <summary>
/// Registers Vietnamese in Lampa's original frontend files.
/// <c>meta.js</c> is bundled into <c>app.min.js</c>, so both must contain
/// <c>vi</c> or the boot language picker never offers Tiếng Việt.
/// Extra locales then load from <c>lang/{code}.js</c> — that is <c>vi.js</c>.
/// </summary>
static class LampaVietnamese
{
    internal const string RegistryEntry =
        "vi: { code: 'vi', name: 'Tiếng Việt', lang_choice_title: 'Chào mừng', lang_choice_subtitle: 'Chọn ngôn ngữ của bạn' },";

    internal static bool HasVietnameseCode(string source)
        => !string.IsNullOrEmpty(source) &&
           source.Contains("Tiếng Việt", StringComparison.Ordinal);

    internal static string EnsureLanguageRegistry(string source)
    {
        if (string.IsNullOrEmpty(source) || HasVietnameseCode(source))
            return source;

        return new Regex(
            @"languages\s*:\s*\{",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        ).Replace(source, match => match.Value + " " + RegistryEntry, 1);
    }

    internal static void PatchFile(string path)
    {
        if (!File.Exists(path))
            return;

        string original = File.ReadAllText(path);
        string patched = EnsureLanguageRegistry(original);
        if (!string.Equals(original, patched, StringComparison.Ordinal))
            File.WriteAllText(path, patched);
    }
}
