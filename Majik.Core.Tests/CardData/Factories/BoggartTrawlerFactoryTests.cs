using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BoggartTrawlerFactory"/> and
/// <see cref="BoggartBogFactory"/> — the front + back faces of the
/// modal double-faced card Boggart Trawler // Boggart Bog.
///
/// Front face (Boggart Trawler, {2}{B}):
///   Creature — Goblin 3/1.
///   "When this creature enters, exile target player's graveyard."
///
/// Back face (Boggart Bog):
///   Land. "As this land enters, you may pay 3 life. If you don't, it
///   enters tapped." "{T}: Add {B}."
///
/// Covers:
/// - Identity for both faces.
/// - <see cref="NamedCardFactory"/> dispatches both printed names to their
///   respective faces.
/// - MDFC face-tracker attachment (front-face carries front-name + back-name;
///   back-face carries the same pair pre-flipped).
/// - Front face — 3/1 Goblin creature, black, mana value 3.
/// - Front face — ETB trigger present; resolves by exiling target player's
///   graveyard.
/// - Back face — pay 3 life → enters untapped; decline / can't pay / no
///   agent → enters tapped; {T}: Add {B}.
/// </summary>
public class BoggartTrawlerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BoggartTrawlerFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void BoggartTrawler_Identity()
    {
        var card = BoggartTrawlerFactory.Create(_alice);

        card.Name.Should().Be("Boggart Trawler");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BoggartTrawler_IsBlack_ManaValueThree()
    {
        var card = BoggartTrawlerFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Black);
        card.ManaCost.Should().Be("{2}{B}",
            "mana value 3: {2} generic + {B} = 3 total");
    }

    [Fact]
    public void BoggartTrawler_IsGoblinCreature_ThreeOneStats()
    {
        var card = BoggartTrawlerFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        var creature = (Creature)card;
        creature.Power.Should().Be(3);
        creature.Toughness.Should().Be(1);
        creature.HasSubtype(CardSubtype.Goblin).Should().BeTrue("Boggart Trawler is a Goblin");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoggartTrawler()
    {
        var card = NamedCardFactory.Create("Boggart Trawler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boggart Trawler");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // =========================================================================
    // Front face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void BoggartTrawler_CarriesMdfcState_FrontNameAndBackName()
    {
        var card = BoggartTrawlerFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Boggart Trawler is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Boggart Trawler");
        card.MdfcState!.BackFaceName.Should().Be("Boggart Bog");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Boggart Trawler");
    }

    // =========================================================================
    // Front face — ETB trigger (shape)
    // =========================================================================

    [Fact]
    public void BoggartTrawler_HasSingleEtbTriggeredAbility()
    {
        var card = BoggartTrawlerFactory.Create(_alice);

        var etbTriggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        etbTriggers.Should().HaveCount(1, "exactly one ETB exile-graveyard triggered ability");
    }

    // =========================================================================
    // Front face — ETB trigger resolves: exiles target player's graveyard
    // =========================================================================

    [Fact]
    public void BoggartTrawler_ETB_ExilesTargetPlayersGraveyard()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        // Seed Bob's graveyard.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        var bolt = new Instant("Lightning Bolt", "{R}");
        foreach (var c in new ICard[] { goyf, bolt })
        {
            c.SetOwner(_bob);
            _bob.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var card = BoggartTrawlerFactory.Create(_alice, eventBus: null, triggers);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        triggers.BindCard(card);

        zones.MoveCardTo(card, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB exile trigger must queue on entering battlefield");

        var etbTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Boggart Trawler exiles every card in the target player's graveyard");
        _bob.Zones.Exile.GetCards().Should().Contain(new ICard[] { goyf, bolt });
        goyf.Zone.Should().Be(ZoneType.Exile);
        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void BoggartTrawler_ETB_EmptyGraveyard_NoOp()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var card = BoggartTrawlerFactory.Create(_alice, eventBus: null, triggers);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        triggers.BindCard(card);

        zones.MoveCardTo(card, ZoneType.Battlefield, controller: _alice);

        var etbTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        etbTrigger.SetChosenTargets(new[]
        {
            (IReadOnlyList<object>)new object[] { _bob },
        });

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().BeEmpty(
            "empty graveyard is a clean no-op (CR 608.2b)");
    }

    [Fact]
    public void BoggartTrawler_ETB_NoTargetChosen_FallsBackToController()
    {
        var (zones, stack, triggers, _) = BuildEngine();

        var ponder = new Instant("Ponder", "{U}");
        ponder.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(ponder);
        ponder.SetZone(ZoneType.Graveyard);

        var card = BoggartTrawlerFactory.Create(_alice, eventBus: null, triggers);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        triggers.BindCard(card);

        zones.MoveCardTo(card, ZoneType.Battlefield, controller: _alice);

        // No SetChosenTargets — fall through to controller.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().Contain(ponder);
        ponder.Zone.Should().Be(ZoneType.Exile);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void BoggartBog_Identity()
    {
        var land = BoggartBogFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Boggart Bog");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Boggart Bog is a non-Basic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoggartBog()
    {
        var card = NamedCardFactory.Create("Boggart Bog", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Boggart Bog");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BoggartBog_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = BoggartBogFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Boggart Bog is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Boggart Trawler");
        land.MdfcState!.BackFaceName.Should().Be("Boggart Bog");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Boggart Bog");
    }

    // =========================================================================
    // Back face — {T}: Add {B}
    // =========================================================================

    [Fact]
    public void BoggartBog_HasSingleManaAbility_AddingBlack()
    {
        var land = BoggartBogFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void BoggartBog_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var land = BoggartBogFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Boggart Bog has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void BoggartBog_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = BoggartBogFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Boggart Bog enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void BoggartBog_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = BoggartBogFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Boggart Bog enters tapped when the controller declines to pay 3 life");
        _alice.LifeTotal.Should().Be(20, "declining keeps Alice's life unchanged");
    }

    [Fact]
    public void BoggartBog_EntersTapped_WhenControllerCannotPayThreeLife()
    {
        // CR 119.4 — you can't pay life you don't have. Below 3 life the
        // agent is never prompted; land enters tapped.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if the predicate (incorrectly) prompted, the
        // ScriptedAgent would throw and the test would fail.
        AgentRegistry.Set(_alice, agent);

        var land = BoggartBogFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"Boggart Bog enters tapped when controller can't pay 3 life (life={_alice.LifeTotal})");
        _alice.LifeTotal.Should().Be(2, "life unchanged — no payment took place");
    }

    [Fact]
    public void BoggartBog_EntersUntapped_AtExactlyThreeLife()
    {
        // CR 119.4 carve-out — payments may bring you to 0. At exactly 3
        // life paying is legal: drop to 0; SBAs handle loss-of-game.
        var bus = new ReplacementBus();
        _alice.LoseLife(17); // life = 3
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = BoggartBogFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "at exactly 3 life the payment is legal — enters untapped");
        _alice.LifeTotal.Should().Be(0,
            "paying 3 life from 3 drops to 0; SBAs run later");
    }

    [Fact]
    public void BoggartBog_EntersTapped_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — the predicate's no-agent branch should
        // default to declining.
        var bus = new ReplacementBus();

        var land = BoggartBogFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no agent registered → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
