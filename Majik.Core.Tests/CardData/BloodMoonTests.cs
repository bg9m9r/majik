using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Blood Moon — Enchantment {2}{R}, "Nonbasic lands
/// are Mountains." (CR 305.6 / 613.1d).
///
/// Validates the four-PR stack:
///   PR #150 — permanent-level layer system / characteristics aggregation.
///   PR #151 — <see cref="SetSubtypesEffect"/> Layer 4 subtype rewriting.
///   PR #155 — <see cref="EffectiveManaAbilities"/> CR 305.6 mana-ability
///             derivation.
///   PR #1xx (this one) — <see cref="BloodMoonFactory"/> +
///             <see cref="BloodMoonStaticEffect"/> ETB/LTB lifecycle that
///             registers/unregisters the Layer 4 effect on the live
///             <see cref="ContinuousEffectsService"/>.
///
/// The tests drive Blood Moon onto and off the battlefield through
/// <see cref="ZoneService.MoveCard"/> so the real CardMovedEvent path
/// exercises the static-effect lifecycle.
/// </summary>
public class BloodMoonTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public BloodMoonTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodMoon_IsEnchantmentNamedBloodMoon_AtCost2R()
    {
        var bm = BloodMoonFactory.Create(_alice);

        bm.Name.Should().Be("Blood Moon");
        bm.HasType(CardType.Enchantment).Should().BeTrue();
        bm.ManaCost.Should().Be("{2}{R}");
        bm.Owner.Should().BeSameAs(_alice);
        bm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodMoon()
    {
        var bm = NamedCardFactory.Create("Blood Moon", _alice);

        bm.Should().BeOfType<Enchantment>();
        bm.Name.Should().Be("Blood Moon");
        bm.ManaCost.Should().Be("{2}{R}");
    }

    // -----------------------------------------------------------------------
    // End-to-end: Blood Moon retypes nonbasic lands → tap for {R}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Bayou (nonbasic, printed Forest + Swamp, mana {G} and {B}). Without
    /// Blood Moon: taps for G or B. With Blood Moon on the battlefield:
    /// CR 305.6 strips the printed abilities and yields a Mountain mana
    /// ability ({R}).
    /// </summary>
    [Fact]
    public void NonbasicLand_UnderBloodMoon_TapsForRed()
    {
        // Bayou — nonbasic dual land with Forest+Swamp subtypes and
        // printed {T}: Add {G}, {T}: Add {B}. No Basic supertype.
        var bayou = new Land(
            "Bayou",
            supertypes: null,
            subtypes: new[] { CardSubtype.Forest, CardSubtype.Swamp });
        bayou.SetOwner(_alice);
        bayou.SetController(_alice);
        bayou.AddAbility(new ManaAbility(bayou, _alice, ManaCost.Parse("G")));
        bayou.AddAbility(new ManaAbility(bayou, _alice, ManaCost.Parse("B")));
        // Land onto the battlefield via the real ZoneService so the zone
        // change is published through the event bus.
        _zones.MoveCard(bayou, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Baseline: without Blood Moon, EffectiveManaAbilities returns the
        // printed {G} and {B}.
        var baseline = EffectiveManaAbilities.For(bayou, _effects, _alice);
        baseline.Should().HaveCount(2);
        baseline.Should().Contain(a => a.ManaGenerated.Green == 1);
        baseline.Should().Contain(a => a.ManaGenerated.Black == 1);

        // Bring Blood Moon onto the battlefield — fully wired so its
        // BloodMoonStaticEffect attaches to the bus and registers the
        // SetSubtypesEffect when Blood Moon's CardMovedEvent fires.
        var bloodMoon = BloodMoonFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(bloodMoon, ZoneType.Library, ZoneType.Battlefield, _alice);

        var underBloodMoon = EffectiveManaAbilities.For(bayou, _effects, _alice);

        underBloodMoon.Should().HaveCount(1, "CR 305.6 strips printed abilities and adds {R}");
        underBloodMoon[0].ManaGenerated.Red.Should().Be(1);
        underBloodMoon[0].ManaGenerated.Green.Should().Be(0);
        underBloodMoon[0].ManaGenerated.Black.Should().Be(0);
    }

    /// <summary>
    /// Forest (Basic supertype, printed {T}: Add {G}). Blood Moon's scope
    /// excludes basic lands, so Forest should continue to tap for {G}
    /// even with Blood Moon on the battlefield.
    /// </summary>
    [Fact]
    public void BasicLand_UnderBloodMoon_Unchanged()
    {
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(forest, _alice);
        _zones.MoveCard(forest, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bloodMoon = BloodMoonFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(bloodMoon, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(forest, _effects, _alice);

        abilities.Should().HaveCount(1, "basic supertype is out of Blood Moon's scope");
        abilities[0].ManaGenerated.Green.Should().Be(1);
        abilities[0].ManaGenerated.Red.Should().Be(0);
    }

    /// <summary>
    /// Lifecycle: when Blood Moon leaves the battlefield, the Layer 4
    /// effect is unregistered and previously-affected nonbasic lands
    /// recover their printed mana abilities (CR 613.1d effect no longer
    /// applies → subtypes revert to printed → CR 305.6 override no
    /// longer fires).
    /// </summary>
    [Fact]
    public void NonbasicLand_AfterBloodMoonLeaves_RegainsPrintedAbilities()
    {
        var bayou = new Land(
            "Bayou",
            supertypes: null,
            subtypes: new[] { CardSubtype.Forest, CardSubtype.Swamp });
        bayou.SetOwner(_alice);
        bayou.SetController(_alice);
        bayou.AddAbility(new ManaAbility(bayou, _alice, ManaCost.Parse("G")));
        bayou.AddAbility(new ManaAbility(bayou, _alice, ManaCost.Parse("B")));
        _zones.MoveCard(bayou, ZoneType.Library, ZoneType.Battlefield, _alice);

        var bloodMoon = BloodMoonFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(bloodMoon, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Sanity: Bayou is currently a Mountain.
        EffectiveManaAbilities.For(bayou, _effects, _alice)
            .Should().ContainSingle().Which.ManaGenerated.Red.Should().Be(1);

        // Send Blood Moon to the graveyard — the BloodMoonStaticEffect
        // lifecycle should unregister the Layer 4 effect on the
        // CardMovedEvent.
        _zones.MoveCard(bloodMoon, ZoneType.Battlefield, ZoneType.Graveyard);

        var restored = EffectiveManaAbilities.For(bayou, _effects, _alice);
        restored.Should().HaveCount(2, "Layer 4 effect dropped → printed abilities apply");
        restored.Should().Contain(a => a.ManaGenerated.Green == 1);
        restored.Should().Contain(a => a.ManaGenerated.Black == 1);
        restored.Should().NotContain(a => a.ManaGenerated.Red == 1);
    }
}
