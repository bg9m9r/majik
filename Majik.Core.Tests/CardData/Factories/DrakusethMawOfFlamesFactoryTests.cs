using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DrakusethMawOfFlamesFactory"/> — Drakuseth, Maw of
/// Flames (Core Set 2020, {4}{R}{R}{R}). Legendary Creature — Dragon 7/7.
/// Oracle text (verified against Scryfall):
///   "Flying
///    Whenever Drakuseth attacks, it deals 4 damage to any target and 3 damage
///    to each of up to two other targets."
///
/// Covers the card's UNIQUE behaviour (the attack burn trigger) plus a single
/// identity assert. NamedCardFactory dispatch + well-formedness is covered for
/// every implemented card by CardFactoryContractTests — not re-asserted here.
///   - Identity: {4}{R}{R}{R}, 7/7, Legendary Creature — Dragon.
///   - Flying keyword marker (CR 702.9).
///   - One attack trigger carrying three TargetRequests: a 1..1 "any target"
///     (4 damage) and two 0..1 "any other target" (3 damage each).
///   - Resolve: 4 to the major target + 3 to each chosen minor target, routed
///     through Fx.DealDamageAny (Creature / Player / Planeswalker).
///   - "up to two" slack: zero or one minor target chosen is a clean no-op for
///     the unfilled clause(s).
/// </summary>
[Trait("Color", "R")]
public class DrakusethMawOfFlamesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Drakuseth_Identity_IsLegendaryDragon_SevenSeven()
    {
        var c = DrakusethMawOfFlamesFactory.Create(_alice);

        c.Name.Should().Be("Drakuseth, Maw of Flames");
        c.ManaCost.Should().Be("{4}{R}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(7);
        c.BaseToughness.Should().Be(7);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_AttachesFlying()
    {
        var c = DrakusethMawOfFlamesFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void HasOneAttackTrigger_WithThreeTargetRequests()
    {
        var c = DrakusethMawOfFlamesFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var requests = triggers[0].TargetRequests;
        requests.Should().HaveCount(3,
            "one 'any target' clause plus 'each of up to two other targets'.");

        // First clause: 4 damage to a single mandatory any target.
        requests[0].MinTargets.Should().Be(1);
        requests[0].MaxTargets.Should().Be(1);

        // Two minor clauses: each an OPTIONAL ('up to') single any target.
        requests[1].MinTargets.Should().Be(0);
        requests[1].MaxTargets.Should().Be(1);
        requests[2].MinTargets.Should().Be(0);
        requests[2].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Trigger body
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_DealsFourToMajor_AndThreeToEachMinor()
    {
        var drakuseth = DrakusethMawOfFlamesFactory.Create(_alice);
        var bob = new Player("Bob", 20);
        var ogre = new Creature("Ogre", "{3}{R}", 4, 4);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);

        // 4 to Bob (any target) + 3 to Ogre + 3 to Bear (the two other targets).
        Resolve(drakuseth,
            new object[] { bob },
            new object[] { ogre },
            new object[] { bear });

        bob.LifeTotal.Should().Be(16, "4 damage to the major 'any target' (20 - 4)");
        ogre.Damage.Should().Be(3, "3 damage to the first 'other target'");
        bear.Damage.Should().Be(3, "3 damage to the second 'other target'");
    }

    [Fact]
    public void Trigger_MajorTarget_CanBeAPlaneswalker_AsLoyaltyRemoval()
    {
        var drakuseth = DrakusethMawOfFlamesFactory.Create(_alice);
        var walker = new Planeswalker("Planey", "{2}{R}", 6);

        Resolve(drakuseth,
            new object[] { walker },
            Array.Empty<object>(),
            Array.Empty<object>());

        walker.Loyalty.Should().Be(2, "6 starting loyalty - 4 = 2 (CR 306.7)");
    }

    [Fact]
    public void Trigger_UpToTwo_AllowsZeroOtherTargets()
    {
        var drakuseth = DrakusethMawOfFlamesFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // Only the mandatory 'any target' is chosen; both 'other target'
        // clauses are empty ('up to two' → may choose zero).
        Resolve(drakuseth,
            new object[] { bob },
            Array.Empty<object>(),
            Array.Empty<object>());

        bob.LifeTotal.Should().Be(16, "only the 4-damage major clause lands");
    }

    [Fact]
    public void Trigger_UpToTwo_AllowsExactlyOneOtherTarget()
    {
        var drakuseth = DrakusethMawOfFlamesFactory.Create(_alice);
        var bob = new Player("Bob", 20);
        var ogre = new Creature("Ogre", "{3}{R}", 4, 4);

        Resolve(drakuseth,
            new object[] { bob },
            new object[] { ogre },
            Array.Empty<object>());

        bob.LifeTotal.Should().Be(16, "4 to the major target");
        ogre.Damage.Should().Be(3, "3 to the single chosen 'other target'");
    }

    [Fact]
    public void Trigger_WithNoTargetsChosen_IsCleanNoOp()
    {
        var drakuseth = DrakusethMawOfFlamesFactory.Create(_alice);
        var trigger = drakuseth.Abilities.OfType<TriggeredAbility>().Single();

        // No SetChosenTargets call — resolving must not throw.
        Action resolve = () => { foreach (var e in trigger.Effects) e.Execute(); };
        resolve.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature drakuseth, object[] major, object[] minor1, object[] minor2)
    {
        var trigger = drakuseth.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { major, minor1, minor2 });
        foreach (var effect in trigger.Effects)
            effect.Execute();
    }
}
