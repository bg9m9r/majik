using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RoilingDragonstormFactory"/>.
///
/// Roiling Dragonstorm — Enchantment {1}{U}. Oracle text (verified against the
/// embedded Modern seed, sourced from Scryfall):
///   "When this enchantment enters, draw two cards, then discard a card.
///    When a Dragon you control enters, return this enchantment to its
///    owner's hand."
///
/// Covers:
/// - Identity (name, Enchantment type, mana cost {1}{U}, mana value 2,
///   owner/controller, blue colour).
/// - Exactly two triggered abilities attached.
/// - ETB loot trigger fires on self-ETB; resolution draws two then discards one.
/// - Dragon trigger condition gating: a Dragon the controller controls fires it;
///   a non-Dragon does not; a Dragon an opponent controls does not; and the
///   enchantment's own (non-Dragon) ETB does not fire it.
/// - Dragon trigger resolution returns the enchantment to its owner's hand.
/// - Dragon trigger resolution is a no-op when the enchantment already left the
///   battlefield (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class RoilingDragonstormFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RoilingDragonstorm_Identity()
    {
        var c = RoilingDragonstormFactory.Create(_alice);

        c.Name.Should().Be("Roiling Dragonstorm");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}");
        c.ManaCostValue.TotalValue.Should().Be(2, "mana value 2: one generic + one blue pip");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RoilingDragonstorm_Colors_ContainsBlueOnly()
    {
        var c = RoilingDragonstormFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Roiling Dragonstorm costs {1}{U}");
        colors.Should().HaveCount(1, "Roiling Dragonstorm is exactly Blue");
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void RoilingDragonstorm_HasExactlyTwoTriggeredAbilities()
    {
        var c = RoilingDragonstormFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB loot trigger + Dragon-ETB self-bounce trigger");
    }

    // -----------------------------------------------------------------------
    // ETB loot trigger — "draw two cards, then discard a card"
    // -----------------------------------------------------------------------

    [Fact]
    public void RoilingDragonstorm_EtbLootTrigger_FiresOnSelfEnter_Only()
    {
        var c = RoilingDragonstormFactory.Create(_alice);
        var loot = LootTrigger(c);

        var selfEnter = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        loot.Condition.Matches(selfEnter, loot).Should().BeTrue(
            "the loot trigger fires when this enchantment enters");

        var dragon = MakeCreature("Dragon Token", CardSubtype.Dragon, _alice);
        var dragonEnter = new CardMovedEvent(dragon, ZoneType.Hand, ZoneType.Battlefield);
        loot.Condition.Matches(dragonEnter, loot).Should().BeFalse(
            "the loot trigger only fires on this enchantment's own ETB");
    }

    [Fact]
    public void RoilingDragonstorm_EtbLootEffect_DrawsTwoThenDiscardsOne()
    {
        var alice = new Player("Alice", 20);

        // Stock the library with three known cards so the draw is observable.
        for (var i = 0; i < 3; i++)
        {
            var lib = new Land($"Island {i}");
            lib.SetOwner(alice);
            alice.Zones.Library.AddCard(lib);
            lib.SetZone(ZoneType.Library);
        }

        var c = RoilingDragonstormFactory.Create(alice);
        var loot = LootTrigger(c);

        foreach (var effect in loot.Effects) effect.Execute();

        // Drew 2, discarded 1 → net +1 card in hand, 1 in graveyard, 1 left in lib.
        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "drew two cards then discarded one — net one card in hand");
        alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            "the discarded card is in the graveyard");
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "two of the three library cards were drawn");
    }

    // -----------------------------------------------------------------------
    // Dragon-ETB self-bounce trigger — condition gating
    // -----------------------------------------------------------------------

    [Fact]
    public void RoilingDragonstorm_DragonTrigger_FiresForADragonYouControl()
    {
        var c = RoilingDragonstormFactory.Create(_alice);
        var dragonTrig = DragonTrigger(c);

        var myDragon = MakeCreature("Bronze Dragon", CardSubtype.Dragon, _alice);
        var evt = new CardMovedEvent(myDragon, ZoneType.Hand, ZoneType.Battlefield);

        dragonTrig.Condition.Matches(evt, dragonTrig).Should().BeTrue(
            "a Dragon the controller controls entering fires the self-bounce");
    }

    [Fact]
    public void RoilingDragonstorm_DragonTrigger_DoesNotFireForNonDragon()
    {
        var c = RoilingDragonstormFactory.Create(_alice);
        var dragonTrig = DragonTrigger(c);

        var bear = MakeCreature("Grizzly Bears", CardSubtype.Bear, _alice);
        var evt = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        dragonTrig.Condition.Matches(evt, dragonTrig).Should().BeFalse(
            "a non-Dragon entering does not fire the self-bounce");
    }

    [Fact]
    public void RoilingDragonstorm_DragonTrigger_DoesNotFireForOpponentDragon()
    {
        var c = RoilingDragonstormFactory.Create(_alice);
        var dragonTrig = DragonTrigger(c);

        var bob = new Player("Bob", 20);
        var oppDragon = MakeCreature("Shivan Dragon", CardSubtype.Dragon, bob);
        var evt = new CardMovedEvent(oppDragon, ZoneType.Hand, ZoneType.Battlefield);

        dragonTrig.Condition.Matches(evt, dragonTrig).Should().BeFalse(
            "\"a Dragon you control\" excludes a Dragon an opponent controls (CR 109.5)");
    }

    [Fact]
    public void RoilingDragonstorm_DragonTrigger_DoesNotFireOnOwnEnter()
    {
        // The enchantment is not a Dragon, so its own ETB never fires the
        // Dragon trigger (only the loot trigger does).
        var c = RoilingDragonstormFactory.Create(_alice);
        var dragonTrig = DragonTrigger(c);

        var selfEnter = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        dragonTrig.Condition.Matches(selfEnter, dragonTrig).Should().BeFalse(
            "Roiling Dragonstorm is not a Dragon, so its own ETB does not self-bounce it");
    }

    // -----------------------------------------------------------------------
    // Dragon-ETB self-bounce trigger — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void RoilingDragonstorm_DragonBounce_ReturnsEnchantmentToOwnersHand()
    {
        var alice = new Player("Alice", 20);

        var c = RoilingDragonstormFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var dragonTrig = DragonTrigger(c);
        foreach (var effect in dragonTrig.Effects) effect.Execute();

        c.Zone.Should().Be(ZoneType.Hand,
            "the Dragon trigger returns this enchantment to its owner's hand");
        alice.Zones.Hand.GetCards().Should().Contain(c);
        alice.Zones.Battlefield.GetCards().Should().NotContain(c,
            "the enchantment has left the battlefield");
    }

    [Fact]
    public void RoilingDragonstorm_DragonBounce_NoOpIfAlreadyGone()
    {
        // CR 608.2b — if the enchantment already left the battlefield at
        // resolution, the ability does nothing.
        var alice = new Player("Alice", 20);

        var c = RoilingDragonstormFactory.Create(alice);
        alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard); // already gone at resolution time

        var dragonTrig = DragonTrigger(c);
        var act = () => { foreach (var effect in dragonTrig.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "CR 608.2b: enchantment off the battlefield at resolution is a no-op");
        alice.Zones.Hand.GetCards().Should().NotContain(c,
            "the already-gone enchantment is not moved to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(c,
            "the enchantment stays where it was");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility LootTrigger(Enchantment c)
        => c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>
                && t.Condition.Matches(
                    new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), t));

    private static TriggeredAbility DragonTrigger(Enchantment c)
        => c.Abilities.OfType<TriggeredAbility>()
            .Single(t => !t.Condition.Matches(
                new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), t));

    private static Creature MakeCreature(string name, CardSubtype subtype, Player controller)
    {
        var creature = new Creature(name, "{2}", 2, 2, subtypes: new[] { subtype });
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }
}
