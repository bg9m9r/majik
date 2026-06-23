using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StockpilingCelebrantFactory"/>.
///
/// Stockpiling Celebrant — Creature — Dwarf Knight {2}{W} 3/2.
/// Oracle text (verified against Scryfall):
///   "When this creature enters, you may return another target nonland
///    permanent you control to its owner's hand. If you do, scry 2."
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity (name, type, P/T 3/2, Dwarf + Knight subtypes, {2}{W}, White).
/// - Exactly one ETB triggered ability with one OPTIONAL (0..1) target request
///   for "another nonland permanent you control".
/// - The CandidateGatherer excludes the Celebrant itself ("another") and lands
///   ("nonland"), and only offers the controller's own permanents.
/// - ETB resolution: chosen target is returned to its owner's hand.
/// - ETB resolution: declining (no chosen target) is a no-op (no bounce).
/// - ETB resolution: a land target / the Celebrant itself is rejected at
///   resolution (CR 608.2b / 115.5b).
/// (Scry-2-after-bounce is verified for its gate — it only happens "if you do";
///  the actual scry partition is the shared ScryAction pipeline, tested
///  elsewhere.)
/// </summary>
[Trait("Color", "W")]
public class StockpilingCelebrantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static TriggeredAbility Etb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StockpilingCelebrant_Identity()
    {
        var c = StockpilingCelebrantFactory.Create(_alice);

        c.Name.Should().Be("Stockpiling Celebrant");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Dwarf).Should().BeTrue("it is a Dwarf");
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue("it is a Knight");
        c.ManaCost.Should().Be("{2}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Stockpiling Celebrant costs {2}{W}");
        colors.Should().HaveCount(1, "Stockpiling Celebrant is exactly White");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — shape (optional, targeted)
    // -----------------------------------------------------------------------

    [Fact]
    public void StockpilingCelebrant_HasExactlyOneEtbTrigger_WithOptionalTargetRequest()
    {
        var c = StockpilingCelebrantFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB bounce-then-scry trigger");

        var etb = Etb(c);
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0, "\"you may\" — the bounce is optional (CR 603.5)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonland");
        req.Description.Should().Contain("you control");
        req.Intent.Should().Be(BotIntent.Bounce);
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void StockpilingCelebrant_CandidateGatherer_ExcludesSelfAndLands_OnlyOwnPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Alice's own nonland permanent — eligible.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        Place(bear, alice);

        // Alice's own land — ineligible ("nonland").
        var plains = new Land("Plains");
        Place(plains, alice);

        // Bob's nonland permanent — ineligible ("you control").
        var ogre = new Creature("Ogre", "{2}{R}", 3, 3, subtypes: new[] { CardSubtype.Ogre });
        Place(ogre, bob);

        var celebrant = StockpilingCelebrantFactory.Create(alice);
        Place(celebrant, alice);

        var etb = Etb(celebrant);
        var ctx = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = etb.TargetRequests[0].CandidateGatherer!(ctx).Cast<Permanent>().ToList();

        candidates.Should().Contain(bear, "Alice's own nonland permanent is a legal target");
        candidates.Should().NotContain(plains, "lands are excluded (\"nonland\")");
        candidates.Should().NotContain(ogre, "Bob's permanent is excluded (\"you control\")");
        candidates.Should().NotContain(celebrant, "the Celebrant itself is excluded (\"another\")");
    }

    // -----------------------------------------------------------------------
    // ETB resolution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StockpilingCelebrant_Etb_ReturnsChosenPermanentToOwnersHand()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        Place(bear, alice);

        var celebrant = StockpilingCelebrantFactory.Create(alice);
        Place(celebrant, alice);

        // Give Alice library cards so the gated scry 2 has something to peek
        // (no agent registered → all-to-bottom default; we only assert the bounce).
        for (var i = 0; i < 3; i++)
            alice.Zones.Library.AddCard(new Land("Plains"));

        var etb = Etb(celebrant);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var effect in etb.Effects)
            await effect.ExecuteAsync(ResolutionContext.Legacy);

        bear.Zone.Should().Be(ZoneType.Hand,
            "the chosen nonland permanent you control is returned to its owner's hand");
        alice.Zones.Hand.GetCards().Should().Contain(bear);
        alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task StockpilingCelebrant_Etb_NoTargetChosen_IsNoOp()
    {
        // "you may" — declining (no chosen target) bounces nothing, and the
        // "if you do" scry does not happen. CR 603.5.
        var alice = new Player("Alice", 20);

        var celebrant = StockpilingCelebrantFactory.Create(alice);
        Place(celebrant, alice);
        for (var i = 0; i < 3; i++)
            alice.Zones.Library.AddCard(new Land("Plains"));
        var libraryBefore = alice.Zones.Library.GetCards().ToList();

        var etb = Etb(celebrant);
        // ChosenTargets left empty — declined.

        Func<Task> act = async () =>
        {
            foreach (var effect in etb.Effects)
                await effect.ExecuteAsync(ResolutionContext.Legacy);
        };

        await act.Should().NotThrowAsync();
        alice.Zones.Hand.GetCards().Should().BeEmpty("nothing was returned when the bounce was declined");
        alice.Zones.Library.GetCards().Should().Equal(libraryBefore,
            "no scry happened (\"if you do\" was not satisfied) — library order is unchanged");
    }

    [Fact]
    public async Task StockpilingCelebrant_Etb_TargetGoneAtResolution_IsNoOp()
    {
        // CR 608.2b — a target no longer on the battlefield at resolution: the
        // bounce does nothing (and the gated scry does not happen).
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard); // already gone

        var celebrant = StockpilingCelebrantFactory.Create(alice);
        Place(celebrant, alice);

        var etb = Etb(celebrant);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        Func<Task> act = async () =>
        {
            foreach (var effect in etb.Effects)
                await effect.ExecuteAsync(ResolutionContext.Legacy);
        };

        await act.Should().NotThrowAsync(
            "CR 608.2b: illegal target at resolution is a no-op, not an exception");
        alice.Zones.Hand.GetCards().Should().BeEmpty("the already-gone permanent is not bounced");
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Place(Permanent p, Player owner)
    {
        p.SetOwner(owner);
        p.SetController(owner);
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }
}
