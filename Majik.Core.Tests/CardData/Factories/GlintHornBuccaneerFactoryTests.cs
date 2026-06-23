using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlintHornBuccaneerFactory"/> (Commander Legends,
/// {1}{R}{R}, Creature — Minotaur Pirate 2/4).
///
/// Oracle text (Scryfall-verified 2026-06-23):
///   "Haste
///    Whenever you discard a card, this creature deals 1 damage to each
///    opponent.
///    {1}{R}, Discard a card: Draw a card. Activate only if this creature is
///    attacking."
///
/// Covers ONLY the card's unique behaviour (plus one identity assert):
/// - Identity (name, {1}{R}{R}, 2/4, Minotaur + Pirate subtypes, Haste).
/// - Discard trigger (CR 603.1 / 701.8): your discard fires it; it pings each
///   opponent for 1 (CR 119.3); a land discard still fires (no nonland gate);
///   an opponent's discard does NOT fire ("you discard" — CR 109.5).
/// - Loot ability shape ({1}{R} + DiscardACardCost) and the "activate only if
///   attacking" gate (CR 602.5c): not activatable out of combat, activatable
///   while attacking.
///
/// (Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "R")]
public class GlintHornBuccaneerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity — CR 205.3m / 702.10
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_MinotaurPirate_2_4_AtCost1RR_Haste()
    {
        var card = GlintHornBuccaneerFactory.Create(_alice);

        card.Name.Should().Be("Glint-Horn Buccaneer");
        card.ManaCost.ToString().Should().Be("{1}{R}{R}");
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Haste", "CR 702.10");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Discard trigger — CR 603.1 / 701.8
    // -----------------------------------------------------------------------

    private Creature OnBattlefield()
    {
        var card = GlintHornBuccaneerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }

    [Fact]
    public void DiscardTrigger_YouDiscard_PingsEachOpponentForOne()
    {
        var card = OnBattlefield();
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);

        // "Whenever you discard a card" — fires on the controller's discard.
        trigger.Condition.Matches(new DiscardedEvent(_alice, bolt, wasCost: false), trigger)
            .Should().BeTrue("your discard fires the trigger (CR 603.1)");

        // Resolve through a live game so ContextOpponents reads the opponent
        // list off the resolution context (resolver-null bug-class fix).
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob, _carol);

        _bob.LifeTotal.Should().Be(19, "CR 119.3 — each opponent takes 1 damage");
        _carol.LifeTotal.Should().Be(19, "each opponent — Carol too");
        _alice.LifeTotal.Should().Be(20, "'each opponent' excludes the controller (CR 109.5)");
    }

    [Fact]
    public void DiscardTrigger_LandDiscard_StillFires_NoNonlandGate()
    {
        var card = OnBattlefield();
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var mountain = new Land("Mountain");
        mountain.SetOwner(_alice);

        trigger.Condition.Matches(new DiscardedEvent(_alice, mountain, wasCost: false), trigger)
            .Should().BeTrue("CR 701.8 — 'discard a card' counts every card type, lands included");

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "a land discard still pings each opponent");
    }

    [Fact]
    public void DiscardTrigger_OpponentDiscards_DoesNotFire()
    {
        var card = OnBattlefield();
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var bobBolt = new Instant("Bob's Bolt", "{R}");
        bobBolt.SetOwner(_bob);

        trigger.Condition.Matches(new DiscardedEvent(_bob, bobBolt, wasCost: false), trigger)
            .Should().BeFalse("'whenever YOU discard' is scoped to the controller (CR 109.5)");
    }

    [Fact]
    public void DiscardTrigger_OnlyActiveOnBattlefield()
    {
        var card = GlintHornBuccaneerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "CR 113.6 — the ability functions only from the battlefield");
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Loot ability — {1}{R}, Discard a card: Draw a card. CR 602 / 602.5c.
    // -----------------------------------------------------------------------

    [Fact]
    public void LootAbility_HasManaAndDiscardCost()
    {
        var card = GlintHornBuccaneerFactory.Create(_alice);
        var loot = card.Abilities.OfType<ActivatedAbility>().Single();

        loot.Costs.OfType<DiscardACardCost>().Should().ContainSingle(
            "the loot's cost includes 'discard a card' (CR 117.1 / 701.16a)");

        var mana = loot.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "loot costs {1}{R}");
        mana.Red.Should().Be(1, "loot costs {1}{R}");
    }

    [Fact]
    public void LootAbility_NotAttacking_CannotActivate()
    {
        var combat = new CombatManager(); // no current combat
        var card = GlintHornBuccaneerFactory.Create(_alice, eventBus: null, triggers: null, combat: combat);
        var loot = card.Abilities.OfType<ActivatedAbility>().Single();

        loot.CanActivateNow().Should().BeFalse(
            "CR 602.5c — 'activate only if this creature is attacking'; it isn't");
    }

    [Fact]
    public void LootAbility_WhileAttacking_CanActivate()
    {
        var combat = new CombatManager();
        var card = GlintHornBuccaneerFactory.Create(_alice, eventBus: null, triggers: null, combat: combat);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness(); // it has Haste anyway — eligible to attack.

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        var loot = card.Abilities.OfType<ActivatedAbility>().Single();
        loot.CanActivateNow().Should().BeTrue(
            "CR 602.5c / 508 — the gate opens while this creature is attacking");
    }
}
