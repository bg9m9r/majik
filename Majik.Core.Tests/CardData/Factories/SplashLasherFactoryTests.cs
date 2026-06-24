using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SplashLasherFactory"/> — Creature — Frog Wizard {3}{U}
/// 3/3 (Scryfall, verified 2026-06-24):
///   "Offspring {1}{U} (You may pay an additional {1}{U} as you cast this spell.
///    If you do, when this creature enters, create a 1/1 token copy of it.)
///    When this creature enters, tap up to one target creature and put a stun
///    counter on it."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// NamedCardFactory dispatch + well-formedness):
///   - Identity (name, cost, Frog + Wizard subtypes, 3/3) from the embedded JSON.
///   - Offspring {1}{U} keyword marker (CR 702.169).
///   - ETB trigger: 0..1 "up to one target creature" — the candidate gatherer
///     offers ANY creature (NOT opponent-scoped); MinTargets = 0.
///   - ETB resolution taps the chosen target (CR 701.20) and puts one stun
///     counter on it (CR 122.1c).
///   - No target chosen = clean no-op (CR 115.1 — "up to one").
///   - Illegal target at resolution = clean no-op (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class SplashLasherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature AddCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static TriggeredAbility EtbTapTrigger(Creature lasher) =>
        lasher.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SplashLasher_IsFrogWizard_3_3_AtCost3U()
    {
        var c = SplashLasherFactory.Create(_alice);

        c.Name.Should().Be("Splash Lasher");
        c.ManaCost.Should().Be("{3}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SplashLasher_HasOffspringKeywordMarker()
    {
        var c = SplashLasherFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Offspring");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — targeting: 0..1 ANY creature (CR 115.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_IsUpToOne_AndOffersAnyCreature_NotOpponentScoped()
    {
        var lasher = SplashLasherFactory.Create(_alice);
        lasher.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lasher);

        var mine = AddCreature(_alice, "My Bear");
        var theirs = AddCreature(_bob, "Their Bear");

        var etb = EtbTapTrigger(lasher);
        var request = etb.TargetRequests.Single();

        // "up to one" — MinTargets = 0.
        request.MinTargets.Should().Be(0);
        request.MaxTargets.Should().Be(1);

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.CandidateGatherer!(ctx);

        candidates.Should().Contain(theirs, "any creature is a legal target.");
        candidates.Should().Contain(mine,
            "the printed text is bare \"target creature\" — own creatures are legal too.");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — resolution (CR 701.20 tap + CR 122.1c stun)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TapsTarget_AndPutsOneStunCounter()
    {
        var lasher = SplashLasherFactory.Create(_alice);

        var target = AddCreature(_bob, "Grizzly Bears");

        var etb = EtbTapTrigger(lasher);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in etb.Effects) e.Execute();

        target.IsTapped.Should().BeTrue("CR 701.20 — the ETB taps the target.");
        target.Counters.Count(CounterType.Stun).Should().Be(1,
            "CR 122.1c — the ETB puts one stun counter on the target.");
    }

    [Fact]
    public void Etb_NoTargetChosen_NoOp()
    {
        // CR 115.1 — "up to one": the controller may choose no target.
        var lasher = SplashLasherFactory.Create(_alice);
        var bystander = AddCreature(_bob, "Bystander");

        var etb = EtbTapTrigger(lasher);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { System.Array.Empty<object>() });

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        bystander.IsTapped.Should().BeFalse();
        bystander.Counters.Count(CounterType.Stun).Should().Be(0);
    }

    [Fact]
    public void Etb_TargetLeftBattlefield_NoOp()
    {
        var lasher = SplashLasherFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = EtbTapTrigger(lasher);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a clean no-op.");
        target.IsTapped.Should().BeFalse();
        target.Counters.Count(CounterType.Stun).Should().Be(0);
    }
}
