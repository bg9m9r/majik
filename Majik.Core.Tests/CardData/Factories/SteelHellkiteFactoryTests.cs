using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Steel Hellkite (Scars of Mirrodin, {6}, Artifact Creature —
/// Dragon 5/5).
///
/// Covers:
/// - Identity (name, types, cost, P/T, subtype).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword wired.
/// - {2}: +1/+0 EOT activated ability registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>.
/// - {X} destruction activated ability is sorcery-speed.
/// - Combat-damage-victim tracker subscribes to
///   <see cref="CombatDamageDealtEvent"/> and accumulates controllers.
/// - Destruction sweep destroys nontoken permanents with mv = X whose
///   controller is in the victim set.
/// - Tokens are excluded from destruction (CR 111.1).
/// </summary>
[Trait("Color", "C")]
public class SteelHellkiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakePermanent(Player owner, string name, string cost, int p = 1, int t = 1)
    {
        var c = new Creature(name, cost, p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Identity_NameTypesCostPT()
    {
        var c = SteelHellkiteFactory.Create(_alice);

        c.Name.Should().Be("Steel Hellkite");
        c.ManaCost.Should().Be("{6}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Steel Hellkite is an Artifact Creature — multi-type via AddCardType.");
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCostValue.TotalValue.Should().Be(6);
    }
    [Fact]
    public void HasFlyingKeyword()
    {
        var c = SteelHellkiteFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");
    }

    [Fact]
    public void HasTwoActivatedAbilities_PumpAndDestructionSweep()
    {
        var c = SteelHellkiteFactory.Create(_alice);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "Steel Hellkite prints two activated abilities — the {2} pump and the {X} destruction sweep.");
    }

    [Fact]
    public void DestructionSweep_IsSorcerySpeed()
    {
        var c = SteelHellkiteFactory.Create(_alice);
        var sweep = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        sweep.IsSorcerySpeed.Should().BeTrue(
            "the {X} ability is gated to the controller's turn (CR 117.1a / 307.5).");
    }

    [Fact]
    public void PumpAbility_GrantsPlusOnePlusZeroEot()
    {
        var svc = new ContinuousEffectsService();
        var hellkite = SteelHellkiteFactory.Create(_alice);
        hellkite.SetZone(ZoneType.Battlefield);
        hellkite.ActiveEffects = svc;

        var pump = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "2"));

        foreach (var e in pump.Effects) e.Execute();

        hellkite.GetPower().Should().Be(6, "5 base + 1 from the pump rider.");
        hellkite.GetToughness().Should().Be(5, "the pump is +1/+0.");
    }

    [Fact]
    public void DestructionSweep_DestroysNontokenPermanentsWithMatchingMv()
    {
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(
            _alice,
            xValueProvider: () => 2,
            eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hellkite);

        // Bob's permanents (his controller is in the victim set after a
        // combat damage hit below).
        var bobBear = MakePermanent(_bob, "Grizzly Bears", "{1}{G}", 2, 2); // mv 2 — destroyed.
        var bobOgre = MakePermanent(_bob, "Hill Giant", "{3}{R}", 3, 3);    // mv 4 — survives.

        // Alice's permanents (her controller is NOT in victim set).
        var aliceBear = MakePermanent(_alice, "Friendly Bear", "{1}{G}", 2, 2); // mv 2 but Alice not a victim → survives.

        // Combat damage from Steel Hellkite to Bob (player target).
        bus.Publish(new CombatDamageDealtEvent(hellkite, _bob, amount: 3));

        // Activate destruction sweep for X = 2.
        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Graveyard.GetCards().Should().Contain(bobBear,
            "Bob took combat damage; his mv-2 nontoken permanent is destroyed.");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobOgre,
            "mv-4 permanent does not match X=2.");
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceBear,
            "Alice took no combat damage from Steel Hellkite → her permanents are not in scope.");
    }

    [Fact]
    public void DestructionSweep_TracksCreatureControllerWhenDamageHitsCreature()
    {
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(
            _alice,
            xValueProvider: () => 1,
            eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hellkite);

        // Bob's creature takes combat damage from Steel Hellkite → Bob is a victim.
        var bobBlocker = MakePermanent(_bob, "Memnite", "{0}", 1, 1); // mv 0 — survives X=1.
        var bobTrinket = MakePermanent(_bob, "Mox Opal", "{0}", 0, 0); // mv 0 — survives.
        var bobOne = MakePermanent(_bob, "Birds of Paradise", "{G}", 0, 1); // mv 1 — destroyed.

        bus.Publish(new CombatDamageDealtEvent(hellkite, target: bobBlocker, amount: 1));

        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Graveyard.GetCards().Should().Contain(bobOne,
            "Bob's mv-1 permanent destroyed (he took combat damage on his creature).");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBlocker,
            "Blocker survives — mv 0 doesn't match X=1.");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobTrinket);
    }

    [Fact]
    public void DestructionSweep_NoVictims_IsNoOp()
    {
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(
            _alice,
            xValueProvider: () => 2,
            eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);

        var bobBear = MakePermanent(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // No combat damage published — victim set is empty.
        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear,
            "no combat damage → no victims → no destruction.");
    }

    [Fact]
    public void VictimSet_IsClearedOnTurnStart()
    {
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(
            _alice,
            xValueProvider: () => 2,
            eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);

        // Hit Bob this turn.
        bus.Publish(new CombatDamageDealtEvent(hellkite, _bob, amount: 3));

        // New turn fires — victim set should clear.
        bus.Publish(new TurnStartedEvent(_bob, turnNumber: 2));

        var bobBear = MakePermanent(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear,
            "the victim set was cleared at turn start — Bob no longer counts as having been dealt combat damage this turn.");
    }

    [Fact]
    public void DestructionSweep_ReadsChosenX_FromLedger_WhenNoProvider()
    {
        // GAP 2 end-to-end: no xValueProvider wired (prod shape). The sweep now
        // reads the per-activation X from the ledger (ActivatedAbility.ChosenX →
        // ResolutionContext.ChosenX). X=3 destroys Bob's mv-3 nonland permanents;
        // previously this resolved X=0 and destroyed nothing.
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(_alice, xValueProvider: null, eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hellkite);

        var bobThree = MakePermanent(_bob, "Hill Giant", "{2}{R}", 3, 3); // mv 3 — destroyed.
        var bobTwo = MakePermanent(_bob, "Grizzly Bears", "{1}{G}", 2, 2);  // mv 2 — survives.

        bus.Publish(new CombatDamageDealtEvent(hellkite, _bob, amount: 3));

        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        sweep.SetChosenX(3);
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Graveyard.GetCards().Should().Contain(bobThree,
            "X=3 from the ledger destroys Bob's mv-3 permanent (was 0 before GAP 2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobTwo,
            "mv-2 does not match X=3");
    }

    [Fact]
    public void DestructionSweep_ExcludesLands_NotTokens()
    {
        // Scryfall oracle: "Destroy each NONLAND permanent…". A nonland TOKEN
        // with the matching mv IS destroyed (it is a nonland permanent); a LAND
        // is never destroyed (lands have mv 0, so X=0 would otherwise sweep them).
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(
            _alice,
            xValueProvider: () => 0,
            eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);

        // Bob has a nonland token at mv 0 → destroyed.
        var bobToken = MakePermanent(_bob, "Soldier", "{0}", 1, 1);
        bobToken.MarkAsToken();

        // Bob also controls a land (mv 0) → NOT destroyed (nonland filter).
        var bobLand = new Majik.Core.Cards.Land("Wastes");
        bobLand.SetOwner(_bob);
        bobLand.SetController(_bob);
        bobLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobLand);

        bus.Publish(new CombatDamageDealtEvent(hellkite, _bob, amount: 1));

        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Graveyard.GetCards().Should().Contain(bobToken,
            "a nonland token with mv = X is destroyed (oracle: 'each nonland permanent').");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobLand,
            "lands are never destroyed by Steel Hellkite's sweep.");
    }

    [Fact]
    public void DestructionSweep_KeysVictimsBySource_NotForeignDamage()
    {
        // RE-SOURCE-SAFE tracker: damage dealt by SOME OTHER permanent must not
        // populate Steel Hellkite's own victim set. Only damage whose source is
        // the sweep's live source (here, Steel Hellkite itself) counts.
        var bus = new EventBus();
        var hellkite = SteelHellkiteFactory.Create(_alice, xValueProvider: () => 2, eventBus: bus);
        hellkite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hellkite);

        var bobBear = MakePermanent(_bob, "Grizzly Bears", "{1}{G}", 2, 2); // mv 2.

        // A DIFFERENT attacker deals combat damage to Bob — must NOT count for
        // Steel Hellkite's sweep.
        var otherAttacker = MakePermanent(_alice, "Random Beater", "{2}", 3, 3);
        bus.Publish(new CombatDamageDealtEvent(otherAttacker, _bob, amount: 3));

        var sweep = hellkite.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        ContextResolve.Resolve(sweep, _alice, _alice, _bob);

        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear,
            "only damage dealt BY THE SWEEP'S SOURCE this turn populates its victim set.");
    }

    [Fact]
    public void DestructionSweep_IsRebindSafe()
    {
        // The {X} sweep reads its source + X + victim set off the live
        // ResolutionContext, so Agatha's Soul Cauldron may re-home it to a
        // bearer via ActivatedAbility.RebindTo (CR 707.2 / 613.1f).
        var c = SteelHellkiteFactory.Create(_alice);
        var sweep = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(m => m.Description == "X"));
        sweep.RebindSafe.Should().BeTrue(
            "the re-sourced sweep reads ResolutionContext.Source for the 'damaged by this creature this turn' linkage.");
    }
}
