using System.Xml.Linq;

namespace AgTerm.Xterm.Tests;

public sealed class SvgSilhouetteTests
{
    private const string Ink = "#ff453a";
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static XElement Root(string svg) => XDocument.Parse(SvgSilhouette.Recolor(svg, Ink)!).Root!;

    [Fact]
    public void Every_painted_color_takes_the_ink()
    {
        var root = Root("""
            <svg xmlns="http://www.w3.org/2000/svg" fill="none">
              <path fill="#123456" stroke="red"/>
              <circle stroke="currentColor"/>
              <stop stop-color="blue"/>
            </svg>
            """);

        var path = root.Element(Svg + "path")!;
        Assert.Equal(Ink, (string?)path.Attribute("fill"));
        Assert.Equal(Ink, (string?)path.Attribute("stroke"));
        Assert.Equal(Ink, (string?)root.Element(Svg + "circle")!.Attribute("stroke"));
        Assert.Equal(Ink, (string?)root.Element(Svg + "stop")!.Attribute("stop-color"));
    }

    [Fact]
    public void None_and_transparent_stay_unpainted()
    {
        var root = Root("""
            <svg xmlns="http://www.w3.org/2000/svg" fill="none">
              <path fill="none" stroke="Transparent"/>
            </svg>
            """);

        Assert.Equal("none", (string?)root.Attribute("fill"));
        var path = root.Element(Svg + "path")!;
        Assert.Equal("none", (string?)path.Attribute("fill"));
        Assert.Equal("Transparent", (string?)path.Attribute("stroke"));
    }

    [Fact]
    public void A_gradient_or_pattern_paint_becomes_the_ink()
    {
        var root = Root("""<svg xmlns="http://www.w3.org/2000/svg"><rect fill="url(#g)"/></svg>""");

        Assert.Equal(Ink, (string?)root.Element(Svg + "rect")!.Attribute("fill"));
    }

    [Fact]
    public void The_default_black_fill_takes_the_ink_through_the_root()
    {
        var root = Root("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0h1v1z"/></svg>""");

        Assert.Equal(Ink, (string?)root.Attribute("fill"));
        Assert.Equal(Ink, (string?)root.Attribute("color"));
    }

    [Fact]
    public void Style_declarations_are_recolored_and_the_rest_is_kept()
    {
        var root = Root("""
            <svg xmlns="http://www.w3.org/2000/svg" style="fill:none">
              <path style="fill: #000; stroke:none; opacity: .5 ;stroke-width:2"/>
            </svg>
            """);

        Assert.Equal("fill:none", (string?)root.Attribute("style"));
        Assert.Null(root.Attribute("fill"));
        Assert.Equal($"fill:{Ink};stroke:none;opacity:.5;stroke-width:2",
            (string?)root.Element(Svg + "path")!.Attribute("style"));
    }

    [Fact]
    public void A_doctype_is_ignored_and_markup_that_is_not_xml_gives_null()
    {
        Assert.NotNull(SvgSilhouette.Recolor("""
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg"><path/></svg>
            """, Ink));
        Assert.Null(SvgSilhouette.Recolor("<svg><path></svg>", Ink));
        Assert.Null(SvgSilhouette.Recolor("<html/>", Ink));
    }
}
