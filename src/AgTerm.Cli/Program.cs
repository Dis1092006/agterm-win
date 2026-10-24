namespace AgTerm.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        ConsoleOutput.Install();
        return Agtermctl.Run(args);
    }
}
