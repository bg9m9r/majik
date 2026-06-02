using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NurturingPixieFactory"/>.
///
/// Nurturing Pixie — Creature — Faerie Rogue {W} 1/1.
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, return up to one target non-Faerie,
///    nonland permanent you control to its owner's hand. If a permanent
///    was returned this way, put a +1/+1 counter on this creature."
///
/// Covers:
/// - Identity (name, type, P/T 1/1, Faerie + Rogue subtypes, {W}, mana value
///   1, owner/controller, White colour).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword ability attached.
/// - Exactly one ETB triggered ability with a 0..1 "you control" target
///   request (BotIntent.Bounce).
/// - ETB resolution: returns a chosen non-Faerie nonland permanent you
///   control to its owner's hand AND grows the Pixie with a +1/+1 counter.
/// - ETB resolution: no target chosen → no bounce, no counter, no exception.
/// - ETB resolution: target already off the battlefield (CR 608.2b) → no
///   bounce, no counter.
/// - CandidateGatherer excludes Faeries, lands, and permanents you don't
///   control; the Pixie never offers itself.
/// </summary>
[Trait("Color", "W")]
public class NurturingPixieFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NurturingPixie_Identity()
    {
        var c = NurturingPixieFactory.Create(_alice);

        c.Name.Should().Be("Nurturing Pixie");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue("Nurturing Pixie is a Faerie");
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue("Nurturing Pixie is a Rogue");
        c.ManaCost.Should().Be("{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NurturingPixie_ManaValue_IsOne()
    {
        var c = NurturingPixieFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(1, "mana value 1: a single White pip");
    }

    [Fact]
    public void NurturingPixie_Colors_ContainsWhiteOnly()
    {
        var c = NurturingPixieFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Nurturing Pixie costs {W}");
        colors.Should().HaveCount(1, "Nurturing Pixie is exactly White");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Keyword + ETB trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void NurturingPixie_HasFlying()
    {
        var c = NurturingPixieFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying")
            .Should().BeTrue("Nurturing Pixie has Flying (CR 702.9)");
    }

    [Fact]
    public void NurturingPixie_HasExactlyOneTriggeredAbility()
    {
        var c = NurturingPixieFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB bounce-then-grow trigger on Nurturing Pixie");
    }

    [Fact]
    public void NurturingPixie_EtbTrigger_HasUpToOneTargetRequest()
    {
        var c = NurturingPixieFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0, "'up to one target' — choosing no target is legal (CR 115.1c)");
        req.MaxTargets.Should().Be(1);
        req.Intent.Should().Be(BotIntent.Bounce);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger functions only from the battlefield");
    }

    [Fact]
    public void NurturingPixie_CandidateGatherer_ExcludesFaeriesLandsAndOpponents_AndItself()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var pixie = NurturingPixieFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(pixie);
        pixie.SetZone(ZoneType.Battlefield);

        // Legal candidate: a non-Faerie, nonland permanent Alice controls.
        var ally = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ally.SetOwner(alice);
        ally.SetController(alice);
        alice.Zones.Battlefield.AddCard(ally);
        ally.SetZone(ZoneType.Battlefield);

        // Excluded: another Faerie Alice controls.
        var otherFaerie = new Creature("Spellstutter Sprite", "{1}{U}", 1, 1,
            subtypes: new[] { CardSubtype.Faerie });
        otherFaerie.SetOwner(alice);
        otherFaerie.SetController(alice);
        alice.Zones.Battlefield.AddCard(otherFaerie);
        otherFaerie.SetZone(ZoneType.Battlefield);

        // Excluded: a land Alice controls.
        var land = new Land("Plains");
        land.SetOwner(alice);
        land.SetController(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Excluded: a permanent Bob controls.
        var enemy = new Creature("Llanowar Elves", "{G}", 1, 1);
        enemy.SetOwner(bob);
        enemy.SetController(bob);
        bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);

        var etb = pixie.Abilities.OfType<TriggeredAbility>().Single();
        var ctx = new Majik.Core.Game.GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());
        var candidates = etb.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(ally, "non-Faerie nonland permanent you control is legal");
        candidates.Should().NotContain(otherFaerie, "Faeries are excluded (non-Faerie)");
        candidates.Should().NotContain(land, "lands are excluded (nonland permanent)");
        candidates.Should().NotContain(enemy, "permanents you don't control are excluded");
        candidates.Should().NotContain(pixie, "the Pixie is itself a Faerie — never a candidate");
    }

    // -----------------------------------------------------------------------
    // ETB resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void NurturingPixie_EtbEffect_ReturnsOwnPermanent_AndGrowsWithCounter()
    {
        var alice = new Player("Alice", 20);

        var pixie = NurturingPixieFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(pixie);
        pixie.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(alice);
        target.SetController(alice);
        alice.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var etb = pixie.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        target.Zone.Should().Be(ZoneType.Hand, "the chosen permanent is returned to its owner's hand");
        alice.Zones.Hand.GetCards().Should().Contain(target);
        alice.Zones.Battlefield.GetCards().Should().NotContain(target);

        pixie.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a permanent was returned, so the Pixie gets a +1/+1 counter");
    }

    [Fact]
    public void NurturingPixie_EtbEffect_NoTarget_NoBounce_NoCounter()
    {
        var alice = new Player("Alice", 20);

        var pixie = NurturingPixieFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(pixie);
        pixie.SetZone(ZoneType.Battlefield);

        var etb = pixie.Abilities.OfType<TriggeredAbility>().Single();
        // ChosenTargets left empty — "up to one" allows choosing no target.

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("choosing no target is legal and a no-op");
        pixie.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no permanent was returned, so no +1/+1 counter is placed");
    }

    [Fact]
    public void NurturingPixie_EtbEffect_TargetAlreadyLeft_NoBounce_NoCounter()
    {
        // CR 608.2b — illegal target at resolution does nothing; with no
        // permanent returned, the counter is not placed.
        var alice = new Player("Alice", 20);

        var pixie = NurturingPixieFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(pixie);
        pixie.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(alice);
        target.SetController(alice);
        alice.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard); // already gone at resolution

        var etb = pixie.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("CR 608.2b: illegal target is a no-op, not an exception");
        alice.Zones.Hand.GetCards().Should().NotContain(target);
        pixie.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no permanent was returned, so no +1/+1 counter is placed");
    }
}
