using System.Xml;
using System.Xml.Linq;

namespace AgTerm.Xterm;

/// <summary>
/// Turns an SVG icon into a one-color silhouette: every painted color, a gradient or pattern included,
/// becomes the ink, while none and transparent stay unpainted. WinUI can show a bitmap as a monochrome
/// icon but not an SVG, so the sidebar draws an icon-tinted SVG workspace icon from this text.
/// </summary>
public static class SvgSilhouette
{
    private static readonly HashSet<string> PaintProperties =
        new(StringComparer.Ordinal) { "fill", "stroke", "stop-color", "flood-color", "lighting-color", "color" };

    /// <summary>The recolored SVG text, or null when the text is not an SVG document.</summary>
    public static string? Recolor(string svg, string ink)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(ink);
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is not { } root || root.Name.LocalName != "svg") return null;
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.Namespace != XNamespace.None) continue;
                if (PaintProperties.Contains(attribute.Name.LocalName))
                    attribute.Value = Paint(attribute.Value, ink);
                else if (attribute.Name.LocalName == "style")
                    attribute.Value = Style(attribute.Value, ink);
            }
        }

        // the initial fill is black, so an icon that never names one is painted through the root
        if (root.Attribute("fill") is null && !StyleNames(root, "fill"))
            root.SetAttributeValue("fill", ink);
        root.SetAttributeValue("color", ink);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string Paint(string value, string ink)
    {
        var trimmed = value.Trim();
        return trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            ? value
            : ink;
    }

    private static string Style(string style, string ink)
    {
        var declarations = new List<string>();
        foreach (var raw in style.Split(';'))
        {
            var colon = raw.IndexOf(':');
            if (colon < 0)
            {
                if (raw.Trim().Length > 0) declarations.Add(raw.Trim());
                continue;
            }
            var name = raw[..colon].Trim();
            var value = raw[(colon + 1)..].Trim();
            declarations.Add($"{name}:{(PaintProperties.Contains(name) ? Paint(value, ink) : value)}");
        }
        return string.Join(";", declarations);
    }

    private static bool StyleNames(XElement element, string property) =>
        ((string?)element.Attribute("style"))?.Split(';')
            .Any(d => d.Split(':', 2)[0].Trim() == property) == true;
}
