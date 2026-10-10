using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore PickTests.swift (the pick half; ask is not ported).
public class PickTests
{
    private static PendingPick Pick(string id) =>
        new(id, new[] { new ControlPickItem { Id = "a", Label = "A" } }, null, null, false, null);

    [Fact]
    public void OpenAndResolveRetainsResultAfterPendingCloses()
    {
        var controller = new PickController();
        Assert.True(controller.Open(Pick("p1")));
        Assert.Equal(ControlPickResult.Pending(), controller.Result("p1"));

        Assert.True(controller.Resolve("p1", ControlPickResult.Picked("a", "A", 0)));

        Assert.Null(controller.Pending);
        Assert.Equal(ControlPickResult.Picked("a", "A", 0), controller.Result("p1"));
    }

    [Fact]
    public void OpenRejectsWhileAnotherPickIsPendingAndKeepsTheFirst()
    {
        var controller = new PickController();
        controller.Open(Pick("first"));

        Assert.False(controller.Open(Pick("second")));
        Assert.Equal("first", controller.PendingId);
        Assert.Equal("pick pending", controller.PendingModalError);
    }

    [Fact]
    public void ResolveIgnoresAStaleId()
    {
        var controller = new PickController();
        controller.Open(Pick("p1"));

        Assert.False(controller.Resolve("other", ControlPickResult.Cancelled()));
        Assert.Equal("p1", controller.PendingId);
    }

    [Fact]
    public void CancelRetainsCancelledResult()
    {
        var controller = new PickController();
        controller.Open(Pick("p1"));
        controller.Cancel("p1");
        Assert.Equal(ControlPickResult.Cancelled(), controller.Result("p1"));
    }

    [Fact]
    public void ResultReturnsNullForUnknownId()
    {
        Assert.Null(new PickController().Result("nope"));
    }

    [Fact]
    public void NextOpenKeepsPriorResultReadable()
    {
        var controller = new PickController();
        controller.Open(Pick("p1"));
        controller.Resolve("p1", ControlPickResult.Picked("a", "A", 0));
        controller.Open(Pick("p2"));

        Assert.Equal(ControlPickResult.Picked("a", "A", 0), controller.Result("p1"));
        Assert.Equal(ControlPickResult.Pending(), controller.Result("p2"));
    }

    [Fact]
    public void RetainedResultsStopAtEightDroppingTheOldest()
    {
        var controller = new PickController();
        for (var i = 0; i < 9; i++)
        {
            controller.Open(Pick($"p{i}"));
            controller.Cancel($"p{i}");
        }
        Assert.Null(controller.Result("p0"));
        Assert.NotNull(controller.Result("p1"));
        Assert.NotNull(controller.Result("p8"));
    }

    [Fact]
    public void ChangedFiresOnOpenAndResolve()
    {
        var controller = new PickController();
        var count = 0;
        controller.Changed += () => count++;
        controller.Open(Pick("p1"));
        controller.Cancel("p1");
        Assert.Equal(2, count);
    }

    [Fact]
    public void RegistryFindsALivePickByIdInAnyWindowWithoutASelector()
    {
        var registry = new PickRegistry();
        var a = new PickController();
        var b = new PickController();
        registry.Register(Guid.NewGuid(), a);
        registry.Register(Guid.NewGuid(), b);
        b.Open(Pick("pb"));

        var response = registry.Result("pb", null);

        Assert.True(response.Ok);
        Assert.Equal(ControlPickResult.Pending(), response.Result!.Pick);
    }

    [Fact]
    public void RegistryUnregisterCancelsAndRetainsResultForItsPollers()
    {
        var registry = new PickRegistry();
        var window = Guid.NewGuid();
        var controller = new PickController();
        registry.Register(window, controller);
        controller.Open(Pick("p1"));

        registry.Unregister(window);

        Assert.Equal(ControlPickResult.Cancelled(), registry.Result("p1", null).Result!.Pick);
        Assert.Equal(ControlPickResult.Cancelled(), registry.Result("p1", window).Result!.Pick);
        var other = registry.Result("p1", Guid.NewGuid());
        Assert.False(other.Ok);
        Assert.Equal("unknown pick: p1", other.Error);
    }

