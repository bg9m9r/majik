using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Fading Hope ({U}).
/// Oracle: "Return target creature to its owner's hand. If its mana value was
/// 3 or less, scry 1."
///
/// A conditional-scry bounce — <see cref="UnsummonFactory"/> with a scry-1
/// rider gated on the bounced creature's mana value being 3 or less
/// (captured before the zone move, CR 608.2g). CR 608.2b: an illegal target
/// at resolution makes the whole effect (bounce + scry) do nothing.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class FadingHopeTests : IDisposable
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FadingHopeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    public void Dispose() => AgentRegistry.Clear();

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var card = FadingHopeFactory.Create(_alice);

        card.Name.Should().Be("Fading Hope");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShape()
    {
        var dispatched = NamedCardFactory.Create("Fading Hope", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Fading Hope");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public async Task ReturnsTargetCreatureToOwnersHand_AndScriesWhenMv3OrLess()
    {
        // MV 2 creature → bounce + scry 1.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Seed Alice's library so the scry has something to look at.
        var topCard = new Instant("Top Card", "{U}") { Owner = _alice, Controller = _alice };
        var secondCard = new Instant("Second Card", "{U}") { Owner = _alice, Controller = _alice };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var card = FadingHopeFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        // Scry decision: put the peeked top card on the bottom.
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { topCard },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(_alice, agent);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, FadingHopeFactory.BuildDefinition(_alice, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Bounce happened.
        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);

        // Scry happened: top card was bottomed, so the second card is now on top.
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(secondCard);
        _alice.Zones.Library.GetCards().Last().Should().BeSameAs(topCard);
    }

    [Fact]
    public async Task BouncesButDoesNotScry_WhenMvGreaterThan3()
    {
        // MV 5 creature → bounce only, no scry.
        var dragon = new Creature("Big Dragon", "{4}{R}", 5, 5) { Owner = _bob, Controller = _bob };
        dragon.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(dragon);

        var topCard = new Instant("Top Card", "{U}") { Owner = _alice, Controller = _alice };
        var secondCard = new Instant("Second Card", "{U}") { Owner = _alice, Controller = _alice };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var card = FadingHopeFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)dragon });
        agent.QueueMana(ManaPayment.Empty);
        // Queue a scry decision that, if (incorrectly) consumed, would reorder
        // the library — proving the scry did NOT fire when it stays untouched.
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { topCard },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(_alice, agent);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, FadingHopeFactory.BuildDefinition(_alice, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Bounce happened.
        dragon.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(dragon);

        // No scry: library order unchanged (top card still on top).
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(topCard);
        _alice.Zones.Library.GetCards().Last().Should().BeSameAs(secondCard);
    }

    [Fact]
    public async Task ScriesWhenMvExactly3()
    {
        // MV exactly 3 → boundary case, still scries.
        var golem = new Creature("Three Drop", "{2}{U}", 3, 3) { Owner = _bob, Controller = _bob };
        golem.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(golem);

        var topCard = new Instant("Top Card", "{U}") { Owner = _alice, Controller = _alice };
        var secondCard = new Instant("Second Card", "{U}") { Owner = _alice, Controller = _alice };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var card = FadingHopeFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)golem });
        agent.QueueMana(ManaPayment.Empty);
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { topCard },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(_alice, agent);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, FadingHopeFactory.BuildDefinition(_alice, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        golem.Zone.Should().Be(ZoneType.Hand);
        // Scry fired: top card bottomed.
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(secondCard);
    }

    [Fact]
    public async Task NoOp_WhenTargetNotOnBattlefieldAtResolution()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var topCard = new Instant("Top Card", "{U}") { Owner = _alice, Controller = _alice };
        var secondCard = new Instant("Second Card", "{U}") { Owner = _alice, Controller = _alice };
        topCard.SetZone(ZoneType.Library);
        secondCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var card = FadingHopeFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        AgentRegistry.Set(_alice, agent);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card, FadingHopeFactory.BuildDefinition(_alice, _zones), agent, ctx, alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Graveyard, because: "illegal target at resolution → no-op");
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);
        // No scry either — library untouched.
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(topCard);
    }
}
