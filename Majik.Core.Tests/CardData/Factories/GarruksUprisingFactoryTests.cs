using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Garruk's Uprising (Lorwyn Eclipsed Commander / many reprints —
/// Enchantment {2}{G}).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "When this enchantment enters, if you control a creature with power 4 or
///    greater, draw a card.
///    Creatures you control have trample.
///    Whenever a creature you control with power 4 or greater enters, draw a
///    card."
///
/// Covers (the card's UNIQUE behaviour + one identity assert):
///   - Identity: name, Enchantment type, mana cost {2}{G}.
///   - Static trample grant: every creature the controller controls gains
///     Trample (CR 613.1f / 702.19); opponents' creatures don't; LTB lifts it.
///   - ETB intervening-if (CR 603.4): condition true iff the controller
///     controls a creature with power >= 4; opponent's power-4 creature
///     doesn't count.
///   - Power-4-enters trigger (CR 603.6a): fires only for the controller's
///     entering creatures whose power is >= 4.
///   - Each draw trigger pulls a card from library to hand on resolution.
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness, so no dispatch test here.)
/// </summary>
[Trait("Color", "G")]
public class GarruksUprisingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GarruksUprising_IsEnchantment_AtCost2G()
    {
        var c = GarruksUprisingFactory.Create(_alice);

        c.Name.Should().Be("Garruk's Uprising");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GarruksUprising_HasTwoDrawTriggers()
    {
        var c = GarruksUprisingFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "ETB intervening-if draw + power-4-creature-enters draw");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
        triggers.Should().ContainSingle(t => t.InterveningIf != null,
            "only the ETB draw carries an intervening-if gate");
    }

    // ─── Static: "Creatures you control have trample" (CR 613.1f / 702.19) ──

    [Fact]
    public void Trample_GrantedToEveryControllerCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeCreature("Bear", _alice, svc, 2, 2);
        var elf = MakeCreature("Elf", _alice, svc, 1, 1);

        var uprising = GarruksUprisingFactory.Create(_alice, svc, triggers: null);
        SeatEnchantment(uprising, _alice, svc);

        // Read through the full-compute keyword path the combat engine actually
        // consumes (CombatAbilities.HasTrample → ActiveEffects.Compute, all
        // layers incl. 7c) — the LordStaticEffect keyword grant lives at Layer
        // 7c, which the combat lookup sees (HasEffectiveKeyword stops at Layer 6
        // and would miss it).
        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "Garruk's Uprising grants Trample to every creature the controller controls");
        CombatAbilities.HasTrample(elf).Should().BeTrue();
    }

    [Fact]
    public void Trample_NotGrantedToOpponentCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bobBear = MakeCreature("Bob's Bear", _bob, svc, 2, 2);

        var uprising = GarruksUprisingFactory.Create(_alice, svc, triggers: null);
        SeatEnchantment(uprising, _alice, svc);

        CombatAbilities.HasTrample(bobBear).Should().BeFalse(
            "the static keys on 'you control' — opponent's creatures don't gain Trample");
    }

    [Fact]
    public void Trample_LiftsWhenUprisingLeavesBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeCreature("Bear", _alice, svc, 2, 2);

        var uprising = GarruksUprisingFactory.Create(_alice, svc, triggers: null);
        SeatEnchantment(uprising, _alice, svc);

        CombatAbilities.HasTrample(bear).Should().BeTrue();

        uprising.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(uprising);
        _alice.Zones.Graveyard.AddCard(uprising);

        CombatAbilities.HasTrample(bear).Should().BeFalse(
            "the grant's IsActive gates on the source being on the battlefield (CR 614)");
    }

    // ─── ETB intervening-if (CR 603.4 / 208.3) ─────────────────────────────

    [Fact]
    public void Etb_FiresForSelfEntering_NotOtherCard()
    {
        var c = GarruksUprisingFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        var selfEvt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(selfEvt, etb).Should().BeTrue(
            "the ETB trigger fires when Garruk's Uprising itself enters");

        var other = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var otherEvt = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(otherEvt, etb).Should().BeFalse(
            "the ETB trigger fires only for this specific card");
    }

    [Fact]
    public void Etb_InterveningIf_FalseWithNoPower4Creature()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        SeatEnchantment(uprising, _alice, continuousEffects: null);

        // A 3-power creature does not satisfy "power 4 or greater".
        MakeCreature("Hill Giant", _alice, svc: null, 3, 3);

        var etb = uprising.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeFalse(
            "no creature with power 4+ ⇒ intervening-if is unmet (CR 603.4)");
    }

    [Fact]
    public void Etb_InterveningIf_TrueWithPower4Creature()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        SeatEnchantment(uprising, _alice, continuousEffects: null);

        MakeCreature("Tusker", _alice, svc: null, 4, 4);

        var etb = uprising.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeTrue(
            "controlling a creature with power 4+ satisfies the intervening-if");
    }

    [Fact]
    public void Etb_InterveningIf_IgnoresOpponentPower4Creatures()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        SeatEnchantment(uprising, _alice, continuousEffects: null);

        // Power-4 creature controlled by the OPPONENT — does not count.
        MakeCreature("Bob's Tusker", _bob, svc: null, 4, 4);

        var etb = uprising.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        etb.InterveningIf!().Should().BeFalse(
            "'you control' — an opponent's power-4 creature does not count");
    }

    [Fact]
    public void Etb_Draw_PullsCardFromLibrary()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        var top = MakeLibraryCard("Forest", _alice);

        var etb = uprising.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf != null);

        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the ETB draw pulls the top card of the controller's library to hand");
    }

    // ─── Power-4-enters trigger (CR 603.6a / 208.3) ────────────────────────

    [Fact]
    public void Power4Enters_FiresForControllerPower4Creature()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        var enters = PowerEntersTrigger(uprising);

        var beast = new Creature("Wild Beast", "{3}{G}", 5, 5);
        beast.SetOwner(_alice);
        beast.SetController(_alice);
        var evt = new CardMovedEvent(beast, ZoneType.Hand, ZoneType.Battlefield);

        enters.Condition.Matches(evt, enters).Should().BeTrue(
            "a creature you control with power 4+ entering triggers the draw");
    }

    [Fact]
    public void Power4Enters_DoesNotFireForSmallCreature()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        var enters = PowerEntersTrigger(uprising);

        var weenie = new Creature("Weenie", "{G}", 3, 3);
        weenie.SetOwner(_alice);
        weenie.SetController(_alice);
        var evt = new CardMovedEvent(weenie, ZoneType.Hand, ZoneType.Battlefield);

        enters.Condition.Matches(evt, enters).Should().BeFalse(
            "a creature with power below 4 does not trigger the draw");
    }

    [Fact]
    public void Power4Enters_DoesNotFireForOpponentCreature()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        var enters = PowerEntersTrigger(uprising);

        var bobBeast = new Creature("Bob's Beast", "{3}{G}", 5, 5);
        bobBeast.SetOwner(_bob);
        bobBeast.SetController(_bob);
        var evt = new CardMovedEvent(bobBeast, ZoneType.Hand, ZoneType.Battlefield);

        enters.Condition.Matches(evt, enters).Should().BeFalse(
            "'you control' — an opponent's power-4 creature entering does not trigger");
    }

    [Fact]
    public void Power4Enters_Draw_PullsCardFromLibrary()
    {
        var uprising = GarruksUprisingFactory.Create(_alice);
        var top = MakeLibraryCard("Island", _alice);
        var enters = PowerEntersTrigger(uprising);

        foreach (var e in enters.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the power-4-enters draw pulls the top card of the controller's library to hand");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static TriggeredAbility PowerEntersTrigger(Enchantment uprising) =>
        uprising.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf == null);

    private static void SeatEnchantment(
        Enchantment card, Player owner, ContinuousEffectsService? continuousEffects)
    {
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        if (continuousEffects != null) card.ActiveEffects = continuousEffects;
    }

    private static Creature MakeCreature(
        string name, Player owner, ContinuousEffectsService? svc, int p, int t)
    {
        var c = new Creature(name, "{G}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        if (svc != null) c.ActiveEffects = svc;
        return c;
    }

    private static Creature MakeLibraryCard(string name, Player owner)
    {
        var c = new Creature(name, "{G}", 1, 1) { Owner = owner };
        c.SetZone(ZoneType.Library);
        owner.Zones.Library.AddCard(c);
        return c;
    }
}
