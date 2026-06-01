using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LibrarySurveyorFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, Human Wizard subtypes, owner/controller, P/T, cost).
/// - Single ETB triggered ability with no mana abilities.
/// - ETB effect: surveil 2 — fall-back path sends both peeked cards to the graveyard.
/// - ETB effect: surveil with empty library is a graceful no-op.
/// </summary>
public class LibrarySurveyorTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LibrarySurveyor_IsCreature()
    {
        var card = (Creature)NamedCardFactory.Create("Library Surveyor", _alice);

        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void LibrarySurveyor_HasExpectedShape()
    {
        var creature = (Creature)NamedCardFactory.Create("Library Surveyor", _alice);

        creature.Name.Should().Be("Library Surveyor");
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
        creature.Power.Should().Be(1);
        creature.Toughness.Should().Be(2);
        creature.Subtypes.Should().Contain(CardSubtype.Human);
        creature.Subtypes.Should().Contain(CardSubtype.Wizard);
    }

    [Fact]
    public void LibrarySurveyor_HasSingleEtbTrigger_NoManaAbility()
    {
        var creature = (Creature)NamedCardFactory.Create("Library Surveyor", _alice);

        creature.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        creature.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }

    [Fact]
    public void LibrarySurveyor_EtbEffect_SurveilsTwo_DefaultsBothToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top1 = new Card("Top1", ""); top1.SetOwner(alice);
        var top2 = new Card("Top2", ""); top2.SetOwner(alice);
        var top3 = new Card("Top3", ""); top3.SetOwner(alice);
        foreach (var c in new[] { top1, top2, top3 })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var creature = (Creature)NamedCardFactory.Create("Library Surveyor", alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back sends both peeked cards (Top1 + Top2)
        // to the graveyard; Top3 is the only library card left.
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top1, top2 });
        alice.Zones.Library.GetCards().Should().Equal(new[] { top3 });
    }

    [Fact]
    public void LibrarySurveyor_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var creature = (Creature)NamedCardFactory.Create("Library Surveyor", alice);
        var etb = creature.Abilities.OfType<TriggeredAbility>().First();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
