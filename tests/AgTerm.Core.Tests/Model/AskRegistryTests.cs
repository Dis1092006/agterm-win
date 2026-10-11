using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore AskTests.swift registry and controller cases (upstream ed831ab). The port's
// registry holds pending ids itself instead of asking the owner through a resolver closure.
public class AskRegistryTests
{
    private static PendingAsk Ask(string id, AskAnchor? anchor = null, ControlAskStyle style = ControlAskStyle.Gui) =>
        new(id, "Choose", null, new[] { new ControlAskButton { Id = "button-0", Label = "Button 0" } },
            null, null, style, ControlAskAlignment.Right, null, anchor);

    private static PendingPick Pick(string id) =>
        new(id, new[] { new ControlPickItem { Id = "a", Label = "A" } }, null, null, false, null);

    [Fact]
    public void RegisteredIdsArePendingWithTheirWindowUntilRetained()
    {
        var registry = new AskRegistry();
        var windowId = Guid.NewGuid();
        var owner = new AskOwner.Session(Guid.NewGuid(), windowId);

        Assert.True(registry.Register("a", owner));
        Assert.Equal(owner, registry.Owner("a"));
        Assert.Equal(new AskLookup(ControlAskResult.Pending(), windowId, owner), registry.Find("a"));
        Assert.Null(registry.Find("unknown"));

        var answer = ControlAskResult.Answered("button-0", "Button 0", 0);
        Assert.True(registry.Retain("a", answer));

        Assert.Null(registry.Owner("a"));
        Assert.Equal(new AskLookup(answer, windowId, null), registry.Find("a"));
    }

    [Fact]
    public void RegistryRefusesDuplicateIdsPendingRetentionAndASecondOutcome()
    {
        var registry = new AskRegistry();
        var windowId = Guid.NewGuid();
        Assert.True(registry.Register("original", new AskOwner.Window(windowId)));
        Assert.False(registry.Register("original", new AskOwner.Window(Guid.NewGuid())));
        Assert.False(registry.Retain("original", ControlAskResult.Pending()));
        Assert.Equal(ControlAskOutcome.Pending, registry.Find("original")!.Result.Result);

        Assert.True(registry.Retain("original", ControlAskResult.Escaped()));
        Assert.False(registry.Retain("original", ControlAskResult.Cancelled()));
        Assert.False(registry.Register("original", new AskOwner.Window(windowId)));
        Assert.Equal(ControlAskResult.Escaped(), registry.Find("original")!.Result);
        Assert.False(registry.Retain("never-registered", ControlAskResult.Cancelled()));
    }

    [Fact]
    public void RegistryEvictsByResolutionOrderAndNeverEvictsPendingIds()
    {
        var registry = new AskRegistry();
        var windowIds = Enumerable.Range(0, AskRegistry.RetainedResultLimit + 3).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < windowIds.Length; i++)
            Assert.True(registry.Register($"ask-{i}", new AskOwner.Window(windowIds[i])));
        for (var i = windowIds.Length - 1; i >= 1; i--)
            Assert.True(registry.Retain($"ask-{i}", ControlAskResult.Cancelled()));

