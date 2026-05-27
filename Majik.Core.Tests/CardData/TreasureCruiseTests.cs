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
/// Tests for Treasure Cruise (Khans of Tarkir, {7}{U}).
/// Sorcery — "Delve. Draw three cards."
///
/// Covers:
///   - Card shape (name, type, mana cost).
///   - "Delve" keyword marker.
///   - NamedCardFactory dispatch.
///   - Cast with delve: N cards in graveyard, pay reduced cost, N exiled.
///   - Cast without delve: full cost paid, no graveyard exile.
///   - Resolve effect draws 3 cards.
///   - Cap-at-available: requesting more delve than graveyard has → CanPay false.
/// </summary>
public class TreasureCruiseTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public TreasureCruiseTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void TreasureCruise_Identity()
    {
        var c = TreasureCruiseFactory.Create(_alice);

        c.Name.Should().Be("Treasure Cruise");
        c.ManaCost.Should().Be("{7}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().Be(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TreasureCruise()
    {
        var card = NamedCardFactory.Create("Treasure Cruise", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Treasure Cruise");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{7}{U}");
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public async Task TreasureCruise_CastWithDelve_ExilesChosenCards_AndReducesCost()
    {
        // Setup: 7 cards in Alice's graveyard, Cruise in hand, library big enough to draw 3.
        var fodder = SeedGraveyard(_alice, 7);
        var cruise = SeedSorceryInHand(_alice, "Treasure Cruise", "{7}{U}");

        // Stuff library so the resolve effect can draw 3.
        SeedLibrary(_alice, 5);

        // Cast with delve: exile all 7 graveyard cards → cost becomes {U}.
        var delve = new DelveCost(cruise, fodder);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => TreasureCruiseFactory.BuildResolveEffect(_alice));

        var spell = await _flow.CastAsync(
            _alice, cruise, def, agent, ctx,
            delveCost: delve);

        // Delve payment exiled all 7 graveyard cards.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().HaveCount(7);
        foreach (var c in fodder) c.Zone.Should().Be(ZoneType.Exile);

        // Cruise made it to the stack.
        cruise.Zone.Should().Be(ZoneType.Stack);

        // Resolve: should draw 3.
        var handBefore = _alice.Zones.Hand.GetCards().Count();
        spell.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 3);
    }

    [Fact]
    public async Task TreasureCruise_CastWithoutDelve_NoGraveyardExile()
    {
        var fodder = SeedGraveyard(_alice, 3);
        var cruise = SeedSorceryInHand(_alice, "Treasure Cruise", "{7}{U}");
        SeedLibrary(_alice, 5);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => TreasureCruiseFactory.BuildResolveEffect(_alice));

        // No delveCost arg — full mana cost path.
        var spell = await _flow.CastAsync(_alice, cruise, def, agent, ctx);

        // Graveyard is untouched: no delve, no exile.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        foreach (var c in fodder) c.Zone.Should().Be(ZoneType.Graveyard);

        spell.Resolve();
    }

    [Fact]
    public void TreasureCruise_CapAtAvailable_TooFewCardsRejected()
    {
        // Cruise {7}{U}. Graveyard has only 2 cards. Asking to delve 3 must fail.
        var cruise = TreasureCruiseFactory.Create(_alice);
        var available = SeedGraveyard(_alice, 2);

        // Try to delve 3 by including a phantom card (still in hand) — illegal.
        var phantom = new Card("Phantom", "");
        phantom.SetOwner(_alice);
        phantom.SetZone(ZoneType.Hand);

        var bad = new DelveCost(cruise, new ICard[] { available[0], available[1], phantom });

        bad.CanPay(_alice, Majik.Core.ValueObjects.ManaCost.Parse("{7}{U}")).Should().BeFalse();
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

    private Sorcery SeedSorceryInHand(Player p, string name, string manaCost)
    {
        var s = new Sorcery(name, manaCost);
        s.SetOwner(p);
        s.SetZone(ZoneType.Hand);
        p.Zones.Hand.AddCard(s);
        return s;
    }

    private void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }
}