    [Fact]
    public void RegistryTrimDropsTheOldestAnswerNotTheFirstTransferred()
    {
        var registry = new PickRegistry();
        var lateWindow = Guid.NewGuid();
        var lateController = new PickController();
        registry.Register(lateWindow, lateController);
        lateController.Open(Pick("oldest"));
        lateController.Cancel("oldest"); // answered first

        var earlyWindows = new List<Guid>();
        for (var w = 0; w < 4; w++)
        {
            var window = Guid.NewGuid();
            var controller = new PickController();
            registry.Register(window, controller);
            earlyWindows.Add(window);
            for (var i = 0; i < 8; i++)
            {
                controller.Open(Pick($"w{w}p{i}"));
                controller.Cancel($"w{w}p{i}");
            }
        }
        foreach (var window in earlyWindows) registry.Unregister(window); // 32 entries, at the cap
        registry.Unregister(lateWindow); // the 33rd, but it was answered before all of them

        Assert.False(registry.Result("oldest", null).Ok);
        Assert.True(registry.Result("w0p0", null).Ok);
    }

    [Fact]
    public void RegistryRetainedResultsStopAtThirtyTwo()
    {
        var registry = new PickRegistry();
        for (var w = 0; w < 5; w++)
        {
            var window = Guid.NewGuid();
            var controller = new PickController();
            registry.Register(window, controller);
            for (var i = 0; i < 8; i++)
            {
                controller.Open(Pick($"w{w}p{i}"));
                controller.Cancel($"w{w}p{i}");
            }
            registry.Unregister(window);
        }

        Assert.False(registry.Result("w0p0", null).Ok); // 40 answered, oldest 8 trimmed
        Assert.True(registry.Result("w1p0", null).Ok);
    }

    [Fact]
    public void RegistryCancelIsIdempotentForAFinishedPickAndRejectsUnknown()
    {
        var registry = new PickRegistry();
        var window = Guid.NewGuid();
        var controller = new PickController();
        registry.Register(window, controller);
        controller.Open(Pick("p1"));

        Assert.True(registry.Cancel("p1", null).Ok);
        Assert.Equal(ControlPickResult.Cancelled(), controller.Result("p1"));
        Assert.True(registry.Cancel("p1", null).Ok);
        Assert.Equal(ControlPickResult.Cancelled(), controller.Result("p1"));
        Assert.Equal("unknown pick: nope", registry.Cancel("nope", null).Error);
    }

    [Fact]
    public void RegistryWithAWindowThatIsNotRegisteredAndNoRetainedResultAnswersWindowNotOpen()
    {
        var registry = new PickRegistry();
        Assert.Equal("window not open — window.select it first", registry.Result("p1", Guid.NewGuid()).Error);
    }

    [Fact]
    public void RegistryCancelAllCancelsEveryPendingPick()
    {
        var registry = new PickRegistry();
        var a = new PickController();
        registry.Register(Guid.NewGuid(), a);
        a.Open(Pick("p1"));

        registry.CancelAll();

        Assert.Equal(ControlPickResult.Cancelled(), a.Result("p1"));
    }

    [Fact]
    public void ResultDuringUnregisterAnswersCancelledNotUnknown()
    {
        var registry = new PickRegistry();
        var window = Guid.NewGuid();
        var controller = new PickController();
        registry.Register(window, controller);
        controller.Open(Pick("p1"));
        var seen = new List<ControlResponse>();
        controller.Changed += () =>
        {
            seen.Add(registry.Result("p1", null));
            seen.Add(registry.Result("p1", window));
        };

        registry.Unregister(window);

        Assert.Equal(2, seen.Count);
        foreach (var response in seen)
        {
            Assert.True(response.Ok, response.Error);
            Assert.Equal(ControlPickResult.Cancelled(), response.Result!.Pick);
        }
    }

    [Fact]
    public void CancelForAWindowThatIsNotRegisteredAndNoRetainedResultAnswersWindowNotOpen()
    {
        var registry = new PickRegistry();
        Assert.Equal("window not open — window.select it first", registry.Cancel("p1", Guid.NewGuid()).Error);
    }

    [Fact]
    public void OpenAfterUnregisterIsRejectedAndTheControllerReportsClosed()
    {
        var registry = new PickRegistry();
        var window = Guid.NewGuid();
        var controller = new PickController();
        registry.Register(window, controller);
        Assert.False(controller.IsClosed);

        registry.Unregister(window);

        Assert.True(controller.IsClosed);
        Assert.False(controller.Open(Pick("late")));
        Assert.Null(controller.Pending);
    }

    [Fact]
    public void ChangedDoesNotFireOnRejectedOpenOrStaleResolve()
    {
        var controller = new PickController();
        controller.Open(Pick("p1"));
        var count = 0;
        controller.Changed += () => count++;

        Assert.False(controller.Open(Pick("p2")));
        Assert.False(controller.Resolve("stale", ControlPickResult.Cancelled()));

        Assert.Equal(0, count);
    }
}
