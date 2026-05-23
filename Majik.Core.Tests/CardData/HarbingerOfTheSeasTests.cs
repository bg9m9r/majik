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
/// End-to-end test for Harbinger of the Seas — Creature — Wizard {1}{U}
/// 2/2, "Nonbasic lands are Islands." Same Layer 4 machinery as Blood
/// Moon, but with Island as the new land subtype (CR 305.6 / 613.1d).
/// Wired through the shared <see cref="RetypeLandsStaticEffect"/> binder.
/// </summary>
public class HarbingerOfTheSeasTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public HarbingerOfTheSeasTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void HarbingerOfTheSeas_IsWizardCreature_AtCost1U_WithPT2_2()
    {
        var harbinger = HarbingerOfTheSeasFactory.Create(_alice);

        harbinger.Name.Should().Be("Harbinger of the Seas");
        harbinger.HasType(CardType.Creature).Should().BeTrue();
        harbinger.ManaCost.Should().Be("{1}{U}");
        harbinger.BasePower.Should().Be(2);
        harbinger.BaseToughness.Should().Be(2);
        harbinger.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HarbingerOfTheSeas()
    {
        var harbinger = NamedCardFactory.Create("Harbinger of the Seas", _alice);

        harbinger.Should().BeOfType<Creature>();
        harbinger.Name.Should().Be("Harbinger of the Seas");
        harbinger.ManaCost.Should().Be("{1}{U}");
    }

    /// <summary>
    /// Headline lifecycle test: a Bayou under Harbinger of the Seas
    /// (nonbasic dual Forest+Swamp with printed {T}: Add {G}, {T}: Add {B})
    /// should lose its printed mana abilities (CR 305.6) and tap for {U}.
    /// </summary>
    [Fact]
    public void NonbasicLand_UnderHarbinger_TapsForBlue()
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

        var harbinger = HarbingerOfTheSeasFactory.Create(_alice, _effects, _bus);
        _zones.MoveCard(harbinger, ZoneType.Library, ZoneType.Battlefield, _alice);

        var abilities = EffectiveManaAbilities.For(bayou, _effects, _alice);

        abilities.Should().HaveCount(1, "CR 305.6 strips printed abilities and adds {U}");
        abilities[0].ManaGenerated.Blue.Should().Be(1);
        abilities[0].ManaGenerated.Green.Should().Be(0);
        abilities[0].ManaGenerated.Black.Should().Be(0);
    }
}
