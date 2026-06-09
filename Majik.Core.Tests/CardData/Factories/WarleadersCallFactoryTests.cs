using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Warleader's Call (Outlaws of Thunder Junction, {1}{R}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creatures you control get +1/+1.
///    Whenever a creature you control enters, this enchantment deals 1
///    damage to each opponent."
///
/// Covers:
///   - Card shape: name, Enchantment type, mana cost {1}{R}{W}.
///   - Anthem: +1/+1 to the controller's creatures (all creatures, no gate).
///   - Opponent's creatures are NOT buffed ("you control").
///   - LTB lifts the anthem (IsActive gate).
///   - ETB-damage trigger attached structurally; fires 1 to each opponent.
///   - No-resolver path no-ops the burn half.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "RW")]
public class WarleadersCallFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void WarleadersCall_IsEnchantment_AtCost1RW()
    {
        var c = WarleadersCallFactory.Create(_alice);

        c.Name.Should().Be("Warleader's Call");
        c.ManaCost.Should().Be("{1}{R}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WarleadersCall_BuffsControllersCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Bear", _alice, svc, 2, 2);

        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: svc, triggers: null);
        call.SetZone(ZoneType.Battlefield);
        call.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(call);

        bear.GetPower().Should().Be(3,
            "Warleader's Call grants +1/+1 to creatures the controller controls");
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void WarleadersCall_DoesNotBuffOpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobBear = MakeCreature("Bob's Bear", _bob, svc, 2, 2);

        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: svc, triggers: null);
        call.SetZone(ZoneType.Battlefield);
        call.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(call);

        bobBear.GetPower().Should().Be(2,
            "Warleader's Call keys on 'you control' — opponent's creatures are unaffected");
        bobBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void WarleadersCall_LeavingBattlefield_LiftsAnthem()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Bear", _alice, svc, 2, 2);

        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: svc, triggers: null);
        call.SetZone(ZoneType.Battlefield);
        call.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(call);

        bear.GetPower().Should().Be(3);

        // Warleader's Call leaves the battlefield — IsActive gate flips false.
        call.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(call);
        _alice.Zones.Graveyard.AddCard(call);

        bear.GetPower().Should().Be(2,
            "the anthem's IsActive gates on the source being on the battlefield");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void WarleadersCall_HasOneTriggeredAbility()
    {
        var c = WarleadersCallFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the printed creature-you-control-enters trigger");
    }

    [Fact]
    public void EntersTrigger_DealsOneDamageToEachOpponent()
    {
        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: null, triggers: null);

        // Drive the trigger's effect directly (matching the Glaring
        // Fleshraker / Voldaren Epicure test posture).
        var entersTrigger = FindEntersTrigger(call);
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(entersTrigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19,
            "a creature you control entering deals 1 damage to each opponent");
    }

    [Fact]
    public void EntersTrigger_WithoutResolver_NoOps()
    {
        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: null, triggers: null);

        var entersTrigger = FindEntersTrigger(call);
        foreach (var e in entersTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no opponent resolver → burn half no-ops");
    }

    [Fact]
    public void EntersTrigger_FiresWhenControllersCreatureEnters_ButNotOpponents()
    {
        var call = WarleadersCallFactory.Create(
            _alice, continuousEffects: null, triggers: null);
        call.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(call);

        var entersTrigger = FindEntersTrigger(call);

        // A creature Alice controls enters the battlefield → condition met.
        var bear = new Creature("Bear", "{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        entersTrigger.Condition.Matches(
            new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield), entersTrigger)
            .Should().BeTrue("a creature you control entering meets the trigger condition");

        // An opponent's creature entering must NOT meet the condition.
        var bobBear = new Creature("Bob's Bear", "{G}", 2, 2);
        bobBear.SetOwner(_bob);
        bobBear.SetController(_bob);

        entersTrigger.Condition.Matches(
            new CardMovedEvent(bobBear, ZoneType.Hand, ZoneType.Battlefield), entersTrigger)
            .Should().BeFalse("'you control' excludes opponents' creatures");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TriggeredAbility FindEntersTrigger(Enchantment card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(CardMovedEvent));

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t)
    {
        var c = new Creature(name, "{G}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
