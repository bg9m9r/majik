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
/// Tests for Snapcaster Mage (Innistrad, {1}{U}).
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost).
///   - Flash keyword presence.
///   - ETB trigger structure (declares a target request for an instant or
///     sorcery card in controller's graveyard).
///   - Integration path: ETB grants flashback, then the granted card is
///     cast from graveyard via <see cref="FlashbackAlternativeCost"/> using
///     the existing spell-cast plumbing.
///   - EOT cleanup: granted flashback is cleared on the next Cleanup step.
///   - NamedCardFactory dispatch.
/// </summary>
public class SnapcasterMageTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SnapcasterMageTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void SnapcasterMage_IsCreature_HumanWizard_2_1_AtCost1U()
    {
        var snap = SnapcasterMageFactory.Create(_alice);

        snap.Name.Should().Be("Snapcaster Mage");
        snap.ManaCost.Should().Be("{1}{U}");
        snap.HasType(CardType.Creature).Should().BeTrue();
        snap.HasSubtype(CardSubtype.Human).Should().BeTrue();
        snap.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        snap.BasePower.Should().Be(2);
        snap.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void SnapcasterMage_HasFlash()
    {
        var snap = SnapcasterMageFactory.Create(_alice);

        var keywords = snap.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
    }

    [Fact]
    public void SnapcasterMage_Etb_PromptsForInstantOrSorceryInGraveyard()
    {
        // Structural check: a single TriggeredAbility with a TargetRequest
        // describing "target instant or sorcery card in your graveyard"
        // (mandatory single target).
        var snap = SnapcasterMageFactory.Create(_alice);

        var triggers = snap.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");

        // Also confirm the trigger only listens while on the battlefield
        // (CR 603.6a — ETB triggers don't fire from other zones).
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public async Task SnapcasterMage_FlashbackGrantAllowsCastFromGraveyard()
    {
        // Setup: Lightning Bolt in Alice's graveyard, Snapcaster in hand.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var snap = SnapcasterInHand(_alice);

        // Cast Snapcaster normally; vanilla spell-def shell — the ETB
        // grant effect lives on the triggered ability, not the spell.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        // Snapcaster resolves onto the battlefield → ETB trigger fires.
        _resolver.ResolveTop(_stack);
        snap.Zone.Should().Be(ZoneType.Battlefield);
        _triggers.PendingCount.Should().Be(1);

        // Manually wire the chosen target on the ETB trigger (mirrors the
        // SolitudeFactory tests' shape — bypasses the async agent flow
        // since we only need the structural target plumbing here).
        var etb = snap.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(1);

        // Now cast Bolt from the graveyard using the granted flashback.
        // FlashbackAlternativeCost already gates on Zone == Graveyard +
        // owner == caster (CR 702.33), so no new plumbing is required.
        // Bolt's effect prompts for a target (Bob) + mana sourcing.
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

        // Bolt deals 3 damage to Bob.
        _bob.LifeTotal.Should().Be(17);

        // CR 702.33b — after flashback resolution, Bolt is exiled.
        bolt.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public async Task SnapcasterMage_FlashbackGrantExpiresAtEndOfTurn()
    {
        // Setup: Lightning Bolt in Alice's graveyard, Snapcaster in hand.
        // This time use the bus-aware overload so the EOT cleanup hook
        // actually subscribes to StepStartedEvent(Cleanup).
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var snap = SnapcasterMageFactory.Create(_alice, _bus);
        snap.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(snap);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, snap,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        _resolver.ResolveTop(_stack);

        var etb = snap.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bolt },
        });
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        // Grant is live before EOT.
        bolt.RuntimeFlashbackCost.Should().NotBeNull();

        // Simulate the Cleanup step on Alice's turn — the factory's bus
        // handler should fire and clear the grant (CR 514.2).
        _bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        bolt.RuntimeFlashbackCost.Should().BeNull(
            "the runtime flashback grant expires at end of turn");

        // The bot would no longer surface a flashback bid (the grant is
        // the only source of flashback for Lightning Bolt); attempting to
        // build a FlashbackAlternativeCost from RuntimeFlashbackCost would
        // dereference null — the grant is fully gone.
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SnapcasterMage()
    {
        var card = NamedCardFactory.Create("Snapcaster Mage", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Snapcaster Mage");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);

        // Flash keyword + ETB trigger should be wired by the factory.
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flash");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature SnapcasterInHand(Player owner)
    {
        var s = SnapcasterMageFactory.Create(owner, _bus);
        s.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(s);
        return s;
    }
}
