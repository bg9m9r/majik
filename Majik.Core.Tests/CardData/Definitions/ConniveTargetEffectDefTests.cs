using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>connive_target</c> effect verb
/// (<see cref="ConniveTargetEffectDef"/>, CR 701.50) — "target creature you
/// control connives [N]". Exercises the shared
/// <see cref="CardDefRuntime.BuildJsonEffect"/> build path against a chosen
/// target read off <see cref="ResolutionContext.ChosenTargets"/>, mirroring the
/// other targeted verbs (explore_target / pump_target). The connive routine
/// itself (draw → discard → +1/+1 counter per nonland discarded) is the shared
/// <see cref="Majik.Core.Keywords.ConniveAction"/> primitive
/// (<see cref="Majik.Core.Primitives.Fx.Connive"/>), so the verb is pure schema
/// wiring onto an existing sink.
/// </summary>
public class ConniveTargetEffectDefTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    private static readonly ConniveTargetEffectDef Def = new();

    [Fact]
    public void TargetRequest_IsCreatureYouControl_OneToOne()
    {
        var req = Def.ToTargetRequest();
        req.Should().NotBeNull();
        req!.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature you control");
    }

    private async Task ConniveTargetAsync(ConniveTargetEffectDef def, Creature host, Creature target)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: host, controller: _alice, replacements: null, targetRequestIndex: 0);
        var ctx = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: new[] { new object[] { target } });
        await effect.ExecuteAsync(ctx);
    }

    [Fact]
    public async Task ConniveTarget_DiscardNonland_PutsCounterOnTarget()
    {
        // Library has a nonland card to draw; the agentless fallback discards
        // the last card in hand (the just-drawn one, a nonland) → +1/+1 counter
        // on the connived creature (CR 701.50a).
        var drawn = new Creature("Drawn Bolt", "{R}", 2, 2) { Owner = _alice };
        _alice.Zones.Library.AddCard(drawn);

        var host = new Creature("Mob source", "{U}", 0, 1) { Owner = _alice, Controller = _alice };
        var target = new Creature("Goon", "{B}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        await ConniveTargetAsync(Def, host, target);

        _alice.Zones.Graveyard.GetCards().Should().Contain(drawn,
            "the drawn nonland card is discarded by the connive routine");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.50a — a +1/+1 counter lands on the connived (target) creature for the nonland discard");
    }

    [Fact]
    public async Task ConniveTarget_DiscardLand_NoCounter()
    {
        var land = new Land("Swamp") { Owner = _alice };
        _alice.Zones.Library.AddCard(land);

        var host = new Creature("Mob source", "{U}", 0, 1) { Owner = _alice, Controller = _alice };
        var target = new Creature("Goon", "{B}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        await ConniveTargetAsync(Def, host, target);

        _alice.Zones.Graveyard.GetCards().Should().Contain(land,
            "the drawn land is discarded by the connive routine");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.50a — no counter when a LAND was discarded");
    }

    [Fact]
    public async Task ConniveTarget_AmountTwo_ConnivesTwice()
    {
        var a = new Creature("A", "{R}", 1, 1) { Owner = _alice };
        var b = new Creature("B", "{R}", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(a);
        _alice.Zones.Library.AddCard(b);

        var host = new Creature("Mob source", "{U}", 0, 1) { Owner = _alice, Controller = _alice };
        var target = new Creature("Goon", "{B}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(target);

        await ConniveTargetAsync(new ConniveTargetEffectDef { Amount = 2 }, host, target);

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "CR 701.50b — connive 2 draws 2 then discards 2; both nonland → two counters");
    }

    [Fact]
    public async Task ConniveTarget_TargetOffBattlefield_Fizzles_NoConnive()
    {
        var drawn = new Creature("Drawn", "{R}", 2, 2) { Owner = _alice };
        _alice.Zones.Library.AddCard(drawn);

        var host = new Creature("Mob source", "{U}", 0, 1) { Owner = _alice, Controller = _alice };
        var target = new Creature("Goon", "{B}", 1, 1) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Graveyard); // not on the battlefield

        await ConniveTargetAsync(Def, host, target);

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 608.2b — an illegal target at resolution fizzles the connive");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(drawn,
            "the connive routine never runs, so no card is drawn or discarded");
    }
}
