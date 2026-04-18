using System.Text.RegularExpressions;

namespace Poodle.Cli.Services;

internal sealed class HtmlBaseTagService
{
    private static readonly Regex BaseTagRegex = new(
        @"<base\b[^>]*href\s*=\s*[""'][^""']*[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeadTagRegex = new(
        @"<head\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<string> EnsureBaseTags(string contentRoot, string baseHref)
    {
        var changedFiles = new List<string>();
        foreach (var htmlFile in EnumerateHtmlFiles(contentRoot))
        {
            var original = File.ReadAllText(htmlFile);
            var updated = EnsureBaseTag(original, baseHref);

            if (string.Equals(original, updated, StringComparison.Ordinal))
            {
                continue;
            }

            File.WriteAllText(htmlFile, updated);
            changedFiles.Add(htmlFile);
        }

        return changedFiles;
    }

    private static IEnumerable<string> EnumerateHtmlFiles(string contentRoot)
    {
        foreach (var path in Directory.EnumerateFiles(contentRoot, "*.html", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(contentRoot, path);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Contains(".git", StringComparer.OrdinalIgnoreCase)
                || segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
                || segments.Contains("obj", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    private static string EnsureBaseTag(string html, string baseHref)
    {
        var baseTag = $"<base href=\"{baseHref}\">";

        if (BaseTagRegex.IsMatch(html))
        {
            return BaseTagRegex.Replace(html, baseTag, 1);
        }

        var headMatch = HeadTagRegex.Match(html);
        if (headMatch.Success)
        {
            return html.Insert(headMatch.Index + headMatch.Length, Environment.NewLine + "    " + baseTag);
        }

        return "<head>" + Environment.NewLine + "    " + baseTag + Environment.NewLine + "</head>" + Environment.NewLine + html;
    }
}
