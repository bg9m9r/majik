using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Get Lost (The Lost Caverns of Ixalan, {1}{W}).
/// Oracle: "Destroy target creature, enchantment, or planeswalker. Its
/// controller creates two Map tokens."
///
/// Coverage:
///   * Card identity ({1}{W} Instant, white, dispatch by name).
///   * SpellDefinition shape (1 "creature, enchantment, or planeswalker" request).
///   * Destroy a creature → graveyard (CR 701.7) AND its controller gets two
///     Map tokens (CR 111.10).
///   * Target off the battlefield at resolution → no destroy, no Maps (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class GetLostFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GetLostFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_White_CmcTwo()
    {
        var get = GetLostFactory.Create(_alice);
        get.Name.Should().Be("Get Lost");
        get.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(get).Should().Contain(ManaColor.White);
        get.ManaCostValue.TotalValue.Should().Be(2);
        get.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_OneRequiredTarget_NoXNoModes()
    {
        var def = GetLostFactory.BuildSpellDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature, enchantment, or planeswalker");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLost_DestroysCreature_AndControllerGetsTwoMaps()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);

        var get = GetLostFactory.Create(_alice);
        get.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(get);

        await CastAndResolveAsync(get, bear);

        bear.Zone.Should().Be(ZoneType.Graveyard, "CR 701.7 — destroyed");
        _bob.Zones.Battlefield.GetCards().Count(c => c.Name == "Map").Should()
            .Be(2, "CR 111.10 — the destroyed creature's controller makes two Maps");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c => c.Name == "Map",
            "the Maps go to the TARGET's controller, not the caster");
    }

    [Fact]
    public async Task GetLost_TargetOffBattlefield_NoDestroy_NoMaps()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _zones.MoveCard(bear, ZoneType.Library, ZoneType.Battlefield, _bob);
        // Leave the battlefield before resolution.
        _zones.MoveCard(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);

        var get = GetLostFactory.Create(_alice);
        get.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(get);

        await CastAndResolveAsync(get, bear);

        _bob.Zones.Battlefield.GetCards().Should().NotContain(c => c.Name == "Map",
            "CR 608.2b — an illegal target fizzles the whole effect: no Maps");
    }

    private async Task CastAndResolveAsync(Instant get, object target)
    {
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, get,
            GetLostFactory.BuildSpellDefinition(o => o, _zones),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);
    }
}
