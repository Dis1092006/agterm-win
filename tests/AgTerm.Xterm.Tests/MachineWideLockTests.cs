using System.Reflection;

namespace AgTerm.Xterm.Tests;

public sealed class MachineWideLockTests
{
    private readonly string _name = "agterm-win-lock-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void A_second_holder_waits_while_the_first_holds_the_lock()
    {
        using var first = new MachineWideLock(_name);
        using var second = new MachineWideLock(_name);
        Assert.True(first.Enter(TimeSpan.FromSeconds(5)));

        Assert.False(second.Enter(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void The_lock_passes_to_the_next_holder_once_released()
    {
        using var first = new MachineWideLock(_name);
        using var second = new MachineWideLock(_name);
        Assert.True(first.Enter(TimeSpan.FromSeconds(5)));

        first.Exit();

        Assert.True(second.Enter(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void One_holder_can_take_the_lock_again_after_releasing_it()
    {
        using var holder = new MachineWideLock(_name);
        Assert.True(holder.Enter(TimeSpan.FromSeconds(5)));
        holder.Exit();

        Assert.True(holder.Enter(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_lock_left_by_a_holder_that_ended_without_releasing_is_taken_over()
    {
        var abandoning = new Thread(() =>
        {
            var mutex = new Mutex(false, _name);
            mutex.WaitOne();
        });
        abandoning.Start();
        abandoning.Join();
        using var next = new MachineWideLock(_name);

        Assert.True(next.Enter(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Each_live_WebView2_test_holds_the_machine_lock()
    {
        Assert.NotNull(typeof(WebView2Collection).GetCustomAttribute<OneLiveTestPerMachineAttribute>());
        Assert.NotNull(typeof(ProcessEnvironmentCollection).GetCustomAttribute<OneLiveTestPerMachineAttribute>());
    }
}
