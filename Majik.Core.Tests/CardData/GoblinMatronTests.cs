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
/// Tests for Goblin Matron (Urza's Legacy, {2}{R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Goblin subtype, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger shape (active on battlefield only).
///   - ETB tutor happy path: a Goblin in library is moved to hand and
///     the predicate filters out non-Goblins.
///   - ETB tutor no-op when the library has no Goblins.
///   - ETB tutor agent decline (returns null) is a legal no-op (CR 701.19a).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class GoblinMatronTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinMatron_Is_GoblinCreature_1_1_At_2R()
    {
        var matron = GoblinMatronFactory.Create(_alice);

        matron.Name.Should().Be("Goblin Matron");
        matron.ManaCost.Should().Be("{2}{R}");
        matron.HasType(CardType.Creature).Should().BeTrue();
        matron.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        matron.BasePower.Should().Be(1);
        matron.BaseToughness.Should().Be(1);
        matron.Owner.Should().BeSameAs(_alice);
        matron.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinMatron()
    {
        var card = NamedCardFactory.Create("Goblin Matron", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Matron");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB tutor trigger is wired");
    }

    // -----------------------------------------------------------------------
    // ETB trigger structure
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinMatron_HasEtbTrigger_ActiveOnBattlefieldOnly()
    {
        var matron = GoblinMatronFactory.Create(_alice);

        var triggers = matron.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Library);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinMatron_Etb_TutorsGoblinFromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-Goblin first, then a Goblin, so we can verify
        // the subtype predicate filters out the non-Goblin.
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

        var matron = GoblinMatronFactory.Create(alice);
        var etb = matron.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        goblinGuide.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the Goblin to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(goblinGuide);
        alice.Zones.Library.GetCards().Should().NotContain(goblinGuide);
        bait.Zone.Should().Be(ZoneType.Library,
            "the non-Goblin card stays in the library (subtype-filtered)");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no Goblins in library
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinMatron_Etb_NoGoblinsInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // Seed library with a non-Goblin creature so we know it isn't
        // accidentally picked up by a too-loose predicate.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(alice);
        alice.Zones.Library.AddCard(bear);
        bear.SetZone(ZoneType.Library);

        var matron = GoblinMatronFactory.Create(alice);
        var etb = matron.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no Goblins in library = no-op (CR 701.19a decline path)");
        bear.Zone.Should().Be(ZoneType.Library,
            "a non-Goblin creature must not be tutored");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB tutor — agent decline path (CR 701.19a — "you may")
    //
    // When an agent is registered and returns null from
    // ChooseLibraryPickAsync, the ETB resolves as a no-op even when the
    // library contains an eligible Goblin. This exercises the agent-
    // driven decline branch that the deterministic fallback can't reach.
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinMatron_Etb_AgentDeclines_GoblinStaysInLibrary()
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
            var matron = GoblinMatronFactory.Create(alice);
            var etb = matron.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var effect in etb.Effects) effect.Execute();

            goblinGuide.Zone.Should().Be(ZoneType.Library,
                "agent declined to find (CR 701.19a) so the Goblin stays in library");
            alice.Zones.Hand.GetCards().Should().BeEmpty();
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
