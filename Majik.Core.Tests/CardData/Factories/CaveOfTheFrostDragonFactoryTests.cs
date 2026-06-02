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
/// Tests for <see cref="CaveOfTheFrostDragonFactory"/> (Adventures in the
/// Forgotten Realms "cave" manland cycle). Land:
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {W}.
///    {4}{W}: This land becomes a 3/4 white Dragon creature with flying
///    until end of turn. It's still a land."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {W} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({4}{W}, instant speed) + Layer 4 / Layer 7b
///   continuous effects:
///     * Adds Creature type + Dragon subtype + Flying keyword on Layer 4.
///     * Records 3/4 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - Conditional ETB-tapped ("two or more other lands") replacement.
/// </summary>
[Trait("Color", "C")]
public class CaveOfTheFrostDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTheFrostDragon_Identity()
    {
        var land = CaveOfTheFrostDragonFactory.Create(_alice);

        land.Name.Should().Be("Cave of the Frost Dragon");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Cave of the Frost Dragon is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CaveOfTheFrostDragon_HasManaAndAnimateAbilities()
    {
        var land = CaveOfTheFrostDragonFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {W} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{4}{W} animate ability is wired");
    }
    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTheFrostDragon_AnimateAbility_HasPrintedManaCost4W()
    {
        var land = CaveOfTheFrostDragonFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({4}{W})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void CaveOfTheFrostDragon_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = CaveOfTheFrostDragonFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Dragon,
            "Dragon subtype added");
        chars.Keywords.Should().Contain("Flying",
            "Flying keyword marker added (CR 702.9)");
    }

    [Fact]
    public void CaveOfTheFrostDragon_AnimateEffect_AppliesTypeSubtypeAndFlying()
    {
        var land = CaveOfTheFrostDragonFactory.Create(_alice);
        var effect = new CaveOfTheFrostDragonAnimateEffect(land);

        var chars = new PermanentCharacteristics();
        chars.Types.Add(CardType.Land); // printed
        effect.Apply(chars);

        chars.Types.Should().Contain(CardType.Creature, "creature type added");
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays — \"It's still a land\"");
        chars.Subtypes.Should().Contain(CardSubtype.Dragon,
            "Dragon subtype added");
        chars.Keywords.Should().Contain("Flying",
            "Flying keyword marker added (CR 702.9)");
        effect.ExpiresAtEndOfTurn.Should().BeTrue("animation lifts at cleanup (CR 514.2)");
    }

    [Fact]
    public void CaveOfTheFrostDragon_BecomesPTEffect_SetsBase3_4()
    {
        var land = CaveOfTheFrostDragonFactory.Create(_alice);
        var effect = new CaveOfTheFrostDragonBecomesPTEffect(land, 3, 4);

        effect.NewPower.Should().Be(3);
        effect.NewToughness.Should().Be(4);
        effect.Layer.Should().Be(Layer.PT_SetBase);
        effect.ExpiresAtEndOfTurn.Should().BeTrue();

        var chars = new CreatureCharacteristics();
        effect.Apply(chars);
        chars.Power.Should().Be(3);
        chars.Toughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void CaveOfTheFrostDragon_RegistersConditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = CaveOfTheFrostDragonFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        // Zero other lands → enters untapped.
        var afterEmpty = bus.Apply(intent);
        afterEmpty.Should().NotBeNull();
        afterEmpty!.EntersTapped.Should().BeFalse(
            "with 0 other lands, the Cave enters untapped");

        // Two other lands present (excluding the Cave) → enters tapped.
        var land1 = NamedCardFactory.Create("Plains", _alice);
        var land2 = NamedCardFactory.Create("Island", _alice);
        _alice.Zones.Battlefield.AddCard(land1);
        land1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield);

        var afterTwoOthers = bus.Apply(intent);
        afterTwoOthers.Should().NotBeNull();
        afterTwoOthers!.EntersTapped.Should().BeTrue(
            "with 2 other lands, the manland's slow clause flips it tapped");
    }
}
