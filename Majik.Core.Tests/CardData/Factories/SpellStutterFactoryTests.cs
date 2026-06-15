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
/// End-to-end tests for Spell Stutter (Modern Horizons 3, {1}{U}).
///
/// Oracle:
///   "Counter target spell unless its controller pays {2} plus an additional
///    {1} for each Faerie you control."
///
/// Coverage (the card's UNIQUE behaviour — the per-Faerie scaling unless-pay):
///   * Identity: {1}{U} Blue Instant, mana value 2.
///   * SpellDefinition shape: 1 "target spell" request.
///   * Resolve, no Faeries: controller can't pay {2} → countered.
///   * Resolve, no Faeries: controller pays exactly {2} → survives.
///   * Resolve, two Faeries controlled: base {2} + {2} = {4}; controller with
///     only {3} can't pay → countered (proves the per-Faerie scaling).
///   * Resolve, two Faeries controlled: controller with {4} pays → survives.
/// </summary>
[Trait("Color", "U")]
public class SpellStutterFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellStutterFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private Creature SeedFaerie(Player p, string name)
    {
        var faerie = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Faerie });
        faerie.SetOwner(p);
        faerie.SetController(p);
        faerie.SetZone(ZoneType.Battlefield);
        p.Zones.Battlefield.AddCard(faerie);
        return faerie;
    }

    private async Task CastStutterAt(Majik.Core.Spells.Spell target)
    {
        var stutter = SpellStutterFactory.Create(_alice);
        stutter.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(stutter);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)target });
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(
            _alice, stutter,
            SpellStutterFactory.BuildSpellDefinition(_alice, o => o, _stack),
            agent, Ctx(),
            alternativeCost: null);
    }

    private Majik.Core.Spells.Spell PushBobBolt(out Instant bolt)
    {
        bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(spell);
        return spell;
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_AtOneU()
    {
        var stutter = SpellStutterFactory.Create(_alice);

        stutter.Name.Should().Be("Spell Stutter");
        stutter.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(stutter).Should().Contain(ManaColor.Blue);
        stutter.ManaCost.Should().Be("{1}{U}");
        stutter.ManaCostValue.TotalValue.Should().Be(2,
            "Spell Stutter has mana value 2 ({1}{U})");
        stutter.Owner.Should().BeSameAs(_alice);
        stutter.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = SpellStutterFactory.BuildSpellDefinition(_alice, o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
    }

    // ── Resolve: no Faeries → base {2} ───────────────────────────────────────

    [Fact]
    public async Task Resolve_NoFaeries_ControllerCantPayTwo_TargetCountered()
    {
        var spell = PushBobBolt(out var bolt);
        // Bob has no mana — can't pay {2}.
        await CastStutterAt(spell);
        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "no Faeries → unless-cost is {2}; Bob couldn't pay → countered (CR 701.5)");
    }

    [Fact]
    public async Task Resolve_NoFaeries_ControllerPaysTwo_TargetSurvives()
    {
        var spell = PushBobBolt(out var bolt);
        // Bob has exactly {2} — enough to pay the base unless-cost.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        await CastStutterAt(spell);
        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "no Faeries → unless-cost is {2}; Bob paid {2} → counter no-ops (CR 118.4)");
    }

    // ── Resolve: two Faeries → {2} + {2} = {4} (the unique per-Faerie scale) ──

    [Fact]
    public async Task Resolve_TwoFaeries_ControllerWithThree_CantPayFour_TargetCountered()
    {
        SeedFaerie(_alice, "Pestermite");
        SeedFaerie(_alice, "Spellstutter Sprite");

        var spell = PushBobBolt(out var bolt);
        // Bob has only {3} — base {2} + {1}×2 Faeries = {4} required.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        await CastStutterAt(spell);
        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "2 Faeries → unless-cost {2}+{2}={4}; Bob's {3} is short → countered");
    }

    [Fact]
    public async Task Resolve_TwoFaeries_ControllerWithFour_PaysFour_TargetSurvives()
    {
        SeedFaerie(_alice, "Pestermite");
        SeedFaerie(_alice, "Spellstutter Sprite");

        var spell = PushBobBolt(out var bolt);
        // Bob has {4} — exactly the scaled unless-cost.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(4));

        await CastStutterAt(spell);
        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "2 Faeries → unless-cost {4}; Bob paid {4} → counter no-ops (CR 118.4)");
    }
}
