using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Searing Blood (Born of the Gods / Modern Horizons, {R}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Searing Blood deals 2 damage to target creature. When that creature
///    dies this turn, Searing Blood deals 3 damage to the creature's
///    controller."
///
/// Covers:
///   - Card identity (Instant, {R}{R}, owner/controller) loaded from the
///     embedded JSON def via <see cref="CardDefinitionLoader"/>.
///   - NamedCardFactory dispatch.
///   - Resolve → 2 damage to the target creature.
///   - The damaged creature dying this turn → 3 damage to its controller
///     (delayed triggered ability, CR 603.7).
///   - Controller-at-resolution is captured (CR 603.10e last-known
///     information): when the creature dies it routes to its owner's
///     graveyard with controller reset to owner, so the 3 damage must use
///     the controller sampled when Searing Blood resolved.
///   - Creature surviving the turn → no 3 damage.
///   - Killing a NON-targeted creature → no 3 damage.
/// </summary>
[Trait("Color", "R")]
public class SearingBloodTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SearingBloodTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SearingBlood_IsInstant_AtCostRR()
    {
        var sb = SearingBloodFactory.Create(_alice);

        sb.Name.Should().Be("Searing Blood");
        sb.ManaCost.Should().Be("{R}{R}");
        sb.HasType(CardType.Instant).Should().BeTrue();
        sb.Owner.Should().BeSameAs(_alice);
        sb.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Resolution — 2 damage to target creature
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolve_Deals2_ToTargetCreature()
    {
        var bobBear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", 2, 2);
        var bobStarting = _bob.LifeTotal;

        await CastAndResolve(bobBear, triggers: null);

        bobBear.Damage.Should().Be(2, "Searing Blood deals 2 damage to the target creature");
        _bob.LifeTotal.Should().Be(bobStarting, "no damage to the controller until the creature dies");
    }

    // -----------------------------------------------------------------------
    // Delayed "when that creature dies this turn" — 3 damage to controller
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TargetDiesThisTurn_Deals3_ToItsController()
    {
        var triggers = new TriggerManager(_stack, _bus);

        // 1/1: the 2 damage is lethal so it dies (CR 704.5g handled by the
        // test driving the death move explicitly through ZoneService).
        var bobCreature = NewCreatureOnBattlefield(_bob, "Goblin", 1, 1);
        var bobStarting = _bob.LifeTotal;

        await CastAndResolve(bobCreature, triggers);
        bobCreature.Damage.Should().Be(2);

        // The creature dies this turn. Route through ZoneService so the
        // CardMovedEvent(Battlefield→Graveyard) publishes (CR 700.4 — a
        // creature is "put into a graveyard from the battlefield" = dies).
        _zones.MoveCard(bobCreature, ZoneType.Battlefield, ZoneType.Graveyard, _bob);

        // Fire the delayed trigger onto the stack and resolve it.
        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!_stack.IsEmpty) resolver.ResolveTop(_stack);

        _bob.LifeTotal.Should().Be(bobStarting - 3,
            "CR 603.7 — when the creature dies this turn Searing Blood deals 3 to its controller");
    }

    [Fact]
    public async Task ControllerCapturedAtResolution_SurvivesGraveyardOwnerReset()
    {
        // When a creature dies it routes to its OWNER's graveyard and
        // ZoneService resets Controller=Owner. Searing Blood must remember
        // the controller as it last existed on the battlefield (CR 603.10e),
        // so the 3 damage lands on Bob even though Controller is reset.
        var triggers = new TriggerManager(_stack, _bus);

        var bobCreature = NewCreatureOnBattlefield(_bob, "Goblin", 1, 1);
        var bobStarting = _bob.LifeTotal;

        await CastAndResolve(bobCreature, triggers);

        _zones.MoveCard(bobCreature, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bobCreature.Controller.Should().BeSameAs(_bob,
            "owns + controlled by Bob — graveyard reset returns it to its owner");

        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!_stack.IsEmpty) resolver.ResolveTop(_stack);

        _bob.LifeTotal.Should().Be(bobStarting - 3);
    }

    [Fact]
    public async Task TargetSurvives_NoDamageToController()
    {
        var triggers = new TriggerManager(_stack, _bus);

        // 4/4 survives the 2 damage; never dies this turn.
        var bobBeast = NewCreatureOnBattlefield(_bob, "Big Beast", 4, 4);
        var bobStarting = _bob.LifeTotal;

        await CastAndResolve(bobBeast, triggers);
        bobBeast.Damage.Should().Be(2);

        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!_stack.IsEmpty) resolver.ResolveTop(_stack);

        _bob.LifeTotal.Should().Be(bobStarting,
            "the targeted creature did not die this turn → no 3 damage");
    }

    [Fact]
    public async Task DifferentCreatureDies_NoDamageToController()
    {
        var triggers = new TriggerManager(_stack, _bus);

        var target = NewCreatureOnBattlefield(_bob, "Targeted Goblin", 1, 1);
        var bystander = NewCreatureOnBattlefield(_bob, "Other Goblin", 1, 1);
        var bobStarting = _bob.LifeTotal;

        await CastAndResolve(target, triggers);

        // A DIFFERENT creature dies — the delayed trigger keys off the exact
        // targeted creature reference, so this must not fire.
        _zones.MoveCard(bystander, ZoneType.Battlefield, ZoneType.Graveyard, _bob);

        triggers.PutPendingTriggersOnStack(_alice);
        var resolver = new StackResolver(_bus, _zones);
        while (!_stack.IsEmpty) resolver.ResolveTop(_stack);

        _bob.LifeTotal.Should().Be(bobStarting,
            "only the targeted creature's death triggers the 3 damage");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    /// <summary>
    /// Cast Searing Blood from Alice's hand at <paramref name="creature"/>
    /// and resolve the resulting stack object. Mirrors the Searing Blaze
    /// cast harness — direct cast/resolve, no priority loop.
    /// </summary>
    private async Task CastAndResolve(object creature, TriggerManager? triggers)
    {
        var sb = SearingBloodFactory.Create(_alice);
        sb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sb);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { creature });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, sb,
            SearingBloodFactory.BuildSpellDefinition(
                _alice,
                resolver: t => t,
                triggers: triggers),
            agent, ctx);

        sb.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
