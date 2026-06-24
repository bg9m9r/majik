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
/// Unit tests for <see cref="WaryThespianFactory"/>.
///
/// Oracle (Scryfall, verified 2026-06-24): {1}{G} Creature — Cat Druid 3/1,
///   "When this creature enters or dies, surveil 1."
///
/// Covers card identity, the two surveil triggers (enters + dies), and their
/// resolution (no-agent default sends the peeked card to the graveyard,
/// CR 701.42). The engine has no OR'd-condition object, so "enters or dies" is
/// modelled as two distinct triggered abilities sharing the surveil effect.
/// </summary>
[Trait("Color", "G")]
public class WaryThespianFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WaryThespian_Identity()
    {
        var creature = (Creature)NamedCardFactory.Create("Wary Thespian", _alice);

        creature.Name.Should().Be("Wary Thespian");
        creature.ManaCost.Should().Be("{1}{G}");
        creature.HasType(CardType.Creature).Should().BeTrue();
        creature.Subtypes.Should().Contain(new[] { CardSubtype.Cat, CardSubtype.Druid });
        creature.Power.Should().Be(3);
        creature.Toughness.Should().Be(1);
    }

    [Fact]
    public void WaryThespian_HasTwoSurveilTriggers_EntersAndDies()
    {
        var creature = (Creature)NamedCardFactory.Create("Wary Thespian", _alice);

        var triggered = creature.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().HaveCount(2,
            "the card has both an enters and a dies surveil trigger");

        // The dies trigger must remain active in the graveyard so it survives the
        // Battlefield -> Graveyard zone stamp (CR 603.6d / 700.4).
        triggered.Should().Contain(
            t => t.ActiveZones.Contains(ZoneType.Graveyard),
            "the dies trigger stays observable after the card moves to the graveyard");
    }

    [Fact]
    public void WaryThespian_Surveil_DefaultsPeekedCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Wary Thespian", alice);
        var trigger = creature.Abilities.OfType<TriggeredAbility>().First();

        foreach (var effect in trigger.Effects) effect.Execute();

        // No agent registered → the no-agent default surveils the peeked card to
        // the graveyard (CR 701.42); the second card remains on the library.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
    }

    [Fact]
    public void WaryThespian_Surveil_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var creature = (Creature)NamedCardFactory.Create("Wary Thespian", alice);
        var trigger = creature.Abilities.OfType<TriggeredAbility>().First();

        Action act = () => { foreach (var effect in trigger.Effects) effect.Execute(); };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }
}
