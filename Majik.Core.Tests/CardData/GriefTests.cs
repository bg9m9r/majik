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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Grief (Modern Horizons 2). Exercises both cast
/// paths (normal + evoke) and asserts the on-resolution triggers behave per
/// CR 702.74 (Evoke), CR 701.8 (Discard), CR 701.16 (Reveal), and Grief's
/// printed ETB reveal-and-discard trigger.
///
/// Mirrors <see cref="SolitudeFactoryTests"/> — the MH2 incarnation cycle
/// shares the same Evoke alt-cost + evoke-sacrifice scaffolding; only the
/// printed ETB differs.
/// </summary>
public class GriefTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GriefTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Shape ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasCorrectShape()
    {
        var grief = GriefFactory.Create(_alice);

        grief.Name.Should().Be("Grief");
        grief.BasePower.Should().Be(3);
        grief.BaseToughness.Should().Be(2);
        grief.HasType(CardType.Creature).Should().BeTrue();
        grief.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        grief.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywordNames = grief.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Menace", "Evoke" });

        // Two triggered abilities: ETB reveal-and-discard + Evoke sacrifice.
        grief.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchesGrief()
    {
        var grief = NamedCardFactory.Create("Grief", _alice);

        grief.Should().BeOfType<Creature>();
        grief.Name.Should().Be("Grief");
        var creature = (Creature)grief;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(2);
        creature.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        creature.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();
        creature.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Menace");
    }

    // ── Cast paths ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CastForEvoke_RevealsHandAndDiscardsNonland_ThenSacrifices()
    {
        // Setup: Grief in Alice's hand, a Swamp-flavoured pitch card.
        var grief = GriefInHand(_alice);
        var pitchCard = new Creature("Vampire Lacerator", "B", 2, 2) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        // Bob's hand: one land + one nonland. v1 picks the first nonland.
        var bobLand = NamedCardFactory.Create("Swamp", _bob);
        bobLand.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobLand);
        var bobSpell = new Creature("Tarmogoyf", "1G", 0, 1) { Owner = _bob };
        bobSpell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobSpell);

        // Cast Grief via Evoke (pitch the black creature; no mana paid).
        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Black, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, grief,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        // Spell resolves: alt-cost cleanup flips EvokeWasPaid and exiles pitch.
        _resolver.ResolveTop(_stack);

        grief.Zone.Should().Be(ZoneType.Battlefield);
        grief.EvokeWasPaid.Should().BeTrue();
        pitchCard.Zone.Should().Be(ZoneType.Exile);

        // Two triggers fired on the ETB CardMovedEvent: discard target + sac.
        _triggers.PendingCount.Should().Be(2);

        // Point the discard trigger at Bob.
        var griefTriggers = grief.Abilities.OfType<TriggeredAbility>().ToList();
        var discardTrigger = griefTriggers.First(t => t.TargetRequests.Count > 0);
        discardTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB discard fired: Bob's nonland is now in his graveyard.
        bobSpell.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobSpell);
        // Bob's land stayed in hand.
        bobLand.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bobLand);

        // Evoke sacrifice fired: Grief is in Alice's graveyard.
        grief.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(grief);
    }

    [Fact]
    public async Task CastForEvoke_OpponentHandIsAllLands_NoDiscard()
    {
        // Edge case: nonland filter empty → no card moves to graveyard, but
        // the rest of the trigger (reveal + sacrifice) still completes.
        var grief = GriefInHand(_alice);
        var pitchCard = new Creature("Vampire Lacerator", "B", 2, 2) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        // Bob's hand: lands only.
        var bobLand1 = NamedCardFactory.Create("Swamp", _bob);
        bobLand1.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobLand1);
        var bobLand2 = NamedCardFactory.Create("Mountain", _bob);
        bobLand2.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobLand2);

        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Black, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, grief,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        _resolver.ResolveTop(_stack);

        var griefTriggers = grief.Abilities.OfType<TriggeredAbility>().ToList();
        var discardTrigger = griefTriggers.First(t => t.TargetRequests.Count > 0);
        discardTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Both lands remain in Bob's hand — nothing discarded.
        bobLand1.Zone.Should().Be(ZoneType.Hand);
        bobLand2.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();

        // Evoke sacrifice still fires.
        grief.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task CastForNormalMana_OnlyDiscardTriggerFires_NoSacrifice()
    {
        // Cast normally — no alternative cost. Sacrifice rider must NOT fire
        // (intervening-if reads EvokeWasPaid == false → trigger dropped at
        // queue-time per CR 603.4).
        var grief = GriefInHand(_alice);

        var bobSpell = new Creature("Tarmogoyf", "1G", 0, 1) { Owner = _bob };
        bobSpell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, grief,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        grief.Zone.Should().Be(ZoneType.Battlefield);
        grief.EvokeWasPaid.Should().BeFalse();

        // Only the ETB discard trigger is pending — evoke-sacrifice's
        // intervening-if (EvokeWasPaid == false) drops it at queue-time.
        _triggers.PendingCount.Should().Be(1);

        var griefTriggers = grief.Abilities.OfType<TriggeredAbility>().ToList();
        var discardTrigger = griefTriggers.First(t => t.TargetRequests.Count > 0);
        discardTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB discard fired.
        bobSpell.Zone.Should().Be(ZoneType.Graveyard);

        // Grief is still on the battlefield (no sacrifice).
        grief.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature GriefInHand(Player owner)
    {
        var g = GriefFactory.Create(owner);
        g.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(g);
        return g;
    }
}
