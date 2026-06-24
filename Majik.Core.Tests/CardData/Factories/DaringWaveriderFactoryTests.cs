using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Daring Waverider (Tarkir: Dragonstorm, {4}{U}{U}).
///
/// Oracle (verified against Scryfall 2026-06-24):
///   "When this creature enters, you may cast target instant or sorcery card
///    with mana value 4 or less from your graveyard without paying its mana
///    cost. If that spell would be put into your graveyard, exile it instead."
///
/// Covers:
///   - Identity: name, type, subtypes, P/T, mana cost materialised from JSON.
///   - ETB trigger structure (declares a target request for an instant or
///     sorcery card with MV ≤ 4 in the controller's graveyard).
///   - Integration: ETB grants a free (zero-cost) flashback-style cast, the
///     granted card is cast from graveyard via the existing alt-cost path, and
///     it is exiled (not put into the graveyard) on resolution.
/// </summary>
[Trait("Color", "U")]
public class DaringWaveriderFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DaringWaveriderFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void DaringWaverider_Identity_IsOtterWizard_4_4_AtCost4UU()
    {
        var dwv = DaringWaveriderFactory.Create(_alice);

        dwv.Name.Should().Be("Daring Waverider");
        dwv.ManaCost.Should().Be("{4}{U}{U}");
        dwv.HasType(CardType.Creature).Should().BeTrue();
        dwv.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        dwv.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        dwv.BasePower.Should().Be(4);
        dwv.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void DaringWaverider_Etb_PromptsForInstantOrSorceryMv4OrLessInGraveyard()
    {
        var dwv = DaringWaveriderFactory.Create(_alice);

        var triggers = dwv.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        // "you may cast target ... card" — single, optional target.
        req.MinTargets.Should().Be(0);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");
        req.Description.Should().Contain("4 or less");

        // ETB triggers only fire from the battlefield (CR 603.6a).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public async Task DaringWaverider_Etb_FreeCastsTargetFromGraveyard_ThenExiles()
    {
        // A 4-mana instant in Alice's graveyard — at the MV bound (legal target).
        var blast = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _alice };
        blast.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(blast);

        var dwv = DaringWaveriderFactory.Create(_alice, _bus);
        dwv.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(dwv);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, dwv,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        // Daring Waverider resolves onto the battlefield → ETB fires.
        _resolver.ResolveTop(_stack);
        dwv.Zone.Should().Be(ZoneType.Battlefield);
        _triggers.PendingCount.Should().Be(1);

        // Wire the chosen target on the ETB trigger, then resolve.
        var etb = dwv.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { blast },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // The ETB grants a FREE (zero mana value) cast-from-graveyard.
        blast.RuntimeFlashbackCost.Should().NotBeNull();
        blast.RuntimeFlashbackCost!.TotalValue.Should().Be(0);

        // Cast it from the graveyard "without paying its mana cost".
        // Flashback-style alt-cost exiles on resolution (CR 702.34b),
        // satisfying "If that spell would be put into your graveyard, exile it
        // instead." for the resolution trip.
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);
        var altCost = new FlashbackAlternativeCost(blast.RuntimeFlashbackCost);
        var castSpell = await _flow.CastAsync(
            _alice, blast,
            new SpellDefinition(
                Modes: Array.Empty<string>(), HasVariableX: false,
                TargetRequests: new[]
                {
                    new TargetRequest("any target", 1, 1, Array.Empty<object>()),
                },
                EffectFactory: p => new IEffect[]
                {
                    new Effect("deal 3 damage", () =>
                    {
                        var t = p.Targets[0][0];
                        if (t is Player pl) pl.LoseLife(3);
                    }),
                }),
            agent, ctx,
            alternativeCost: altCost);

        blast.Zone.Should().Be(ZoneType.Stack);
        castSpell.Resolve();

        _bob.LifeTotal.Should().Be(17);

        // "If that spell would be put into your graveyard, exile it instead."
        blast.Zone.Should().Be(ZoneType.Exile);
    }
}
