using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Tests for <see cref="FloodpitsDrownerFactory"/> — Creature — Merfolk {1}{U} 2/1
/// (Scryfall, verified 2026-06-02):
///   "Flash
///    Vigilance
///    When this creature enters, tap target creature an opponent controls and
///    put a stun counter on it.
///    {1}{U}, {T}: Shuffle this creature and target creature with a stun counter
///    on it into their owners' libraries."
///
/// Covers:
///   - Card identity (name, cost, type, subtype, P/T, owner / controller)
///     materialised from the embedded JSON definition.
///   - Flash + Vigilance keyword markers (CR 702.8 / CR 702.20).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger: 1..1 "target creature an opponent controls"; the opponent-
///     scoped candidate gatherer excludes the controller's own creatures.
///   - ETB resolution taps the target (CR 701.20) and puts one stun counter on
///     it (CR 122.1c); illegal target at resolution = clean no-op (CR 608.2b).
///   - {1}{U}, {T} activated ability: cost is mana + self-tap; 1..1 "target
///     creature with a stun counter on it"; resolution shuffles Floodpits and
///     the target into their owners' libraries (CR 701.19).
/// </summary>
[Trait("Color", "U")]
public class FloodpitsDrownerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature AddCreature(Player owner, string name, int seedLibrary = 0)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodpitsDrowner_IsMerfolk_2_1_AtCost1U()
    {
        var c = FloodpitsDrownerFactory.Create(_alice);

        c.Name.Should().Be("Floodpits Drowner");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FloodpitsDrowner_HasFlashAndVigilance()
    {
        var c = FloodpitsDrownerFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Vigilance");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FloodpitsDrowner()
    {
        var card = NamedCardFactory.Create("Floodpits Drowner", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Floodpits Drowner");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — targeting (CR 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TargetGatherer_OffersOnlyCreaturesAnOpponentControls()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);
        drowner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drowner);

        var mine = AddCreature(_alice, "My Bear");
        var theirs = AddCreature(_bob, "Their Bear");

        var etb = drowner.Abilities.OfType<TriggeredAbility>().Single();
        var request = etb.TargetRequests.Single();

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.CandidateGatherer!(ctx);

        candidates.Should().Contain(theirs, "Bob's creature is one an opponent controls.");
        candidates.Should().NotContain(mine, "CR 109.5 — Alice's own creatures aren't legal.");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — resolution (CR 701.20 tap + CR 122.1c stun)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TapsTarget_AndPutsOneStunCounter()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);

        var target = AddCreature(_bob, "Grizzly Bears");

        var etb = drowner.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var e in etb.Effects) e.Execute();

        target.IsTapped.Should().BeTrue("CR 701.20 — the ETB taps the target.");
        target.Counters.Count(CounterType.Stun).Should().Be(1,
            "CR 122.1c — the ETB puts one stun counter on the target.");
    }

    [Fact]
    public void Etb_TargetLeftBattlefield_NoOp()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = drowner.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a clean no-op.");
        target.IsTapped.Should().BeFalse();
        target.Counters.Count(CounterType.Stun).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {1}{U}, {T}: shuffle self + stunned target away
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleActivatedAbility_With1U_AndSelfTap()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);

        var ability = drowner.Abilities.OfType<ActivatedAbility>().Single();
        // {1}{U} mana cost + {T} self-tap = two cost components.
        ability.Costs.Should().HaveCount(2);
    }

    [Fact]
    public void Activated_TargetGatherer_OffersOnlyCreaturesWithAStunCounter()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);
        drowner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drowner);

        var stunned = AddCreature(_bob, "Stunned Bear");
        stunned.Counters.Add(CounterType.Stun, 1);
        var notStunned = AddCreature(_bob, "Plain Bear");

        var ability = drowner.Abilities.OfType<ActivatedAbility>().Single();
        var request = ability.TargetRequests.Single();

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.CandidateGatherer!(ctx);

        candidates.Should().Contain(stunned, "it has a stun counter.");
        candidates.Should().NotContain(notStunned, "no stun counter — not a legal target.");
    }

    [Fact]
    public void Activated_Resolve_ShufflesSelfAndTargetIntoOwnersLibraries()
    {
        var drowner = FloodpitsDrownerFactory.Create(_alice);
        drowner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drowner);

        var stunned = AddCreature(_bob, "Stunned Bear");
        stunned.Counters.Add(CounterType.Stun, 1);

        var ability = drowner.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { stunned } });

        ability.Resolve();

        // CR 701.19 — both go to their owners' libraries.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(drowner);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(stunned);
        _alice.Zones.Library.GetCards().Should().Contain(drowner,
            "Floodpits Drowner shuffles into ITS owner's (Alice's) library.");
        _bob.Zones.Library.GetCards().Should().Contain(stunned,
            "the target shuffles into ITS owner's (Bob's) library.");
        drowner.Zone.Should().Be(ZoneType.Library);
        stunned.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Activated_Resolve_TargetWithoutStunCounter_NoOp()
    {
        // CR 608.2b — re-check at resolution: a target that no longer has a
        // stun counter is left where it is.
        var drowner = FloodpitsDrownerFactory.Create(_alice);
        drowner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(drowner);

        var target = AddCreature(_bob, "Plain Bear"); // no stun counter

        var ability = drowner.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        ability.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().Contain(target,
            "CR 608.2b — target without a stun counter is illegal; the ability does nothing.");
        _alice.Zones.Battlefield.GetCards().Should().Contain(drowner,
            "the whole ability does nothing (it has only one target) when that target is illegal.");
    }
}
