using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FacelessHavenFactory"/> (Kaldheim snow manland).
/// Snow Land:
///   "{T}: Add {C}.
///    {S}{S}{S}: This land becomes a 4/3 creature with vigilance and all
///    creature types until end of turn. It's still a land."
///
/// Covers:
/// - Identity (Snow Land supertype, Land type, no subtypes, name,
///   owner/controller).
/// - JSON-backed {T}: Add {C} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({S}{S}{S}, instant speed) + Layer 4 / Layer 7b
///   continuous effects:
///     * Adds Creature type + all creature subtypes + Vigilance keyword on
///       Layer 4 (printed Land type stays).
///     * Records 4/3 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// </summary>
[Trait("Color", "C")]
public class FacelessHavenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FacelessHaven_Identity()
    {
        var land = FacelessHavenFactory.Create(_alice);

        land.Name.Should().Be("Faceless Haven");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Faceless Haven is a Snow Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Faceless Haven is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FacelessHaven_HasManaAndAnimateAbilities()
    {
        var land = FacelessHavenFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{S}{S}{S} animate ability is wired");
    }
    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void FacelessHaven_AnimateAbility_HasPrintedManaCostSSS()
    {
        var land = FacelessHavenFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({S}{S}{S})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void FacelessHaven_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = FacelessHavenFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Goblin,
            "all creature types granted — a sample tribe is present");
        chars.Keywords.Should().Contain("Vigilance",
            "Vigilance keyword marker added (CR 702.20)");
    }

    [Fact]
    public void FacelessHaven_AnimateEffect_AppliesTypeAllSubtypesAndVigilance()
    {
        var land = FacelessHavenFactory.Create(_alice);
        var effect = new FacelessHavenAnimateEffect(land);

        var chars = new PermanentCharacteristics();
        chars.Types.Add(CardType.Land); // printed
        effect.Apply(chars);

        chars.Types.Should().Contain(CardType.Creature, "creature type added");
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays — \"It's still a land\"");
        chars.Subtypes.Should().Contain(MutavaultAnimateEffect.EveryCreatureType,
            "all creature types the engine models are granted (CR 205.3m)");
        chars.Keywords.Should().Contain("Vigilance",
            "Vigilance keyword marker added (CR 702.20)");
        effect.ExpiresAtEndOfTurn.Should().BeTrue("animation lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void FacelessHaven_BecomesPTEffect_SetsBase4_3()
    {
        var land = FacelessHavenFactory.Create(_alice);
        var effect = new FacelessHavenBecomesPTEffect(land, 4, 3);

        effect.NewPower.Should().Be(4);
        effect.NewToughness.Should().Be(3);
        effect.Layer.Should().Be(Layer.PT_SetBase);
        effect.ExpiresAtEndOfTurn.Should().BeTrue();

        var chars = new CreatureCharacteristics();
        effect.Apply(chars);
        chars.Power.Should().Be(4);
        chars.Toughness.Should().Be(3);
    }
}
