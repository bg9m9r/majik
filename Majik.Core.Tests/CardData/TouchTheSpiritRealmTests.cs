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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Touch the Spirit Realm (Kamigawa: Neon Dynasty, {2}{W}).
///
/// Oracle text:
///   "Exile target artifact, creature, or enchantment.
///    Channel — {2}{W}, Discard Touch the Spirit Realm: Exile target
///    creature or enchantment you control. Return it to the battlefield
///    under its owner's control at the beginning of the next end step."
///
/// Covers:
///   - Card identity (Instant, {2}{W}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 target, Removal intent.
///   - Cast-body resolve: exiles a Creature / Artifact / Enchantment.
///   - Cast-body resolve: Land target is illegal (CR 608.2b — exile no-op).
///   - Channel cost shape — {2}{W} + DiscardSelfCost (CR 702.74a).
///   - Channel resolve: exiles a controller-side Creature; delayed end-step
///     trigger registered on the supplied TriggerManager.
///   - Channel delayed trigger: returns the exiled card on End step.
///   - Channel resolve: opponent-controlled creature → resolution-time
///     legality re-check fizzles (CR 608.2b).
/// </summary>
public class TouchTheSpiritRealmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TouchTheSpiritRealm_IsInstant_AtCost2W()
    {
        var card = TouchTheSpiritRealmFactory.Create(_alice);

        card.Name.Should().Be("Touch the Spirit Realm");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TouchTheSpiritRealm()
    {
        var card = NamedCardFactory.Create("Touch the Spirit Realm", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Touch the Spirit Realm");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TouchTheSpiritRealm_Definition_HasSingleArtifactCreatureEnchantmentTarget()
    {
        var def = TouchTheSpiritRealmFactory.BuildSpellDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("artifact");
        tr.Description.Should().Contain("creature");
        tr.Description.Should().Contain("enchantment");
        tr.Intent.Should().Be(Majik.Core.Cards.BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Cast body — exiles target
    // -----------------------------------------------------------------------

    [Fact]
    public void TouchTheSpiritRealm_Cast_ExilesTargetCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        ResolveCastBody(goblin);

        goblin.Zone.Should().Be(ZoneType.Exile,
            "Touch the Spirit Realm exiles the targeted permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void TouchTheSpiritRealm_Cast_ExilesTargetArtifact()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        ResolveCastBody(artifact);

        artifact.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void TouchTheSpiritRealm_Cast_ExilesTargetEnchantment()
    {
        var enchantment = new Enchantment("Rest in Peace", "{1}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        ResolveCastBody(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public void TouchTheSpiritRealm_Cast_LandTarget_IsIllegalAtResolution()
    {
        // Pure Land — illegal (CR 608.2b artifact/creature/enchantment filter).
        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        ResolveCastBody(land);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Touch the Spirit Realm cannot exile a pure land");
    }

    [Fact]
    public void TouchTheSpiritRealm_Cast_TargetOffBattlefield_Fizzles()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");
        _bob.Zones.Battlefield.RemoveCard(creature);
        _bob.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        ResolveCastBody(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "off-battlefield target is illegal at resolution");
        _bob.Zones.Exile.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Channel — cost shape + activation gate
    // -----------------------------------------------------------------------

    [Fact]
    public void Channel_HasManaAndDiscardSelfCosts_2W()
    {
        var card = TouchTheSpiritRealmFactory.Create(_alice);

        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        channel.Costs.Should().HaveCount(2);
        channel.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = channel.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2);
        manaCost.White.Should().Be(1);
    }

    [Fact]
    public void Channel_DiscardSelfCost_PayableWhenInHand_RejectedWhenNot()
    {
        var card = TouchTheSpiritRealmFactory.Create(_alice);
        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = channel.Costs.OfType<DiscardSelfCost>().Single();

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        discardCost.CanPay(_alice).Should().BeTrue("Channel is in hand — CR 702.74a");

        _alice.Zones.Hand.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        discardCost.CanPay(_alice).Should().BeFalse(
            "Channel cannot be activated from outside the hand (CR 702.74a)");
    }

    [Fact]
    public void Channel_TargetRequest_HasYouControlGather_ProtectionIntent()
    {
        var card = TouchTheSpiritRealmFactory.Create(_alice);
        var channel = card.Abilities.OfType<ActivatedAbility>().Single();

        channel.TargetRequests.Should().HaveCount(1);
        var tr = channel.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(Majik.Core.Cards.BotIntent.Protection);
    }

    // -----------------------------------------------------------------------
    // Channel — resolve behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void Channel_Resolve_ExilesControllerSideCreature_RegistersDelayedReturn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = TouchTheSpiritRealmFactory.Create(_alice, triggers, zones: null);
        var bear = NewControlledCreature(_alice, "Bear", "{1}{G}");

        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { bear } });
        channel.Resolve();

        bear.Zone.Should().Be(ZoneType.Exile, "Channel exiles the controller-side creature");
        _alice.Zones.Exile.GetCards().Should().Contain(bear);

        // Publish an End-step started event — the delayed return should fire.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(1, "the delayed end-step return is pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "delayed end-step trigger returns the exiled creature (CR 603.7)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Channel_Resolve_ExilesControllerSideEnchantment_RegistersDelayedReturn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = TouchTheSpiritRealmFactory.Create(_alice, triggers, zones: null);
        var aura = new Enchantment("Sigil of the Empty Throne", "{4}{W}")
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { aura } });
        channel.Resolve();

        aura.Zone.Should().Be(ZoneType.Exile);

        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        aura.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled enchantment returns to the battlefield at end step");
    }

    [Fact]
    public void Channel_Resolve_OpponentControlledTarget_FizzlesAtResolveLegalityCheck()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = TouchTheSpiritRealmFactory.Create(_alice, triggers, zones: null);
        var bobBear = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        // The agent shouldn't pick an opponent creature, but if the choice
        // somehow lands here, the resolve closure must still re-check
        // controller (CR 608.2b).
        channel.SetChosenTargets(new[] { new object[] { bobBear } });
        channel.Resolve();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "Channel only legally targets controller-side permanents — resolve-time " +
            "legality re-check (CR 608.2b) fizzles the exile");
    }

    [Fact]
    public void Channel_Resolve_ShapeOnlyMode_ExilesButSkipsDelayedReturn()
    {
        // No TriggerManager → no delayed return rider registered. The exile
        // half still fires so shape-only tests don't drift.
        var card = TouchTheSpiritRealmFactory.Create(_alice);
        var bear = NewControlledCreature(_alice, "Bear", "{1}{G}");

        var channel = card.Abilities.OfType<ActivatedAbility>().Single();
        channel.SetChosenTargets(new[] { new object[] { bear } });
        channel.Resolve();

        bear.Zone.Should().Be(ZoneType.Exile,
            "exile half runs even in shape-only mode — only the return rider is skipped");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ResolveCastBody(object targetToken)
    {
        var def = TouchTheSpiritRealmFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
