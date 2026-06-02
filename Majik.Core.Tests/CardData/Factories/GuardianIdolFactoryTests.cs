using System.Linq;
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
/// Tests for <see cref="GuardianIdolFactory"/> (Tenth Edition / reprints,
/// {2}). Artifact:
///   "This artifact enters tapped.
///    {T}: Add {C}.
///    {2}: This artifact becomes a 2/2 Golem artifact creature until end of
///    turn."
///
/// Covers:
/// - Identity (Artifact, name, cost {2}, owner / controller).
/// - JSON-backed {T}: Add {C} mana ability.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} — taps the idol, produces one colourless (bucketed as +1
///   generic per <see cref="ValueObjects.ManaCost.Parse"/>).
/// - Animate ability cost ({2}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type on Layer 4 (printed Artifact stays).
///     * Adds Golem subtype on Layer 4.
///     * Records 2/2 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement when a bus is wired.
/// </summary>
[Trait("Color", "C")]
public class GuardianIdolFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GuardianIdol_Identity()
    {
        var idol = GuardianIdolFactory.Create(_alice);

        idol.Name.Should().Be("Guardian Idol");
        idol.ManaCost.Should().Be("{2}");
        idol.HasType(CardType.Artifact).Should().BeTrue();
        idol.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is a plain Artifact (not a creature until animated)");
        idol.Owner.Should().BeSameAs(_alice);
        idol.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GuardianIdol_HasManaAndAnimateAbility()
    {
        var idol = GuardianIdolFactory.Create(_alice);

        idol.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} is wired from the JSON definition");
        idol.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2} animate ability is wired");
    }
    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void GuardianIdol_TapForColorless_TapsIdolAndProducesOneGeneric()
    {
        var idol = GuardianIdolFactory.Create(_alice);
        // CR 302.6 — summoning sickness only gates creatures; a non-creature
        // artifact's {T} ability is usable immediately, but clear anyway to
        // exercise the mana production rather than any sickness gate.
        idol.ClearSummoningSickness();

        var manaAbility = idol.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue("untapped idol — gate is open");
        var produced = manaAbility.Activate();

        // {C} buckets as +1 generic in ValueObjects.ManaCost today (CR 107.4c —
        // no dedicated colourless bucket; same convention as Mind Stone).
        produced.Generic.Should().Be(1);
        idol.IsTapped.Should().BeTrue("{T} cost tapped the idol on activation");

        manaAbility.CanActivate().Should().BeFalse(
            "tapped idol — mana ability !IsTapped gate is closed");
    }

    // -----------------------------------------------------------------------
    // {2} animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void GuardianIdol_AnimateAbility_HasPrintedManaCost2_InstantSpeed()
    {
        var idol = GuardianIdolFactory.Create(_alice);

        var animate = idol.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle (no sorcery rider)");
    }

    [Fact]
    public void GuardianIdol_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var idol = GuardianIdolFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(idol);
        idol.SetZone(ZoneType.Battlefield);

        var animate = idol.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)idol);
        chars.Types.Should().Contain(CardType.Artifact,
            "printed Artifact type stays through Layer 4 — \"becomes a … artifact creature\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Golem,
            "Layer 4 adds the Golem subtype");
    }

    [Fact]
    public void GuardianIdol_Layer7bBecomesEffect_SetsBase22_AndExpiresEot()
    {
        // The 2/2 animated body is a Layer-7b set-base effect (CR 613.7b).
        // Compute(Permanent) on a non-Creature Artifact runtime instance
        // doesn't surface P/T yet (shared manland-cycle shim posture), so the
        // body is asserted by applying the effect to a CreatureCharacteristics
        // directly — the row Compute would produce once the artifact's runtime
        // type upgrades to Creature.
        var idol = GuardianIdolFactory.Create(_alice);
        var ptEffect = new ManlandCycleBecomesPTEffect(
            idol, GuardianIdolFactory.AnimatedPower, GuardianIdolFactory.AnimatedToughness);

        var chars = new CreatureCharacteristics();
        ptEffect.Apply(chars);

        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue(
            "the animation lasts \"until end of turn\" — CR 514.2 cleanup lifts it");
    }

    [Fact]
    public void GuardianIdol_Animate_ExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var idol = GuardianIdolFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(idol);
        idol.SetZone(ZoneType.Battlefield);

        var animate = idol.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        effects.Compute((Permanent)idol).Types.Should().Contain(CardType.Creature);

        // CR 514.2 — cleanup step lifts "until end of turn" effects.
        effects.ExpireEndOfTurn();

        effects.Compute((Permanent)idol).Types.Should().NotContain(CardType.Creature,
            "the artifact reverts to a plain Artifact at end of turn");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void GuardianIdol_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var idol = GuardianIdolFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: idol,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This artifact enters tapped\" — unconditional (CR 614.1c)");
    }
}
