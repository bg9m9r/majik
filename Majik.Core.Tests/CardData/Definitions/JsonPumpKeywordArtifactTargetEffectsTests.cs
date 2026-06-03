using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the three declarative until-end-of-turn targeted verbs that wire
/// onto pre-existing continuous-effect primitives:
/// <list type="bullet">
///   <item><c>pump_target</c> (<see cref="PumpTargetEffectDef"/>, CR 611) —
///   "target creature gets +X/+X until end of turn"
///   (<see cref="PumpUntilEndOfTurnEffect"/>).</item>
///   <item><c>grant_keyword_until_eot_target</c>
///   (<see cref="GrantKeywordUntilEotTargetEffectDef"/>, CR 613.1c) — "target
///   creature gains [keyword] until end of turn"
///   (<see cref="GrantKeywordUntilEndOfTurnEffect"/>).</item>
///   <item><c>becomes_artifact_target</c>
///   (<see cref="BecomesArtifactTargetEffectDef"/>, CR 613.1d) — "target
///   permanent becomes an artifact in addition to its other types until end of
///   turn" (<see cref="LiquimetalCoatingAddArtifactEffect"/>).</item>
/// </list>
/// Each verb reads the chosen target off
/// <see cref="ResolutionContext.ChosenTargets"/> at the reserved index, registers
/// the until-EOT modifier on the target's own
/// <see cref="ContinuousEffectsService"/>, and fizzles on an illegal target
/// (CR 608.2b). Mirrors <see cref="ExploreTargetEffectDefTests"/>.
/// </summary>
public class JsonPumpKeywordArtifactTargetEffectsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();

    private Creature BattlefieldCreature(string name, int p, int t, ContinuousEffectsService fx)
    {
        var c = new Creature(name, "{G}", p, t) { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = fx;
        return c;
    }

    private static async Task ResolveAsync(
        EffectDefinition def, ICard host, Player controller, object target)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: host, controller: controller, replacements: null, targetRequestIndex: 0);
        var ctx = ResolutionContext.For(
            controller, agent: null, game: null,
            chosenTargets: new[] { new object[] { target } });
        await effect.ExecuteAsync(ctx);
    }

    // ── pump_target ───────────────────────────────────────────────────────────

    [Fact]
    public void PumpTarget_DeclaresSingleTargetRequest()
    {
        var req = new PumpTargetEffectDef { TargetFilter = "legendary_creature" }.ToTargetRequest();
        req.Should().NotBeNull();
        req!.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task PumpTarget_GrantsPlusOnePlusOne_UntilEndOfTurn()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = BattlefieldCreature("Bear", 2, 2, fx);

        await ResolveAsync(
            new PumpTargetEffectDef { Power = 1, Toughness = 1, TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        target.Power.Should().Be(3, "CR 611 — +1/+1 until end of turn");
        target.Toughness.Should().Be(3);

        fx.ExpireEndOfTurn(); // CR 514.2
        target.Power.Should().Be(2, "the +1/+1 ends at cleanup");
        target.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task PumpTarget_SignedNegative_GivesMinusXMinusX()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = BattlefieldCreature("Bear", 3, 3, fx);

        await ResolveAsync(
            new PumpTargetEffectDef { Power = -2, Toughness = -1, TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        target.Power.Should().Be(1, "CR 611 — a signed −2/−1 is a negative Layer-7c delta");
        target.Toughness.Should().Be(2);
    }

    [Fact]
    public async Task PumpTarget_IllegalTarget_FizzlesCleanly()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = new Creature("Bear", "{G}", 2, 2) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Graveyard); // not on the battlefield
        target.ActiveEffects = fx;

        await ResolveAsync(
            new PumpTargetEffectDef { Power = 1, Toughness = 1, TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        target.Power.Should().Be(2, "CR 608.2b — an illegal target fizzles the pump");
        target.Toughness.Should().Be(2);
    }

    // ── grant_keyword_until_eot_target ──────────────────────────────────────────

    [Fact]
    public async Task GrantKeyword_GrantsFlying_UntilEndOfTurn()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = BattlefieldCreature("Ground Pounder", 2, 2, fx);
        CombatAbilities.HasFlying(target).Should().BeFalse();

        await ResolveAsync(
            new GrantKeywordUntilEotTargetEffectDef { Keyword = "Flying", TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        CombatAbilities.HasFlying(target).Should().BeTrue("CR 613.1c — gains flying until end of turn");

        fx.ExpireEndOfTurn();
        CombatAbilities.HasFlying(target).Should().BeFalse("the grant ends at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task GrantKeyword_GrantsDoubleStrike()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = BattlefieldCreature("Legionnaire", 2, 2, fx);

        await ResolveAsync(
            new GrantKeywordUntilEotTargetEffectDef { Keyword = "Double strike", TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        CombatAbilities.HasDoubleStrike(target).Should().BeTrue("CR 702.4 — gains double strike until EOT");
    }

    [Fact]
    public async Task GrantKeyword_IllegalTarget_FizzlesCleanly()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = new Creature("Ground Pounder", "{G}", 2, 2) { Owner = _alice, Controller = _alice };
        target.SetZone(ZoneType.Graveyard);
        target.ActiveEffects = fx;

        await ResolveAsync(
            new GrantKeywordUntilEotTargetEffectDef { Keyword = "Flying", TargetFilter = "creature" },
            host: target, controller: _alice, target: target);

        CombatAbilities.HasFlying(target).Should().BeFalse("CR 608.2b — illegal target fizzles the grant");
    }

    // ── becomes_artifact_target ────────────────────────────────────────────────

    [Fact]
    public async Task BecomesArtifact_AddsArtifactType_UntilEndOfTurn()
    {
        var fx = new ContinuousEffectsService(_bus);
        var target = BattlefieldCreature("Bear", 2, 2, fx);
        fx.Compute(target).Types.Should().NotContain(CardType.Artifact);

        await ResolveAsync(
            new BecomesArtifactTargetEffectDef { TargetFilter = "permanent" },
            host: target, controller: _alice, target: target);

        fx.Compute(target).Types.Should().Contain(CardType.Artifact,
            "CR 613.1d — becomes an artifact in addition to its other types");
        fx.Compute(target).Types.Should().Contain(CardType.Creature,
            "'in addition to its other types' — the printed Creature type remains");

        fx.ExpireEndOfTurn();
        fx.Compute(target).Types.Should().NotContain(CardType.Artifact,
            "the type-add ends at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task BecomesArtifact_NonlandPermanentFilter_FizzlesOnLand()
    {
        var fx = new ContinuousEffectsService(_bus);
        var land = new Land("Forest") { Owner = _alice, Controller = _alice };
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        land.ActiveEffects = fx;

        await ResolveAsync(
            new BecomesArtifactTargetEffectDef { TargetFilter = "nonland_permanent" },
            host: land, controller: _alice, target: land);

        fx.Compute(land).Types.Should().NotContain(CardType.Artifact,
            "CR 608.2b — a Land does not match nonland_permanent, so the verb fizzles");
    }
}
