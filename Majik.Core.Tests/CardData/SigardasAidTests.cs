using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sigarda's Aid (Eldritch Moon, {1}{W}).
///
/// Covers:
///   - Card identity (name, type, mana cost, owner/controller, one
///     triggered ability) + <see cref="NamedCardFactory"/> dispatch shape.
///   - Printed static (CR 117.1 / 702.8) grants flash to Equipment cards
///     owned by Sigarda's controller while Sigarda is on the battlefield —
///     <see cref="TimingRules.CanCastAtInstantSpeed"/> surfaces the grant.
///   - Same flash grant covers Aura cards.
///   - When Sigarda leaves the battlefield (LTB) the grant is lifted —
///     equipment in hand returns to sorcery-speed-only.
///   - ETB-attach rider fires when an Equipment enters under Sigarda's
///     controller, attaching it to the first available controller-side
///     creature (CR 603.6a / 701.3a).
/// </summary>
public class SigardasAidTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SigardasAidTests()
    {
        // Defensive — every test that exercises FlashGrantRegistry should
        // start from a clean registry. Other named-card tests that touch
        // CastingRestrictions follow the same pattern.
        FlashGrantRegistry.Clear();
    }

    public void Dispose()
    {
        FlashGrantRegistry.Clear();
    }

    [Fact]
    public void SigardasAid_Identity_EnchantmentAt1W()
    {
        var aid = SigardasAidFactory.Create(_alice);

        aid.Name.Should().Be("Sigarda's Aid");
        aid.ManaCost.Should().Be("{1}{W}");
        aid.HasType(CardType.Enchantment).Should().BeTrue();
        aid.Owner.Should().BeSameAs(_alice);
        aid.Controller.Should().BeSameAs(_alice);
        aid.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SigardasAid_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Sigarda's Aid", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sigarda's Aid");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SigardasAid_OnBattlefield_GrantsFlashToOwnersEquipmentInHand()
    {
        var (bus, zones, _, _) = BuildEngine();

        // Sigarda's Aid lands on the battlefield through ZoneService so
        // the FlashGrantStaticEffect's CardMovedEvent-driven sync fires.
        var aid = SigardasAidFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        // Equipment card in Alice's hand — Sigarda's static should grant it
        // flash. Use a vanilla Artifact with Equipment subtype to keep the
        // test isolated from any future Equipment-card factories.
        var equipment = new Artifact(
            name: "Hammer",
            manaCost: "{1}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Equipment });
        equipment.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(equipment);
        equipment.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(equipment).Should().BeTrue();

        // ActionValidator path — at sorcery-speed-unavailable timing
        // (opponent's turn / non-main / stack non-empty) the cast should
        // still be legal because the card "has flash".
        var validator = new ActionValidator();
        var action = new CastSpellAction(equipment, _alice, sorcerySpeedAvailable: false);
        validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void SigardasAid_OnBattlefield_GrantsFlashToOwnersAuraInHand()
    {
        var (bus, zones, _, _) = BuildEngine();

        var aid = SigardasAidFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        // Aura card in Alice's hand — same grant.
        var aura = new Enchantment(
            name: "Some Aura",
            manaCost: "{1}{W}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(aura);
        aura.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(aura).Should().BeTrue();
    }

    [Fact]
    public void SigardasAid_DoesNotGrantFlashToOpponentsEquipment()
    {
        var (bus, zones, _, _) = BuildEngine();

        var aid = SigardasAidFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        // Equipment in Bob's hand should NOT get flash (oracle: "YOU
        // control" — predicate keys on owner).
        var bobEquipment = new Artifact(
            name: "Bob's Hammer",
            manaCost: "{1}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Equipment });
        bobEquipment.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobEquipment);
        bobEquipment.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(bobEquipment).Should().BeFalse();
    }

    [Fact]
    public void SigardasAid_LeavesBattlefield_FlashGrantLifted()
    {
        var (bus, zones, _, _) = BuildEngine();

        var aid = SigardasAidFactory.Create(_alice, bus, triggers: null);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        var equipment = new Artifact(
            name: "Hammer",
            manaCost: "{1}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Equipment });
        equipment.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(equipment);
        equipment.SetZone(ZoneType.Hand);

        TimingRules.CanCastAtInstantSpeed(equipment).Should().BeTrue();

        // Sigarda's Aid leaves the battlefield — the FlashGrantStaticEffect
        // sees the CardMovedEvent and unregisters its grant.
        zones.MoveCardTo(aid, ZoneType.Graveyard, controller: _alice);

        TimingRules.CanCastAtInstantSpeed(equipment).Should().BeFalse();
    }

    [Fact]
    public void EquipmentEntersUnderController_TriggerAttachesToCreature()
    {
        var (bus, zones, stack, triggers) = BuildEngine();

        // Sigarda's Aid on the battlefield under Alice's control, fully
        // wired so the ETB-attach trigger registers with the bus.
        var aid = SigardasAidFactory.Create(_alice, bus, triggers);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        // Creature on the battlefield to receive the Equipment.
        var bearer = new Creature("Bearer", "{W}", 2, 2);
        bearer.SetOwner(_alice);
        bearer.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        // Equipment ETBs under Alice's control.
        var equipment = new Artifact(
            name: "Hammer",
            manaCost: "{1}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Equipment });
        equipment.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(equipment);
        equipment.SetZone(ZoneType.Hand);
        zones.MoveCardTo(equipment, ZoneType.Battlefield, controller: _alice);

        // Sigarda's trigger should have queued.
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        equipment.AttachedTo.Should().BeSameAs(bearer);
    }

    [Fact]
    public void EquipmentEntersUnderOpponent_NoSigardaTrigger()
    {
        var (bus, zones, _, triggers) = BuildEngine();

        // Alice controls Sigarda's Aid.
        var aid = SigardasAidFactory.Create(_alice, bus, triggers);
        _alice.Zones.Hand.AddCard(aid);
        aid.SetZone(ZoneType.Hand);
        zones.MoveCardTo(aid, ZoneType.Battlefield, controller: _alice);

        // Bob casts Equipment — Sigarda should NOT trigger (oracle: "under
        // YOUR control").
        var bobEquipment = new Artifact(
            name: "Bob's Hammer",
            manaCost: "{1}",
            supertypes: null,
            subtypes: new[] { CardSubtype.Equipment });
        bobEquipment.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobEquipment);
        bobEquipment.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bobEquipment, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (EventBus bus, ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (bus, zones, stack, triggers);
    }
}
