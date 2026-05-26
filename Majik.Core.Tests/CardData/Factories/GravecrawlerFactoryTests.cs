using FluentAssertions;
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
/// Unit tests for <see cref="GravecrawlerFactory"/> (Dark Ascension, {B}).
///
/// Creature — Zombie 2/1. Oracle text:
///   "Gravecrawler can't block.
///    You may cast Gravecrawler from your graveyard as long as you control
///    a Zombie."
///
/// Covers:
///   - Identity (Zombie 2/1 at {B}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Shape-only Create overload does NOT register the combat restriction
///     (no service supplied).
///   - With a <see cref="ContinuousEffectsService"/>, the CannotBlock
///     restriction is registered, scoped to Gravecrawler, and does NOT
///     expire at end of turn.
///
/// Deferred: the "may cast from graveyard with a Zombie-in-play predicate"
/// clause is not exercised — the primitive doesn't exist yet (see
/// <see cref="GravecrawlerFactory"/> Deferred section).
/// </summary>
public class GravecrawlerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Gravecrawler_Identity()
    {
        var c = GravecrawlerFactory.Create(_alice);

        c.Name.Should().Be("Gravecrawler");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Gravecrawler_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Gravecrawler", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Gravecrawler");
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
    }

    [Fact]
    public void Gravecrawler_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        var effects = new ContinuousEffectsService();
        // Shape-only path — no service.
        var c = GravecrawlerFactory.Create(_alice);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    [Fact]
    public void Gravecrawler_WithEffectsService_RegistersCannotBlockRestriction()
    {
        var effects = new ContinuousEffectsService();
        var c = GravecrawlerFactory.Create(_alice, effects);

        effects.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "CR 509.1c — Gravecrawler's static 'can't block' rider is registered " +
            "as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void Gravecrawler_RestrictionIsScopedToGravecrawler()
    {
        var effects = new ContinuousEffectsService();
        var gc = GravecrawlerFactory.Create(_alice, effects);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);

        effects.HasRestriction(bystander, CombatRestriction.CannotBlock).Should().BeFalse(
            "the restriction targets Gravecrawler specifically");
        effects.HasRestriction(gc, CombatRestriction.CannotBlock).Should().BeTrue();
    }

    [Fact]
    public void Gravecrawler_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var gc = GravecrawlerFactory.Create(_alice, effects);

        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(gc, CombatRestriction.CannotBlock).Should().BeTrue(
            "Gravecrawler's can't-block is a permanent static — it does NOT " +
            "expire at end of turn");
    }
}
