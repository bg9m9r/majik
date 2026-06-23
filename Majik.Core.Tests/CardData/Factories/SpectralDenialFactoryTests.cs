using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
/// End-to-end tests for Spectral Denial (Marvel's Spider-Man, {X}{U}, Instant).
///
/// Oracle text (verified via Scryfall 2026-06):
///   "This spell costs {1} less to cast for each creature you control with
///    power 4 or greater.
///    Counter target spell unless its controller pays {X}."
///
/// Covers only the card's UNIQUE behaviour (the contract test covers dispatch
/// + well-formedness automatically):
///   - Identity: Instant, {X}{U}, blue (the non-vanilla cost).
///   - SpellDefinition shape: HasVariableX, one 1..1 "target spell" request.
///   - Cost reduction (CR 117.7): {1} less per creature you control with
///     power 4 or greater; coloured/X pips untouched; floor at zero.
///   - Counter unless pay {X}: controller can't pay → countered (CR 701.5).
///   - Auto-pay path: controller has {X} → spell resolves uncountered (CR 118.4).
/// </summary>
[Trait("Color", "U")]
public class SpectralDenialFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpectralDenialFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_IsInstant_AtXU_Blue()
    {
        var sd = SpectralDenialFactory.Create(_alice);

        sd.Name.Should().Be("Spectral Denial");
        sd.HasType(CardType.Instant).Should().BeTrue();
        sd.ManaCost.Should().Be("{X}{U}");
        CardColors.GetColors(sd).Should().Contain(ManaColor.Blue);
        sd.Owner.Should().BeSameAs(_alice);
        sd.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_OneTargetSpellRequest()
    {
        var def = SpectralDenialFactory.BuildSpellDefinition(o => o, _stack);

        def.HasVariableX.Should().BeTrue("X is declared at cast time (CR 107.3)");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    // ── Cost reduction (CR 117.7) ───────────────────────────────────────────

    [Fact]
    public void CostReduction_NoQualifyingCreatures_LeavesGenericUnchanged()
    {
        var sd = SpectralDenialFactory.Create(_alice);

        // Printed generic of {X}{U} is 0 (X is variable, U is coloured); the
        // reducer floors at zero. With no power-4 creatures the effective
        // generic stays 0 and the {U} pip is untouched (CR 117.7c).
        var cost = CostReduction.GetEffectiveCost(sd, _alice);

        cost.Generic.Should().Be(0);
        cost.Blue.Should().Be(1,
            because: "the {U} coloured pip is never reduced (CR 117.7c)");
    }

    [Fact]
    public void CostReduction_CountsOnlyPower4OrGreaterCreatures()
    {
        // Two qualifying creatures (power >= 4) and one that does not qualify.
        AddCreatureToBattlefield(_alice, "Big A", 4, 4);
        AddCreatureToBattlefield(_alice, "Big B", 5, 1);
        AddCreatureToBattlefield(_alice, "Small", 2, 2);

        var sd = SpectralDenialFactory.Create(_alice);

        // Use a synthetic card with printed generic high enough to observe the
        // reduction directly: the reducer is per-instance {1}, so two qualifying
        // creatures remove {2} of generic mana.
        var probe = new Instant("Probe", "{5}{U}") { Owner = _alice, Controller = _alice };
        foreach (var ab in sd.Abilities.OfType<CostReductionAbility>())
        {
            probe.AddAbility(ab);
        }

        var cost = CostReduction.GetEffectiveCost(probe, _alice);

        cost.Generic.Should().Be(3,
            because: "two creatures with power >= 4 each reduce {1}; the power-2 creature does not count (CR 117.7)");
        cost.Blue.Should().Be(1,
            because: "the coloured pip is untouched (CR 117.7c)");
    }

    // ── Counter unless pay {X} ──────────────────────────────────────────────

    [Fact]
    public async Task CountersTargetSpell_WhenControllerCannotPayX()
    {
        var sd = SpectralDenialInHand(_alice);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sd,
            SpectralDenialFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob has no {3} to pay; the unless-pay rider fails and Spectral Denial counters (CR 701.5)");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerAutoPaysX()
    {
        var sd = SpectralDenialInHand(_alice);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has {2} available and X is chosen as 2 — he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var agent = new ScriptedAgent();
        agent.QueueX(2);
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, sd,
            SpectralDenialFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {2} = the chosen X; the counter no-ops (CR 118.4)");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Instant SpectralDenialInHand(Player owner)
    {
        var c = SpectralDenialFactory.Create(owner);
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }

    private static void AddCreatureToBattlefield(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{2}{G}", power, toughness) { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
    }
}
