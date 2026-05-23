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
/// End-to-end test for Magus of the Moon — Creature — Human Wizard {2}{R}
/// 2/2, "Nonbasic lands are Mountains." Same Layer 4 type-changing effect
/// as Blood Moon (CR 305.6 / 613.1d), wired through the shared
/// <see cref="RetypeLandsStaticEffect"/> binder.
/// </summary>
public class MagusOfTheMoonTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public MagusOfTheMoonTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void MagusOfTheMoon_IsHumanWizardCreature_AtCost2R_WithPT2_2()
    {
        var magus = MagusOfTheMoonFactory.Create(_alice);

        magus.Name.Should().Be("Magus of the Moon");
        magus.HasType(CardType.Creature).Should().BeTrue();
        magus.ManaCost.Should().Be("{2}{R}");
        magus.BasePower.Should().Be(2);
        magus.BaseToughness.Should().Be(2);
        magus.HasSubtype(CardSubtype.Human).Should().BeTrue();
        magus.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MagusOfTheMoon()
    {
        var magus = NamedCardFactory.Create("Magus of the Moon", _alice);

        magus.Should().BeOfType<Creature>();
        magus.Name.Should().Be("Magus of the Moon");
        magus.ManaCost.Should().Be("{2}{R}");
    }

    /// <summary>
    /// Headline lifecycle test: a Bayou under Magus of the Moon (nonbasic
    /// dual Forest+Swamp with printed {T}: Add {G}, {T}: Add {B}) should
    /// lose its printed mana abilities (CR 305.6) and tap for {R}.
    /// </summary>
    [Fact]
    public void NonbasicLand_UnderMagus_TapsForRed()
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

        var magus = MagusOfTheMoonFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(magus, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(bayou, _effects, _alice);

        abilities.Should().HaveCount(1, "CR 305.6 strips printed abilities and adds {R}");
        abilities[0].ManaGenerated.Red.Should().Be(1);
        abilities[0].ManaGenerated.Green.Should().Be(0);
        abilities[0].ManaGenerated.Black.Should().Be(0);
    }
}
