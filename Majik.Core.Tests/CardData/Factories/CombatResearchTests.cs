using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="CombatResearchFactory"/>.
///
/// Card: Combat Research — Enchantment — Aura {U} (Dominaria United).
///   "Enchant creature
///    Enchanted creature has 'Whenever this creature deals combat damage
///    to a player, draw a card.'
///    As long as enchanted creature is legendary, it gets +1/+1 and has
///    ward {1}."
///
/// Covers:
///   - Identity / dispatch / Aura subtype (loaded from JSON).
///   - Granted draw-on-combat-damage-to-a-player trigger (CR 510 / 603.1),
///     gated on the enchanted creature and a player target.
///   - Conditional legendary boost: +1/+1 only while the enchanted
///     creature is legendary (CR 613 Layer 7c).
///   - Conditional ward {1} marker keyword while legendary (CR 702.21).
///   - Inert while unattached.
/// </summary>
[Trait("Color", "U")]
public class CombatResearchTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CombatResearch_Identity()
    {
        var c = CombatResearchFactory.Create(_alice);

        c.Name.Should().Be("Combat Research");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }
    // -----------------------------------------------------------------------
    // Granted trigger — "Whenever this creature deals combat damage to a
    // player, draw a card." (CR 510 / CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void CombatTrigger_GatesOnEnchantedCreatureAndPlayerTarget()
    {
        var bear = NewCreature("Bear", _alice);
        var other = NewCreature("Other", _alice);

        var aura = CombatResearchFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);
        aura.AttachTo(bear);

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();

        // Enchanted Bear damages a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("enchanted creature dealt combat damage to a player (CR 510)");

        // A different (unenchanted) creature damages a player → no match.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("the granted trigger fires only for the enchanted creature");

        // Enchanted Bear damages a creature (not a player) → no match.
        var dummy = new Creature("Dummy", "1G", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, target: dummy, amount: 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    [Fact]
    public void CombatTrigger_DrawsForEnchantedCreaturesController()
    {
        var bear = NewCreature("Bear", _alice);
        var aura = CombatResearchFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);
        aura.AttachTo(bear);

        SeedLibrary(_alice, 3);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the granted trigger draws a card for the enchanted creature's controller");
    }

    // -----------------------------------------------------------------------
    // Conditional legendary boost — +1/+1 + ward {1} only while legendary
    // -----------------------------------------------------------------------

    [Fact]
    public void Legendary_GetsPlusOnePlusOne_AndWard()
    {
        var effects = new ContinuousEffectsService();
        var aura = CombatResearchFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(aura, _alice);

        var legend = NewLegendaryCreature("Legend", _alice, 2, 2);
        aura.AttachTo(legend);

        var chars = effects.Compute(legend);
        chars.Power.Should().Be(3, "2 + 1 = 3 while legendary");
        chars.Toughness.Should().Be(3, "2 + 1 = 3 while legendary");
        chars.Keywords.Should().Contain("Ward");
    }

    [Fact]
    public void NonLegendary_NoBoostNoWard()
    {
        var effects = new ContinuousEffectsService();
        var aura = CombatResearchFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreature("Bear", _alice);
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2, "no boost while the enchanted creature is not legendary");
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Ward");
    }

    [Fact]
    public void Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = CombatResearchFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(aura, _alice);

        var legend = NewLegendaryCreature("Legend", _alice, 2, 2);
        // Don't attach.
        var chars = effects.Compute(legend);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Ward");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature NewLegendaryCreature(string name, Player owner, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness,
            supertypes: new[] { CardSupertype.Legendary })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }

    private static void SeedLibrary(Player player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var card = new Creature($"Lib{i}", "{G}", 1, 1) { Owner = player };
            player.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
