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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HallOfStormGiantsFactory"/> (Strixhaven: Mystical
/// Archive / "creature land" cycle). Land:
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {U}.
///    {5}{U}: Until end of turn, this land becomes a 7/7 blue Giant creature
///    with ward {3}. It's still a land."
///
/// Same shape as <see cref="HiveOfTheEyeTyrantFactory"/> (the AFR
/// conditional-ETB creature-land analogue): conditional ETB-tapped on
/// "two or more other lands", a single-colour mana ability, and a
/// {cost}: animate-until-EOT activated ability. Hall of Storm Giants has
/// no per-instance attack trigger; the animated body's only granted
/// keyword is Ward {3}, recorded as a keyword marker on the Layer-4
/// animate effect (CR 702.21 — same posture as the Menace marker on Hive
/// of the Eye Tyrant and the keyword grants across the manland cycle).
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mana ability ({T}: Add {U}) + animate ability ({5}{U}).
/// - Animate registers a <see cref="ManlandCycleAnimateEffect"/> +
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature type + Giant subtype + Ward keyword on Layer 4.
///     * Records 7/7 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - Conditional ETB-tapped ("two or more other lands").
/// </summary>
public class HallOfStormGiantsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HallOfStormGiants_Identity()
    {
        var land = HallOfStormGiantsFactory.Create(_alice);

        land.Name.Should().Be("Hall of Storm Giants");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hall of Storm Giants is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HallOfStormGiants()
    {
        var card = NamedCardFactory.Create("Hall of Storm Giants", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hall of Storm Giants");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {U} mana ability is wired");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "{5}{U} animate ability is wired");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void HallOfStormGiants_TapForU_ProducesBlue()
    {
        var land = HallOfStormGiantsFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Blue.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Animate ability ({5}{U})
    // -----------------------------------------------------------------------

    [Fact]
    public void HallOfStormGiants_AnimateAbility_HasPrintedManaCost5U()
    {
        var land = HallOfStormGiantsFactory.Create(_alice);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({5}{U})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void HallOfStormGiants_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = HallOfStormGiantsFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Giant,
            "Giant subtype added");
        chars.Keywords.Should().Contain("Ward",
            "Ward keyword marker added (CR 702.21)");
    }

    [Fact]
    public void HallOfStormGiants_Animate_RegistersLayer4AndLayer7b_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = HallOfStormGiantsFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Keywords.Should().BeEquivalentTo(new[] { "Ward" });
        animateEffect.Subtypes.Should().Contain(CardSubtype.Giant);

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 7 && e.NewToughness == 7);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void HallOfStormGiants_Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = HallOfStormGiantsFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land))
            .Should().BeEmpty();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Giant);
        chars.Keywords.Should().NotContain("Ward");
    }

    [Fact]
    public void HallOfStormGiants_Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = HallOfStormGiantsFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void HallOfStormGiants_RegistersConditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = HallOfStormGiantsFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        // Zero other lands → enters untapped.
        var afterEmpty = bus.Apply(intent);
        afterEmpty.Should().NotBeNull();
        afterEmpty!.EntersTapped.Should().BeFalse(
            "with 0 other lands, Hall of Storm Giants enters untapped");

        // Two other lands present (excluding Hall) → enters tapped.
        var land1 = NamedCardFactory.Create("Island", _alice);
        var land2 = NamedCardFactory.Create("Forest", _alice);
        _alice.Zones.Battlefield.AddCard(land1);
        land1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield);

        var afterTwoOthers = bus.Apply(intent);
        afterTwoOthers.Should().NotBeNull();
        afterTwoOthers!.EntersTapped.Should().BeTrue(
            "with 2 other lands, the clause flips it tapped (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
