namespace AgTerm.Xterm;

public sealed record TerminalDashboardCell
{
    public TerminalDashboardCell(string paneId, string controlRef, double? fontSize = null)
    {
        if (string.IsNullOrWhiteSpace(paneId))
            throw new ArgumentException("Pane id is required.", nameof(paneId));
        if (string.IsNullOrWhiteSpace(controlRef))
            throw new ArgumentException("Control reference is required.", nameof(controlRef));
        if (fontSize is { } size && (!double.IsFinite(size) || size <= 0))
            throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be finite and positive.");
        PaneId = paneId.Trim();
        ControlRef = controlRef.Trim();
        FontSize = fontSize;
    }

    public string PaneId { get; }

    public string ControlRef { get; }

    /// <summary>The tile's own size after a font chord on the dashboard; null uses the dashboard
    /// size.</summary>
    public double? FontSize { get; }
}

public sealed record TerminalLayer
{
    public TerminalLayer(string paneId, int? sizePercent = null, string? backgroundColor = null)
    {
        if (string.IsNullOrWhiteSpace(paneId))
            throw new ArgumentException("Pane id is required.", nameof(paneId));

        paneId = paneId.Trim();

        if (sizePercent is { } percent && (percent < 1 || percent > 100))
            throw new ArgumentOutOfRangeException(nameof(sizePercent), "Layer size percent must be in range 1..100.");

        if (backgroundColor is not null && !IsValidHexColor(backgroundColor))
            throw new ArgumentException("Background color must be # followed by six hex digits.", nameof(backgroundColor));

        PaneId = paneId;
        SizePercent = sizePercent;
        BackgroundColor = backgroundColor;
    }

    public string PaneId { get; }

    public int? SizePercent { get; }

    public string? BackgroundColor { get; }

    private static bool IsValidHexColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            var isDecimal = c >= '0' && c <= '9';
            var isLowerHex = c >= 'a' && c <= 'f';
            var isUpperHex = c >= 'A' && c <= 'F';
            if (!isDecimal && !isLowerHex && !isUpperHex)
                return false;
        }

        return true;
    }
}

public abstract record TerminalLayout(long Generation)
{
    public sealed record Empty(long Generation) : TerminalLayout(Generation);

    public sealed record Single(long Generation, string PaneId, string Title, string? WorkspaceName = null)
        : TerminalLayout(Generation);

    public sealed record Zoomed(
        long Generation,
        string PaneId,
        string? Title = null,
        string? WorkspaceName = null)
        : TerminalLayout(Generation);

    public sealed record Dashboard : TerminalLayout
    {
        public Dashboard(
            long generation,
            IReadOnlyList<TerminalDashboardCell> cells,
            string highlightedControlRef,
            double? fontSize = null)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(cells);
            if (cells.Count is < 1 or > 9)
                throw new ArgumentException("Dashboard must contain 1..9 cells.", nameof(cells));
            if (string.IsNullOrWhiteSpace(highlightedControlRef))
                throw new ArgumentException("Highlighted control reference is required.", nameof(highlightedControlRef));
            if (fontSize is { } size && (!double.IsFinite(size) || size <= 0))
                throw new ArgumentOutOfRangeException(nameof(fontSize), "Dashboard font size must be positive and finite.");

            var copy = cells.ToArray();
            if (copy.Any(cell => cell is null))
                throw new ArgumentException("Dashboard cells cannot contain null.", nameof(cells));
            if (copy.Select(cell => cell.PaneId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
                throw new ArgumentException("Dashboard pane ids must be distinct.", nameof(cells));
            if (copy.Select(cell => cell.ControlRef).Distinct(StringComparer.Ordinal).Count() != copy.Length)
                throw new ArgumentException("Dashboard control references must be distinct.", nameof(cells));

            highlightedControlRef = highlightedControlRef.Trim();
            if (!copy.Any(cell => cell.ControlRef == highlightedControlRef))
                throw new ArgumentException("Highlighted control reference must identify a dashboard cell.", nameof(highlightedControlRef));

            Cells = Array.AsReadOnly(copy);
            HighlightedControlRef = highlightedControlRef;
            FontSize = fontSize;
        }

        public IReadOnlyList<TerminalDashboardCell> Cells { get; }

        public string HighlightedControlRef { get; }

        public double? FontSize { get; }
    }

    public sealed record Split : TerminalLayout
    {
        public Split(
            long generation,
            string leftPaneId,
            string rightPaneId,
            double ratio,
            string focusedPaneId,
            string? title = null,
            string? workspaceName = null)
            : base(generation)
        {
            if (string.IsNullOrWhiteSpace(leftPaneId))
                throw new ArgumentException("Left pane id is required.", nameof(leftPaneId));
            if (string.IsNullOrWhiteSpace(rightPaneId))
                throw new ArgumentException("Right pane id is required.", nameof(rightPaneId));
            if (!double.IsFinite(ratio))
                throw new ArgumentException("Split ratio must be finite.", nameof(ratio));

            leftPaneId = leftPaneId.Trim();
            rightPaneId = rightPaneId.Trim();
            focusedPaneId = focusedPaneId?.Trim()
                ?? throw new ArgumentNullException(nameof(focusedPaneId));
            if (leftPaneId == rightPaneId)
                throw new ArgumentException("Split pane ids must be distinct.", nameof(rightPaneId));
            if (focusedPaneId != leftPaneId && focusedPaneId != rightPaneId)
                throw new ArgumentException("Focused pane id must identify one split pane.", nameof(focusedPaneId));

            LeftPaneId = leftPaneId;
            RightPaneId = rightPaneId;
            Ratio = Core.Model.AppStore.ClampSplitRatio(ratio);
            FocusedPaneId = focusedPaneId;
            Title = title;
            WorkspaceName = workspaceName;
        }

        public string LeftPaneId { get; }

        public string RightPaneId { get; }

        public double Ratio { get; }

        public string FocusedPaneId { get; }

        public string? Title { get; }

        public string? WorkspaceName { get; }
    }

    public sealed record Layered : TerminalLayout
    {
        public Layered(long generation, TerminalLayout baseLayout, TerminalLayer layer)
            : base(generation)
        {
            ArgumentNullException.ThrowIfNull(baseLayout);
            ArgumentNullException.ThrowIfNull(layer);

            if (baseLayout is not Empty && baseLayout is not Single && baseLayout is not Split)
                throw new ArgumentException("Base layout must be empty, single, or split.", nameof(baseLayout));

            if (baseLayout is Single singleBase)
            {
                if (string.Equals(layer.PaneId, singleBase.PaneId, StringComparison.Ordinal))
                    throw new ArgumentException("Layer pane id must be distinct from base panes.", nameof(layer));
            }
            else if (baseLayout is Split splitBase)
            {
                if (string.Equals(layer.PaneId, splitBase.LeftPaneId, StringComparison.Ordinal)
                    || string.Equals(layer.PaneId, splitBase.RightPaneId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Layer pane id must be distinct from base panes.", nameof(layer));
                }
            }

            BaseLayout = baseLayout;
            Layer = layer;
        }

        public TerminalLayout BaseLayout { get; }

        public TerminalLayer Layer { get; }
    }
}