        Assert.Equal(ControlAskOutcome.Pending, registry.Find("ask-0")!.Result.Result);
        for (var i = 1; i <= AskRegistry.RetainedResultLimit; i++)
        {
            Assert.Equal(ControlAskResult.Cancelled(), registry.Find($"ask-{i}")!.Result);
            Assert.Equal(windowIds[i], registry.Find($"ask-{i}")!.WindowId);
        }
        Assert.Null(registry.Find($"ask-{AskRegistry.RetainedResultLimit + 1}"));
        Assert.Null(registry.Find($"ask-{AskRegistry.RetainedResultLimit + 2}"));
    }

    [Fact]
    public void ResultAnswersByIdAndRefusesAMismatchedWindow()
    {
        var registry = new AskRegistry();
        var owner = Guid.NewGuid();
        registry.Register("a", new AskOwner.Window(owner));

        Assert.Equal(ControlAskResult.Pending(), registry.Result("a", null).Result!.Ask);
        Assert.Equal(ControlAskResult.Pending(), registry.Result("a", owner).Result!.Ask);
        Assert.Equal("unknown ask: a", registry.Result("a", Guid.NewGuid()).Error);
        Assert.Equal("unknown ask: nope", registry.Result("nope", null).Error);

        registry.Retain("a", ControlAskResult.Cancelled());
        Assert.Equal(ControlAskResult.Cancelled(), registry.Result("a", owner).Result!.Ask);
        Assert.Equal("unknown ask: a", registry.Result("a", Guid.NewGuid()).Error);
    }

    [Fact]
    public void GuiAskKeepsItsAnchorAndExcludesPicks()
    {
        var controller = new PickController();
        var windowId = Guid.NewGuid();
        var anchor = new AskAnchor(Guid.NewGuid(), OverlayPane.Right, Guid.NewGuid());
        var ask = Ask("gui", anchor);

        Assert.True(controller.OpenAsk(ask, windowId));
        Assert.Same(ask, controller.PendingAsk);
        Assert.Equal(new AskOwner.Window(windowId), controller.Asks.Owner("gui"));
        Assert.False(controller.OpenAsk(Ask("other"), windowId));
        Assert.False(controller.Open(Pick("pick")));
        Assert.Equal("ask already pending", controller.SlotTakenError);
        Assert.Equal("ask pending", controller.PendingModalError);
        Assert.True(controller.ModalPending);

        Assert.True(controller.CancelAsk("gui"));

        Assert.Null(controller.PendingAsk);
        Assert.Equal(ControlAskResult.Cancelled(), controller.Asks.Find("gui")!.Result);
        Assert.True(controller.Open(Pick("pick")));
        Assert.False(controller.OpenAsk(ask, windowId));
        Assert.Equal("pick already pending", controller.SlotTakenError);
        Assert.Equal("pick pending", controller.PendingModalError);
    }

    [Fact]
    public void ResolveAskIgnoresAStaleIdAndAPendingOutcome()
    {
        var controller = new PickController();
        controller.OpenAsk(Ask("a"), Guid.NewGuid());

        Assert.False(controller.ResolveAsk("other", ControlAskResult.Escaped()));
        Assert.False(controller.ResolveAsk("a", ControlAskResult.Pending()));
        Assert.True(controller.EscapeAsk("a"));
        Assert.False(controller.ResolveAsk("a", ControlAskResult.Answered("button-0", "Button 0", 0)));
        Assert.Equal(ControlAskResult.Escaped(), controller.Asks.Find("a")!.Result);
    }

    [Fact]
    public void AskChangesRaiseChanged()
    {
        var controller = new PickController();
        var raised = 0;
        controller.Changed += () => raised++;

        controller.OpenAsk(Ask("a"), Guid.NewGuid());
        controller.ResolveAsk("a", ControlAskResult.Answered("button-0", "Button 0", 0));

        Assert.Equal(2, raised);
    }

    [Fact]
    public void UnregisterCancelsAPendingAskAndClosesTheController()
    {
        var registry = new PickRegistry();
        var windowId = Guid.NewGuid();
        var controller = new PickController(registry.Asks);
        registry.Register(windowId, controller);
        controller.OpenAsk(Ask("a"), windowId);

        registry.Unregister(windowId);

        Assert.Null(controller.PendingAsk);
        Assert.Equal(new AskLookup(ControlAskResult.Cancelled(), windowId, null), registry.Asks.Find("a"));
        Assert.False(controller.OpenAsk(Ask("b"), windowId));
    }

    [Fact]
    public void CancelAllCancelsPendingAsks()
    {
        var registry = new PickRegistry();
        var windowId = Guid.NewGuid();
        var controller = new PickController(registry.Asks);
        registry.Register(windowId, controller);
        controller.OpenAsk(Ask("a"), windowId);

        registry.CancelAll();

        Assert.Equal(ControlAskResult.Cancelled(), registry.Asks.Find("a")!.Result);
    }
}
