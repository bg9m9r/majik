using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SoulGuideLanternFactory"/>.
///
/// Card: Soul-Guide Lantern (Ikoria, {1}). Artifact.
///   "When this artifact enters, exile target card from a graveyard.
///    {T}, Sacrifice this artifact: Exile each opponent's graveyard.
///    {1}, {T}, Sacrifice this artifact: Draw a card."
///
/// Covers:
/// - Identity (Artifact, {1}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one ETB <see cref="TriggeredAbility"/> + two
///   <see cref="ActivatedAbility"/>s with the correct cost shapes.
/// - ETB trigger condition: fires on self-entering battlefield.
/// - ETB resolution: chosen graveyard card → exile (its owner's exile zone).
/// - {T}, sac: opponents' graveyards exiled, lantern sacrificed.
/// - {1}, {T}, sac: controller draws a card, lantern sacrificed.
/// </summary>
[Trait("Color", "C")]
public class SoulGuideLanternFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SoulGuideLantern_Identity()
    {
        var lantern = SoulGuideLanternFactory.Create(_alice);

        lantern.Name.Should().Be("Soul-Guide Lantern");
        lantern.ManaCost.Should().Be("{1}");
        lantern.HasType(CardType.Artifact).Should().BeTrue();
        lantern.Owner.Should().BeSameAs(_alice);
        lantern.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void SoulGuideLantern_AbilityShape()
    {
        var lantern = SoulGuideLanternFactory.Create(_alice);

        // One ETB triggered ability.
        var triggers = lantern.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].TargetRequests.Should().HaveCount(1);
        triggers[0].TargetRequests[0].Description.Should().Contain("graveyard");

        // Two activated abilities.
        var activated = lantern.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(2);

        // The {T}, sac sweep — costs are tap + sacrifice, no mana, no targets.
        var sweep = activated.Single(a => !a.Costs.OfType<ManaCostCost>().Any());
        sweep.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        sweep.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        sweep.TargetRequests.Should().BeEmpty();

        // The {1}, {T}, sac cantrip — mana cost {1} + tap + sacrifice.
        var draw = activated.Single(a => a.Costs.OfType<ManaCostCost>().Any());
        draw.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice);
        draw.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void EtbTrigger_FiresOnSelfEntering()
    {
        var lantern = SoulGuideLanternFactory.Create(_alice);
        var trigger = lantern.Abilities.OfType<TriggeredAbility>().Single();

        var movedEvent = new CardMovedEvent(
            card: lantern, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(movedEvent, ability: null!).Should().BeTrue();
    }

    [Fact]
    public void EtbTrigger_DoesNotFireOnOtherCardEntering()
    {
        var lantern = SoulGuideLanternFactory.Create(_alice);
        var trigger = lantern.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Artifact("Other Artifact", "{1}");
        other.SetOwner(_alice);
        other.SetController(_alice);

        var movedEvent = new CardMovedEvent(
            card: other, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(movedEvent, ability: null!).Should().BeFalse();
    }

    [Fact]
    public void EtbResolution_ExilesChosenGraveyardCard()
    {
        // Bob has a card in his graveyard — Alice's lantern ETB targets it.
        var bobCard = new Card("Bob's Spell", "{2}");
        bobCard.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Graveyard);

        var lantern = SoulGuideLanternFactory.Create(_alice);
        var trigger = lantern.Abilities.OfType<TriggeredAbility>().Single();

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobCard },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobCard);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCard);
        bobCard.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void EtbResolution_OffGraveyardTarget_IsSilentNoOp()
    {
        // Target's zone changed since chose; CR 608.2b rejects.
        var bobCard = new Card("Bob's Card", "{2}");
        bobCard.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var lantern = SoulGuideLanternFactory.Create(_alice);
        var trigger = lantern.Abilities.OfType<TriggeredAbility>().Single();

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobCard },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        bobCard.Zone.Should().Be(ZoneType.Hand,
            "the chosen target was not in a graveyard at resolution");
        _bob.Zones.Exile.GetCards().Should().NotContain(bobCard);
    }

    [Fact]
    public void SweepAbility_ExilesOpponentsGraveyards_AndSacrificesLantern()
    {
        // Bob has two cards in his graveyard.
        var card1 = new Card("Bob's 1", "{2}");
        card1.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(card1);
        card1.SetZone(ZoneType.Graveyard);

        var card2 = new Card("Bob's 2", "{1}");
        card2.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(card2);
        card2.SetZone(ZoneType.Graveyard);

        // Alice has a card in HER graveyard — must NOT be exiled.
        var aliceCard = new Card("Alice's Card", "{1}");
        aliceCard.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Graveyard);

        var lantern = SoulGuideLanternFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lantern);
        lantern.SetZone(ZoneType.Battlefield);

        // The sweep ability has tap + sac costs, no mana cost, no targets.
        var sweep = lantern.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());

        // The sweep reads opponents off the LIVE resolution context — resolve
        // it through a GameContext exactly as the live engine does.
        ResolveWithGame(sweep, _alice, _alice, _bob);

        // Bob's graveyard fully exiled.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().Contain(new[] { card1, card2 });
        card1.Zone.Should().Be(ZoneType.Exile);
        card2.Zone.Should().Be(ZoneType.Exile);

        // Alice's own graveyard untouched.
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard);
        aliceCard.Zone.Should().Be(ZoneType.Graveyard);

        // Lantern sacrificed (and now also in Alice's graveyard).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(lantern);
        _alice.Zones.Graveyard.GetCards().Should().Contain(lantern);
        lantern.Zone.Should().Be(ZoneType.Graveyard);
    }

    /// <summary>
    /// PROD-PATH guard (the resolver-null bug class). The production
    /// <c>GameFacade</c> routed build dispatches
    /// <see cref="NamedCardFactory.Create(string, Player)"/> (single-arg shape
    /// build); the sweep ability resolves through
    /// <see cref="ActivatedAbility.ResolveAsync"/> with the live
    /// <see cref="GameContext"/>. The sweep must read opponents off that context
    /// (not a captured null resolver) so it actually exiles their graveyards.
    /// </summary>
    [Fact]
    public void SweepAbility_ExilesOpponentGraveyard_OnProdBuild()
    {
        var bobCard = new Card("Bob's Spell", "{2}");
        bobCard.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Graveyard);

        var built = NamedCardFactory.Create("Soul-Guide Lantern", _alice);
        built.Should().BeOfType<Artifact>();
        var lantern = (Artifact)built;
        _alice.Zones.Battlefield.AddCard(lantern);
        lantern.SetZone(ZoneType.Battlefield);

        var sweep = lantern.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<ManaCostCost>().Any());

        ResolveWithGame(sweep, _alice, _alice, _bob);

        _bob.Zones.Exile.GetCards().Should().Contain(bobCard,
            "the prod-built sweep reads opponents from the live context (not inert)");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobCard);
    }

    /// <summary>
    /// Resolve an activated ability through the async path with a live
    /// <see cref="GameContext"/> built from <paramref name="players"/>, so a
    /// context-reading effect sees opponents exactly as it does in a live match.
    /// </summary>
    private static void ResolveWithGame(
        ActivatedAbility ability, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        ability.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void DrawAbility_DrawsACard_AndSacrificesLantern()
    {
        var top = new Card("Top of Library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var lantern = SoulGuideLanternFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lantern);
        lantern.SetZone(ZoneType.Battlefield);

        var draw = lantern.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        // Lantern sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(lantern);
        lantern.Zone.Should().Be(ZoneType.Graveyard);
    }
}

