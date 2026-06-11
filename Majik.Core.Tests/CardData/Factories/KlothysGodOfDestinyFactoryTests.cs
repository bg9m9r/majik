using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KlothysGodOfDestinyFactory"/> (Theros Beyond
/// Death, {1}{R}{G}).
///
/// Legendary Enchantment Creature — God 4/5. Oracle text (verified against
/// Scryfall):
///   "Indestructible
///    As long as your devotion to red and green is less than seven, Klothys
///    isn't a creature.
///    At the beginning of your first main phase, exile target card from a
///    graveyard. If it was a land card, add {R} or {G}. Otherwise, you gain
///    2 life and Klothys deals 2 damage to each opponent."
///
/// Covers:
///   - Identity / shape (Legendary Enchantment Creature — God, 4/5,
///     {1}{R}{G}).
///   - Indestructible keyword marker.
///   - Devotion-to-red-and-green compute (sum of {R} + {G} pips on the
///     controller's battlefield).
///   - Devotion-gated Layer-4 "isn't a creature" type-strip
///     (< 7 strips Creature; >= 7 surfaces it).
///   - First-main-phase triggered ability: exile target graveyard card,
///     land -> add {R}/{G}; nonland -> gain 2 life + 2 damage to each
///     opponent.
/// </summary>
[Trait("Color", "RG")]
public class KlothysGodOfDestinyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsLegendaryEnchantmentGodShape()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(_alice);

        klothys.Should().BeOfType<Creature>();
        klothys.Name.Should().Be("Klothys, God of Destiny");
        klothys.Power.Should().Be(4);
        klothys.Toughness.Should().Be(5);
        klothys.ManaCost.Should().Be("{1}{R}{G}");
        klothys.ManaCostValue.TotalValue.Should().Be(3);
        klothys.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        klothys.HasSubtype(CardSubtype.God).Should().BeTrue();
        klothys.HasType(CardType.Creature).Should().BeTrue();
        klothys.HasType(CardType.Enchantment).Should().BeTrue();
        klothys.Owner.Should().BeSameAs(_alice);
        klothys.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_AttachesIndestructible()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(_alice);

        klothys.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Indestructible");
    }

    // -------------------------------------------------------------------------
    // Devotion to red and green
    // -------------------------------------------------------------------------

    [Fact]
    public void Devotion_CountsRedAndGreenPips_OfControllersBattlefield()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(klothys);
        klothys.SetZone(ZoneType.Battlefield);

        // Klothys itself: {1}{R}{G} = one {R} + one {G} = 2 devotion.
        KlothysGodOfDestinyFactory.ComputeDevotionToRedAndGreen(_alice)
            .Should().Be(2);

        // Add a {R}{R}{G} permanent — three more pips.
        var perm = new Creature("Rolling Spoil", "{R}{R}{G}", 3, 3);
        perm.SetOwner(_alice);
        perm.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        KlothysGodOfDestinyFactory.ComputeDevotionToRedAndGreen(_alice)
            .Should().Be(5);
    }

    // -------------------------------------------------------------------------
    // Devotion-gated "isn't a creature" Layer-4 type strip
    // -------------------------------------------------------------------------

    [Fact]
    public void DevotionLessThanSeven_KlothysIsNotACreature()
    {
        var effects = new ContinuousEffectsService();
        var klothys = KlothysGodOfDestinyFactory.Create(_alice, triggers: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(klothys);
        klothys.SetZone(ZoneType.Battlefield);

        // Devotion = 2 (Klothys alone) < 7 — Creature stripped.
        effects.Compute(klothys).Types.Should().NotContain(CardType.Creature);
        effects.Compute(klothys).Types.Should().Contain(CardType.Enchantment);
    }

    [Fact]
    public void DevotionSevenOrMore_KlothysIsACreature()
    {
        var effects = new ContinuousEffectsService();
        var klothys = KlothysGodOfDestinyFactory.Create(_alice, triggers: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(klothys);
        klothys.SetZone(ZoneType.Battlefield);

        // Stack up red/green pips to hit devotion 7 (Klothys=2, need 5 more).
        var sink = new Creature("Devotion Sink", "{R}{R}{R}{G}{G}", 1, 1);
        sink.SetOwner(_alice);
        sink.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sink);
        sink.SetZone(ZoneType.Battlefield);

        KlothysGodOfDestinyFactory.ComputeDevotionToRedAndGreen(_alice)
            .Should().Be(7);
        effects.Compute(klothys).Types.Should().Contain(CardType.Creature,
            "devotion 7 >= 7 — Klothys is a creature");
    }

    // -------------------------------------------------------------------------
    // First-main-phase triggered ability
    // -------------------------------------------------------------------------

    [Fact]
    public void Trigger_IsBeginningOfFirstMainPhase_WithGraveyardTarget()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(_alice);
        var trigger = klothys.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Trigger_LandTarget_AddsRedOrGreenMana()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(_alice);
        var trigger = klothys.Abilities.OfType<TriggeredAbility>().Single();

        // A land card in Bob's graveyard.
        var land = new Land("Sacred Foundry");
        land.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);

        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { land } });

        var manaBefore = _alice.ManaPool.Total;
        foreach (var e in trigger.Effects) e.Execute();

        // The land is exiled and one R-or-G mana is added.
        land.Zone.Should().Be(ZoneType.Exile);
        (_alice.ManaPool.Red + _alice.ManaPool.Green).Should().Be(1);
        _alice.ManaPool.Total.Should().Be(manaBefore + 1);
    }

    [Fact]
    public void Trigger_NonlandTarget_GainsLifeAndDamagesEachOpponent()
    {
        var klothys = KlothysGodOfDestinyFactory.Create(
            _alice);
        var trigger = klothys.Abilities.OfType<TriggeredAbility>().Single();

        // A nonland (creature) card in Bob's graveyard.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { creature } });

        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        creature.Zone.Should().Be(ZoneType.Exile);
        _alice.LifeTotal.Should().Be(aliceLifeBefore + KlothysGodOfDestinyFactory.LifeGain);
        _bob.LifeTotal.Should().Be(bobLifeBefore - KlothysGodOfDestinyFactory.DamageToEachOpponent);
    }
}
