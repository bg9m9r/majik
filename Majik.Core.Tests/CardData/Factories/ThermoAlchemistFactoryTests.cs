using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThermoAlchemistFactory"/> (Eldritch Moon, {1}{R}).
///
/// Creature — Human Shaman 0/3 (red). Oracle text (verified against Scryfall):
///   "Defender
///    {T}: This creature deals 1 damage to each opponent.
///    Whenever you cast an instant or sorcery spell, untap this creature."
///
/// Covers:
///   - Identity (Human Shaman 0/3 at {1}{R}, red — NOT colorless).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Defender keyword marker present (CR 702.3).
///   - {T}: 1 damage to each opponent (resolver-injected burn).
///   - Burn no-ops without a resolver.
///   - Untap trigger watches SpellCastEvent; one activated + one triggered.
///   - Casting an instant untaps the (tapped) creature.
///   - Casting a sorcery untaps the (tapped) creature.
///   - Casting an artifact / creature spell does NOT untap.
///   - An opponent casting an instant does NOT untap ("you cast").
/// </summary>
[Trait("Color", "R")]
public class ThermoAlchemistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private List<Player> AllPlayers => new() { _alice, _bob };

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Trinket")
    {
        var artifact = new Artifact(name, "1") { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ThermoAlchemist_Identity_HumanShaman_0_3_At1R()
    {
        var c = ThermoAlchemistFactory.Create(_alice);

        c.Name.Should().Be("Thermo-Alchemist");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Red, not colorless — distinct from Nettle Drone (Devoid).
        CardColors.GetColors(c).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void ThermoAlchemist_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Thermo-Alchemist", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thermo-Alchemist");
        card.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void ThermoAlchemist_HasDefenderKeyword()
    {
        var c = ThermoAlchemistFactory.Create(_alice);

        // CR 702.3 — Defender keyword marker; surfaced for block legality.
        CombatAbilities.HasDefender(c).Should().BeTrue();
    }

    [Fact]
    public void ThermoAlchemist_HasTapBurnActivatedAbility_AndUntapTrigger()
    {
        var c = ThermoAlchemistFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>()
            .Should().HaveCount(1, "the {T}: deal 1 to each opponent ability");
        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the untap-on-instant/sorcery-cast trigger");
    }

    // -----------------------------------------------------------------------
    // {T}: 1 damage to each opponent
    // -----------------------------------------------------------------------

    [Fact]
    public void TapBurn_DealsOneDamageToEachOpponent()
    {
        var card = ThermoAlchemistFactory.Create(
            _alice, triggers: null, opponentResolver: () => new[] { _bob });

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in burn.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "{T} deals 1 damage to each opponent (CR 119 — damage is life loss)");
        _alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }

    [Fact]
    public void TapBurn_WithoutResolver_NoOps()
    {
        var card = ThermoAlchemistFactory.Create(
            _alice, triggers: null, opponentResolver: null);

        var burn = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in burn.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no opponent resolver → burn half no-ops");
    }

    [Fact]
    public void UntapTrigger_WatchesSpellCastEvent()
    {
        var card = ThermoAlchemistFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.EventType
            .Should().Be(typeof(SpellCastEvent),
                "the untap clause triggers on casting an instant or sorcery");
    }

    // -----------------------------------------------------------------------
    // Untap-on-cast behaviour (the new wrinkle vs. Electrostatic Field)
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_UntapsThisCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ThermoAlchemistFactory.Create(_alice, triggers, () => AllPlayers);
        card.SetZone(ZoneType.Battlefield);
        card.Tap();
        card.IsTapped.Should().BeTrue("tapped to pay the {T} burn cost");

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        card.IsTapped.Should().BeFalse(
            "casting an instant untaps Thermo-Alchemist (CR 603.1)");
    }

    [Fact]
    public void CastingSorcery_UntapsThisCreature()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ThermoAlchemistFactory.Create(_alice, triggers, () => AllPlayers);
        card.SetZone(ZoneType.Battlefield);
        card.Tap();

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        card.IsTapped.Should().BeFalse(
            "casting a sorcery untaps Thermo-Alchemist (CR 603.1)");
    }

    [Fact]
    public void CastingArtifactSpell_DoesNotUntap()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ThermoAlchemistFactory.Create(_alice, triggers, () => AllPlayers);
        card.SetZone(ZoneType.Battlefield);
        card.Tap();

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));

        triggers.PendingCount.Should().Be(0);
        card.IsTapped.Should().BeTrue("an artifact spell is not an instant/sorcery");
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotUntap()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ThermoAlchemistFactory.Create(_alice, triggers, () => AllPlayers);
        card.SetZone(ZoneType.Battlefield);
        card.Tap();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        card.IsTapped.Should().BeTrue("a creature spell is not an instant/sorcery");
    }

    [Fact]
    public void OpponentCastingInstant_DoesNotUntap()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = ThermoAlchemistFactory.Create(_alice, triggers, () => AllPlayers);
        card.SetZone(ZoneType.Battlefield);
        card.Tap();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
        card.IsTapped.Should().BeTrue("only YOU casting an instant/sorcery untaps it");
    }
}
