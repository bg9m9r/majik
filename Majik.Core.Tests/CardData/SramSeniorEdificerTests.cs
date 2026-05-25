using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sram, Senior Edificer (Aether Revolt, {1}{W}, Legendary
/// Creature — Dwarf Advisor 2/2).
///
/// Covers:
///   - Identity (Legendary, Dwarf + Advisor, 2/2, {1}{W}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Casting an Aura spell → draw 1.
///   - Casting an Equipment spell → draw 1.
///   - Casting a Vehicle spell → draw 1.
///   - Casting a vanilla creature spell → no draw.
///   - Opponent casting an Aura → no draw.
/// </summary>
public class SramSeniorEdificerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewAuraSpell(Player controller)
    {
        var aura = new Enchantment("Shape the Sands", "1G",
            subtypes: new[] { CardSubtype.Aura }) { Owner = controller };
        return new Majik.Core.Spells.Spell(aura, controller);
    }

    private static Majik.Core.Spells.Spell NewEquipmentSpell(Player controller)
    {
        var equip = new Artifact("Bone Saw", "0",
            subtypes: new[] { CardSubtype.Equipment }) { Owner = controller };
        return new Majik.Core.Spells.Spell(equip, controller);
    }

    private static Majik.Core.Spells.Spell NewVehicleSpell(Player controller)
    {
        // Vehicle spells are Artifact (Creature shell in our v1 model, but
        // for a cast event we only need the printed-types view; building a
        // bare Artifact with the Vehicle subtype is sufficient to exercise
        // Sram's predicate without leaning on the full vehicle factory).
        var vehicle = new Artifact("Skysovereign, Consul Flagship", "6",
            subtypes: new[] { CardSubtype.Vehicle }) { Owner = controller };
        return new Majik.Core.Spells.Spell(vehicle, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller)
    {
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    [Fact]
    public void Sram_Identity()
    {
        var sram = SramSeniorEdificerFactory.Create(_alice);

        sram.Name.Should().Be("Sram, Senior Edificer");
        sram.ManaCost.Should().Be("{1}{W}");
        sram.HasType(CardType.Creature).Should().BeTrue();
        sram.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        sram.HasSubtype(CardSubtype.Dwarf).Should().BeTrue();
        sram.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        sram.BasePower.Should().Be(2);
        sram.BaseToughness.Should().Be(2);
        sram.Owner.Should().BeSameAs(_alice);
        sram.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sram_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sram, Senior Edificer", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sram, Senior Edificer");
        c.HasSubtype(CardSubtype.Dwarf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
    }

    [Fact]
    public void Sram_CastAuraSpell_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sram = SramSeniorEdificerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sram);
        sram.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewAuraSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "Aura spell fires Sram's cast trigger exactly once (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1", "drew the top card of the library");
    }

    [Fact]
    public void Sram_CastEquipmentSpell_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sram = SramSeniorEdificerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sram);
        sram.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewEquipmentSpell(_alice)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Sram_CastVehicleSpell_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sram = SramSeniorEdificerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sram);
        sram.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewVehicleSpell(_alice)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Sram_CastVanillaCreatureSpell_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sram = SramSeniorEdificerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sram);
        sram.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "vanilla creature spell does not match Aura / Equipment / Vehicle");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Sram_OpponentCastsAura_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sram = SramSeniorEdificerFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(sram);
        sram.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewAuraSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Sram's controller");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
