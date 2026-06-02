using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CuriosityFactory"/>.
///
/// Card: Curiosity — Enchantment — Aura {U} (Tempest).
///   "Enchant creature
///    Whenever enchanted creature deals damage to an opponent, you may
///    draw a card."
///
/// Covers:
///   - Identity / dispatch / Aura subtype (loaded from JSON).
///   - Granted draw-on-damage-to-an-opponent trigger (CR 603.1), gated on
///     the enchanted creature being the source AND the target being an
///     opponent player. Fires on ANY damage type — combat / spell / ability
///     (CR 119.1) — not just combat damage.
///   - Optional "you may draw" (CR 603.5): default draws; mayDraw=false skips.
///   - Inert while unattached.
/// </summary>
[Trait("Color", "U")]
public class CuriosityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Curiosity_Identity()
    {
        var c = CuriosityFactory.Create(_alice);

        c.Name.Should().Be("Curiosity");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Granted trigger — "Whenever enchanted creature deals damage to an
    // opponent, you may draw a card." (CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_GatesOnEnchantedSourceAndOpponentTarget()
    {
        var bear = NewCreature("Bear", _alice);
        var other = NewCreature("Other", _alice);

        var aura = CuriosityFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);
        aura.AttachTo(bear);

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();

        // Enchanted Bear damages an opponent (Bob) → matches.
        trigger.IsTriggered(DamageToPlayer(bear, _bob, DamageType.Combat))
            .Should().BeTrue("enchanted creature dealt damage to an opponent (CR 603.1)");

        // Non-combat (spell/ability) damage to an opponent also fires — the
        // printed text is "deals damage", not "combat damage" (CR 119.1).
        trigger.IsTriggered(DamageToPlayer(bear, _bob, DamageType.Ability))
            .Should().BeTrue("trigger fires on ANY damage type, not just combat");

        // A different (unenchanted) creature damages an opponent → no match.
        trigger.IsTriggered(DamageToPlayer(other, _bob, DamageType.Combat))
            .Should().BeFalse("the granted trigger fires only for the enchanted creature");

        // Enchanted Bear damages a creature (not a player) → no match.
        var dummy = new Creature("Dummy", "{1}{G}", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new DamageDealtEvent(bear, null, dummy, null, 2, DamageType.Combat))
            .Should().BeFalse("printed text gates on damage 'to an opponent' (a player)");

        // Enchanted Bear damages its OWN controller (Alice) → no match —
        // Alice is not an opponent (CR 109.1).
        trigger.IsTriggered(DamageToPlayer(bear, _alice, DamageType.Combat))
            .Should().BeFalse("the controller is not an opponent");
    }

    [Fact]
    public void Trigger_DrawsForEnchantedCreaturesController_ByDefault()
    {
        var bear = NewCreature("Bear", _alice);
        var aura = CuriosityFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);
        aura.AttachTo(bear);

        SeedLibrary(_alice, 3);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "the optional draw defaults to drawing for the enchanted creature's controller");
    }

    [Fact]
    public void Trigger_OptionalDraw_SkippedWhenMayReturnsFalse()
    {
        var bear = NewCreature("Bear", _alice);
        var aura = CuriosityFactory.Create(_alice, mayDraw: () => false);
        PlaceOnBattlefield(aura, _alice);
        aura.AttachTo(bear);

        SeedLibrary(_alice, 3);

        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the 'you may' choice declined the draw (CR 603.5)");
    }

    [Fact]
    public void Inert_WhileUnattached()
    {
        var aura = CuriosityFactory.Create(_alice);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreature("Bear", _alice);
        // Don't attach.
        var trigger = aura.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(DamageToPlayer(bear, _bob, DamageType.Combat))
            .Should().BeFalse("an unattached aura grants no trigger");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static DamageDealtEvent DamageToPlayer(ICard source, Player target, DamageType type) =>
        new(source, null, null, target, 2, type);

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
