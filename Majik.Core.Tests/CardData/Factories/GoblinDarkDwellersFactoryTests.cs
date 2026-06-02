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
/// Tests for Goblin Dark-Dwellers (Oath of the Gatewatch, {3}{R}{R}).
///
/// Oracle (verified against Scryfall 2026-05-29):
///   "Menace (This creature can't be blocked except by two or more creatures.)
///    When this creature enters, you may cast target instant or sorcery card
///    with mana value 3 or less from your graveyard without paying its mana
///    cost. If that spell would be put into your graveyard, exile it instead."
///
/// Covers:
///   - Card shape (name, type, subtype, P/T, mana cost) materialised from JSON.
///   - Menace keyword marker (CR 702.111).
///   - ETB trigger structure (declares a target request for an instant or
///     sorcery card with MV ≤ 3 in the controller's graveyard).
///   - Integration: ETB grants a free (zero-cost) flashback-style cast, the
///     granted card is cast from graveyard via the existing alt-cost path, and
///     it is exiled (not put into the graveyard) on resolution.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "R")]
public class GoblinDarkDwellersFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GoblinDarkDwellersFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void GoblinDarkDwellers_IsCreature_Goblin_4_4_AtCost3RR()
    {
        var gdd = GoblinDarkDwellersFactory.Create(_alice);

        gdd.Name.Should().Be("Goblin Dark-Dwellers");
        gdd.ManaCost.Should().Be("{3}{R}{R}");
        gdd.HasType(CardType.Creature).Should().BeTrue();
        gdd.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        gdd.BasePower.Should().Be(4);
        gdd.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void GoblinDarkDwellers_HasMenace()
    {
        var gdd = GoblinDarkDwellersFactory.Create(_alice);

        var keywords = gdd.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Menace");
    }

    [Fact]
    public void GoblinDarkDwellers_Etb_PromptsForInstantOrSorceryMv3OrLessInGraveyard()
    {
        var gdd = GoblinDarkDwellersFactory.Create(_alice);

        var triggers = gdd.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        // "you may cast target ... card" — single, optional target.
        req.MinTargets.Should().Be(0);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");
        req.Description.Should().Contain("3 or less");

        // ETB triggers don't fire from other zones (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public async Task GoblinDarkDwellers_Etb_FreeCastsTargetFromGraveyard_ThenExiles()
    {
        // Lightning Bolt (MV 1, instant) in Alice's graveyard — a legal target.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var gdd = GoblinDarkDwellersFactory.Create(_alice, _bus);
        gdd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gdd);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, gdd,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        // Goblin Dark-Dwellers resolves onto the battlefield → ETB fires.
        _resolver.ResolveTop(_stack);
        gdd.Zone.Should().Be(ZoneType.Battlefield);
        _triggers.PendingCount.Should().Be(1);

        // Wire the chosen target (Bolt) on the ETB trigger, then resolve.
        var etb = gdd.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // The ETB grants a FREE (zero mana value) cast-from-graveyard.
        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(0);

        // Cast Bolt from the graveyard "without paying its mana cost".
        // Flashback-style alt-cost exiles on resolution (CR 702.34b),
        // satisfying "If that spell would be put into your graveyard, exile
        // it instead." for the resolution trip.
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);
        var altCost = new FlashbackAlternativeCost(bolt.RuntimeFlashbackCost);
        var boltSpell = await _flow.CastAsync(
            _alice, bolt,
            new SpellDefinition(
                Modes: Array.Empty<string>(), HasVariableX: false,
                TargetRequests: new[]
                {
                    new TargetRequest("any target", 1, 1, Array.Empty<object>()),
                },
                EffectFactory: p => new IEffect[]
                {
                    new Effect("Lightning Bolt: deal 3 damage", () =>
                    {
                        var t = p.Targets[0][0];
                        if (t is Player pl) pl.LoseLife(3);
                    }),
                }),
            agent, ctx,
            alternativeCost: altCost);

        bolt.Zone.Should().Be(ZoneType.Stack);
        boltSpell.Resolve();

        _bob.LifeTotal.Should().Be(17);

        // "If that spell would be put into your graveyard, exile it instead."
        bolt.Zone.Should().Be(ZoneType.Exile);
    }
}
