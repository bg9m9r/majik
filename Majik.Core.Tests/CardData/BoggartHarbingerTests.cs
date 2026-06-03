using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Boggart Harbinger (Lorwyn, {2}{B}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Goblin + Shaman subtypes,
///     owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger shape (active on battlefield only).
///   - ETB tutor happy path: a Goblin card (any type) in library is moved
///     to the top of the library.
///   - ETB tutor predicate accepts a non-creature Goblin card and rejects
///     non-Goblin cards.
///   - ETB tutor no-op when no Goblin cards are present.
///   - ETB "may" decline (agent returns null) leaves the library untouched.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class BoggartHarbingerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BoggartHarbinger_Is_GoblinShaman_2_1_At_2B()
    {
        var harbinger = BoggartHarbingerFactory.Create(_alice);

        harbinger.Name.Should().Be("Boggart Harbinger");
        harbinger.ManaCost.Should().Be("{2}{B}");
        harbinger.HasType(CardType.Creature).Should().BeTrue();
        harbinger.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        harbinger.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        harbinger.BasePower.Should().Be(2);
        harbinger.BaseToughness.Should().Be(1);
        harbinger.Owner.Should().BeSameAs(_alice);
        harbinger.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoggartHarbinger()
    {
        var card = NamedCardFactory.Create("Boggart Harbinger", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boggart Harbinger");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB tutor trigger is wired");
    }

    [Fact]
    public void BoggartHarbinger_HasEtbTrigger_ActiveOnBattlefieldOnly()
    {
        var harbinger = BoggartHarbingerFactory.Create(_alice);

        var triggers = harbinger.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Library);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void BoggartHarbinger_Etb_StacksGoblinCardOnTopOfLibrary()
    {
        var alice = new Player("Alice", 20);

        // Library seeded with a non-Goblin first, then a Goblin card. After
        // ETB the Goblin should be on top (FirstOrDefault = top).
        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var goblinGuide = new Creature(
            name: "Goblin Guide",
            manaCost: "{R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinGuide.SetOwner(alice);
        alice.Zones.Library.AddCard(goblinGuide);
        goblinGuide.SetZone(ZoneType.Library);

        var harbinger = BoggartHarbingerFactory.Create(alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var top = alice.Zones.Library.GetCards().FirstOrDefault();
        top.Should().BeSameAs(goblinGuide,
            "the chosen Goblin card was stacked on top of the library");
        alice.Zones.Library.GetCards().Should().Contain(goblinGuide,
            "the card is still in the library (Library -> Library top)");
        alice.Zones.Hand.GetCards().Should().NotContain(goblinGuide,
            "Harbinger stacks the deck — it does NOT pull to hand");
    }

    [Fact]
    public void BoggartHarbinger_Etb_AcceptsNonCreatureGoblinCard()
    {
        var alice = new Player("Alice", 20);

        // "a Goblin card" — not "Goblin creature card". A non-creature card
        // carrying the Goblin subtype is a legal pick. (Tribal cards such as
        // Tarfire are Goblin cards without being creatures.)
        var goblinNonCreature = new Card("Tarfire", "{R}",
            subtypes: new[] { CardSubtype.Goblin });
        goblinNonCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(goblinNonCreature);
        goblinNonCreature.SetZone(ZoneType.Library);

        var harbinger = BoggartHarbingerFactory.Create(alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Library.GetCards().FirstOrDefault().Should().BeSameAs(
            goblinNonCreature,
            "a non-creature Goblin card is a legal 'Goblin card' pick");
    }

    [Fact]
    public void BoggartHarbinger_Etb_NoGoblinCardInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var harbinger = BoggartHarbingerFactory.Create(alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no Goblin card to find is a legal no-op");
        alice.Zones.Library.GetCards().FirstOrDefault().Should().BeSameAs(bear,
            "the non-Goblin stays put; no Goblin card to stack");
    }

    [Fact]
    public void BoggartHarbinger_Etb_AgentDeclines_LibraryUntouched()
    {
        var alice = new Player("Alice", 20);

        var goblinGuide = new Creature(
            name: "Goblin Guide",
            manaCost: "{R}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinGuide.SetOwner(alice);
        alice.Zones.Library.AddCard(goblinGuide);
        goblinGuide.SetZone(ZoneType.Library);

        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var agent = new Mock<IPlayerAgent>(MockBehavior.Loose);
        agent.Setup(a => a.ChooseLibraryPickAsync(
                It.IsAny<GameContext?>(),
                It.IsAny<IReadOnlyList<ICard>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ICard?)null);

        AgentRegistry.Set(alice, agent.Object);
        try
        {
            var beforeOrder = alice.Zones.Library.GetCards().ToList();

            var harbinger = BoggartHarbingerFactory.Create(alice);
            var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var effect in etb.Effects) effect.Execute();

            // "may" decline = no shuffle, no top-of-library mutation.
            var afterOrder = alice.Zones.Library.GetCards().ToList();
            afterOrder.Should().BeEquivalentTo(beforeOrder,
                "agent declined the 'may' search; library should be untouched");

            agent.Verify(a => a.ChooseLibraryPickAsync(
                It.IsAny<GameContext?>(),
                It.Is<IReadOnlyList<ICard>>(list => list.Count == 1 && list[0] == goblinGuide),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }
}
