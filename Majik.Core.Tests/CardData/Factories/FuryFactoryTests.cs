using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Fury (Modern Horizons 2, {3}{R}). Mirrors
/// <see cref="SolitudeFactoryTests"/>: both cast paths (normal + evoke) plus
/// shape + dispatch coverage. The ETB trigger deals a FIXED 4 damage (post-
/// errata oracle) DIVIDED as the controller's agent announces at stack entry
/// (CR 601.2d / CR 119.4) via the triggered/activated divide-damage seam —
/// or per the caller-supplied distribute Func (deterministic in tests).
/// </summary>
[Trait("Color", "R")]
public class FuryFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FuryFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private Dictionary<Player, IPlayerAgent> Agents(IPlayerAgent a) =>
        new() { [_alice] = a };

    // ── Shape + dispatch ──────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var fury = FuryFactory.Create(_alice);

        fury.Name.Should().Be("Fury");
        fury.BasePower.Should().Be(3);
        fury.BaseToughness.Should().Be(3);
        fury.HasType(CardType.Creature).Should().BeTrue();
        fury.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        fury.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywords = fury.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Double strike", "Evoke" });

        // Two triggered abilities: ETB damage + Evoke sacrifice.
        fury.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Create_DispatchesThroughNamedFactory()
    {
        var c = NamedCardFactory.Create("Fury", _alice);
        c.Should().BeAssignableTo<Creature>();
        c.Name.Should().Be("Fury");
    }

    [Fact]
    public void DamageTrigger_DeclaresDivisionSpecForFour()
    {
        var fury = FuryFactory.Create(_alice);
        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);

        damageTrigger.DamageDivision.Should().NotBeNull(
            "CR 601.2d — Fury announces a fixed-4 divide-damage at stack entry.");
        damageTrigger.DamageDivision!.TotalDamage.Should().Be(4);
        damageTrigger.TargetRequests[0].Description.Should()
            .Contain("creatures and/or planeswalkers");
    }

    // ── ETB divide-damage trigger (agent-driven, CR 601.2d) ───────────────────

    [Fact]
    public async Task CastNormal_AgentAnnouncedSplit_DealtVerbatim()
    {
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var fury = FuryFactory.Create(_alice);
        fury.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fury);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // ETB damage trigger: choose grizzly + bear, then announce 3/1.
        agent.QueueTriggerOrder(new ITriggeredAbility[]
        {
            fury.Abilities.OfType<TriggeredAbility>().First(t => t.TargetRequests.Count > 0),
        });
        agent.QueueTargets(new object[] { grizzly, bear });
        agent.QueueDamageDivision(3, 1); // 3 to grizzly, 1 to bear

        await _flow.CastAsync(
            _alice, fury,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, Ctx(), alternativeCost: null);

        _resolver.ResolveTop(_stack);

        fury.Zone.Should().Be(ZoneType.Battlefield);
        fury.EvokeWasPaid.Should().BeFalse();

        // Only ETB damage trigger pending (evoke sac dropped by intervening-if).
        _triggers.PendingCount.Should().Be(1);

        await _triggers.PutPendingTriggersOnStackAsync(_alice, Agents(agent), Ctx());
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);

        grizzly.Damage.Should().Be(3, "agent announced 3 on grizzly (CR 601.2d)");
        bear.Damage.Should().Be(1, "agent announced 1 on bear");
        fury.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task CastForEvoke_AgentSplit_DealtAndFurySacrificed()
    {
        var fury = FuryFactory.Create(_alice);
        fury.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fury);

        var pitch = new Creature("Mountain Lion", "R", 2, 1) { Owner = _alice };
        pitch.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitch);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var evokeCost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.Red, pitch);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        // Order the two ETB triggers (damage + evoke sacrifice); pick the
        // damage trigger first so its prompt drains first.
        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        var evokeTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 0 && t != damageTrigger);
        agent.QueueTriggerOrder(new ITriggeredAbility[] { damageTrigger, evokeTrigger });
        agent.QueueTargets(new object[] { grizzly, bear });
        agent.QueueDamageDivision(2, 2); // 2 to grizzly, 2 to bear

        await _flow.CastAsync(
            _alice, fury,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, Ctx(), alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        fury.Zone.Should().Be(ZoneType.Battlefield);
        fury.EvokeWasPaid.Should().BeTrue();
        pitch.Zone.Should().Be(ZoneType.Exile);

        _triggers.PendingCount.Should().Be(2);

        await _triggers.PutPendingTriggersOnStackAsync(_alice, Agents(agent), Ctx());
        while (!_stack.IsEmpty) _resolver.ResolveTop(_stack);

        grizzly.Damage.Should().Be(2);
        bear.Damage.Should().Be(2);
        // Evoke sacrifice trigger fired → Fury in graveyard.
        fury.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DefaultEvenSplit_NoAgentDivision_SplitsFourEvenly()
    {
        // No division recorded (direct Resolve, no stack-entry prompt) → the
        // resolve body even-splits 4 over the chosen targets (CR 119.4):
        // two targets → 2, 2.
        var fury = FuryFactory.Create(_alice);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly, bear },
        });

        damageTrigger.Resolve();

        grizzly.Damage.Should().Be(2, "CR 119.4 — even split of 4 over two targets");
        bear.Damage.Should().Be(2);
    }

    [Fact]
    public void Distribute_Override_HonouredOverAgentPrompt()
    {
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var bear = new Creature("Runeclaw Bear", "1G", 2, 2)
            { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        IReadOnlyDictionary<Permanent, int> Distribute(IReadOnlyList<Permanent> ts, int total)
        {
            total.Should().Be(4);
            return new Dictionary<Permanent, int> { [grizzly] = 3, [bear] = 1 };
        }

        var fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly, bear },
        });

        damageTrigger.Resolve();

        grizzly.Damage.Should().Be(3);
        bear.Damage.Should().Be(1);
    }

    [Fact]
    public void EtbDamage_DealsToPlaneswalkerViaRemoveLoyalty()
    {
        var pw = new Planeswalker("Test Walker", "{2}{B}", startingLoyalty: 5)
            { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        IReadOnlyDictionary<Permanent, int> Distribute(IReadOnlyList<Permanent> ts, int total) =>
            new Dictionary<Permanent, int> { [pw] = total };

        var fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        damageTrigger.Resolve();

        pw.Loyalty.Should().Be(1, "4 loyalty removed (5 - 4), CR 306.7");
    }

    [Fact]
    public void EtbDamage_DealsToEffectivePlaneswalkerBackFace_AsLoyaltyRemoval()
    {
        // CR 711 / 306.7 — a creature-front transform DFC flipped to its
        // planeswalker BACK face absorbs Fury's noncombat damage as transient
        // loyalty removal rather than marked creature damage.
        var ces = new ContinuousEffectsService();
        var ral = RalMonsoonMageFactory.Create(_bob);
        ral.ActiveEffects = ces;
        ral.SetController(_bob);
        ral.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ral);
        ral.MdfcState!.Transform(); // → Ral, Leyline Prodigy, loyalty 2 PW back
        ral.IsEffectivePlaneswalker().Should().BeTrue("the back face is an effective planeswalker");

        IReadOnlyDictionary<Permanent, int> Distribute(IReadOnlyList<Permanent> ts, int total) =>
            new Dictionary<Permanent, int> { [ral] = 1 };

        var fury = FuryFactory.Create(_alice, Distribute);
        fury.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(fury);

        var damageTrigger = fury.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count > 0);
        damageTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ral },
        });

        damageTrigger.Resolve();

        ral.GetEffectiveLoyalty().Should().Be(1, "1 damage removes 1 loyalty from the back-face PW (CR 306.7)");
        ral.Damage.Should().Be(0, "noncombat damage to an effective PW is loyalty removal, not marked creature damage");
    }
}
