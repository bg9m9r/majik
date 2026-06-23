using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for No More Lies (Murders at Karlov Manor, {W}{U}).
/// Oracle: "Counter target spell unless its controller pays {3}. If that spell
/// is countered this way, exile it instead of putting it into its owner's
/// graveyard."
///
/// Covers ONLY the card's unique behaviour:
///   * Identity ({W}{U}, multicolour W/U Instant).
///   * Counter-unless-pay-{3} with the exile-on-counter rider: failure (no mana
///     → countered, card goes to EXILE not graveyard) and success (pays →
///     resolves, neither exiled nor countered).
/// </summary>
[Trait("Color", "M")]
public class NoMoreLiesTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NoMoreLiesTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Multicolour_WU()
    {
        var spell = NoMoreLiesFactory.Create(_alice);

        spell.Name.Should().Be("No More Lies");
        spell.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(spell).Should().Contain(new[] { ManaColor.White, ManaColor.Blue });
        spell.ManaCost.Should().Be("{W}{U}");
        spell.ManaCostValue.TotalValue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {3}, EXILE if countered (CR 118.4 / 701.5 / 614)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Counters_ToExile_WhenControllerCannotPayThree()
    {
        var nml = NoMoreLiesFactory.Create(_alice);
        nml.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(nml);

        // Bob casts Lightning Bolt {R} with 0 mana → cannot pay {3} → countered.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, nml,
            NoMoreLiesFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Exile,
            because: "Bob couldn't pay {3} so No More Lies counters his spell and exiles it instead of putting it into his graveyard");
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard);
        _bob.Zones.Exile.GetCards().Should().Contain(c => ReferenceEquals(c, bobBolt),
            because: "the countered card is placed into its owner's exile zone");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysThree()
    {
        var nml = NoMoreLiesFactory.Create(_alice);
        nml.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(nml);

        // Bob has {3} available → he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, nml,
            NoMoreLiesFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Exile,
            because: "Bob paid {3} so No More Lies is countered into a no-op — nothing is exiled");
    }
}
