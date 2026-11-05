using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// Port of agtermCoreTests MessageAddressTests.swift (upstream b0839692). The port's pane ids are the
// 32-digit surface tokens AGTERM_PANE_ID carries, so the canonical form uses that format.
public class MessageAddressTests
{
    private const string Session = "C8A3AD57-5ABC-4715-9EF1-62DBFD03F541";
    private static readonly Guid Pane = Guid.Parse("DA8C8908-18DD-4A3F-BCA4-3C90C80BEF78");

    [Fact]
    public void ARecipientTakesAPaneId()
    {
        var address = MessageAddress.Recipient($"{Session}:{Pane:N}");

        Assert.Equal(new MessageAddress(Session, MessageAddressPane.Id(Pane)), address);
    }

    [Theory]
    [InlineData("da8c890818dd4a3fbca43c90c80bef78")]
    [InlineData("DA8C890818DD4A3FBCA43C90C80BEF78")]
    [InlineData("da8c8908-18dd-4a3f-bca4-3c90c80bef78")]
    [InlineData("DA8C8908-18DD-4A3F-BCA4-3C90C80BEF78")]
    public void APaneIdMatchesInAnyCaseAndEitherForm(string pane)
    {
        Assert.Equal(MessageAddressPane.Id(Pane), MessageAddress.Identity($"{Session}:{pane}").Pane);
    }

    [Theory]
    [InlineData("left", StatusPane.Left)]
    [InlineData("right", StatusPane.Right)]
    [InlineData("scratch", StatusPane.Scratch)]
    [InlineData("primary", StatusPane.Left)]
    public void ARecipientTakesARole(string name, StatusPane role)
    {
        Assert.Equal(MessageAddressPane.Role(role), MessageAddress.Recipient($"{Session}:{name}").Pane);
    }

    [Fact]
    public void AnIdentityRefusesARole()
    {
        var error = Assert.Throws<MessageAddressException>(() => MessageAddress.Identity($"{Session}:right"));

        Assert.Equal($"identity \"{Session}:right\" must name a pane id, not a role", error.Message);
    }

    [Fact]
    public void ABareSessionIsRefused()
    {
        var expected = $"address \"{Session}\" names a session, not a pane";
        Assert.Equal(expected, Assert.Throws<MessageAddressException>(() => MessageAddress.Recipient(Session)).Message);
        Assert.Equal(expected, Assert.Throws<MessageAddressException>(() => MessageAddress.Identity(Session)).Message);
    }

    [Theory]
    [InlineData(":left")]
    [InlineData(Session + ":")]
    [InlineData(Session + ":nope")]
    [InlineData(Session + ":left:extra")]
    [InlineData("")]
    [InlineData(Session + ":{da8c8908-18dd-4a3f-bca4-3c90c80bef78}")]
    public void AMalformedAddressIsRefused(string text)
    {
        var error = Assert.Throws<MessageAddressException>(() => MessageAddress.Recipient(text));

        Assert.Equal($"invalid address \"{text}\": expected SESSION:PANE", error.Message);
    }

    [Fact]
    public void TheCanonicalFormNamesSessionAndPaneId()
    {
        Assert.Equal($"{Session}:da8c890818dd4a3fbca43c90c80bef78",
            MessageAddress.Canonical(Guid.Parse(Session), Pane));
    }

    [Fact]
    public void APaneTextIsItsIdOrRoleWireName()
    {
        Assert.Equal("da8c890818dd4a3fbca43c90c80bef78", MessageAddressPane.Id(Pane).Text);
        Assert.Equal("right", MessageAddressPane.Role(StatusPane.Right).Text);
    }
}
