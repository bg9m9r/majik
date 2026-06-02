using FluentAssertions;
using Majik.Core.Abilities;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShriekmawFactory"/> — Creature — Elemental {5}{B}
/// 3/2 with Fear, an ETB "destroy target nonartifact, nonblack creature"
/// trigger, and Evoke {1}{B}.
///
/// Covers:
///   - Card identity (name, cost, type, subtype, P/T, owner / controller).
///   - Keyword markers (Fear + Evoke).
///   - ETB destroy-trigger shape (1..1 "nonartifact, nonblack creature").
///   - Candidate gatherer excludes artifacts + black creatures.
///   - Resolve: nonblack nonartifact creature → destroyed.
///   - Resolve: black creature picked (illegal) → clean no-op.
///   - Resolve: artifact creature picked (illegal) → clean no-op.
///   - Resolve: target left battlefield → clean no-op.
///   - Resolve: no chosen target → clean no-op.
///   - Evoke cast path: sacrifice trigger fires + ETB destroy fires.
///   - Normal cast path: no sacrifice; ETB destroy still fires.
/// </summary>
[Trait("Color", "B")]
public class ShriekmawFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count > 0);

    private static Creature BearOn(Player p, string name = "Grizzly Bears", string cost = "{1}{G}")
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(p);
        bear.SetController(p);
        p.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }

    /// <summary>
    /// Build an artifact creature on <paramref name="p"/>'s battlefield —
    /// a <see cref="Creature"/> with the Artifact card type added
    /// (CR 301.1 / 302.1). Ornithopter ({0}, colourless, 0/2).
    /// </summary>
    private static Creature ArtifactCreatureOn(Player p)
    {
        var orn = new Creature("Ornithopter", "{0}", 0, 2);
        orn.AddCardType(CardType.Artifact);
        orn.SetOwner(p);
        orn.SetController(p);
        p.Zones.Battlefield.AddCard(orn);
        orn.SetZone(ZoneType.Battlefield);
        return orn;
    }

    // ── Shape ───────────────────────────────────────────────────────────────

    [Fact]
    public void Shriekmaw_IsElemental_At5B_ThreeTwo_WithFearAndEvoke()
    {
        var c = ShriekmawFactory.Create(_alice);

        c.Name.Should().Be("Shriekmaw");
        c.ManaCost.Should().Be("{5}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Fear", "Evoke" });

        // Two triggered abilities: ETB destroy + Evoke sacrifice.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Shriekmaw_Etb_HasNonartifactNonblackCreatureTargetRequest()
    {
        var c = ShriekmawFactory.Create(_alice);
        var etb = GetEtb(c);

        etb.TargetRequests.Should().ContainSingle();
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonartifact").And.Contain("nonblack");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Shriekmaw_BuildEvokeCost_IsOneB()
    {
        var cost = ShriekmawFactory.BuildEvokeCost();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{1}{B}"));
        cost.PitchColor.Should().BeNull();
    }

    // ── Candidate gatherer ──────────────────────────────────────────────────

    [Fact]
    public void Shriekmaw_Etb_CandidateGatherer_ExcludesArtifactsAndBlack()
    {
        var greenBear = BearOn(_bob, "Grizzly Bears", "{1}{G}");
        var blackZombie = new Creature("Walking Corpse", "{2}{B}", 2, 2);
        blackZombie.SetOwner(_bob); blackZombie.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(blackZombie); blackZombie.SetZone(ZoneType.Battlefield);

        var artifactGolem = ArtifactCreatureOn(_bob);

        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack(_bus));
        var candidates = etb.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(greenBear);
        candidates.Should().NotContain(blackZombie);
        candidates.Should().NotContain(artifactGolem);
    }

    // ── Resolve paths ───────────────────────────────────────────────────────

    [Fact]
    public void Shriekmaw_Etb_DestroysNonblackNonartifactCreature()
    {
        var bear = BearOn(_bob);

        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Shriekmaw_Etb_BlackCreatureTarget_NoOp()
    {
        var blackZombie = new Creature("Walking Corpse", "{2}{B}", 2, 2);
        blackZombie.SetOwner(_bob); blackZombie.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(blackZombie); blackZombie.SetZone(ZoneType.Battlefield);

        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { blackZombie } });
        foreach (var e in etb.Effects) e.Execute();

        blackZombie.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(blackZombie);
    }

    [Fact]
    public void Shriekmaw_Etb_ArtifactCreatureTarget_NoOp()
    {
        var artifactGolem = ArtifactCreatureOn(_bob);

        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);
        // Force the illegal pick to verify the resolution guard.
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifactGolem } });
        foreach (var e in etb.Effects) e.Execute();

        artifactGolem.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(artifactGolem);
    }

    [Fact]
    public void Shriekmaw_Etb_TargetLeftBattlefield_NoOp()
    {
        var bear = BearOn(_bob);

        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        // Bear bounces before resolution (CR 608.2b).
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Shriekmaw_Etb_NoChosenTarget_NoOp()
    {
        var shriek = ShriekmawFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(shriek); shriek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(shriek);

        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    // ── Cast paths (Evoke vs normal) ────────────────────────────────────────

    private readonly EventBus _bus = new();

    [Fact]
    public async Task CastForEvoke_SacrificeTriggerFires_AndDestroyResolves()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);

        var shriek = ShriekmawFactory.Create(_alice, triggers);
        shriek.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(shriek);

        var bear = BearOn(_bob);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

        await flow.CastAsync(
            _alice, shriek,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: ShriekmawFactory.BuildEvokeCost());

        resolver.ResolveTop(stack);

        shriek.Zone.Should().Be(ZoneType.Battlefield);
        shriek.EvokeWasPaid.Should().BeTrue();

        // Two triggers fired on the ETB CardMovedEvent: destroy + sacrifice.
        triggers.PendingCount.Should().Be(2);

        var etb = GetEtb(shriek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        // ETB destroy fired.
        bear.Zone.Should().Be(ZoneType.Graveyard);
        // Evoke sacrifice fired.
        shriek.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(shriek);
    }

    [Fact]
    public async Task CastForNormalMana_OnlyDestroyTriggerFires_NoSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);
        var triggers = new TriggerManager(stack, _bus);
        var resolver = new StackResolver(_bus, zones);

        var shriek = ShriekmawFactory.Create(_alice, triggers);
        shriek.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(shriek);

        var bear = BearOn(_bob);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

        await flow.CastAsync(
            _alice, shriek,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        resolver.ResolveTop(stack);

        shriek.Zone.Should().Be(ZoneType.Battlefield);
        shriek.EvokeWasPaid.Should().BeFalse();

        // Only the ETB destroy trigger is pending — the evoke sacrifice
        // trigger's intervening-if (EvokeWasPaid) failed (CR 603.4).
        triggers.PendingCount.Should().Be(1);

        var etb = GetEtb(shriek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        // Shriekmaw stays on the battlefield (no sacrifice).
        shriek.Zone.Should().Be(ZoneType.Battlefield);
    }
}
