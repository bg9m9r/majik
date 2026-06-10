using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BigGameHunterFactory"/> — Creature — Human Rebel
/// Assassin {1}{B}{B} 1/1 with an ETB "destroy target creature with power 4 or
/// greater. It can't be regenerated." trigger.
///
/// Madness {B} (CR 702.35) is intrinsic (MadnessCatalog + Fx.DiscardCard) and
/// is NOT exercised here — the funnel + catalog cover the mechanic.
///
/// Covers:
///   - Card identity (name, cost, type, subtypes, P/T, owner / controller).
///   - ETB destroy-trigger shape (1..1 "power 4 or greater").
///   - Candidate gatherer includes power-4+, excludes power-3-or-less.
///   - Resolve: power-4+ creature → destroyed.
///   - Resolve: power-3 creature picked (illegal) → clean no-op.
///   - Resolve: target left battlefield → clean no-op.
///   - Resolve: no chosen target → clean no-op.
///   - Full cast path: ETB destroy fires on enter and resolves.
/// </summary>
[Trait("Color", "B")]
public class BigGameHunterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    private static TriggeredAbility GetEtb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count > 0);

    private static Creature CreatureOn(Player p, string name, int power, int toughness, string cost = "{4}{G}")
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [Fact]
    public void BigGameHunter_IsHumanRebelAssassin_At1BB_OneOne()
    {
        var c = BigGameHunterFactory.Create(_alice);

        c.Name.Should().Be("Big Game Hunter");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rebel).Should().BeTrue();
        c.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Exactly one triggered ability: the ETB destroy.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void BigGameHunter_Etb_HasPower4OrGreaterTargetRequest()
    {
        var c = BigGameHunterFactory.Create(_alice);
        var etb = GetEtb(c);

        etb.TargetRequests.Should().ContainSingle();
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("power 4 or greater");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ── Candidate gatherer ──────────────────────────────────────────────────

    [Fact]
    public void BigGameHunter_Etb_CandidateGatherer_IncludesPower4Plus_ExcludesSmaller()
    {
        var bigBeast = CreatureOn(_bob, "Craw Wurm", 6, 4);
        var exactly4 = CreatureOn(_bob, "Hill Giant", 4, 3);
        var smallBear = CreatureOn(_bob, "Grizzly Bears", 2, 2, "{1}{G}");
        var three = CreatureOn(_bob, "Trained Armodon", 3, 3, "{1}{G}{G}");

        var bgh = BigGameHunterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bgh); bgh.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(bgh);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(_bus));
        var candidates = etb.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(bigBeast);
        candidates.Should().Contain(exactly4);
        candidates.Should().NotContain(smallBear);
        candidates.Should().NotContain(three);
    }

    // ── Resolve paths ───────────────────────────────────────────────────────

    [Fact]
    public void BigGameHunter_Etb_DestroysPower4PlusCreature()
    {
        var beast = CreatureOn(_bob, "Craw Wurm", 6, 4);

        var bgh = BigGameHunterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bgh); bgh.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(bgh);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { beast } });
        foreach (var e in etb.Effects) e.Execute();

        beast.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(beast);
    }

    [Fact]
    public void BigGameHunter_Etb_Power3Target_NoOp()
    {
        // power < 4 picked (illegal) — verify the resolution guard (CR 608.2b).
        var armodon = CreatureOn(_bob, "Trained Armodon", 3, 3, "{1}{G}{G}");

        var bgh = BigGameHunterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bgh); bgh.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(bgh);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { armodon } });
        foreach (var e in etb.Effects) e.Execute();

        armodon.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(armodon);
    }

    [Fact]
    public void BigGameHunter_Etb_TargetLeftBattlefield_NoOp()
    {
        var beast = CreatureOn(_bob, "Craw Wurm", 6, 4);

        var bgh = BigGameHunterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bgh); bgh.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(bgh);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { beast } });

        // Beast bounces before resolution (CR 608.2b).
        _bob.Zones.Battlefield.RemoveCard(beast);
        _bob.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        foreach (var e in etb.Effects) e.Execute();

        beast.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(beast);
    }

    [Fact]
    public void BigGameHunter_Etb_NoChosenTarget_NoOp()
    {
        var bgh = BigGameHunterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bgh); bgh.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(bgh);

        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    // ── Full cast path: ETB destroy fires on enter ──────────────────────────

    [Fact]
    public async Task Cast_EtbDestroyTriggerFires_AndResolves()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);

        var bgh = BigGameHunterFactory.Create(_alice, triggers);
        bgh.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bgh);

        var beast = CreatureOn(_bob, "Craw Wurm", 6, 4);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

        await flow.CastAsync(
            _alice, bgh,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        resolver.ResolveTop(stack);

        bgh.Zone.Should().Be(ZoneType.Battlefield);

        // The ETB destroy trigger fired on the CardMovedEvent into play.
        triggers.PendingCount.Should().Be(1);

        var etb = GetEtb(bgh);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { beast } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        beast.Zone.Should().Be(ZoneType.Graveyard);
    }
}
