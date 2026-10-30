using AgTerm.Core.Model;

namespace AgTerm.Xterm;

public static class StatusGlyphPresentation
{
    public static StatusShape Shape(AgentIndicator indicator, AppSettings settings) =>
        indicator.Shape ?? settings.EffectiveStatusShape(indicator.Status) ?? StatusShape.Circle;

    public static string Color(AgentIndicator indicator, AppSettings settings) =>
        settings.EffectiveStatusColorHex(indicator.Status, indicator.Color);

    public static string Data(StatusShape shape) => shape switch
    {
        StatusShape.Square => "M1.5,1.5 H11.5 V11.5 H1.5 Z",
        StatusShape.Triangle => "M6.5,1 L12.3,12 L0.7,12 Z",
        StatusShape.Diamond => "M6.5,0.5 L12.5,6.5 L6.5,12.5 L0.5,6.5 Z",
        StatusShape.Capsule => "M4,2.5 H9 A4,4 0 0 1 9,10.5 H4 A4,4 0 0 1 4,2.5 Z",
        StatusShape.Star => "M6.5,0.6 L8.1,4.8 L12.6,5.0 L9.1,7.9 L10.3,12.2 L6.5,9.8 L2.7,12.2 " +
                            "L3.9,7.9 L0.4,5.0 L4.9,4.8 Z",
        _ => "M6.5,0.75 A5.75,5.75 0 1 1 6.49,0.75 Z",
    };
}
