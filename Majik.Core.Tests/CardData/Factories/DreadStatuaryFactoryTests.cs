using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="DreadStatuaryFactory"/> (Magic 2010 colorless manland).
/// Land:
///   "{T}: Add {C}.
///    {4}: This land becomes a 4/2 Golem artifact creature until end of
///    turn. It's still a land."
///   (Oracle verified against Scryfall 2026-06-02.)
///
/// Same colorless artifact-creature manland animate shape as
/// <see cref="MishrasFactoryFactory"/>: a {T}: Add {C} mana ability (from
/// the embedded JSON definition) and a {4}: animate-until-EOT activated
/// ability registering <see cref="ManlandCycleAnimateEffect"/> (Layer 4 —
/// Creature + Artifact + Golem) + <see cref="ManlandCycleBecomesPTEffect"/>
/// (Layer 7b — 4/2). Distinct from Mishra's Factory: animates to a 4/2
/// Golem (not a 2/2 Assembly-Worker), costs {4} (not {1}), and has no
/// printed pump ability.
/// </summary>
[Trait("Color", "C")]
public class DreadStatuaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DreadStatuary_Identity()
    {
        var land = DreadStatuaryFactory.Create(_alice);

        land.Name.Should().Be("Dread Statuary");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land until activated");
        land.HasType(CardType.Artifact).Should().BeFalse(
            "printed shape is not an artifact until activated");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Dread Statuary is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void DreadStatuary_TapForC_ProducesColorless()
    {
        var land = DreadStatuaryFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Generic.Should().Be(1);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Animate ability ({4})
    // -----------------------------------------------------------------------

    [Fact]
    public void DreadStatuary_AnimateAbility_HasPrintedManaCost4()
    {
        var land = DreadStatuaryFactory.Create(_alice);

        var animate = AnimateOf(land);
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({4})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void DreadStatuary_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = DreadStatuaryFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Types.Should().Contain(CardType.Artifact,
            "Layer 4 adds Artifact — animated body is an artifact creature");
        chars.Subtypes.Should().Contain(CardSubtype.Golem,
            "Golem subtype added");
    }

    [Fact]
    public void DreadStatuary_Animate_RegistersLayer4AndLayer7b_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = DreadStatuaryFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Subtypes.Should().Contain(CardSubtype.Golem);
        animateEffect.ExtraTypes.Should().Contain(CardType.Artifact);
        animateEffect.Keywords.Should().BeEmpty(
            "Dread Statuary's animated body has no keyword abilities");

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 4 && e.NewToughness == 2);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void DreadStatuary_Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = DreadStatuaryFactory.Create(_alice, effects);
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
        chars.Types.Should().NotContain(CardType.Artifact);
        chars.Subtypes.Should().NotContain(CardSubtype.Golem);
    }

    [Fact]
    public void DreadStatuary_Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = DreadStatuaryFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var resolve = () => AnimateOf(land).Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasType(CardType.Artifact).Should().BeFalse();
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
