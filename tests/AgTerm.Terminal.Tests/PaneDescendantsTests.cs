namespace AgTerm.Terminal.Tests;

/// <summary>The rule that picks which pane processes the sweep ends after the console closed.
/// Values are raw ProcessConsoleHostProcess results: a pid with two flag bits.</summary>
public class PaneDescendantsTests
{
    private const int Conhost = 4000;
    private const int DeadCreator = 5000;
    private const int LiveCreator = 6000;

    private static PaneDescendants.Verdict Decide(
        long? value, bool attachedAtClose = false, bool? consoleSubsystem = true) =>
        PaneDescendants.Decide(
            value, Conhost, attachedAtClose, pid => pid == DeadCreator, () => consoleSubsystem);

    [Fact]
    public void A_failed_query_keeps_the_process() =>
        Assert.Equal(PaneDescendants.Verdict.Keep, Decide(null));

    [Fact]
    public void A_process_attached_before_the_close_is_left_to_ctrl_close() =>
        Assert.Equal(PaneDescendants.Verdict.Attached, Decide(Conhost | 1, attachedAtClose: true));

    [Fact]
    public void A_process_that_attached_after_the_close_is_ended() =>
        Assert.Equal(PaneDescendants.Verdict.Kill, Decide(Conhost | 1));

    [Theory]
    [InlineData(1L)] // DETACHED_PROCESS, or FreeConsole
    [InlineData(7000L | 1)] // its own console
    public void A_process_on_another_console_or_none_is_kept(long value) =>
        Assert.Equal(PaneDescendants.Verdict.Keep, Decide(value));

    [Theory]
    [InlineData(DeadCreator | 2L)] // pending
    [InlineData(DeadCreator | 0L)] // never connected
    public void A_console_program_whose_creator_died_with_the_pane_is_ended(long value) =>
        Assert.Equal(PaneDescendants.Verdict.Kill, Decide(value));

    [Theory]
    [InlineData(DeadCreator | 2L, false)] // a GUI program
    [InlineData(DeadCreator | 0L, null)] // the image could not be read
    [InlineData(LiveCreator | 2L, true)] // its creator still runs
    [InlineData(LiveCreator | 0L, true)]
    [InlineData(0L, true)]
    [InlineData(DeadCreator | 3L, true)] // a flag combination that is not known
    public void Other_processes_are_kept(long value, bool? consoleSubsystem) =>
        Assert.Equal(PaneDescendants.Verdict.Keep, Decide(value, consoleSubsystem: consoleSubsystem));

    [Theory]
    [InlineData("cmd.exe", 3)]
    [InlineData("wscript.exe", 2)]
    public void The_subsystem_is_read_from_the_image(string image, int subsystem) =>
        Assert.Equal(subsystem, PaneDescendants.ReadSubsystem(Path.Combine(Environment.SystemDirectory, image)));

    [Fact]
    public void An_image_that_cannot_be_read_has_no_subsystem()
    {
        var notAnImage = Path.Combine(Path.GetTempPath(), $"agterm-not-an-image-{Guid.NewGuid():N}.exe");
        File.WriteAllText(notAnImage, "not a PE file");
        try
        {
            Assert.Null(PaneDescendants.ReadSubsystem(notAnImage));
            Assert.Null(PaneDescendants.ReadSubsystem(notAnImage + ".missing"));
        }
        finally
        {
            File.Delete(notAnImage);
        }
    }
}
