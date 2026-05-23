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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Dig Through Time (Khans of Tarkir, {6}{U}{U}).
/// Instant — "Delve. Look at top 7, put 2 into hand, rest on bottom."
/// </summary>
public class DigThroughTimeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public DigThroughTimeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void DigThroughTime_Identity()
    {
        var c = DigThroughTimeFactory.Create(_alice);

        c.Name.Should().Be("Dig Through Time");
        c.ManaCost.Should().Be("{6}{U}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().Be(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DigThroughTime()
    {
        var card = NamedCardFactory.Create("Dig Through Time", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Dig Through Time");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{6}{U}{U}");
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public async Task DigThroughTime_CastWithDelve_ExilesCards_AndDigs()
    {
        // 6 cards in graveyard for delve, plus a 7-card library so the dig
        // can peek the full 7.
        var fodder = SeedGraveyard(_alice, 6);
        var libCards = SeedLibrary(_alice, 7);
        var dig = SeedInstantInHand(_alice, "Dig Through Time", "{6}{U}{U}");

        var delve = new DelveCost(dig, fodder);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => DigThroughTimeFactory.BuildResolveEffect(_alice));

        var spell = await _flow.CastAsync(
            _alice, dig, def, agent, ctx,
            delveCost: delve);

        // All six graveyard cards exiled by delve.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().HaveCount(6);

        var handBefore = _alice.Zones.Hand.GetCards().Count();
        spell.Resolve();

        // Default selector: first two peeked → hand. Library shrinks by 2.
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 2);
        _alice.Zones.Hand.GetCards().Should().Contain(new[] { libCards[0], libCards[1] });

        // Library still has 5 cards (the rest, in original peek order at bottom).
        _alice.Zones.Library.GetCards().Count().Should().Be(5);
    }

    [Fact]
    public async Task DigThroughTime_CastWithoutDelve_LeavesGraveyardAlone()
    {
        var fodder = SeedGraveyard(_alice, 3);
        SeedLibrary(_alice, 7);
        var dig = SeedInstantInHand(_alice, "Dig Through Time", "{6}{U}{U}");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.Main, _stack);
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => DigThroughTimeFactory.BuildResolveEffect(_alice));

        var spell = await _flow.CastAsync(_alice, dig, def, agent, ctx);

        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();

        spell.Resolve();
    }

    [Fact]
    public void DigThroughTime_DefaultSelector_HandFirstTwoBottomRest()
    {
        // Pure selector test — no cast flow needed.
        var peeked = Enumerable.Range(0, 7)
            .Select(i => (ICard)new Card($"P{i}", ""))
            .ToList();

        var (toHand, toBottom) = DigThroughTimeFactory.DefaultDigSelector(peeked);

        toHand.Should().HaveCount(2);
        toBottom.Should().HaveCount(5);
        toHand[0].Name.Should().Be("P0");
        toHand[1].Name.Should().Be("P1");
        toBottom[0].Name.Should().Be("P2");
        toBottom[4].Name.Should().Be("P6");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private Instant SeedInstantInHand(Player p, string name, string manaCost)
    {
        var s = new Instant(name, manaCost);
        s.SetOwner(p);
        s.SetZone(ZoneType.Hand);
        p.Zones.Hand.AddCard(s);
        return s;
    }

    private IReadOnlyList<ICard> SeedLibrary(Player p, int count)
    {
        var list = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
            list.Add(c);
        }
        return list;
    }
}
