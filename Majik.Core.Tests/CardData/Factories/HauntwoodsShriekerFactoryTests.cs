using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Hauntwoods Shrieker (Duskmourn: House of Horror, {1}{G}{G}).
///
/// Oracle (verified against the embedded Modern seed, 2026-06-24):
///   "Whenever this creature attacks, manifest dread.
///    {1}{G}: Reveal target face-down permanent. If it's a creature card, you
///    may turn it face up."
///
/// Coverage (unique behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
/// - Identity (mana cost / P-T / Beast Mutant subtypes).
/// - Attack trigger fires on the Shrieker attacking (live TriggerManager).
/// - Attack trigger resolves real manifest dread (CR 701.59).
/// - {1}{G} reveal ability turns a face-down creature manifest face up.
/// - {1}{G} reveal ability no-ops on a face-down whose underlying is a
///   non-creature (CR 708.6 gate).
/// </summary>
[Trait("Color", "G")]
public class HauntwoodsShriekerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasBeastMutantIdentity()
    {
        var shrieker = HauntwoodsShriekerFactory.Create(_alice);

        shrieker.Should().BeOfType<Creature>();
        shrieker.Name.Should().Be("Hauntwoods Shrieker");
        shrieker.HasType(CardType.Creature).Should().BeTrue();
        shrieker.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        shrieker.HasSubtype(CardSubtype.Mutant).Should().BeTrue();
        shrieker.ManaCost.Should().Be("{1}{G}{G}");
        shrieker.ManaCostValue.TotalValue.Should().Be(3);
        shrieker.Power.Should().Be(3);
        shrieker.Toughness.Should().Be(3);
        shrieker.Owner.Should().BeSameAs(_alice);
        shrieker.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasAttackTrigger_AndRevealActivatedAbility()
    {
        var shrieker = HauntwoodsShriekerFactory.Create(_alice);

        shrieker.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        var reveal = shrieker.Abilities.OfType<ActivatedAbility>().Single();
        reveal.TargetRequests.Should().ContainSingle();
        reveal.TargetRequests[0].MinTargets.Should().Be(1);
        reveal.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void AttackTrigger_LiveBus_FiresWhenShriekerAttacks()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var shrieker = HauntwoodsShriekerFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(shrieker);
        shrieker.SetZone(ZoneType.Battlefield);

        // A different attacker should NOT trigger the Shrieker (per-attacker
        // "whenever this creature attacks").
        var other = new Creature("Other Attacker", "{G}", 2, 2);
        other.SetOwner(_alice);
        bus.Publish(new CreatureAttacksEvent(other, _bob));
        triggers.PendingCount.Should().Be(0, "trigger is gated on the Shrieker itself attacking");

        // The Shrieker attacking surfaces the manifest-dread trigger.
        bus.Publish(new CreatureAttacksEvent(shrieker, _bob));
        triggers.PendingCount.Should().Be(1, "the Shrieker attacking triggers manifest dread");
    }

    [Fact]
    public void AttackTrigger_Effect_ResolvesManifestDread()
    {
        // CR 701.59 — top of Alice's library becomes a face-down 2/2
        // ManifestedCreature on her battlefield; the second goes to her
        // graveyard.
        var shrieker = HauntwoodsShriekerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shrieker);
        shrieker.SetZone(ZoneType.Battlefield);

        var topCard = new Creature("Top Card Creature", "{1}{G}", 3, 3);
        topCard.SetOwner(_alice);
        var secondCard = new Card("Second Card", "{R}");
        secondCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var libraryBefore = _alice.Zones.Library.GetCards().Count();
        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        var attack = shrieker.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in attack.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Count().Should().Be(libraryBefore - 2,
            "manifest dread looks at + consumes top 2 of library");
        _alice.Zones.Graveyard.GetCards().Should().Contain(secondCard,
            "second-of-two looked-at card goes to graveyard");
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore + 1,
            "manifested wrapper joins the battlefield as a face-down 2/2");

        var wrapper = _alice.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.UnderlyingCard.Should().BeSameAs(topCard);
    }

    [Fact]
    public void RevealAbility_TurnsCreatureManifestFaceUp()
    {
        // CR 708.6 — {1}{G} reveal a face-down permanent; if the underlying
        // card is a creature, turn it face up.
        var shrieker = HauntwoodsShriekerFactory.Create(_alice);

        var underlying = new Creature("Hidden Bear", "{1}{G}", 2, 2);
        underlying.SetOwner(_alice);
        var wrapper = new ManifestedCreature(underlying);
        wrapper.SetOwner(_alice);
        wrapper.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(wrapper);
        wrapper.SetZone(ZoneType.Battlefield);

        var reveal = shrieker.Abilities.OfType<ActivatedAbility>().Single();
        reveal.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { wrapper } });
        foreach (var e in reveal.Effects) e.Execute();

        // The wrapper has been swapped out for the underlying creature.
        _alice.Zones.Battlefield.GetCards().Should().Contain(underlying,
            "the creature card is turned face up onto the battlefield");
        _alice.Zones.Battlefield.GetCards().OfType<ManifestedCreature>()
            .Should().BeEmpty("the face-down wrapper is removed when turned face up");
        underlying.IsFaceDown.Should().BeFalse();
    }

    [Fact]
    public void RevealAbility_NonCreatureUnderlying_NoOps()
    {
        // CR 708.6 — "if it's a creature card." A face-down wrapper over a
        // non-creature card cannot be turned face up; it stays face-down.
        var shrieker = HauntwoodsShriekerFactory.Create(_alice);

        var underlying = new Card("Hidden Sorcery", "{1}{R}");
        underlying.SetOwner(_alice);
        var wrapper = new ManifestedCreature(underlying);
        wrapper.SetOwner(_alice);
        wrapper.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(wrapper);
        wrapper.SetZone(ZoneType.Battlefield);

        var reveal = shrieker.Abilities.OfType<ActivatedAbility>().Single();
        reveal.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { wrapper } });
        foreach (var e in reveal.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<ManifestedCreature>()
            .Should().ContainSingle("a non-creature face-down stays face-down (CR 708.6)");
        wrapper.IsFaceDown.Should().BeTrue();
    }
}
