using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GhituEncampmentFactory"/> (Urza's Saga creature-land).
/// Land:
///   "This land enters tapped.
///    {T}: Add {R}.
///    {1}{R}: This land becomes a 2/1 red Warrior creature with first
///    strike until end of turn. It's still a land."
///   (Oracle verified against Scryfall 2026-05-29.)
///
/// Same manland animate shape as <see cref="NeedleSpiresFactory"/> /
/// <see cref="RestlessSpireFactory"/>: unconditional ETB-tapped, a {T}: Add
/// {R} mana ability (from the embedded JSON definition), and a {1}{R}:
/// animate-until-EOT activated ability registering
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — Creature + Warrior +
/// First Strike) + <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — 2/1).
///
/// Distinct from the dual-land cycle: mono-red (one mana ability), animates
/// to a Warrior (not Elemental), and has no printed attack trigger.
/// </summary>
public class GhituEncampmentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituEncampment_Identity()
    {
        var land = GhituEncampmentFactory.Create(_alice);

        land.Name.Should().Be("Ghitu Encampment");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land until activated");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Ghitu Encampment is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GhituEncampment()
    {
        var card = NamedCardFactory.Create("Ghitu Encampment", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Ghitu Encampment");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {R}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "the {1}{R} animate ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituEncampment_TapForR_ProducesRed()
    {
        var land = GhituEncampmentFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().Single();

        red.CanActivate().Should().BeTrue();
        var produced = red.Activate();

        produced.Red.Should().Be(1);
        produced.Green.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Animate ability ({1}{R})
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituEncampment_AnimateAbility_HasPrintedManaCost1R()
    {
        var land = GhituEncampmentFactory.Create(_alice);

        var animate = AnimateOf(land);
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{R})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void GhituEncampment_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = GhituEncampmentFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Warrior,
            "Warrior subtype added");
        chars.Keywords.Should().Contain("First Strike",
            "the animated body has first strike");
    }

    [Fact]
    public void GhituEncampment_Animate_RegistersLayer4AndLayer7b_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = GhituEncampmentFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Subtypes.Should().Contain(CardSubtype.Warrior);
        animateEffect.Keywords.Should().Contain("First Strike");

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 2 && e.NewToughness == 1);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void GhituEncampment_Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = GhituEncampmentFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land))
            .Should().BeEmpty();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Warrior);
    }

    [Fact]
    public void GhituEncampment_Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = GhituEncampmentFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var resolve = () => AnimateOf(land).Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB-tapped (unconditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void GhituEncampment_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = GhituEncampmentFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Ghitu Encampment always enters tapped (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ActivatedAbility AnimateOf(Land land) =>
        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

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
