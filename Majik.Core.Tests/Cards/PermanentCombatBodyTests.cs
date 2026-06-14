using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// CR 613.1c / 704.5f — the <see cref="Permanent"/>-level combat body surface
/// (effective P/T + marked-damage + lethal-damage) that an animated NON-creature
/// C# instance (a manland: a <see cref="Land"/> computing as a creature via the
/// Layer-4 type grant) consults — the foundational sub-primitive for declaring
/// an animated non-creature as a live combatant
/// (deferral <c>animated-noncreature-as-combatant</c>, 4B).
///
/// <para>The combat damage / lethal-damage model historically lived only on
/// <see cref="Creature"/> (<c>Power</c>/<c>Toughness</c>/<c>_damage</c>), so an
/// animated <see cref="Land"/> — never a <see cref="Creature"/> C# instance —
/// had no P/T and no damage surface even though
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> already upgrades its
/// working row to a <see cref="CreatureCharacteristics"/>. This suite lifts that
/// surface to <see cref="Permanent"/> and pins single-source-of-truth parity for
/// a real <see cref="Creature"/> (which overrides every member to read its own
/// authoritative fields).</para>
/// </summary>
public class PermanentCombatBodyTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land AnimatedColonnade(ContinuousEffectsService effects)
    {
        // Celestial Colonnade — "{3}{W}{U}: … becomes a 4/4 white and blue
        // Elemental creature with flying and vigilance until end of turn …".
        var land = CelestialColonnadeFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(Majik.Core.Abilities.ActivatedAbility))
            .Cast<Majik.Core.Abilities.ActivatedAbility>()
            .Single();
        animate.Resolve();
        return land;
    }

    // -----------------------------------------------------------------------
    // Effective P/T on an animated Land (CR 613.1c / 613.7b)
    // -----------------------------------------------------------------------

    [Fact]
    public void AnimatedLand_EffectivePowerToughness_SurfacesThroughPermanent()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects);

        land.IsEffectivelyCreature().Should().BeTrue("Layer-4 grant added Creature");
        land.GetEffectivePower().Should().Be(4);
        land.GetEffectiveToughness().Should().Be(4);
    }

    [Fact]
    public void NonAnimatedLand_HasZeroEffectivePT_AndIsNotCreature()
    {
        var effects = new ContinuousEffectsService();
        var land = CelestialColonnadeFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;

        land.IsEffectivelyCreature().Should().BeFalse();
        land.GetEffectivePower().Should().Be(0);
        land.GetEffectiveToughness().Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Marked-damage + lethal-damage on an animated Land (CR 119 / 704.5f)
    // -----------------------------------------------------------------------

    [Fact]
    public void AnimatedLand_MarkDamage_AccumulatesAndReportsLethal()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects);

        land.MarkedDamage.Should().Be(0);
        land.HasLethalMarkedDamage().Should().BeFalse();

        land.MarkDamage(3);
        land.MarkedDamage.Should().Be(3);
        land.HasLethalMarkedDamage().Should().BeFalse("3 < 4 toughness");
        land.WasDealtDamageThisTurn.Should().BeTrue("CR 120.3 stamp fires on the Permanent seam");

        land.MarkDamage(1);
        land.MarkedDamage.Should().Be(4);
        land.HasLethalMarkedDamage().Should().BeTrue("4 >= 4 toughness");
    }

    [Fact]
    public void AnimatedLand_DeathtouchFlag_ReportsLethal_EvenBelowToughness()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects);

        land.MarkDamage(1);
        land.MarkedForDestructionByDeathtouch = true;
        land.HasLethalMarkedDamage().Should().BeTrue("CR 702.2b — any deathtouch damage is lethal");
    }

    [Fact]
    public void AnimatedLand_ClearMarkedDamage_Resets()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects);

        land.MarkDamage(4);
        land.MarkedForDestructionByDeathtouch = true;
        land.ClearMarkedDamage();

        land.MarkedDamage.Should().Be(0);
        land.MarkedForDestructionByDeathtouch.Should().BeFalse();
        land.HasLethalMarkedDamage().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Single-source-of-truth parity for a real Creature (overrides delegate
    // to the authoritative Creature fields — mirrors Planeswalker.Loyalty).
    // -----------------------------------------------------------------------

    [Fact]
    public void Creature_PermanentSurface_DelegatesToAuthoritativeFields()
    {
        var effects = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = effects;

        bear.GetEffectivePower().Should().Be(bear.Power).And.Be(2);
        bear.GetEffectiveToughness().Should().Be(bear.Toughness).And.Be(2);

        bear.MarkDamage(2);
        // The Permanent-level marked-damage reads through the Creature's own
        // authoritative Damage field — they are the same value, not two stores.
        bear.MarkedDamage.Should().Be(bear.Damage).And.Be(2);
        bear.HasLethalMarkedDamage().Should().BeTrue();
        bear.IsDead().Should().BeTrue();

        bear.ClearMarkedDamage();
        bear.Damage.Should().Be(0);
        bear.MarkedDamage.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Creature-death SBA (CR 704.5f / 711) now reaches an animated manland.
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureDeathSba_DestroysAnimatedLand_WithLethalMarkedDamage()
    {
        var eventBus = new Mock<IEventBus>();
        var zoneService = new ZoneService(eventBus.Object);
        var sba = new StateBasedActions(eventBus.Object, zoneService);

        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects); // 4/4 animated body

        // Below-lethal: the SBA leaves the animated land on the battlefield.
        land.MarkDamage(3);
        sba.CheckStateBasedActions(new List<Player> { _alice }, new List<ICard> { land });
        land.Zone.Should().Be(ZoneType.Battlefield, "3 < 4 toughness — not lethal");

        // Lethal: the SBA destroys it even though it's a Land instance, never a
        // Creature instance (CR 704.5f reached via the lifted Permanent surface).
        land.MarkDamage(1);
        land.HasLethalMarkedDamage().Should().BeTrue();
        sba.CheckStateBasedActions(new List<Player> { _alice }, new List<ICard> { land });
        land.Zone.Should().Be(ZoneType.Graveyard, "4 >= 4 toughness — lethal, dies as a creature");
    }

    [Fact]
    public void CreatureDeathSba_LeavesNonAnimatedLandAlone()
    {
        var eventBus = new Mock<IEventBus>();
        var zoneService = new ZoneService(eventBus.Object);
        var sba = new StateBasedActions(eventBus.Object, zoneService);

        var effects = new ContinuousEffectsService();
        var land = CelestialColonnadeFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;

        // A plain (non-animated) land is not effectively a creature, so the
        // 0-effective-toughness branch must NOT touch it.
        sba.CheckStateBasedActions(new List<Player> { _alice }, new List<ICard> { land });
        land.Zone.Should().Be(ZoneType.Battlefield);
    }
}
