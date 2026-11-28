using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Stack;

/// <summary>
/// CR 601.2a — a spell being cast is on the stack from the moment its card
/// leaves the hand. The stack exposes it (display only) until the Spell is
/// pushed under the same id, or the cast is rewound (CR 732.1) and it is gone.
/// </summary>
public class CastInProgressTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CastInProgressTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new Majik.Core.Services.ZoneService(_bus), _bus);
    }

    private GameContext Ctx => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private (Instant card, SpellDefinition def) Bolt()
    {
        var card = new Instant("Lightning Bolt", "{R}");
        card.SetOwner(_alice); card.SetController(_alice);
        _alice.Zones.Hand.AddCard(card); card.SetZone(ZoneType.Hand);
        var def = OracleSpellBinder.Bind(new EmbeddedCardRepository().GetByName("Lightning Bolt")!, _alice, raw => raw, _stack)!;
        return (card, def);
    }

    [Fact]
    public async Task WhileTargetsAreChosen_TheCastIsInProgress_AndThePushedSpellKeepsItsId()
    {
        var (card, def) = Bolt();
        Majik.Core.Stack.CastInProgress? seen = null;
        // Observe the in-progress entry from inside the cast, at the targets prompt.
        var probe = new ProbingAgent(_bob, () => seen = _stack.BeingCast.SingleOrDefault());

        var spell = await _flow.CastAsync(_alice, card, def, probe, Ctx);

        seen.Should().NotBeNull();
        seen!.Card.Should().BeSameAs(card);
        spell.Id.Should().Be(seen.Id, "the pushed spell keeps the in-progress id");
        _stack.BeingCast.Should().BeEmpty();
        _stack.GetAll().Should().ContainSingle().Which.Should().BeSameAs(spell);
    }

    [Fact]
    public async Task ARewoundCast_LeavesNoInProgressEntry_AndTheCardIsBackInHand()
    {
        var (card, def) = Bolt();
        var agent = new ScriptedAgent();
        agent.QueueTargets(Array.Empty<object>()); // CR 601.2c — no legal choice made: the cast is illegal

        var act = async () => await _flow.CastAsync(_alice, card, def, agent, Ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _stack.BeingCast.Should().BeEmpty();
        _stack.IsEmpty.Should().BeTrue();
        card.Zone.Should().Be(ZoneType.Hand);
    }

    /// <summary>Picks <paramref name="target"/>, running <paramref name="probe"/> when targets are requested.</summary>
    private sealed class ProbingAgent(object target, Action probe) : DelegatingAgent
    {
        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        {
            probe();
            return Task.FromResult<IReadOnlyList<object>>(new[] { target });
        }

        public override Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
    }
}