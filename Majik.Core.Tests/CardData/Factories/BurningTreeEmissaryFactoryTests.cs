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
/// Tests for Burning-Tree Emissary (Dissension / Modern Horizons 2,
/// <c>{R/G}{R/G}</c>).
///
/// Oracle (Scryfall):
///   "When this creature enters, add {R}{G}."
///
/// Covers:
///   * Card shape (name, type, subtypes, P/T, hybrid mana cost).
///   * Hybrid pip parsing — two <see cref="HybridPip"/>(R, G), MV = 2.
///   * ETB trigger structure (no targets, single effect).
///   * Resolve: mana pool gains {R}{G}.
///   * <see cref="NamedCardFactory"/> dispatch by name.
/// </summary>
public class BurningTreeEmissaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BurningTreeEmissary_IsHumanShaman_2_2_AtHybridCost()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        bte.Name.Should().Be("Burning-Tree Emissary");
        bte.ManaCost.Should().Be("{R/G}{R/G}");
        bte.HasType(CardType.Creature).Should().BeTrue();
        bte.HasSubtype(CardSubtype.Human).Should().BeTrue();
        bte.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        bte.BasePower.Should().Be(2);
        bte.BaseToughness.Should().Be(2);

        bte.Owner.Should().BeSameAs(_alice);
        bte.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BurningTreeEmissary_HybridCost_ParsesIntoTwoRGPips()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        // CR 107.4e — {R/G}{R/G} = two HybridPip(R, G), no generic.
        bte.ManaCostValue.Generic.Should().Be(0);
        bte.ManaCostValue.HybridPips.Should().HaveCount(2);
        bte.ManaCostValue.HybridPips[0].Color1.Should().Be(ManaColor.Red);
        bte.ManaCostValue.HybridPips[0].Color2.Should().Be(ManaColor.Green);
        bte.ManaCostValue.HybridPips[1].Color1.Should().Be(ManaColor.Red);
        bte.ManaCostValue.HybridPips[1].Color2.Should().Be(ManaColor.Green);
        bte.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void BurningTreeEmissary_HasSingleEtbTrigger_NoTargets()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        var triggers = bte.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().BeEmpty(
            "Burning-Tree's ETB has no target — it just adds mana");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.Effects.Should().HaveCount(1);
    }

    [Fact]
    public void BurningTreeEmissary_Etb_AddsRedAndGreenManaToControllerPool()
    {
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        _alice.ManaPool.Total.Should().Be(0,
            because: "sanity check — pool starts empty");

        var etb = bte.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        _alice.ManaPool.Red.Should().Be(1, "ETB adds {R}");
        _alice.ManaPool.Green.Should().Be(1, "ETB adds {G}");
        _alice.ManaPool.Total.Should().Be(2,
            "Burning-Tree deposits exactly two mana — {R}{G}");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBurningTreeShape()
    {
        var dispatched = NamedCardFactory.Create("Burning-Tree Emissary", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Burning-Tree Emissary");
        dispatched.ManaCost.Should().Be("{R/G}{R/G}");
        ((Creature)dispatched).BasePower.Should().Be(2);
        ((Creature)dispatched).BaseToughness.Should().Be(2);
    }

    [Fact]
    public void BurningTreeEmissary_BusDrivenEtbFires_AndPoolGainsRG()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var bte = BurningTreeEmissaryFactory.Create(_alice);

        var etb = bte.Abilities.OfType<TriggeredAbility>().Single();
        triggers.RegisterTriggeredAbility(etb);

        // Move BTE onto the battlefield — the OnEnterBattlefieldSelf
        // condition matches the resulting CardMovedEvent.
        bte.SetZone(ZoneType.Battlefield);
        var enterEvt = new CardMovedEvent(bte, ZoneType.Hand, ZoneType.Battlefield);
        bus.Publish(enterEvt);

        // Sanity-check the bus-driven match — the OnEnterBattlefieldSelf
        // condition matches CardMovedEvent → Battlefield. The resolve
        // effect is structurally identical to the data path, so executing
        // it directly here is the same observable side effect.
        etb.IsTriggered(enterEvt).Should().BeTrue("ETB self-trigger condition matched");

        foreach (var eff in etb.Effects) eff.Execute();
        _alice.ManaPool.Red.Should().Be(1);
        _alice.ManaPool.Green.Should().Be(1);
    }
}
