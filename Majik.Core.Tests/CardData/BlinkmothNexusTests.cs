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
/// Tests for <see cref="BlinkmothNexusFactory"/> — Land (Mirrodin) with
/// three abilities:
///   {T}: Add {C}.
///   {1}: This land becomes a 1/1 Blinkmoth artifact creature with flying
///        until end of turn. It's still a land.
///   {1}, {T}: Target Blinkmoth creature gets +1/+1 until end of turn.
///
/// Near-twin of <see cref="InkmothNexusFactory"/> (colorless mana + {1}
/// animate to a 1/1 flying artifact creature, still a land), but:
///   - animates to a <see cref="CardSubtype.Blinkmoth"/> body (no infect),
///   - adds a third <b>{1}, {T}: target Blinkmoth creature gets +1/+1 EOT</b>
///     activated ability, modelled with the Shadowspear target-creature
///     pattern + the shared <see cref="PumpUntilEndOfTurnEffect"/> primitive.
/// </summary>
public class BlinkmothNexusTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BlinkmothNexus_IsPlainLand_WithCorrectIdentity()
    {
        var land = BlinkmothNexusFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse("printed shape is plain Land");
        land.HasType(CardType.Artifact).Should().BeFalse("printed shape is plain Land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Blinkmoth Nexus is a nonbasic land");
        land.Name.Should().Be("Blinkmoth Nexus");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BlinkmothNexus()
    {
        var card = NamedCardFactory.Create("Blinkmoth Nexus", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Blinkmoth Nexus");
        card.HasType(CardType.Land).Should().BeTrue();
        // Mana ability + two ActivatedAbilities (animate + targeted pump)
        // all attached on the dispatcher path (single-arg factory call).
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void BlinkmothNexus_TapForColorless_TapsLandAndProducesOneGeneric()
    {
        var land = BlinkmothNexusFactory.Create(_alice);

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
    public void Animate_RegistersLayer4Effect_GrantingArtifactCreatureBlinkmothFlying()
    {
        var effects = new ContinuousEffectsService();
        var land = BlinkmothNexusFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        var animate = BlinkmothNexusFactory.GetAnimateAbility(land);
        animate.Resolve();

        var registered = GetRegisteredEffects(effects)
            .OfType<BlinkmothAnimateLandEffect>()
            .SingleOrDefault();
        registered.Should().NotBeNull("the animate resolution registers the layer effect");
        registered!.Target.Should().BeSameAs(land);
        registered.Layer.Should().Be(Layer.Type);
        registered.ExpiresAtEndOfTurn.Should().BeTrue();
        registered.NewPower.Should().Be(1);
        registered.NewToughness.Should().Be(1);

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Artifact + Creature are added, subtype carries Blinkmoth, and the
        // Flying keyword marker is present. No Infect (unlike Inkmoth).
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Artifact);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Blinkmoth);
        chars.Keywords.Should().Contain("Flying");
        chars.Keywords.Should().NotContain("Infect", "Blinkmoth Nexus has no infect");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = BlinkmothNexusFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        BlinkmothNexusFactory.GetAnimateAbility(land).Resolve();

        GetRegisteredEffects(effects).OfType<BlinkmothAnimateLandEffect>()
            .Should().HaveCount(1);

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<BlinkmothAnimateLandEffect>()
            .Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Types.Should().NotContain(CardType.Artifact);
        chars.Subtypes.Should().NotContain(CardSubtype.Blinkmoth);
        chars.Keywords.Should().NotContain("Flying");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Target Blinkmoth creature gets +1/+1 until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_HasCorrectCostShape_ManaAndTapAndOneTarget()
    {
        var land = BlinkmothNexusFactory.Create(_alice);

        var pump = BlinkmothNexusFactory.GetPumpAbility(land);

        pump.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        pump.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(1);
        pump.Costs.OfType<AdditionalCost>().Should().HaveCount(1, "{T} is part of the cost");
        pump.TargetRequests.Should().ContainSingle();
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void PumpAbility_OnResolution_RegistersPlusOnePlusOneUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = BlinkmothNexusFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);

        // A target Blinkmoth creature on the battlefield (e.g. another
        // animated Blinkmoth Nexus). Use a plain creature primed with the
        // continuous-effects service so the pump can register against it.
        var target = new Creature("Blinkmoth", "", 1, 1, null,
            new[] { CardSubtype.Blinkmoth })
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);

        var pump = BlinkmothNexusFactory.GetPumpAbility(land);
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        // A +1/+1 EOT pump is registered against the target's effects.
        var registered = GetRegisteredEffects(effects)
            .OfType<PumpUntilEndOfTurnEffect>()
            .SingleOrDefault();
        registered.Should().NotBeNull("the pump registers a +1/+1 EOT effect on the target");

        var chars = effects.Compute(target);
        chars.Power.Should().Be(2, "1/1 base + 1/1 from the pump");
        chars.Toughness.Should().Be(2);

        // CR 514.2 — expires during cleanup, reverting the target.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(1);
        after.Toughness.Should().Be(1);
    }

    [Fact]
    public void PumpAbility_NoEffectsService_NoOp_DoesNotThrow()
    {
        // Single-arg dispatcher path — no ContinuousEffectsService wired.
        var land = BlinkmothNexusFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var pump = BlinkmothNexusFactory.GetPumpAbility(land);
        // No target primed + no effects service — resolving must not throw.
        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow();
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
