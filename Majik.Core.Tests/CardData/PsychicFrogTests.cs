using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PsychicFrogFactory"/> (Modern Horizons 3,
/// {U}{B}).
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Frog + Mutant subtypes,
///   Flying keyword marker, owner/controller).
/// - NamedCardFactory dispatch.
/// - Combat-damage-to-a-player trigger: 1 damage → draw 1 + discard 1.
/// - Combat-damage trigger does NOT fire on damage to a creature.
/// - Activated "Discard a card: +1/+1 counter" ability cost shape +
///   payment + counter placement.
/// </summary>
public class PsychicFrogTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PsychicFrog_Identity()
    {
        var frog = PsychicFrogFactory.Create(_alice);

        frog.Name.Should().Be("Psychic Frog");
        frog.ManaCost.Should().Be("{U}{B}");
        frog.HasType(CardType.Creature).Should().BeTrue();
        frog.HasSubtype(CardSubtype.Frog).Should().BeTrue(
            "Psychic Frog is a Frog");
        frog.HasSubtype(CardSubtype.Mutant).Should().BeTrue(
            "Psychic Frog is a Mutant");
        frog.BasePower.Should().Be(1);
        frog.BaseToughness.Should().Be(3);
        frog.Owner.Should().BeSameAs(_alice);
        frog.Controller.Should().BeSameAs(_alice);

        // Flying keyword marker (CR 702.9).
        frog.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "Flying is wired as a KeywordAbility marker");
    }

    [Fact]
    public void PsychicFrog_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Psychic Frog", _alice);

        card.Should().BeOfType<Creature>("Psychic Frog is a Creature");
        card.Name.Should().Be("Psychic Frog");
        card.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mutant).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage loot trigger is attached");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "discard-pump activated ability is wired");
    }

    // -----------------------------------------------------------------------
    // Combat-damage trigger — draw N + discard N
    // -----------------------------------------------------------------------

    [Fact]
    public void PsychicFrog_CombatDamageToPlayer_1_Damage_Draws_1_AndDiscards_1()
    {
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        // Library top — should land in hand via the draw half.
        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Pre-existing hand card — first-card pick will discard this one
        // (the drawn card was appended at the end of the hand zone, so the
        // deterministic "first card in hand" picker pulls the older card).
        var oldHand = new Creature("OldHand", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(oldHand);
        oldHand.SetZone(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        // Fire the trigger — Psychic Frog deals 1 combat damage to Bob.
        var trigger = frog.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(frog, _bob, 1);

        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "Psychic Frog dealing combat damage to a player matches the trigger");

        foreach (var e in trigger.Effects) e.Execute();

        // Net: draw 1 (top → hand), then discard 1 (oldHand → graveyard).
        // Hand ends with exactly the drawn card.
        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "the top card was drawn");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top,
                "the drawn card is the only card left in hand");
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(oldHand,
                "v1 deterministic first-card-in-hand discard picks the older card");
    }

    [Fact]
    public void PsychicFrog_CombatDamageToCreature_DoesNotFire()
    {
        // Oracle text says "deals combat damage to a player". Damage to a
        // creature must NOT fire the loot trigger.
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = frog.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(frog, (ICard)blocker, 1);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "combat damage to a creature does not match — TargetPlayer is null");
    }

    // -----------------------------------------------------------------------
    // Activated ability — Discard a card: +1/+1 counter
    // -----------------------------------------------------------------------

    [Fact]
    public void PsychicFrog_DiscardPump_HasDiscardACardCost_AndNoManaCost()
    {
        var frog = PsychicFrogFactory.Create(_alice);

        var pump = frog.Abilities.OfType<ActivatedAbility>().Single();
        pump.Costs.OfType<DiscardACardCost>().Should().ContainSingle(
            "the activation cost is exactly \"discard a card\"");
        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Psychic Frog's discard-pump has no mana cost");
    }

    [Fact]
    public void PsychicFrog_DiscardPump_DiscardsACard_AndAdds_PlusOne_Counter()
    {
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        // One card in Alice's hand — the discard cost will burn it.
        var fodder = new Creature("Fodder", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(fodder);
        fodder.SetZone(ZoneType.Hand);

        var pump = frog.Abilities.OfType<ActivatedAbility>().Single();

        // Cost is payable, no counters yet.
        var discardCost = pump.Costs.OfType<DiscardACardCost>().Single();
        discardCost.CanPay(_alice).Should().BeTrue(
            "Alice has a card in hand → discard cost is payable");
        frog.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        // Pay the cost + execute the effect (resolution sequence).
        discardCost.Pay(_alice);
        foreach (var effect in pump.Effects) effect.Execute();

        // Fodder went to graveyard (CR 701.16a).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(fodder);

        // +1/+1 counter landed on Psychic Frog.
        frog.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the activated ability places a +1/+1 counter");

        // With no cards left in hand the cost can no longer be paid.
        discardCost.CanPay(_alice).Should().BeFalse(
            "empty hand → \"discard a card\" cannot be paid (CR 117.1)");
    }
}
