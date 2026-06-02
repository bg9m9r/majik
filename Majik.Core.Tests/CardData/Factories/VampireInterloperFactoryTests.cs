using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VampireInterloperFactory"/> (Innistrad, {1}{B}).
///
/// Creature — Vampire Scout 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Flying
///    This creature can't block."
///
/// Covers:
///   - Identity (Vampire Scout 2/1 at {1}{B}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flying keyword marker (CR 702.9) read by CombatAbilities.HasFlying.
///   - Shape-only Create overload does NOT register the combat restriction;
///     with a <see cref="ContinuousEffectsService"/> the CannotBlock
///     restriction is registered, scoped, and non-expiring (CR 509.1c).
/// </summary>
[Trait("Color", "B")]
public class VampireInterloperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void VampireInterloper_Identity()
    {
        var c = VampireInterloperFactory.Create(_alice);

        c.Name.Should().Be("Vampire Interloper");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VampireInterloper_DispatchesViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Vampire Interloper", _alice);

        c.Should().BeOfType<Creature>();
        ((Creature)c).Name.Should().Be("Vampire Interloper");
    }

    // -----------------------------------------------------------------------
    // Flying — CR 702.9
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireInterloper_HasFlyingKeyword()
    {
        var c = VampireInterloperFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Vampire Interloper has Flying");
    }

    [Fact]
    public void VampireInterloper_CombatAbilities_RecognizesFlying()
    {
        var c = VampireInterloperFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "CR 702.9 — the combat validator reads the Flying keyword for evasion");
    }

    // -----------------------------------------------------------------------
    // Can't block — CR 509.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void VampireInterloper_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = VampireInterloperFactory.Create(_alice);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    [Fact]
    public void VampireInterloper_WithEffectsService_RegistersCannotBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = VampireInterloperFactory.Create(_alice, effects);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "CR 509.1c — Vampire Interloper's static 'can't block' rider is " +
            "registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void VampireInterloper_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var c = VampireInterloperFactory.Create(_alice, effects);

        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "the can't-block is a permanent static — it does NOT expire at end of turn");
    }
}
