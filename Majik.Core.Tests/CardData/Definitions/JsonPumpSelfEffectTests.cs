using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>pump_self</c> verb
/// (<see cref="PumpSelfEffectDef"/>, CR 611 / CR 514.2) — "this creature gets
/// +X/+X until end of turn". The Subject=self mirror of the targeted
/// <see cref="PumpTargetEffectDef"/>: it registers the SAME
/// <see cref="PumpUntilEndOfTurnEffect"/> Layer-7c modifier, but on the SOURCE
/// card's own <see cref="ContinuousEffectsService"/> with no target slot — the
/// posture the fluent <c>PumpUntilEndOfTurn</c> family already uses. Canonical
/// case: Atog — "Sacrifice an artifact: This creature gets +2/+2 until end of
/// turn."
/// </summary>
public class JsonPumpSelfEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();

    private Creature BattlefieldCreature(string name, int p, int t, ContinuousEffectsService fx)
    {
        var c = new Creature(name, "{1}{R}", p, t) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = fx;
        return c;
    }

    private async Task ResolveAsync(PumpSelfEffectDef def, ICard host)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: host, controller: _alice, replacements: null);
        var ctx = ResolutionContext.For(_alice, agent: null, game: null, chosenTargets: null);
        await effect.ExecuteAsync(ctx);
    }

    [Fact]
    public void PumpSelf_DeclaresNoTargetRequest()
    {
        // Subject=self — no target slot (CR 601.2c does not apply).
        new PumpSelfEffectDef { Power = 2, Toughness = 2 }.ToTargetRequest()
            .Should().BeNull();
    }

    [Fact]
    public async Task PumpSelf_GrantsPlusTwoPlusTwo_UntilEndOfTurn()
    {
        var fx = new ContinuousEffectsService(_bus);
        var atog = BattlefieldCreature("Atog", 1, 2, fx);

        await ResolveAsync(new PumpSelfEffectDef { Power = 2, Toughness = 2 }, atog);

        atog.Power.Should().Be(3, "CR 611 — +2/+2 until end of turn on the source");
        atog.Toughness.Should().Be(4);

        fx.ExpireEndOfTurn(); // CR 514.2
        atog.Power.Should().Be(1, "the +2/+2 ends at cleanup");
        atog.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task PumpSelf_Stacks_PerActivation()
    {
        var fx = new ContinuousEffectsService(_bus);
        var atog = BattlefieldCreature("Atog", 1, 2, fx);

        await ResolveAsync(new PumpSelfEffectDef { Power = 2, Toughness = 2 }, atog);
        await ResolveAsync(new PumpSelfEffectDef { Power = 2, Toughness = 2 }, atog);

        atog.Power.Should().Be(5, "two activations each add +2/+2 (CR 611.2c additive)");
        atog.Toughness.Should().Be(6);
    }

    [Fact]
    public async Task PumpSelf_SignedNegative_GivesMinusXMinusX()
    {
        var fx = new ContinuousEffectsService(_bus);
        var c = BattlefieldCreature("Selfish", 3, 3, fx);

        await ResolveAsync(new PumpSelfEffectDef { Power = -1, Toughness = -2 }, c);

        c.Power.Should().Be(2, "a signed −1/−2 is a negative Layer-7c delta (CR 611)");
        c.Toughness.Should().Be(1);
    }

    [Fact]
    public async Task PumpSelf_NotOnBattlefield_NoOps()
    {
        var fx = new ContinuousEffectsService(_bus);
        var c = new Creature("Atog", "{1}{R}", 1, 2) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Graveyard); // left the battlefield
        c.ActiveEffects = fx;

        await ResolveAsync(new PumpSelfEffectDef { Power = 2, Toughness = 2 }, c);

        c.Power.Should().Be(1, "a self-pump only applies while the source is on the battlefield");
        c.Toughness.Should().Be(2);
    }
}
