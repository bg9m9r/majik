using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WrennsResolveFactory"/>.
///
/// Card: Wrenn's Resolve — Sorcery {R} (Murders at Karlov Manor).
///   "Draw two cards. Exile cards drawn this way at the next end step."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve effect draws two cards from the top of the library.
///   - Next End-step trigger exiles the two drawn cards from hand.
///   - Drawn card played (left hand) before EOT: not exiled by the rider.
///   - Empty library: draws what's available and flags the SBA loss flag.
/// </summary>
public class WrennsResolveTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WrennsResolve_Identity()
    {
        var c = WrennsResolveFactory.Create(_alice);

        c.Name.Should().Be("Wrenn's Resolve");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WrennsResolve()
    {
        var card = NamedCardFactory.Create("Wrenn's Resolve", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Wrenn's Resolve");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: draw two
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCardsFromLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3"); // remains in library

        var effects = WrennsResolveFactory.BuildResolveEffect(_alice, triggers: null);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
    }

    // -----------------------------------------------------------------------
    // Next end step: exile the drawn cards from hand
    // -----------------------------------------------------------------------

    [Fact]
    public void NextEndStep_ExilesDrawnCardsFromHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var c1 = SeedLibraryCard(_alice, "Drawn1");
        var c2 = SeedLibraryCard(_alice, "Drawn2");

        var effects = WrennsResolveFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // Both cards landed in Alice's hand.
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });

        // Fire the next end step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1, "the delayed end-step exile is pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { c1, c2 },
            "the delayed rider exiles cards still in hand at end step");
        _alice.Zones.Hand.GetCards().Should().NotContain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Exile);
        c2.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Drawn cards played before end step: not exiled
    // -----------------------------------------------------------------------

    [Fact]
    public void DrawnCardPlayedBeforeEndStep_NotExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var c1 = SeedLibraryCard(_alice, "Played");
        var c2 = SeedLibraryCard(_alice, "Kept");

        var effects = WrennsResolveFactory.BuildResolveEffect(_alice, triggers);
        foreach (var e in effects) e.Execute();

        // Simulate Alice "playing" c1 — it leaves her hand into the graveyard
        // (e.g. an instant resolved). The exact destination doesn't matter:
        // the rider only checks whether the card is still in hand.
        _alice.Zones.Hand.RemoveCard(c1);
        _alice.Zones.Graveyard.AddCard(c1);
        c1.SetZone(ZoneType.Graveyard);

        // End step arrives.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // c1 was not in hand → not pulled into exile by Wrenn's Resolve.
        c1.Zone.Should().Be(ZoneType.Graveyard,
            "played card was no longer in hand when EOT fired — rider leaves it alone");
        _alice.Zones.Exile.GetCards().Should().NotContain(c1);

        // c2 was still in hand → exiled as normal.
        c2.Zone.Should().Be(ZoneType.Exile,
            "the card that was still in hand at EOT does get exiled");
        _alice.Zones.Exile.GetCards().Should().Contain(c2);
    }

    // -----------------------------------------------------------------------
    // Empty library
    // -----------------------------------------------------------------------

    [Fact]
    public void EmptyLibrary_DrawsOnlyWhatsAvailable_AndFlagsSbaLoss()
    {
        // Only one card in library — the second draw should flag the SBA
        // loss state but the first draw still lands.
        var only = SeedLibraryCard(_alice, "Only");

        var effects = WrennsResolveFactory.BuildResolveEffect(_alice, triggers: null);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(only);
        only.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw hit an empty library — SBA flag must be set");
    }

    [Fact]
    public void EmptyLibrary_FromTheStart_DrawsNothing_AndFlagsSbaLoss()
    {
        var effects = WrennsResolveFactory.BuildResolveEffect(_alice, triggers: null);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
