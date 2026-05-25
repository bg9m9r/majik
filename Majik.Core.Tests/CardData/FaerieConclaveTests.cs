using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="FaerieConclaveFactory"/> — Land manland.
///
/// Covers:
/// - Card identity (Land, no subtypes, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch produces the same shape.
/// - {T}: Add {U} — mana ability produces blue.
/// - {1}{U}: animate ActivatedAbility — resolution registers Layer 4
///   (<see cref="FaerieConclaveAnimateEffect"/>) + Layer 7b
///   (<see cref="FaerieConclaveBecomesPTEffect"/>), both
///   <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>.
/// - Compute after activation: Creature + Faerie subtype + Flying keyword,
///   Land type retained, P/T 1/1.
/// - End-of-turn expiry lifts both effects; Compute drops back to plain
///   Land identity.
/// - ETB-tapped replacement is registered when a <see cref="ReplacementBus"/>
///   is supplied.
/// </summary>
public class FaerieConclaveTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieConclave_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = FaerieConclaveFactory.Create(_alice);

        land.Name.Should().Be("Faerie Conclave");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed Faerie Conclave is just a Land until activated");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FaerieConclave()
    {
        var card = NamedCardFactory.Create("Faerie Conclave", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Faerie Conclave");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {U}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "{1}{U}: animate ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void FaerieConclave_TapForBlue_TapsLandAndProducesBlue()
    {
        var land = FaerieConclaveFactory.Create(_alice);
        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.Blue.Should().Be(1);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {1}{U}: animate — Layer 4 + Layer 7b grant.
    // -----------------------------------------------------------------------

    [Fact]
    public void Animate_RegistersLayer4AndLayer7b_EotExpiring_OnTheLand()
    {
        var effects = new ContinuousEffectsService();
        var land = FaerieConclaveFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<FaerieConclaveAnimateEffect>()
            .SingleOrDefault();
        animateEffect.Should().NotBeNull();
        animateEffect!.Target.Should().BeSameAs(land);
        animateEffect.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<FaerieConclaveBecomesPTEffect>()
            .SingleOrDefault();
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        ptEffect.NewPower.Should().Be(1);
        ptEffect.NewToughness.Should().Be(1);

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Creature added, Faerie subtype + Flying marker present.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Faerie);
        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = FaerieConclaveFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        GetRegisteredEffects(effects).OfType<FaerieConclaveAnimateEffect>().Should().HaveCount(1);
        GetRegisteredEffects(effects).OfType<FaerieConclaveBecomesPTEffect>().Should().HaveCount(1);

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<FaerieConclaveAnimateEffect>().Should().BeEmpty();
        GetRegisteredEffects(effects).OfType<FaerieConclaveBecomesPTEffect>().Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Faerie);
        chars.Keywords.Should().NotContain("Flying");
    }

    [Fact]
    public void Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        // Single-arg dispatcher path — no service wired. Resolve must not
        // throw, and the printed shape is unchanged.
        var land = FaerieConclaveFactory.Create(_alice);
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
    // ETB-tapped replacement
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTappedReplacement_IsRegistered_WhenReplacementBusSupplied()
    {
        var replacements = new ReplacementBus();
        var act = () => FaerieConclaveFactory.Create(
            _alice, effects: null, replacements: replacements);

        // Smoke test — factory accepts a ReplacementBus and registers the
        // ETB-tapped replacement without throwing. ReplacementBus internals
        // are private; behavioural verification of the tapped-ETB flow is
        // covered by the broader ETB-replacement test suite.
        act.Should().NotThrow();
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
