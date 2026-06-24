using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WaryWatchdogFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): {1}{G} Creature — Dog 3/1,
///   "When this creature enters or dies, surveil 1."
///
/// The "enters or dies" wording (CR 603.6e / CR 700.4) is modelled as TWO
/// triggered abilities, each a surveil 1. Covers card identity, that there are
/// exactly two surveil triggers (one over the source's entry, one over its
/// death) and no mana ability, and that each surveil's no-agent default sends
/// the peeked card to the graveyard (CR 701.42).
/// </summary>
[Trait("Color", "G")]
public class WaryWatchdogFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WaryWatchdog_HasExpectedIdentity()
    {
        var creature = (Creature)NamedCardFactory.Create("Wary Watchdog", _alice);

        creature.Name.Should().Be("Wary Watchdog");
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.Subtypes.Should().Contain(CardSubtype.Dog);
        creature.Power.Should().Be(3);
        creature.Toughness.Should().Be(1);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WaryWatchdog_HasTwoSurveilTriggers_NoManaAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Wary Watchdog", _alice);

        // "Enters or dies" → two distinct triggered abilities (CR 603.6e / 700.4).
        creature.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "enters and dies each get their own surveil-1 triggered ability");
        creature.Abilities.OfType<ManaAbility>().Should().BeEmpty();

        // Each triggered ability surveils — no targets (the controller's own library).
        foreach (var trig in creature.Abilities.OfType<TriggeredAbility>())
        {
            trig.TargetRequests.Should().BeEmpty(
                "surveil targets nothing — the controller's own library");
            trig.Effects.Should().ContainSingle("each surveil trigger has one effect");
        }
    }

    [Theory]
    [InlineData(0)] // the etb_self surveil trigger
    [InlineData(1)] // the dies_self surveil trigger
    public void WaryWatchdog_EachTrigger_Surveils1_DefaultsPeekedCardToGraveyard(int triggerIndex)
    {
        // Each of the two surveil-1 triggers, executed independently, sends the
        // single peeked top card to the graveyard under the no-agent default
        // (CR 701.42 — look at the top card, may put it into the graveyard).
        var owner = new Player("Owner", 20);
        var top = new Card("Top", ""); top.SetOwner(owner);
        var second = new Card("Second", ""); second.SetOwner(owner);
        foreach (var c in new[] { top, second })
        {
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Wary Watchdog", owner);
        var ability = creature.Abilities.OfType<TriggeredAbility>().ElementAt(triggerIndex);
        foreach (var effect in ability.Effects) effect.Execute();

        // Surveil 1: the single peeked card (Top) goes to the graveyard; Second
        // stays on the library.
        owner.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        owner.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void WaryWatchdog_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var creature = (Creature)NamedCardFactory.Create("Wary Watchdog", alice);

        Action act = () =>
        {
            foreach (var trig in creature.Abilities.OfType<TriggeredAbility>())
                foreach (var effect in trig.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
