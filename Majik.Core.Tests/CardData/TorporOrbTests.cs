using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="TorporOrbFactory"/> and <see cref="TorporOrbStaticEffect"/>.
///
/// CR 614 (continuous effects) + CR 603.3 (triggered abilities).
/// Oracle: "Creatures entering the battlefield don't cause abilities to trigger."
/// </summary>
public class TorporOrbTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggerManager;

    public TorporOrbTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggerManager = new TriggerManager(_stack, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TorporOrb_IsArtifact()
    {
        var orb = TorporOrbFactory.Create(_alice);

        orb.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void TorporOrb_IsNotCreature()
    {
        var orb = TorporOrbFactory.Create(_alice);

        orb.HasType(CardType.Creature).Should().BeFalse();
    }

    [Fact]
    public void TorporOrb_HasCorrectName()
    {
        var orb = TorporOrbFactory.Create(_alice);

        orb.Name.Should().Be("Torpor Orb");
    }

    [Fact]
    public void TorporOrb_HasManaCostTwoColorless()
    {
        var orb = TorporOrbFactory.Create(_alice);

        orb.ManaCost.Should().Be("{2}");
    }

    [Fact]
    public void TorporOrb_OwnerAndControllerAreSet()
    {
        var orb = TorporOrbFactory.Create(_alice);

        orb.Owner.Should().BeSameAs(_alice);
        orb.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // TorporOrbStaticEffect — suppression counter lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void StaticEffect_NotActive_WhenOrbNotOnBattlefield()
    {
        var orb = new Artifact("Torpor Orb", "{2}");
        orb.SetOwner(_alice);
        // Default zone is Hand (or Library) — not Battlefield.

        var effect = new TorporOrbStaticEffect(orb, _triggerManager);
        effect.Attach();

        effect.IsActive.Should().BeFalse();
        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(0);
    }

    [Fact]
    public void StaticEffect_BecomesActive_WhenOrbMovesToBattlefield()
    {
        var orb = new Artifact("Torpor Orb", "{2}");
        orb.SetOwner(_alice);
        var effect = new TororOrbEffectBuilder(orb, _triggerManager, _bus);

        // Move Orb to battlefield
        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        effect.StaticEffect.IsActive.Should().BeTrue();
        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(1);
    }

    [Fact]
    public void StaticEffect_DeactivatesOnLeaving_RestoresCountToZero()
    {
        var orb = new Artifact("Torpor Orb", "{2}");
        orb.SetOwner(_alice);
        var effect = new TororOrbEffectBuilder(orb, _triggerManager, _bus);

        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        orb.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Battlefield, ZoneType.Graveyard));

        effect.StaticEffect.IsActive.Should().BeFalse();
        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(0);
    }

    [Fact]
    public void TwoOrbs_CountIsTwo_OneBothOnBattlefield()
    {
        var orb1 = new Artifact("Torpor Orb", "{2}");
        orb1.SetOwner(_alice);
        var orb2 = new Artifact("Torpor Orb", "{2}");
        orb2.SetOwner(_bob);

        var eff1 = new TorporOrbStaticEffect(orb1, _triggerManager, _bus);
        eff1.Attach();
        var eff2 = new TorporOrbStaticEffect(orb2, _triggerManager, _bus);
        eff2.Attach();

        orb1.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb1, ZoneType.Hand, ZoneType.Battlefield));
        orb2.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb2, ZoneType.Hand, ZoneType.Battlefield));

        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(2);

        // One leaves — count drops to 1, suppression still active
        orb1.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(orb1, ZoneType.Battlefield, ZoneType.Graveyard));

        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(1);

        // Second leaves — count reaches 0
        orb2.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(orb2, ZoneType.Battlefield, ZoneType.Graveyard));

        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(0);
    }

    [Fact]
    public void Detach_WithdrawsSuppressionEvenIfOrbOnBattlefield()
    {
        var orb = new Artifact("Torpor Orb", "{2}");
        orb.SetOwner(_alice);
        var effect = new TorporOrbStaticEffect(orb, _triggerManager, _bus);
        effect.Attach();

        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));
        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(1);

        effect.Detach();

        _triggerManager.CreatureEtbTriggerSuppressionCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // TriggerManager — creature ETB suppression gate
    // -----------------------------------------------------------------------

    [Fact]
    public void TorporOrb_OnBattlefield_SuppressesCreatureEtbTrigger()
    {
        // Set up Torpor Orb on the battlefield (fully wired).
        var orb = TorporOrbFactory.Create(_alice, _triggerManager, _bus);
        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        // Creature with an ETB self-trigger (e.g. "when ~ enters, draw a card").
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Battlefield);
        var etbTrigger = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        _triggerManager.RegisterTriggeredAbility(etbTrigger);

        // Bear enters the battlefield.
        _bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        // Trigger must NOT have been queued.
        _triggerManager.PendingCount.Should().Be(0, "Torpor Orb suppresses creature ETB triggers");
    }

    [Fact]
    public void TorporOrb_NotOnBattlefield_DoesNotSuppressTrigger()
    {
        // Orb created but never moved to battlefield.
        var orb = TorporOrbFactory.Create(_alice, _triggerManager, _bus);
        // (orb.Zone remains Hand/default)

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Battlefield);
        var etbTrigger = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        _triggerManager.RegisterTriggeredAbility(etbTrigger);

        _bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        // Trigger fires normally.
        _triggerManager.PendingCount.Should().Be(1, "Orb not on battlefield — trigger should fire");
    }

    [Fact]
    public void TorporOrb_LeavingBattlefield_RestoresTriggerFiring()
    {
        var orb = TorporOrbFactory.Create(_alice, _triggerManager, _bus);
        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        // Orb leaves.
        orb.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Battlefield, ZoneType.Graveyard));

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Battlefield);
        var etbTrigger = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear));
        _triggerManager.RegisterTriggeredAbility(etbTrigger);

        _bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        // Trigger fires again now that the Orb is gone.
        _triggerManager.PendingCount.Should().Be(1);
    }

    [Fact]
    public void TorporOrb_SuppressesAnyCreatureEtbTrigger_NotJustSelfTriggers()
    {
        // Soul Warden-style: on ANY creature entering, its controller gains 1 life.
        var orb = TorporOrbFactory.Create(_alice, _triggerManager, _bus);
        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        var warden = new Creature("Soul Warden", "W", 1, 1);
        warden.SetOwner(_alice);
        warden.SetZone(ZoneType.Battlefield);
        var wardenTrigger = new TriggeredAbility(warden, _alice, Triggers.OnAnyCreatureEntersBattlefield());
        _triggerManager.RegisterTriggeredAbility(wardenTrigger);

        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_bob);
        bear.SetZone(ZoneType.Battlefield);

        _bus.Publish(new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield));

        _triggerManager.PendingCount.Should().Be(0,
            "Torpor Orb suppresses all abilities triggered by a creature entering, including watchers on other permanents");
    }

    [Fact]
    public void TorporOrb_DoesNotSuppressNonCreatureEtbTriggers()
    {
        // A trigger that fires when a *land* enters — NOT suppressed by Torpor Orb.
        var orb = TorporOrbFactory.Create(_alice, _triggerManager, _bus);
        orb.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(orb, ZoneType.Hand, ZoneType.Battlefield));

        var landfallSource = new Creature("Landfall Bear", "1G", 2, 2);
        landfallSource.SetOwner(_alice);
        landfallSource.SetZone(ZoneType.Battlefield);
        var landfallTrigger = new TriggeredAbility(landfallSource, _alice, Triggers.OnLandEntersUnderControl(_alice));
        _triggerManager.RegisterTriggeredAbility(landfallTrigger);

        var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);

        _bus.Publish(new CardMovedEvent(forest, ZoneType.Hand, ZoneType.Battlefield));

        _triggerManager.PendingCount.Should().Be(1,
            "Torpor Orb only suppresses creature ETB triggers; land ETB is unaffected");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory integration
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_CreatesTorporOrb_WithCorrectType()
    {
        var orb = Majik.Core.CardData.NamedCardFactory.Create("Torpor Orb", _alice);

        orb.HasType(CardType.Artifact).Should().BeTrue();
        orb.Name.Should().Be("Torpor Orb");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tiny helper that builds and attaches a TorporOrbStaticEffect, exposing
    /// it for assertions without changing the production API.
    /// </summary>
    private sealed class TororOrbEffectBuilder
    {
        public TorporOrbStaticEffect StaticEffect { get; }

        public TororOrbEffectBuilder(ICard orb, TriggerManager tm, IEventBus bus)
        {
            StaticEffect = new TorporOrbStaticEffect(orb, tm, bus);
            StaticEffect.Attach();
        }
    }
}
