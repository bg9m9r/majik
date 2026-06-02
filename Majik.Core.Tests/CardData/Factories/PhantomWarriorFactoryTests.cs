using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PhantomWarriorFactory"/>
/// (7th Edition / various, {1}{U}{U}).
///
/// Creature — Illusion Warrior 2/2. Oracle text:
///   "Phantom Warrior can't be blocked."
///
/// Covers:
///   - Identity (name, cost, P/T, subtypes Illusion / Warrior,
///     owner / controller, mana value 3, blue colour).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Unblockable keyword marker attached on the shape-only path.
///   - When a <see cref="ContinuousEffectsService"/> is supplied a
///     non-expiring <see cref="CombatRestrictionEffect"/> with
///     <see cref="CombatRestriction.CannotBeBlocked"/> is registered
///     and <see cref="ContinuousEffectsService.HasRestriction"/>
///     answers true for the warrior (CR 509.1c read path).
///   - The unblockable restriction is scoped to Phantom Warrior — a
///     different creature is NOT restricted.
///   - The restriction does not expire at end of turn (static ability).
/// </summary>
[Trait("Color", "U")]
public class PhantomWarriorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void PhantomWarrior_Identity()
    {
        var c = PhantomWarriorFactory.Create(_alice);

        c.Name.Should().Be("Phantom Warrior");
        c.ManaCost.Should().Be("{1}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhantomWarrior_ManaValueIsThree()
    {
        var c = PhantomWarriorFactory.Create(_alice);

        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3,
            "1 generic + 2 blue = converted mana cost 3 (CR 202.3)");
    }

    [Fact]
    public void PhantomWarrior_IsBlue()
    {
        var c = PhantomWarriorFactory.Create(_alice);

        // {1}{U}{U} — blue is derived from the U pips in the cost.
        c.ManaCost.Should().Contain("U",
            "Phantom Warrior is a blue card — its cost contains blue pips");
    }
    // -------------------------------------------------------------------------
    // Keyword marker — Unblockable always attached
    // -------------------------------------------------------------------------

    [Fact]
    public void PhantomWarrior_HasUnblockableKeywordMarker_ShapeOnly()
    {
        var c = PhantomWarriorFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Unblockable",
            "card-text marker for the static 'can't be blocked' rider");
    }

    [Fact]
    public void PhantomWarrior_ShapeOnly_DoesNotRegisterCombatRestriction()
    {
        // Shape-only path — pass no service.
        var effects = new ContinuousEffectsService();
        var c = PhantomWarriorFactory.Create(_alice);

        // The standalone service should see no restriction for this creature.
        effects.HasRestriction(c, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "shape-only Create overload does not install the combat restriction");
    }

    // -------------------------------------------------------------------------
    // Live continuous-effects path — CombatRestriction.CannotBeBlocked
    // -------------------------------------------------------------------------

    [Fact]
    public void PhantomWarrior_WithEffectsService_RegistersCannotBeBlockedRestriction()
    {
        var effects = new ContinuousEffectsService();
        var warrior = PhantomWarriorFactory.Create(_alice, effects);

        effects.HasRestriction(warrior, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "CR 509.1c — Phantom Warrior's static 'can't be blocked' rider " +
            "is registered as a non-expiring CombatRestrictionEffect");
    }

    [Fact]
    public void PhantomWarrior_RestrictionIsScopedToWarrior_OtherCreatureUnaffected()
    {
        var effects = new ContinuousEffectsService();
        var warrior = PhantomWarriorFactory.Create(_alice, effects);

        var bystander = new Creature("Bystander", "{1}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);

        effects.HasRestriction(bystander, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "Phantom Warrior's restriction targets the warrior specifically — " +
            "other creatures are not affected");
        // Sanity — the warrior itself still reads true.
        effects.HasRestriction(warrior, CombatRestriction.CannotBeBlocked).Should().BeTrue();
    }

    [Fact]
    public void PhantomWarrior_Restriction_DoesNotExpireAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var warrior = PhantomWarriorFactory.Create(_alice, effects);

        // Static ability — must survive end-of-turn pruning.
        effects.ExpireEndOfTurn();
        effects.Prune();

        effects.HasRestriction(warrior, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Phantom Warrior's unblockable is a static ability — it does NOT " +
            "expire at end of turn");
    }
}
