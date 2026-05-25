using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CuriosityFactory"/>.
///
/// Card: Curiosity — Enchantment — Aura {U} (Odyssey).
///   "Enchant creature"
///   "Whenever enchanted creature deals damage to a player, you may
///    draw a card."
///
/// Covers:
///   - Identity / dispatch.
///   - Aura subtype.
///   - Damage trigger bound to DamageDealtEvent (parent — not just
///     combat damage), gating on enchanted creature + player target.
///   - Trigger does NOT fire when damage hits a creature instead of
///     a player, or when the source is a different creature.
///   - Trigger fires for combat AND non-combat damage (subclass
///     CombatDamageDealtEvent inherits from DamageDealtEvent).
///   - On resolution, controller draws one card.
///   - Build-spell-definition emits a creature-only target predicate.
/// </summary>
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
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Curiosity()
    {
        var card = NamedCardFactory.Create("Curiosity", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Curiosity");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "damage-to-a-player trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Trigger condition — gates on enchanted creature + player target
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_GatesOnEnchantedCreatureAndPlayerTarget()
    {
        var bear = NewCreature("Bear", _alice);
        var other = NewCreature("Other", _alice);

        var curiosity = CuriosityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Battlefield);
        curiosity.AttachTo(bear);

        var trigger = curiosity.Abilities.OfType<TriggeredAbility>().Single();

        // Enchanted Bear damages a player → matches.
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeTrue("enchanted creature dealt damage to a player");

        // A different (un-enchanted) creature damages a player → does not match.
        trigger.IsTriggered(new CombatDamageDealtEvent(other, _bob, 2))
            .Should().BeFalse("trigger fires only for the enchanted creature");

        // Enchanted Bear damages a creature (not a player) → does not match.
        var dummy = new Creature("Dummy", "{1}{G}", 1, 1) { Owner = _bob, Controller = _bob };
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, dummy, 2))
            .Should().BeFalse("printed text gates on 'to a player'");
    }

    [Fact]
    public void Trigger_FiresOnNonCombatDamage()
    {
        // Curiosity binds to DamageDealtEvent (parent), so a non-combat
        // damage event from the enchanted creature should still fire it.
        // (e.g. an activated ability ping — DamageType.Ability.)
        var bear = NewCreature("Bear", _alice);

        var curiosity = CuriosityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Battlefield);
        curiosity.AttachTo(bear);

        var trigger = curiosity.Abilities.OfType<TriggeredAbility>().Single();

        var abilityDamage = new DamageDealtEvent(
            sourceCard: bear,
            sourcePlayer: null,
            targetCard: null,
            targetPlayer: _bob,
            amount: 1,
            damageType: DamageType.Ability);

        trigger.IsTriggered(abilityDamage).Should().BeTrue(
            "Curiosity reads 'deals damage to a player' — any damage type counts");
    }

    [Fact]
    public void Trigger_DoesNotFire_WhenUnattached()
    {
        var bear = NewCreature("Bear", _alice);

        var curiosity = CuriosityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Battlefield);

        // Don't attach.
        var trigger = curiosity.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2))
            .Should().BeFalse("no enchanted creature → trigger condition is false");
    }

    // -----------------------------------------------------------------------
    // Effect resolution — draws one card
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_Resolution_DrawsOneCard()
    {
        var bear = NewCreature("Bear", _alice);

        var curiosity = CuriosityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Battlefield);
        curiosity.AttachTo(bear);

        // Seed library so the draw resolves.
        var top = new Creature("Top", "{1}{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var trigger = curiosity.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "Curiosity draws one card on resolution");
        _alice.Zones.Hand.GetCards().Single().Should().BeSameAs(top,
            "top card was drawn");
    }

    [Fact]
    public void Trigger_Resolution_EmptyLibrary_MarksLossCondition()
    {
        var bear = NewCreature("Bear", _alice);

        var curiosity = CuriosityFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(curiosity);
        curiosity.SetZone(ZoneType.Battlefield);
        curiosity.AttachTo(bear);

        // Library is empty.
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        var trigger = curiosity.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the SBA loss condition (CR 704.5b)");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Spell definition — target predicate filters to creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersCreaturesOnly()
    {
        var curiosity = CuriosityFactory.Create(_alice);

        var bear = NewCreature("Bear", _alice);
        var land = new Land("Plains");
        land.SetOwner(_alice);
        land.SetController(_alice);

        var battlefield = new Permanent[] { bear, land };
        var def = CuriosityFactory.BuildSpellDefinition(curiosity, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
