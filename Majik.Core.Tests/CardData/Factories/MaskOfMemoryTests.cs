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
/// Unit tests for <see cref="MaskOfMemoryFactory"/>.
///
/// Card: Mask of Memory — Artifact — Equipment {2} (Legions).
///   "Whenever equipped creature deals combat damage to a player, you may
///    draw two cards. If you do, discard a card."
///   "Equip {1}"
///
/// Covers:
///   - Identity (loaded from JSON): {2}, Artifact, Equipment.
///   - Equip {1} activated ability (CR 702.6).
///   - Combat-damage-to-a-player trigger gating (CR 510 / 603.1): equipped
///     source + player target; NOT combat damage to a creature, NOT a
///     non-equipped source, NOT while unattached.
///   - Loot resolve: "draw two, then discard one" nets +1 card.
///   - Optional draw (CR 603.5): mayDraw=false skips draw AND discard.
///   - "If you do" reflexive discard: empty library (zero drawn) skips the
///     discard (no must-discard-having-drawn-nothing).
///   - Custom discard pick honoured.
/// </summary>
[Trait("Color", "C")]
public class MaskOfMemoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MaskOfMemory_Identity()
    {
        var c = MaskOfMemoryFactory.Create(_alice);

        c.Name.Should().Be("Mask of Memory");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Mask of Memory is an Equipment");
    }

    [Fact]
    public void MaskOfMemory_HasEquipOne()
    {
        var c = MaskOfMemoryFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(1, "Equip {1} is the printed cost");
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-player trigger gating (CR 510 / 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_GatesOnEquippedSourceAndPlayerTarget()
    {
        var bear = NewCreature("Bear", _alice);
        var other = NewCreature("Other", _alice);

        var mask = MaskOfMemoryFactory.Create(_alice);
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        var trigger = mask.Abilities.OfType<TriggeredAbility>().Single();

        // Equipped Bear deals combat damage to a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("equipped creature dealt combat damage to a player (CR 603.1)");

        // A different (unequipped) creature deals combat damage to a player → no.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("the trigger fires only for the equipped creature");

        // Equipped Bear deals combat damage to a CREATURE (not a player) → no.
        var dummy = new Creature("Dummy", "{1}{G}", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("printed text gates on combat damage 'to a player'");
    }

    [Fact]
    public void Trigger_DoesNotFireOnNonCombatDamage()
    {
        var bear = NewCreature("Bear", _alice);
        var mask = MaskOfMemoryFactory.Create(_alice);
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        var trigger = mask.Abilities.OfType<TriggeredAbility>().Single();

        // Plain (non-combat) damage to a player → no match: the printed text
        // is "combat damage", which binds CombatDamageDealtEvent only.
        trigger.IsTriggered(new DamageDealtEvent(bear, null, null, _bob, 2, DamageType.Ability))
            .Should().BeFalse("printed text is 'combat damage', not any damage");
    }

    [Fact]
    public void Inert_WhileUnattached()
    {
        var mask = MaskOfMemoryFactory.Create(_alice);
        PlaceOnBattlefield(mask, _alice);

        var bear = NewCreature("Bear", _alice);
        // Don't attach.

        var trigger = mask.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeFalse("an unequipped Mask grants no trigger");
    }

    // -----------------------------------------------------------------------
    // Loot resolve — "draw two, then discard one" (CR 121.1 / 701.16)
    // -----------------------------------------------------------------------

    [Fact]
    public void Loot_DrawsTwoThenDiscardsOne_NetsPlusOne()
    {
        var bear = NewCreature("Bear", _alice);
        var mask = MaskOfMemoryFactory.Create(_alice);
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        SeedLibrary(_alice, 5);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        Resolve(mask);

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "drew two, discarded one — net +1 card");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            "exactly one card was discarded");
        _alice.Zones.Library.GetCards().Should().HaveCount(3, "two cards left the library");
    }

    [Fact]
    public void Loot_OptionalDraw_SkippedWhenMayReturnsFalse()
    {
        var bear = NewCreature("Bear", _alice);
        var mask = MaskOfMemoryFactory.Create(_alice, triggers: null, mayDraw: () => false, discardPick: null);
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        SeedLibrary(_alice, 5);

        Resolve(mask);

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "declining the 'you may draw' choice skips the draw (CR 603.5)");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "no discard when no cards were drawn ('If you do')");
    }

    [Fact]
    public void Loot_EmptyLibrary_SkipsDiscard_IfYouDo()
    {
        var bear = NewCreature("Bear", _alice);
        var mask = MaskOfMemoryFactory.Create(_alice);
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        // Seed a SINGLE card in hand, EMPTY library. Drew zero → no discard.
        var inHand = new Creature("InHand", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        Resolve(mask);

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(inHand,
            "drew nothing (empty library) so 'If you do' suppresses the discard");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "no card was drawn, so the reflexive discard does not happen");
    }

    [Fact]
    public void Loot_CustomDiscardPickHonoured()
    {
        var bear = NewCreature("Bear", _alice);

        // Pre-seed a marked card in hand; the picker discards exactly that one.
        var marked = new Creature("Marked", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(marked);
        marked.SetZone(ZoneType.Hand);

        var mask = MaskOfMemoryFactory.Create(
            _alice,
            triggers: null,
            mayDraw: null,
            discardPick: hand => hand.FirstOrDefault(c => c.Name == "Marked"));
        PlaceOnBattlefield(mask, _alice);
        mask.AttachTo(bear);

        SeedLibrary(_alice, 5);

        Resolve(mask);

        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Marked", "the custom discard picker chose the marked card");
        _alice.Zones.Hand.GetCards().Should().NotContain(c => c.Name == "Marked");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Artifact mask)
    {
        var trigger = mask.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();
    }

    private Creature NewCreature(string name, Player owner)
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

    private static void PlaceOnBattlefield(Artifact mask, Player owner)
    {
        mask.SetOwner(owner);
        mask.SetController(owner);
        owner.Zones.Battlefield.AddCard(mask);
        mask.SetZone(ZoneType.Battlefield);
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
