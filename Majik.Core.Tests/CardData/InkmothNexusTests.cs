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
/// Tests for <see cref="InkmothNexusFactory"/> — Land with
/// {T}: Add {C} and {1}: until EOT becomes a 1/1 Phyrexian Insect
/// artifact creature with flying + infect (still a land).
///
/// Covers:
/// - Card identity (Land, no supertype, name).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability (taps, produces colorless).
/// - {1}: animate registers an <see cref="InkmothAnimateLandEffect"/>:
///     * Layer 4 — adds Artifact + Creature types (printed Land stays).
///     * Phyrexian + Insect subtypes added.
///     * Flying + Infect keyword markers added.
///     * 1/1 P/T recorded on the effect for inspection.
///     * ExpiresAtEndOfTurn — <see cref="ContinuousEffectsService.ExpireEndOfTurn"/>
///       drops the effect, reverting the land to its printed shape.
/// </summary>
public class InkmothNexusTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void InkmothNexus_IsPlainLand_WithCorrectIdentity()
    {
        var land = InkmothNexusFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
        land.HasType(CardType.Artifact).Should().BeFalse("printed shape is plain Land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Inkmoth Nexus is a nonbasic land");
        land.Name.Should().Be("Inkmoth Nexus");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_InkmothNexus()
    {
        var card = NamedCardFactory.Create("Inkmoth Nexus", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Inkmoth Nexus");
        card.HasType(CardType.Land).Should().BeTrue();
        // Mana ability + animate ActivatedAbility both attached on the
        // dispatcher path (single-arg factory call).
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void InkmothNexus_TapForColorless_TapsLandAndProducesOneGeneric()
    {
        var land = InkmothNexusFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} is bucketed as +1 generic in ValueObjects.ManaCost today
        // (see the parser comment at ManaCost.Parse). No coloured pips.
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}: animate — Layer 4 type/subtype/keyword grant + 1/1 shim P/T.
    // -----------------------------------------------------------------------

    [Fact]
    public void Animate_RegistersLayer4Effect_GrantingArtifactCreaturePhyrexianInsectFlyingInfect()
    {
        var effects = new ContinuousEffectsService();
        var land = InkmothNexusFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Resolve();

        // The Layer 4 effect is registered.
        var registered = GetRegisteredEffects(effects)
            .OfType<InkmothAnimateLandEffect>()
            .SingleOrDefault();
        registered.Should().NotBeNull("the animate resolution registers the layer effect");
        registered!.Target.Should().BeSameAs(land);
        registered.Layer.Should().Be(Layer.Type);
        registered.ExpiresAtEndOfTurn.Should().BeTrue();
        registered.NewPower.Should().Be(1);
        registered.NewToughness.Should().Be(1);

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Artifact + Creature are added, subtypes carry Phyrexian + Insect,
        // and the Flying / Infect keyword markers are present.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Phyrexian);
        chars.Subtypes.Should().Contain(CardSubtype.Insect);
        chars.Keywords.Should().Contain("Flying");
        chars.Keywords.Should().Contain("Infect");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = InkmothNexusFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Resolve();

        GetRegisteredEffects(effects).OfType<InkmothAnimateLandEffect>()
            .Should().HaveCount(1);

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<InkmothAnimateLandEffect>()
            .Should().BeEmpty();

        // Printed shape is back: no Artifact / Creature in the effective
        // type set, no added subtypes, no Flying / Infect markers.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Types.Should().NotContain(CardType.Artifact);
        chars.Subtypes.Should().NotContain(CardSubtype.Phyrexian);
        chars.Subtypes.Should().NotContain(CardSubtype.Insect);
        chars.Keywords.Should().NotContain("Flying");
        chars.Keywords.Should().NotContain("Infect");
    }

    [Fact]
    public void Animate_NoEffectsService_NoOpRegisters_AndAbilityCostShapeIsCorrect()
    {
        // Single-arg dispatcher path — no ContinuousEffectsService wired.
        // The {1} cost is still attached to the animate ability so the
        // dispatch shape (costs / mana ability) is complete; the effect
        // body short-circuits because no service is available to register
        // against (legal — the deferred-wiring contract documented on
        // InkmothNexusFactory.Create(owner)).
        var land = InkmothNexusFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        animate.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(1);

        // Resolving without an effects service must not throw and must
        // leave the land's printed shape untouched.
        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();

        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasType(CardType.Artifact).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        // ContinuousEffectsService keeps its effects list private; reflect
        // into _effects (mirrors the helper used by KarnTheGreatCreatorTests).
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
