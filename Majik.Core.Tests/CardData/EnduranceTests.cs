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
/// End-to-end tests for Endurance (Modern Horizons 2). Exercises both cast
/// paths (normal + evoke) and asserts the on-resolution triggers behave per
/// CR 702.74 (Evoke), CR 701.19c (Shuffle), and Endurance's printed ETB
/// graveyard-to-library trigger.
///
/// Mirrors <see cref="GriefTests"/> — the MH2 incarnation cycle shares the
/// same Evoke alt-cost + evoke-sacrifice scaffolding; only the printed ETB
/// differs.
/// </summary>
public class EnduranceTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EnduranceTests()
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
        var endurance = EnduranceFactory.Create(_alice);

        endurance.Name.Should().Be("Endurance");
        endurance.BasePower.Should().Be(3);
        endurance.BaseToughness.Should().Be(4);
        endurance.HasType(CardType.Creature).Should().BeTrue();
        endurance.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        endurance.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywordNames = endurance.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Reach", "Trample", "Evoke" });

        // Two triggered abilities: ETB graveyard-to-library + Evoke sacrifice.
        endurance.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchesEndurance()
    {
        var endurance = NamedCardFactory.Create("Endurance", _alice);

        endurance.Should().BeOfType<Creature>();
        endurance.Name.Should().Be("Endurance");
        var creature = (Creature)endurance;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(4);
        creature.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        creature.HasSubtype(CardSubtype.Incarnation).Should().BeTrue();

        var keywordNames = creature.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flash", "Reach", "Trample", "Evoke" });
    }

    [Fact]
    public void Create_HasPrintedTrampleKeyword()
    {
        // CR 702.19 — printed Trample. Regression for the keyword-grant gap
        // where Endurance only attached Flash + Reach + Evoke markers
        // despite the MH2 print listing Flash, Reach, AND Trample.
        var endurance = EnduranceFactory.Create(_alice);

        var keywordNames = endurance.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain("Trample");
    }

    // ── ETB targeting ────────────────────────────────────────────────────────

    [Fact]
    public async Task CastForNormalMana_TargetOpponent_GraveyardShufflesIntoLibrary()
    {
        // Bob has a stacked graveyard; Endurance enters, targets Bob, his
        // graveyard cycles back into his library.
        var endurance = EnduranceInHand(_alice);

        var bobGyA = new Creature("Tarmogoyf", "1G", 0, 1) { Owner = _bob };
        bobGyA.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobGyA);
        var bobGyB = new Creature("Dark Confidant", "1B", 2, 1) { Owner = _bob };
        bobGyB.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobGyB);

        var bobLibBefore = _bob.Zones.Library.GetCards().Count();

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, endurance,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        endurance.Zone.Should().Be(ZoneType.Battlefield);
        endurance.EvokeWasPaid.Should().BeFalse();

        // Only the ETB shuffle trigger is pending — evoke-sacrifice's
        // intervening-if (EvokeWasPaid == false) drops it at queue-time.
        _triggers.PendingCount.Should().Be(1);

        var enduranceTriggers = endurance.Abilities.OfType<TriggeredAbility>().ToList();
        var shuffleTrigger = enduranceTriggers.First(t => t.TargetRequests.Count > 0);
        shuffleTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Bob's graveyard is empty; both creatures are now in his library.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Count().Should().Be(bobLibBefore + 2);
        bobGyA.Zone.Should().Be(ZoneType.Library);
        bobGyB.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(bobGyA);
        _bob.Zones.Library.GetCards().Should().Contain(bobGyB);

        // Endurance stayed on the battlefield (normal cast — no evoke sac).
        endurance.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task CastForNormalMana_TargetSelf_OwnGraveyardRecycles()
    {
        // Endurance is legal to target one's own controller — useful for
        // recycling delve / graveyard-fuel cards. Targeting Alice must
        // produce the same graveyard-to-library cycle on her side.
        var endurance = EnduranceInHand(_alice);

        var aliceGy1 = new Creature("Wild Mongrel", "1G", 2, 2) { Owner = _alice };
        aliceGy1.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceGy1);
        var aliceGy2 = new Creature("Stinkweed Imp", "2B", 1, 2) { Owner = _alice };
        aliceGy2.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceGy2);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, endurance,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        var enduranceTriggers = endurance.Abilities.OfType<TriggeredAbility>().ToList();
        var shuffleTrigger = enduranceTriggers.First(t => t.TargetRequests.Count > 0);
        shuffleTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _alice },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        aliceGy1.Zone.Should().Be(ZoneType.Library);
        aliceGy2.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Library.GetCards().Should().Contain(aliceGy1);
        _alice.Zones.Library.GetCards().Should().Contain(aliceGy2);

        // Bob's graveyard is untouched.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── Evoke path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CastForEvoke_ShufflesGraveyardThenSacrifices()
    {
        // Pitch path: Alice exiles a green card to evoke Endurance, the ETB
        // resolves on Bob's stacked graveyard, then the evoke sacrifice
        // sends Endurance to the graveyard.
        var endurance = EnduranceInHand(_alice);
        var pitchCard = new Creature("Tarmogoyf", "1G", 0, 1) { Owner = _alice };
        pitchCard.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(pitchCard);

        var bobGy = new Creature("Tarmogoyf", "1G", 0, 1) { Owner = _bob };
        bobGy.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobGy);

        var evokeCost = new EvokeAlternativeCost(
            ManaCost.Zero, ManaColor.Green, pitchCard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, endurance,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: evokeCost);

        // Spell resolves: alt-cost cleanup flips EvokeWasPaid and exiles pitch.
        _resolver.ResolveTop(_stack);

        endurance.Zone.Should().Be(ZoneType.Battlefield);
        endurance.EvokeWasPaid.Should().BeTrue();
        pitchCard.Zone.Should().Be(ZoneType.Exile);

        // Two triggers fired on the ETB CardMovedEvent: shuffle target + sac.
        _triggers.PendingCount.Should().Be(2);

        // Point the shuffle trigger at Bob.
        var enduranceTriggers = endurance.Abilities.OfType<TriggeredAbility>().ToList();
        var shuffleTrigger = enduranceTriggers.First(t => t.TargetRequests.Count > 0);
        shuffleTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // ETB shuffle fired: Bob's graveyard is now in his library.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        bobGy.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(bobGy);

        // Evoke sacrifice fired: Endurance is in Alice's graveyard.
        endurance.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(endurance);
    }

    [Fact]
    public async Task EmptyGraveyard_TargetingIsLegal_NoOp()
    {
        // No cards in target's graveyard: trigger still resolves cleanly,
        // library stays the same size.
        var endurance = EnduranceInHand(_alice);

        // Seed Bob's library so we can assert it's unchanged.
        var bobLibCard = new Creature("Wild Nacatl", "G", 3, 3) { Owner = _bob };
        bobLibCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bobLibCard);
        var bobLibBefore = _bob.Zones.Library.GetCards().Count();

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, endurance,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        var enduranceTriggers = endurance.Abilities.OfType<TriggeredAbility>().ToList();
        var shuffleTrigger = enduranceTriggers.First(t => t.TargetRequests.Count > 0);
        shuffleTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        _triggers.PutPendingTriggersOnStack(_alice);
        while (!_stack.IsEmpty)
        {
            _resolver.ResolveTop(_stack);
        }

        // Library is unchanged; graveyard is still empty.
        _bob.Zones.Library.GetCards().Count().Should().Be(bobLibBefore);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();

        // Endurance is still on the battlefield (no evoke).
        endurance.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature EnduranceInHand(Player owner)
    {
        var e = EnduranceFactory.Create(owner);
        e.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(e);
        return e;
    }
}
