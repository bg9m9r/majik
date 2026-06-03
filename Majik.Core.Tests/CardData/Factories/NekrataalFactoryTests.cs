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
/// Tests for <see cref="NekrataalFactory"/> — Creature — Human Assassin
/// {2}{B}{B} 2/1 with First strike and an ETB "destroy target nonartifact,
/// nonblack creature. That creature can't be regenerated." trigger.
///
/// Covers:
///   - Card identity (name, cost, type, subtypes, P/T, owner / controller).
///   - First strike keyword marker.
///   - ETB destroy-trigger shape (1..1 "nonartifact, nonblack creature").
///   - Candidate gatherer excludes artifacts + black creatures.
///   - Resolve: nonblack nonartifact creature → destroyed.
///   - Resolve: black creature picked (illegal) → clean no-op.
///   - Resolve: artifact creature picked (illegal) → clean no-op.
///   - Resolve: target left battlefield → clean no-op.
///   - Resolve: no chosen target → clean no-op.
/// </summary>
[Trait("Color", "B")]
public class NekrataalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

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
    public void Nekrataal_IsHumanAssassin_At2BB_TwoOne_WithFirstStrike()
    {
        var c = NekrataalFactory.Create(_alice);

        c.Name.Should().Be("Nekrataal");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("First strike");

        // Exactly one triggered ability: the ETB destroy.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Nekrataal_Etb_HasNonartifactNonblackCreatureTargetRequest()
    {
        var c = NekrataalFactory.Create(_alice);
        var etb = GetEtb(c);

        etb.TargetRequests.Should().ContainSingle();
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonartifact").And.Contain("nonblack");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ── Candidate gatherer ──────────────────────────────────────────────────

    [Fact]
    public void Nekrataal_Etb_CandidateGatherer_ExcludesArtifactsAndBlack()
    {
        var greenBear = BearOn(_bob, "Grizzly Bears", "{1}{G}");
        var blackZombie = new Creature("Walking Corpse", "{2}{B}", 2, 2);
        blackZombie.SetOwner(_bob); blackZombie.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(blackZombie); blackZombie.SetZone(ZoneType.Battlefield);

        var artifactGolem = ArtifactCreatureOn(_bob);

        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(_bus));
        var candidates = etb.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(greenBear);
        candidates.Should().NotContain(blackZombie);
        candidates.Should().NotContain(artifactGolem);
    }

    // ── Resolve paths ───────────────────────────────────────────────────────

    [Fact]
    public void Nekrataal_Etb_DestroysNonblackNonartifactCreature()
    {
        var bear = BearOn(_bob);

        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Nekrataal_Etb_BlackCreatureTarget_NoOp()
    {
        var blackZombie = new Creature("Walking Corpse", "{2}{B}", 2, 2);
        blackZombie.SetOwner(_bob); blackZombie.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(blackZombie); blackZombie.SetZone(ZoneType.Battlefield);

        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { blackZombie } });
        foreach (var e in etb.Effects) e.Execute();

        blackZombie.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(blackZombie);
    }

    [Fact]
    public void Nekrataal_Etb_ArtifactCreatureTarget_NoOp()
    {
        var artifactGolem = ArtifactCreatureOn(_bob);

        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);
        // Force the illegal pick to verify the resolution guard.
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifactGolem } });
        foreach (var e in etb.Effects) e.Execute();

        artifactGolem.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(artifactGolem);
    }

    [Fact]
    public void Nekrataal_Etb_TargetLeftBattlefield_NoOp()
    {
        var bear = BearOn(_bob);

        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);
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
    public void Nekrataal_Etb_NoChosenTarget_NoOp()
    {
        var nek = NekrataalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(nek); nek.SetZone(ZoneType.Battlefield);

        var etb = GetEtb(nek);

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

        var nek = NekrataalFactory.Create(_alice, triggers);
        nek.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(nek);

        var bear = BearOn(_bob);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);

        await flow.CastAsync(
            _alice, nek,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        resolver.ResolveTop(stack);

        nek.Zone.Should().Be(ZoneType.Battlefield);

        // The ETB destroy trigger fired on the CardMovedEvent into play.
        triggers.PendingCount.Should().Be(1);

        var etb = GetEtb(nek);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        triggers.PutPendingTriggersOnStack(_alice);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        bear.Zone.Should().Be(ZoneType.Graveyard);
    }
}
