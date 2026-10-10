namespace AgTerm.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Agtermctl.UseUtf8Output();
        return Agtermctl.Run(args);
    }
}
