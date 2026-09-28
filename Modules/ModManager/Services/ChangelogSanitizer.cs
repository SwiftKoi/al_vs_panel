using Ganss.Xss;

namespace AlegacyWebPanel.Modules.ModManager.Services;

public interface IChangelogSanitizer
{
    string? Sanitize(string? html);
}

/// <summary>ModDB changelogs are author-written HTML; only basic formatting and https links survive.</summary>
public sealed class ChangelogSanitizer : IChangelogSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public ChangelogSanitizer()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "ul", "ol", "li", "b", "strong", "i", "em", "u", "s", "code", "pre", "blockquote", "a", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "span" })
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is AngleSharp.Dom.IElement { TagName: "A" } link)
            {
                link.SetAttribute("target", "_blank");
                link.SetAttribute("rel", "noopener noreferrer nofollow");
            }
        };
    }

    public string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var result = _sanitizer.Sanitize(html).Trim();
        return result.Length == 0 ? null : result;
    }
}
