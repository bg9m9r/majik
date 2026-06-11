using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BloodhallPriestFactory"/> — Bloodhall Priest
/// (Eldritch Moon, {2}{B}{R}). Creature — Vampire Cleric 4/4. Oracle text:
///   "Whenever this creature enters or attacks, if you have no cards in hand,
///    this creature deals 2 damage to any target.
///    Madness {1}{B}{R} (...)"
///
/// Covers ONLY the non-madness body (Madness is intrinsic via MadnessCatalog +
/// Fx.DiscardCard):
///   - Identity (mana cost / P-T / subtypes).
///   - Two enters-or-attacks triggers, each with a single 1..1 "any target".
///   - Hellbent intervening-if (CR 603.4): trigger does NOT queue when the
///     controller has cards in hand; queues when the hand is empty.
///   - Trigger body: deals 2 damage to any target — Creature (marked damage),
///     Player (life loss), Planeswalker (loyalty removal).
/// </summary>
[Trait("Color", "M")]
public class BloodhallPriestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BloodhallPriest_Identity_IsVampireCleric_FourFour()
    {
        var c = BloodhallPriestFactory.Create(_alice);

        c.Name.Should().Be("Bloodhall Priest");
        c.ManaCost.Should().Be("{2}{B}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTwoTriggers_EntersAndAttacks_EachWithAnyTarget()
    {
        var c = BloodhallPriestFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "Bloodhall Priest prints one ability that triggers on enters OR "
            + "attacks — modelled as two TriggeredAbility instances.");
        triggers.Should().OnlyContain(
            t => t.TargetRequests.Count == 1
                 && t.TargetRequests[0].MinTargets == 1
                 && t.TargetRequests[0].MaxTargets == 1,
            "each trigger deals 2 damage to a single any target.");
    }

    // -----------------------------------------------------------------------
    // Hellbent intervening-if (CR 603.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void InterveningIf_DoesNotQueue_WhenControllerHasCardsInHand()
    {
        var c = BloodhallPriestFactory.Create(_alice);
        // Give Alice a card in hand.
        _alice.Zones.Hand.AddCard(new Creature("Filler", "{1}", 1, 1));

        var trigger = c.Abilities.OfType<TriggeredAbility>().First();
        trigger.CanBePutOnStack().Should().BeFalse(
            "the controller has a card in hand — hellbent gate fails (CR 603.4).");
    }

    [Fact]
    public void InterveningIf_Queues_WhenControllerHandIsEmpty()
    {
        var c = BloodhallPriestFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().First();
        trigger.CanBePutOnStack().Should().BeTrue(
            "the controller has no cards in hand — hellbent gate passes.");
    }

    // -----------------------------------------------------------------------
    // Trigger body: deal 2 damage to any target (hand empty -> hellbent on)
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_DealsTwoDamage_ToCreature()
    {
        var priest = BloodhallPriestFactory.Create(_alice);
        var victim = new Creature("Victim", "{2}{G}", 4, 5);

        ResolveFirstTriggerAt(priest, victim);

        victim.Damage.Should().Be(2, "deals 2 damage to any target");
    }

    [Fact]
    public void Trigger_DealsTwoDamage_ToPlayer()
    {
        var priest = BloodhallPriestFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        ResolveFirstTriggerAt(priest, bob);

        bob.LifeTotal.Should().Be(18, "20 - 2 = 18");
    }

    [Fact]
    public void Trigger_DealsTwoDamage_ToPlaneswalker_AsLoyaltyRemoval()
    {
        var priest = BloodhallPriestFactory.Create(_alice);
        var walker = new Planeswalker("Planey", "{2}{B}", 6);

        ResolveFirstTriggerAt(priest, walker);

        walker.Loyalty.Should().Be(4, "6 starting loyalty - 2 = 4 (CR 306.7)");
    }

    [Fact]
    public void Trigger_WhenControllerHasCardsInHand_IsCleanNoOp()
    {
        var priest = BloodhallPriestFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(new Creature("Filler", "{1}", 1, 1));
        var bob = new Player("Bob", 20);

        ResolveFirstTriggerAt(priest, bob);

        bob.LifeTotal.Should().Be(20,
            "hellbent intervening-if fails on resolution — no damage (CR 603.4).");
    }

    [Fact]
    public void Trigger_WithNoTargetChosen_IsCleanNoOp()
    {
        var priest = BloodhallPriestFactory.Create(_alice);
        var trigger = priest.Abilities.OfType<TriggeredAbility>().First();

        // No SetChosenTargets call — resolving must not throw.
        Action resolve = () => { foreach (var e in trigger.Effects) e.Execute(); };
        resolve.Should().NotThrow();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void ResolveFirstTriggerAt(Creature priest, object target)
    {
        var trigger = priest.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        foreach (var effect in trigger.Effects)
            effect.Execute();
    }
}
