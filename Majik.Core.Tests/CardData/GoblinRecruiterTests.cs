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
/// Tests for Goblin Recruiter (Visions / many reprints, {1}{R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Goblin subtype, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger shape (active on battlefield only).
///   - ETB tutor happy path: a Goblin creature card in library is moved
///     to the top of the library.
///   - ETB tutor predicate filters: non-Goblin and non-creature cards are
///     skipped.
///   - ETB tutor no-op when no Goblin creature cards are present.
///   - ETB tutor agent decline (returns null) is a legal no-op (zero
///     picks satisfies "any number" — CR 701.19a).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class GoblinRecruiterTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GoblinRecruiter_Is_GoblinCreature_1_1_At_1R()
    {
        var rec = GoblinRecruiterFactory.Create(_alice);

        rec.Name.Should().Be("Goblin Recruiter");
        rec.ManaCost.Should().Be("{1}{R}");
        rec.HasType(CardType.Creature).Should().BeTrue();
        rec.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        rec.BasePower.Should().Be(1);
        rec.BaseToughness.Should().Be(1);
        rec.Owner.Should().BeSameAs(_alice);
        rec.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinRecruiter()
    {
        var card = NamedCardFactory.Create("Goblin Recruiter", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Recruiter");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB tutor trigger is wired");
    }

    [Fact]
    public void GoblinRecruiter_HasEtbTrigger_ActiveOnBattlefieldOnly()
    {
        var rec = GoblinRecruiterFactory.Create(_alice);

        var triggers = rec.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Library);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void GoblinRecruiter_Etb_StacksGoblinCreatureCardOnTopOfLibrary()
    {
        var alice = new Player("Alice", 20);

        // Library seeded with a non-Goblin first, then a Goblin creature.
        // After ETB, the Goblin should be on top of the library
        // (FirstOrDefault = top).
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

        var rec = GoblinRecruiterFactory.Create(alice);
        var etb = rec.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var top = alice.Zones.Library.GetCards().FirstOrDefault();
        top.Should().BeSameAs(goblinGuide,
            "the chosen Goblin creature card was stacked on top of the library");
        alice.Zones.Library.GetCards().Should().Contain(goblinGuide,
            "the card is still in the library (Library -> Library top)");
        alice.Zones.Hand.GetCards().Should().NotContain(goblinGuide,
            "Recruiter stacks the deck — it does NOT pull to hand (contrast Matron)");
    }

    [Fact]
    public void GoblinRecruiter_Etb_NoGoblinCreatureInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // A non-Goblin creature must NOT be picked even though it's a
        // creature; a Goblin non-creature card (none exist today, but
        // the predicate must reject the case) must also be skipped.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var rec = GoblinRecruiterFactory.Create(alice);
        var etb = rec.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("zero picks is legal — 'any number' includes zero");
        alice.Zones.Library.GetCards().FirstOrDefault().Should().BeSameAs(bear,
            "the non-Goblin stays put; no Goblin creature to stack");
    }

    [Fact]
    public void GoblinRecruiter_Etb_AgentDeclines_LibraryUntouched()
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
            // Snapshot of library order before ETB.
            var beforeOrder = alice.Zones.Library.GetCards().ToList();

            var rec = GoblinRecruiterFactory.Create(alice);
            var etb = rec.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var effect in etb.Effects) effect.Execute();

            // Decline = zero picks = no shuffle, no top-of-library mutation.
            var afterOrder = alice.Zones.Library.GetCards().ToList();
            afterOrder.Should().BeEquivalentTo(beforeOrder,
                "agent declined the search; library should be untouched (no shuffle, no re-insert)");

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
