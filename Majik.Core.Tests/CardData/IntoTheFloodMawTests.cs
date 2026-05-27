using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Into the Flood Maw (Bloomburrow, {U}).
///
/// Printed oracle:
///   "Gift a tapped Fish (...). Return target creature an opponent controls
///    to its owner's hand. If the gift was promised, instead return target
///    nonland permanent an opponent controls to its owner's hand."
///
/// v1 ships the printed base mode (no-gift) — bounce target creature an
/// opponent controls. Gift mechanic (cast-time prompt + conditional target
/// predicate upgrade) is documented as DEFERRED in the factory xmldoc and
/// in MODERN_COVERAGE.md.
///
/// Coverage:
///   * Identity per Scryfall (Instant, {U}, mana value 1, Blue).
///   * Dispatcher returns the correct shape by name.
///   * Bounces a creature an opponent controls to its owner's hand.
///   * Targeting a non-creature (land) permanent is illegal at resolution
///     (CR 608.2b) — the resolve-time gate is the same as Vapor Snag.
///   * Token bounced is moved to its owner's hand and then ceases to
///     exist via SBA 704.5d on the next SBA pass (CR 111.7).
/// </summary>
public class IntoTheFloodMawTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public IntoTheFloodMawTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue_OneManaValue()
    {
        var card = IntoTheFloodMawFactory.Create(_alice);

        card.Name.Should().Be("Into the Flood Maw");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "single {U} pip");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsInstantShape()
    {
        var dispatched = NamedCardFactory.Create("Into the Flood Maw", _alice);

        dispatched.Should().BeAssignableTo<Instant>(
            because: "Into the Flood Maw is now backed by the IGiftClause-implementing " +
                     "IntoTheFloodMawCard subclass — the dispatcher still hands back an " +
                     "Instant by static type but the runtime type is the gift-aware subclass.");
        dispatched.Name.Should().Be("Into the Flood Maw");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_BouncesCreatureAnOpponentControls_ToOwnerHand()
    {
        // Bob controls a 2/2 Bear that he owns.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Alice casts Into the Flood Maw at Bob's bear.
        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public async Task Resolve_LandTarget_IsNoOp()
    {
        // Bob has a Mountain on the battlefield. Lands are not Creatures,
        // so the resolve-time legality check rejects them (CR 608.2b).
        var mountain = new Land("Mountain") { Owner = _bob, Controller = _bob };
        mountain.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(mountain);

        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)mountain });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — land is not a creature; effect does nothing.
        mountain.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(mountain);
        _bob.Zones.Hand.GetCards().Should().NotContain(mountain);
    }

    [Fact]
    public async Task Resolve_TokenTarget_BouncedThenCeasesToExist()
    {
        // Bob controls a 1/1 Fish creature token (the kind Into the Flood
        // Maw's gift clause itself would create — fitting test fixture).
        var fish = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Fish", 1, 1, new[] { CardSubtype.Fish }),
            _bob,
            _zones);
        fish.IsToken.Should().BeTrue();

        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)fish });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 701.20 — bounce moves the token to its owner's Hand.
        // CR 111.7 / SBA 704.5d — once SBAs run, the token ceases to
        // exist (removed from Hand). It is no longer on the battlefield
        // either way.
        _bob.Zones.Battlefield.GetCards().Should().NotContain(fish);

        // Run SBAs (the engine's normal post-resolve checkpoint). The
        // TokensCeaseToExistCheck removes the fish from Bob's Hand.
        var sba = new StateBasedActions(_bus, _zones);
        sba.CheckStateBasedActions(
            new[] { _alice, _bob },
            new ICard[] { fish });

        _bob.Zones.Hand.GetCards().Should().NotContain(fish);
        // Card object survives, but it is no longer present in any of
        // Bob's zones — that's the v1 cease-to-exist contract.
    }
}
