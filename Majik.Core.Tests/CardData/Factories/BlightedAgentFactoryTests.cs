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
/// Unit tests for <see cref="BlightedAgentFactory"/>
/// (New Phyrexia, {1}{U}).
///
/// Creature — Phyrexian Human Rogue 1/1. Oracle text:
///   "Blighted Agent can't be blocked.
///    Infect"
///
/// Covers:
///   - Identity (name, cost, P/T, subtypes Phyrexian / Human / Rogue,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Unblockable + Infect keyword markers attached on the shape-only
///     path (no continuous-effects service).
///   - When a <see cref="ContinuousEffectsService"/> is supplied a
///     non-expiring <see cref="CombatRestrictionEffect"/> with
///     <see cref="CombatRestriction.CannotBeBlocked"/> is registered
///     and <see cref="ContinuousEffectsService.HasRestriction"/>
///     answers true for the agent (CR 509.1c read path).
///   - The unblockable restriction is scoped to Blighted Agent — a
///     different creature is NOT restricted.
/// </summary>
public class BlightedAgentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void BlightedAgent_Identity()
    {
        var c = BlightedAgentFactory.Create(_alice);

        c.Name.Should().Be("Blighted Agent");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Blighted Agent is a plain Creature, not an Artifact Creature");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlightedAgent_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Blighted Agent", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Blighted Agent");
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Keyword markers — Unblockable + Infect always attached
    // -------------------------------------------------------------------------

    [Fact]
    public void BlightedAgent_HasUnblockableAndInfectKeywordMarkers_ShapeOnly()
    {
        var c = BlightedAgentFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Unblockable",
            "card-text marker for the static 'can't be blocked' rider");
        keywords.Should().Contain("Infect",
            "card-text marker for the Infect keyword (CR 702.90)");
    }

    [Fact]
    public void BlightedAgent_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        // Shape-only path — pass no service. A standalone service
        // observer should see no registered restriction.
        var c = BlightedAgentFactory.Create(_alice);
        c.Should().NotBeNull();

        // No restriction was registered against the standalone service
        // because the shape-only ctor never touched it.
        effects.HasRestriction(c, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    // -------------------------------------------------------------------------
    // Live continuous-effects path — CombatRestriction.CannotBeBlocked
    // registered and queryable.
    // -------------------------------------------------------------------------

    [Fact]
    public void BlightedAgent_WithEffectsService_RegistersCannotBeBlockedRestriction()
    {
        var effects = new ContinuousEffectsService();
        var agent = BlightedAgentFactory.Create(_alice, effects);

        effects.HasRestriction(agent, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "CR 702.x / CR 509.1c — Blighted Agent's static unblockable rider " +
            "is registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void BlightedAgent_RestrictionIsScopedToAgent_OtherCreatureUnaffected()
    {
        var effects = new ContinuousEffectsService();
        var agent = BlightedAgentFactory.Create(_alice, effects);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);

        effects.HasRestriction(bystander, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "Blighted Agent's restriction targets the agent specifically — " +
            "other creatures are not affected");
        // Sanity — the agent itself still reads true.
        effects.HasRestriction(agent, CombatRestriction.CannotBeBlocked).Should().BeTrue();
    }

    [Fact]
    public void BlightedAgent_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var agent = BlightedAgentFactory.Create(_alice, effects);

        // The unblockable is permanent (static) — Prune of expired effects
        // should leave it in place.
        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(agent, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Blighted Agent's unblockable is a static ability — it does NOT " +
            "expire at end of turn");
    }
}
