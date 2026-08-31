namespace AgTerm.Xterm;

public abstract record TerminalLayout(long Generation)
{
    public sealed record Empty(long Generation) : TerminalLayout(Generation);

    public sealed record Single(long Generation, string PaneId, string Title, string? WorkspaceName = null)
        : TerminalLayout(Generation);

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
}