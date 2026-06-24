using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HandThatFeedsFactory"/> (Modern Horizons 3, {1}{R},
/// Creature — Mutant 2/2).
///
/// Oracle text:
///   "Delirium — Whenever this creature attacks while there are four or more
///    card types among cards in your graveyard, it gets +2/+0 and gains menace
///    until end of turn. (It can't be blocked except by two or more
///    creatures.)"
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (Mutant 2/2, {1}{R}).
///   - Delirium boundary (CR 702.105) at 3 / 4 distinct graveyard card types.
///   - The attack trigger is delirium-gated (CR 603.4 intervening-if): it does
///     NOT fire below threshold, DOES fire at/above threshold.
///   - On resolution the creature gets +2/+0 and gains menace until end of turn
///     (CR 613 / CR 702.111 / CR 514.2).
/// </summary>
[Trait("Color", "R")]
public class HandThatFeedsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void HandThatFeeds_Identity_Mutant_2_2_At_1R()
    {
        var card = HandThatFeedsFactory.Create(_alice);

        card.Name.Should().Be("Hand That Feeds");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mutant).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Delirium boundary (CR 702.105) ───────────────────────────────────────

    [Fact]
    public void IsDeliriumActive_FalseAtThreeTypes()
    {
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Land });
        HandThatFeedsFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "three distinct card types is below the delirium threshold");
    }

    [Fact]
    public void IsDeliriumActive_TrueAtFourTypes()
    {
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Land });
        HandThatFeedsFactory.IsDeliriumActive(_alice).Should().BeTrue(
            "four distinct card types satisfies delirium");
    }

    // ── Delirium-gated attack trigger (CR 603.4 / CR 508.1f) ─────────────────

    [Fact]
    public void AttackTrigger_DoesNotFire_BelowDeliriumThreshold()
    {
        var card = SeatOnBattlefield();
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Land });

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var attack = new CreatureAttacksEvent(card, _bob);

        trigger.IsTriggered(attack).Should().BeFalse(
            "the intervening-if delirium gate is not met (3 types)");
    }

    [Fact]
    public void AttackTrigger_Fires_AtDeliriumThreshold()
    {
        var card = SeatOnBattlefield();
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Land });

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var attack = new CreatureAttacksEvent(card, _bob);

        trigger.IsTriggered(attack).Should().BeTrue(
            "delirium is satisfied (4 types) at attack declaration");
    }

    [Fact]
    public void AttackTrigger_DoesNotFire_ForAnotherAttacker()
    {
        var card = SeatOnBattlefield();
        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Land });

        var other = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "only this creature's own attack triggers the ability");
    }

    // ── Resolution: +2/+0 and menace until end of turn ───────────────────────

    [Fact]
    public void Resolve_GivesPlus2Plus0_AndMenace_UntilEndOfTurn()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var card = HandThatFeedsFactory.Create(_alice, bus, triggers: null, effects);
        SeatOnBattlefield(card);

        // Printed body before the trigger resolves.
        card.Power.Should().Be(2);
        card.Toughness.Should().Be(2);
        CombatAbilities.HasMenace(card).Should().BeFalse(
            "Hand That Feeds has no printed menace");

        // Resolve the attack-trigger body.
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        card.Power.Should().Be(4, "+2/+0 raises power to 4");
        card.Toughness.Should().Be(2, "the bonus is +0 toughness");
        CombatAbilities.HasMenace(card).Should().BeTrue(
            "it gains menace until end of turn (CR 702.111)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Creature SeatOnBattlefield()
    {
        var card = HandThatFeedsFactory.Create(_alice);
        SeatOnBattlefield(card);
        return card;
    }

    private static void SeatOnBattlefield(Creature card)
    {
        card.SetZone(ZoneType.Battlefield);
        card.Owner!.Zones.Battlefield.AddCard(card);
    }

    private static void SeedGraveyard(Player owner, params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
        }
    }
}
