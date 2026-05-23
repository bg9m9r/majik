using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Murktide Regent (Modern Horizons 2, {3}{U}{U}).
///
/// Covers:
///   - Card shape (name, type, subtype, P/T, mana cost).
///   - NamedCardFactory dispatch.
///   - Flying + Delve marker keywords.
///   - ETB trigger structure (target instant/sorcery in graveyard).
///   - Cast without delve / without ETB exile → 0 counters.
///   - Cast with delve exiles + ETB exile → counter count = delve + 1.
///   - Cast with delve exiles + no ETB target → counter count = delve.
/// </summary>
public class MurktideRegentTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MurktideRegentTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void MurktideRegent_IsCreature_Dragon_3_3_AtCost3UU()
    {
        var murk = MurktideRegentFactory.Create(_alice);

        murk.Name.Should().Be("Murktide Regent");
        murk.ManaCost.Should().Be("{3}{U}{U}");
        murk.HasType(CardType.Creature).Should().BeTrue();
        murk.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        murk.BasePower.Should().Be(3);
        murk.BaseToughness.Should().Be(3);
        murk.Owner.Should().Be(_alice);
    }

    [Fact]
    public void MurktideRegent_HasFlyingAndDelveKeywords()
    {
        var murk = MurktideRegentFactory.Create(_alice);

        var keywords = murk.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Delve");
    }

    [Fact]
    public void MurktideRegent_Etb_PromptsForInstantOrSorceryInGraveyard()
    {
        // Structural check: a single TriggeredAbility with a TargetRequest
        // describing "target instant or sorcery card in a graveyard".
        var murk = MurktideRegentFactory.Create(_alice);

        var triggers = murk.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MurktideRegent()
    {
        var card = NamedCardFactory.Create("Murktide Regent", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Murktide Regent");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain(new[] { "Flying", "Delve" });
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public async Task MurktideRegent_CastNormal_NoDelve_NoEtbExile_GetsZeroCounters()
    {
        // Cast Murktide normally (no DelveCost), and on the ETB trigger
        // we supply no chosen target. The result: 0 counters, stays 3/3.
        var murk = MurktideInHand(_alice);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, murk,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        _resolver.ResolveTop(_stack);
        murk.Zone.Should().Be(ZoneType.Battlefield);

        // ETB trigger pending. Leave it with no chosen targets (no legal
        // graveyard candidates) — the effect's recheck no-ops the exile.
        var etb = murk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        murk.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no delve was paid and no ETB exile target was chosen");
        murk.BasePower.Should().Be(3);
        murk.BaseToughness.Should().Be(3);
    }

    [Fact]
    public async Task MurktideRegent_CastWithTwoDelve_PlusEtbExile_GetsThreeCounters()
    {
        // Setup: 2 cards in Alice's graveyard for delve, plus a Lightning
        // Bolt in Bob's graveyard to serve as the ETB exile target.
        var fodder = SeedGraveyard(_alice, 2);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);

        var murk = MurktideInHand(_alice);

        var delve = new DelveCost(murk, fodder);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, murk,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            delveCost: delve);

        // Delve paid: 2 cards exiled, stamp landed on Murktide.
        _alice.Zones.Exile.GetCards().Should().HaveCount(2);
        murk.PendingDelveExiledCount.Should().Be(2);

        _resolver.ResolveTop(_stack);
        murk.Zone.Should().Be(ZoneType.Battlefield);

        // Wire the ETB target = the Bolt in Bob's graveyard, then resolve.
        var etb = murk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // ETB exiled the Bolt.
        bolt.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bolt);

        // 2 delve + 1 ETB exile = 3 +1/+1 counters → 6/6 effectively.
        murk.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "2 delve-exiled cards + 1 ETB-exiled card = 3 counters per CR 122.1g");

        // Stamp consumed so re-entry doesn't double-count.
        murk.PendingDelveExiledCount.Should().BeNull();
    }

    [Fact]
    public async Task MurktideRegent_CastWithMaxDelve_NoEtbTarget_GetsDelveCounters()
    {
        // Murktide costs {3}{U}{U} — generic = 3, so max delve = 3.
        // Setup: 3 cards in Alice's graveyard for delve. No instants or
        // sorceries in any graveyard → ETB trigger has no legal target.
        // 3 delve + 0 ETB = 3 counters → effectively 6/6.
        var fodder = SeedGraveyard(_alice, 3);
        var murk = MurktideInHand(_alice);

        var delve = new DelveCost(murk, fodder);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, murk,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            delveCost: delve);

        _resolver.ResolveTop(_stack);
        murk.Zone.Should().Be(ZoneType.Battlefield);

        // ETB trigger with no chosen target → CR 603.10b illegal at
        // declaration in the strict rules, but the simulated path here
        // simply resolves with empty targets; the effect's recheck no-ops
        // the exile portion and still applies the delve-based counters.
        var etb = murk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // 3 delve + 0 ETB exile = 3 +1/+1 counters → 6/6 effectively.
        murk.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "3 delve-exiled cards + 0 ETB-exiled card = 3 counters per CR 122.1g");
    }

    [Fact]
    public async Task MurktideRegent_EtbExile_IgnoresNonInstantSorceryTargets()
    {
        // Defensive: if a non-instant/sorcery card is somehow supplied as
        // the ETB target (e.g. wrong agent pick), the recheck no-ops the
        // exile and only delve counters land.
        var fodder = SeedGraveyard(_alice, 2);

        // A creature card in Bob's graveyard — not legal for the trigger.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var murk = MurktideInHand(_alice);

        var delve = new DelveCost(murk, fodder);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, murk,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            delveCost: delve);

        _resolver.ResolveTop(_stack);

        var etb = murk.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // Bear still in Bob's graveyard — not exiled.
        bear.Zone.Should().Be(ZoneType.Graveyard);

        // Counter count = delve only.
        murk.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature MurktideInHand(Player owner)
    {
        var m = MurktideRegentFactory.Create(owner);
        m.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(m);
        return m;
    }

    private IReadOnlyList<ICard> SeedGraveyard(Player p, int count)
    {
        var list = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Yard{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Graveyard);
            p.Zones.Graveyard.AddCard(c);
            list.Add(c);
        }
        return list;
    }
}
