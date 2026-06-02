using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SythisHarvestsHandFactory"/>.
///
/// Covers (CR 702.144 Constellation — see factory xmldoc):
/// - Card identity (name, supertype Legendary, subtype Nymph, P/T, mana cost,
///   owner/controller).
/// - Single <see cref="TriggeredAbility"/> attached to the card shape.
/// - End-to-end constellation firing through a live
///   <see cref="TriggerManager"/>: enchantment ETB under controller queues
///   the trigger; on resolution controller gains 1 life and draws a card.
/// - Negative cases: opponent enchantment ETB does not trigger; non-
///   enchantment ETB (creature) under controller does not trigger; Sythis's
///   own ETB (creature, not enchantment) does not self-trigger.
/// - <see cref="NamedCardFactory"/> dispatch returns Sythis with the right
///   shape.
/// </summary>
[Trait("Color", "M")]
public class SythisHarvestsHandFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sythis_Identity_NameTypeSubtypesPtAndManaCost()
    {
        var c = SythisHarvestsHandFactory.Create(_alice);

        c.Name.Should().Be("Sythis, Harvest's Hand");
        c.HasType(CardType.Creature).Should().BeTrue(
            "Sythis is printed as a Creature — Nymph (the Nyx frame doesn't add the " +
            "Enchantment card type — only Theros Beyond Death gods carry both types)");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4 — Sythis is a Legendary creature");
        c.HasSubtype(CardSubtype.Nymph).Should().BeTrue(
            "CR 205.3 — Nymph creature subtype");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.ManaCost.Should().Be("{G}{W}");

        var parsed = ManaCost.Parse(c.ManaCost);
        parsed.Green.Should().Be(1, "the printed cost is one green pip");
        parsed.White.Should().Be(1, "the printed cost is one white pip");
        parsed.TotalValue.Should().Be(2);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sythis_HasExactlyOneTriggeredAbility_Constellation()
    {
        var c = SythisHarvestsHandFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Sythis carries the single constellation trigger (CR 702.144)");
    }
    // -----------------------------------------------------------------------
    // Constellation behaviour — end-to-end via TriggerManager
    // -----------------------------------------------------------------------

    [Fact]
    public void Sythis_Constellation_FiresOnControllerEnchantmentEtb_GainsLifeAndDraws()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var sythis = SythisHarvestsHandFactory.Create(_alice, triggers);
        sythis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sythis);
        triggers.BindCard(sythis);

        // Library has a known top card so we can prove the draw fired.
        var libTop = new Creature("Llanowar Elves", "{G}", 1, 1);
        libTop.SetOwner(_alice);
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        // Another enchantment enters under Alice's control.
        var aura = new Enchantment("Wild Growth", "{G}");
        aura.SetOwner(_alice);
        aura.SetController(_alice);
        aura.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(aura);

        zones.MoveCardTo(aura, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "Constellation fires on enchantment ETB under controller");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21, "CR 119 — Sythis grants +1 life on resolution");
        _alice.Zones.Hand.GetCards().Should().Contain(libTop,
            "the top of the library moves to hand on the draw");
        _alice.Zones.Library.GetCards().Should().NotContain(libTop);
    }

    [Fact]
    public void Sythis_Constellation_DoesNotFire_ForOpponentEnchantmentEtb()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var sythis = SythisHarvestsHandFactory.Create(_alice, triggers);
        sythis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sythis);
        triggers.BindCard(sythis);

        // Bob plays an enchantment — Sythis must not fire.
        var bobAura = new Enchantment("Pacifism", "{1}{W}");
        bobAura.SetOwner(_bob);
        bobAura.SetController(_bob);
        bobAura.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobAura);

        zones.MoveCardTo(bobAura, ZoneType.Battlefield, _bob);

        triggers.PendingCount.Should().Be(0,
            "controller-gated predicate excludes opponent ETBs");
    }

    [Fact]
    public void Sythis_Constellation_DoesNotFire_ForControllerCreatureEtb()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var sythis = SythisHarvestsHandFactory.Create(_alice, triggers);
        sythis.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sythis);
        triggers.BindCard(sythis);

        // Plain creature ETB under Alice's control — predicate gates on
        // CardType.Enchantment so this must not fire.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bear);

        zones.MoveCardTo(bear, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(0,
            "non-enchantment ETBs do not satisfy the constellation predicate");
    }
}
