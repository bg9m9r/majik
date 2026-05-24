using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GuideOfSoulsFactory"/>.
///
/// Covers:
/// - Card identity (name, mana cost, Creature type, Spirit + Cleric
///   subtypes, 1/2 P/T, owner/controller).
/// - ETB triggered ability shape: exactly one TriggeredAbility, no
///   TargetRequests (the trigger fires; the activated ability is the
///   targeted one).
/// - ETB predicate semantics:
///   - Fires for Guide of Souls itself (printed 1 ≤ 2 — "or another"
///     disjunction includes Guide).
///   - Fires for another small (P ≤ 2) creature you control entering.
///   - Does NOT fire for a power-3 creature entering under controller.
///   - Does NOT fire for an opponent's small creature entering.
///   - Does NOT fire when the card leaves the battlefield (To != Battlefield).
/// - Activated ability shape: exactly one ActivatedAbility, one cost
///   (PayEnergyCost 2), one 1..1 "target creature" TargetRequest.
/// - Activation legality: CanPay(controller) false at 0/1 energy, true at 2+.
/// - Activation resolution: pays 2 energy, registers Flying + +1/+1
///   EOT on target's ActiveEffects. After ExpireEndOfTurn both clear.
/// - Dispatcher integration via NamedCardFactory.
/// </summary>
public class GuideOfSoulsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GuideOfSouls_NameIsCorrect()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.Name.Should().Be("Guide of Souls");
    }

    [Fact]
    public void GuideOfSouls_IsCreature()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void GuideOfSouls_HasCorrectSubtypes()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.HasSubtype(CardSubtype.Spirit).Should().BeTrue("printed type is Spirit Cleric");
        g.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void GuideOfSouls_HasCorrectStats()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.BasePower.Should().Be(1);
        g.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void GuideOfSouls_OwnerAndControllerAreSet()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GuideOfSouls_HasExactlyOneTriggeredAbility()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the small-creature-enters energy trigger is the only triggered ability");
    }

    [Fact]
    public void GuideOfSouls_HasExactlyOneActivatedAbility()
    {
        var g = GuideOfSoulsFactory.Create(_alice);

        g.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {E}{E} pump+flying activated ability is the only activated ability");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_HasPayEnergyCostOfTwo()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        pump.Costs.Should().HaveCount(1,
            "the only printed activation cost is Pay {E}{E}");
        var cost = pump.Costs.OfType<PayEnergyCost>().Single();
        cost.Amount.Should().Be(2,
            "printed cost is two energy counters (CR 106.13)");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_DeclaresOneCreatureTargetRequest()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        pump.TargetRequests.Should().HaveCount(1, "the printed pump targets one creature");
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger predicate — CR 603.6a + CR 106.13
    // -----------------------------------------------------------------------

    [Fact]
    public void GuideOfSouls_OwnEtb_FiresAndGrantsOneEnergy()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var etb = g.Abilities.OfType<TriggeredAbility>().Single();

        // Guide enters under its controller — power 1 ≤ 2 → predicate matches.
        var evt = new CardMovedEvent(g, ZoneType.Library, ZoneType.Battlefield);

        etb.Condition.Matches(evt, etb).Should().BeTrue(
            "Guide of Souls's own ETB triggers the energy ability "
            + "(the 'or another' disjunction includes Guide itself; printed 1 ≤ 2)");

        _alice.EnergyCounters.Should().Be(0);
        foreach (var effect in etb.Effects) effect.Execute();
        _alice.EnergyCounters.Should().Be(1,
            "ETB grants the controller one energy (CR 106.13)");
    }

    [Fact]
    public void GuideOfSouls_AnotherSmallCreatureEnters_TriggerFires()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var etb = g.Abilities.OfType<TriggeredAbility>().Single();

        // A separate 1/1 creature under Alice (power 1 ≤ 2).
        var bear = new Creature("Mausoleum Wanderer", "{W}", 1, 1);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var evt = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        etb.Condition.Matches(evt, etb).Should().BeTrue(
            "another small (P≤2) creature you control entering triggers the energy ability");
    }

    [Fact]
    public void GuideOfSouls_PowerThreeCreatureEnters_TriggerDoesNotFire()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var etb = g.Abilities.OfType<TriggeredAbility>().Single();

        var hillGiant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        hillGiant.SetOwner(_alice);
        hillGiant.SetController(_alice);

        var evt = new CardMovedEvent(hillGiant, ZoneType.Hand, ZoneType.Battlefield);

        etb.Condition.Matches(evt, etb).Should().BeFalse(
            "printed power 3 > 2 — the predicate rejects (CR 208.2 reading on printed P/T)");
    }

    [Fact]
    public void GuideOfSouls_OpponentSmallCreatureEnters_TriggerDoesNotFire()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var etb = g.Abilities.OfType<TriggeredAbility>().Single();

        var bobBear = new Creature("Savannah Lions", "{W}", 2, 1);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);

        var evt = new CardMovedEvent(bobBear, ZoneType.Hand, ZoneType.Battlefield);

        etb.Condition.Matches(evt, etb).Should().BeFalse(
            "the printed clause is 'a creature YOU control' — opponent creatures are out of scope");
    }

    [Fact]
    public void GuideOfSouls_NonBattlefieldZoneMove_TriggerDoesNotFire()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var etb = g.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        // Battlefield → Graveyard (death) is not an ETB.
        var evt = new CardMovedEvent(bear, ZoneType.Battlefield, ZoneType.Graveyard);

        etb.Condition.Matches(evt, etb).Should().BeFalse(
            "the trigger requires ToZone == Battlefield (entering, not leaving)");
    }

    // -----------------------------------------------------------------------
    // Activated ability — pay energy + pump/flying EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void GuideOfSouls_PayEnergyCost_CannotPayWithZeroEnergy()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = pump.Costs.OfType<PayEnergyCost>().Single();

        _alice.EnergyCounters.Should().Be(0);
        cost.CanPay(_alice).Should().BeFalse(
            "CR 119.4 — Alice has zero energy, cannot pay {E}{E}");
    }

    [Fact]
    public void GuideOfSouls_PayEnergyCost_CannotPayWithOneEnergy()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = pump.Costs.OfType<PayEnergyCost>().Single();

        _alice.GainEnergy(1);
        cost.CanPay(_alice).Should().BeFalse(
            "one energy is short of the printed {E}{E} cost");
    }

    [Fact]
    public void GuideOfSouls_PayEnergyCost_CanPayWithTwoEnergy()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = pump.Costs.OfType<PayEnergyCost>().Single();

        _alice.GainEnergy(2);
        cost.CanPay(_alice).Should().BeTrue("two energy meets the printed cost");
    }

    [Fact]
    public void GuideOfSouls_PayEnergyCost_DeductsTwoEnergyOnPay()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();
        var cost = pump.Costs.OfType<PayEnergyCost>().Single();

        _alice.GainEnergy(3);
        cost.Pay(_alice);

        _alice.EnergyCounters.Should().Be(1, "two of the three energy spent on activation");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_GrantsFlyingAndPlusOnePlusOneEot()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        var svc = new ContinuousEffectsService();
        target.ActiveEffects = svc;

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        // Sanity: no flying / base 2/2 before activation.
        target.GetPower().Should().Be(2);
        target.GetToughness().Should().Be(2);
        svc.Compute(target).Keywords.Should().NotContain("Flying");

        foreach (var effect in pump.Effects) effect.Execute();

        target.GetPower().Should().Be(3, "+1/+1 EOT registered (Layer 7c)");
        target.GetToughness().Should().Be(3);
        svc.Compute(target).Keywords.Should().Contain("Flying",
            "Flying grant registered (CR 613.1c Layer 6)");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_GrantsExpireAtEndOfTurn()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        var svc = new ContinuousEffectsService();
        target.ActiveEffects = svc;

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in pump.Effects) effect.Execute();

        target.GetPower().Should().Be(3);
        svc.Compute(target).Keywords.Should().Contain("Flying");

        // CR 514.2 — cleanup step removes EOT effects.
        svc.ExpireEndOfTurn();

        target.GetPower().Should().Be(2, "pump expired (PumpUntilEndOfTurnEffect)");
        target.GetToughness().Should().Be(2);
        svc.Compute(target).Keywords.Should().NotContain("Flying",
            "Flying grant expired (GrantKeywordUntilEndOfTurnEffect)");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_TargetOffBattlefield_NoOp()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        // Target went to graveyard between target-pick and resolution.
        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Graveyard);
        var svc = new ContinuousEffectsService();
        target.ActiveEffects = svc;

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in pump.Effects) effect.Execute();

        // No grants registered.
        target.GetPower().Should().Be(2);
        svc.Compute(target).Keywords.Should().NotContain("Flying",
            "CR 608.2b — target left the battlefield, effect does nothing");
    }

    [Fact]
    public void GuideOfSouls_ActivatedAbility_NullActiveEffects_DoesNotThrow()
    {
        var g = GuideOfSoulsFactory.Create(_alice);
        var pump = g.Abilities.OfType<ActivatedAbility>().Single();

        var target = new Creature("Bear", "{1}{G}", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects intentionally null.

        pump.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in pump.Effects) effect.Execute(); };
        act.Should().NotThrow("shape-only test: effect body guards on null ActiveEffects");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GuideOfSouls_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Guide of Souls", _alice);

        card.Should().BeOfType<Creature>("Guide of Souls is a Creature");
        card.Name.Should().Be("Guide of Souls");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the dispatcher attaches the ETB energy trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the dispatcher attaches the {E}{E} pump+flying activated ability");
    }
}
