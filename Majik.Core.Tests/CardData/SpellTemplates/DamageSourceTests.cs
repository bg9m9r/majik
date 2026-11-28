using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// CR 120.2 — the source of a spell's damage is the spell, so the damage event
/// names the spell's card, not its controller (Lava Dart used to report the
/// caster's player id as the source).
/// </summary>
public class DamageSourceTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DamageSourceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
    }

    private GameContext Ctx => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    [Theory]
    [InlineData("Lava Dart", "{R}")]
    [InlineData("Lightning Bolt", "{R}")]
    [InlineData("Shock", "{R}")]
    public async Task ASpellsDamage_HasTheSpellsCardAsItsSource(string name, string cost)
    {
        var card = new Instant(name, cost);
        card.SetOwner(_alice); card.SetController(_alice);
        _alice.Zones.Hand.AddCard(card); card.SetZone(ZoneType.Hand);
        var def = OracleSpellBinder.Bind(new EmbeddedCardRepository().GetByName(name)!, _alice, raw => raw,
            null, _stack, null, null, _bus, _zones)!;
        var dealt = new List<DamageDealtEvent>();
        _bus.Subscribe<DamageDealtEvent>(dealt.Add);

        await new SpellCastFlow(_stack, _zones, _bus).CastAsync(_alice, card, def, new TargetsBob(_bob), Ctx);
        new StackResolver(_bus, _zones).ResolveTop(_stack);

        var e = dealt.Should().ContainSingle().Subject;
        e.SourceCard.Should().BeSameAs(card);
        e.SourcePlayer.Should().BeNull();
        e.SourceInstanceId.Should().Be(card.InstanceId);
        e.TargetPlayer.Should().BeSameAs(_bob);
    }

    private sealed class TargetsBob(Player bob) : DelegatingAgent
    {
        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(new object[] { bob });

        public override Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
    }
}