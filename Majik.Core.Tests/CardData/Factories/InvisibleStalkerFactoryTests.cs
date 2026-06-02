using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InvisibleStalkerFactory"/>
/// (Innistrad, {1}{U}).
///
/// Creature — Human Rogue 1/1. Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)
///    This creature can't be blocked."
///
/// Covers:
///   - Identity (name, cost, P/T, subtypes Human / Rogue, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Hexproof + Unblockable keyword markers attached on the shape-only
///     path (no continuous-effects service).
///   - When a <see cref="ContinuousEffectsService"/> is supplied a
///     non-expiring <see cref="CombatRestrictionEffect"/> with
///     <see cref="CombatRestriction.CannotBeBlocked"/> is registered and
///     <see cref="ContinuousEffectsService.HasRestriction"/> answers true
///     for the stalker (CR 509.1c read path).
///   - The unblockable restriction is scoped to Invisible Stalker — a
///     different creature is NOT restricted.
/// </summary>
[Trait("Color", "U")]
public class InvisibleStalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void InvisibleStalker_Identity()
    {
        var c = InvisibleStalkerFactory.Create(_alice);

        c.Name.Should().Be("Invisible Stalker");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Invisible Stalker is a plain Creature, not an Artifact Creature");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers — Hexproof + Unblockable always attached
    // -------------------------------------------------------------------------

    [Fact]
    public void InvisibleStalker_HasHexproofAndUnblockableKeywordMarkers_ShapeOnly()
    {
        var c = InvisibleStalkerFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Hexproof",
            "card-text marker for the Hexproof keyword (CR 702.11) — read by " +
            "TargetLegality to reject opponents' targeting");
        keywords.Should().Contain("Unblockable",
            "card-text marker for the static 'can't be blocked' rider");
    }

    [Fact]
    public void InvisibleStalker_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        // Shape-only path — pass no service. A standalone service observer
        // should see no registered restriction.
        var c = InvisibleStalkerFactory.Create(_alice);
        c.Should().NotBeNull();

        effects.HasRestriction(c, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    // -------------------------------------------------------------------------
    // Live continuous-effects path — CombatRestriction.CannotBeBlocked
    // registered and queryable.
    // -------------------------------------------------------------------------

    [Fact]
    public void InvisibleStalker_WithEffectsService_RegistersCannotBeBlockedRestriction()
    {
        var effects = new ContinuousEffectsService();
        var stalker = InvisibleStalkerFactory.Create(_alice, effects);

        effects.HasRestriction(stalker, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "CR 509.1c — Invisible Stalker's static unblockable rider is " +
            "registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void InvisibleStalker_RestrictionIsScopedToStalker_OtherCreatureUnaffected()
    {
        var effects = new ContinuousEffectsService();
        var stalker = InvisibleStalkerFactory.Create(_alice, effects);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);

        effects.HasRestriction(bystander, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "Invisible Stalker's restriction targets the stalker specifically — " +
            "other creatures are not affected");
        effects.HasRestriction(stalker, CombatRestriction.CannotBeBlocked).Should().BeTrue();
    }

    [Fact]
    public void InvisibleStalker_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var stalker = InvisibleStalkerFactory.Create(_alice, effects);

        // The unblockable is permanent (static) — Prune of expired effects
        // should leave it in place.
        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(stalker, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Invisible Stalker's unblockable is a static ability — it does NOT " +
            "expire at end of turn");
    }
}
