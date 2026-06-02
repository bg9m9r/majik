using System.Linq;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StickyFingersFactory"/>.
///
/// Card: Sticky Fingers — Enchantment — Aura {R} (Streets of New Capenna).
///   "Enchant creature"
///   "Enchanted creature has menace and \"Whenever this creature deals
///    combat damage to a player, create a Treasure token.\""
///   "When enchanted creature dies, draw a card."
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {R}).
///   - Granted Menace keyword (CR 702.111) via an AttachedBoostEffect.
///   - Granted combat-damage→Treasure trigger (CR 510 / 603.1) on the
///     enchanted creature.
///   - The aura's own "enchanted creature dies → draw a card" trigger
///     (CR 603.6e — leaves-the-battlefield ability, looks back in time).
///   - "Enchant creature" cast-time target predicate filters non-creatures.
/// </summary>
public class StickyFingersTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StickyFingers_Identity()
    {
        var c = StickyFingersFactory.Create(_alice);

        c.Name.Should().Be("Sticky Fingers");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StickyFingers()
    {
        var card = NamedCardFactory.Create("Sticky Fingers", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sticky Fingers");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Granted Menace keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_GrantsMenace_WhileAttached()
    {
        var effects = new ContinuousEffectsService();
        var aura = StickyFingersFactory.Create(_alice, effects, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("Menace");
    }

    [Fact]
    public void Static_Menace_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = StickyFingersFactory.Create(_alice, effects, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Keywords.Should().NotContain("Menace");
    }

    // -----------------------------------------------------------------------
    // Granted combat-damage → Treasure trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void CombatTrigger_CreatesTreasure_WhenEnchantedCreatureHitsPlayer()
    {
        var aura = StickyFingersFactory.Create(_alice, continuousEffects: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var trigger = aura.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .Single(t => t.Condition is Majik.Core.Abilities.EventTriggerCondition<CombatDamageDealtEvent>);

        var ev = new CombatDamageDealtEvent(bear, _bob, 2);
        trigger.Condition.Matches(ev, null!).Should().BeTrue();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(0);

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure)).Should().Be(1,
                "the enchanted creature dealt combat damage to a player");
    }

    [Fact]
    public void CombatTrigger_DoesNotFire_ForCreatureCombatDamage()
    {
        var aura = StickyFingersFactory.Create(_alice, continuousEffects: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var blocker = NewCreatureOnBattlefield("Blocker");

        var trigger = aura.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .Single(t => t.Condition is Majik.Core.Abilities.EventTriggerCondition<CombatDamageDealtEvent>);

        // Damage to a creature, not a player → no trigger.
        var ev = new CombatDamageDealtEvent(bear, blocker, 2);
        trigger.Condition.Matches(ev, null!).Should().BeFalse();
    }

    [Fact]
    public void CombatTrigger_DoesNotFire_WhenSourceIsNotEnchantedCreature()
    {
        var aura = StickyFingersFactory.Create(_alice, continuousEffects: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var other = NewCreatureOnBattlefield("Other");

        var trigger = aura.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .Single(t => t.Condition is Majik.Core.Abilities.EventTriggerCondition<CombatDamageDealtEvent>);

        var ev = new CombatDamageDealtEvent(other, _bob, 2);
        trigger.Condition.Matches(ev, null!).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Aura's own "enchanted creature dies → draw a card" trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_DrawsACard_WhenEnchantedCreatureDies()
    {
        var aura = StickyFingersFactory.Create(_alice, continuousEffects: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        // Stock Alice's library so the draw has a card to find.
        var libCard = new Creature("LibCard", "{G}", 1, 1);
        libCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var diesTrigger = aura.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .Single(t => t.Condition is Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            bear, ZoneType.Battlefield, ZoneType.Graveyard);
        diesTrigger.Condition.Matches(moveEvent, null!).Should().BeTrue();

        _alice.Zones.Hand.GetCards().Should().NotContain(libCard);

        foreach (var e in diesTrigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(libCard,
            "Sticky Fingers draws a card when the enchanted creature dies");
    }

    [Fact]
    public void DiesTrigger_DoesNotFire_ForUnrelatedCreatureDeath()
    {
        var aura = StickyFingersFactory.Create(_alice, continuousEffects: null, triggers: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var other = NewCreatureOnBattlefield("Other");

        var diesTrigger = aura.Abilities
            .OfType<Majik.Core.Abilities.TriggeredAbility>()
            .Single(t => t.Condition is Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Events.CardMovedEvent>);

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            other, ZoneType.Battlefield, ZoneType.Graveyard);
        diesTrigger.Condition.Matches(moveEvent, null!).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var aura = StickyFingersFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Mountain");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = StickyFingersFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
