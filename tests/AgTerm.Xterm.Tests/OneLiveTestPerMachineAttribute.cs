using System.Reflection;
using Xunit.Sdk;

namespace AgTerm.Xterm.Tests;

/// <summary>Runs one live WebView2 test at a time across every Xterm test run on this machine. Two
/// full runs at once took 7.5 minutes each instead of 4, piled up 80 renderers, and failed a shell
/// exit test on its 15 s wait. The lock is per test, not per run: the 4 minute blame hang timeout
/// would kill a run that waited for a whole other run.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OneLiveTestPerMachineAttribute : BeforeAfterTestAttribute
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromMinutes(3);
    private static readonly MachineWideLock Lock = new("agterm-win-xterm-live-tests");

    public override void Before(MethodInfo methodUnderTest)
    {
        if (!Lock.Enter(WaitBudget))
            Console.Error.WriteLine(
                $"{methodUnderTest.Name}: another Xterm test run held the machine lock for {WaitBudget}; running anyway.");
    }

    public override void After(MethodInfo methodUnderTest) => Lock.Exit();
}
