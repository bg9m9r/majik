using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HealerOfTheGladeFactory"/>.
///
/// Covers:
/// - Identity ({G} Creature — Elemental, 1/2, green, mana value 1).
/// - No Flying keyword (clean keyword list).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability (no intervening-if).
/// - ETB trigger fires and gains the controller exactly 3 life (CR 119.3).
/// - Wired Create(Player, IEventBus?, TriggerManager?) path: entering the
///   battlefield via a bus event gains controller 3 life end-to-end.
/// </summary>
[Trait("Color", "G")]
public class HealerOfTheGladeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HealerOfTheGlade_Identity()
    {
        var c = HealerOfTheGladeFactory.Create(_alice);

        c.Name.Should().Be("Healer of the Glade");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue("Healer of the Glade is an Elemental");
        c.ManaCost.Should().Be("{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HealerOfTheGlade_IsGreen()
    {
        var c = HealerOfTheGladeFactory.Create(_alice);
        // Color is derived from mana cost — {G} pip makes it green.
        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Green,
            "Healer of the Glade has a {G} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color");
    }

    [Fact]
    public void HealerOfTheGlade_ManaValue_IsOne()
    {
        var c = HealerOfTheGladeFactory.Create(_alice);
        // {G} = mana value 1 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(1, "CR 202.3 — {G} has mana value 1");
    }

    [Fact]
    public void HealerOfTheGlade_HasNoFlyingKeyword()
    {
        var c = HealerOfTheGladeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Healer of the Glade has no Flying");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HealerOfTheGlade_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = HealerOfTheGladeFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // ETB gain-life effect
    // -----------------------------------------------------------------------

    [Fact]
    public void HealerOfTheGlade_EtbTrigger_ControllerGainsThreeLife()
    {
        var alice = new Player("Alice", 20);
        var healer = HealerOfTheGladeFactory.Create(alice);

        var etb = healer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(23,
            "ETB grants controller exactly 3 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Wired path: TriggerManager + bus — entering battlefield gains 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void HealerOfTheGlade_WiredCreate_EnteringBattlefield_GainsThreeLife()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var healer = HealerOfTheGladeFactory.Create(alice, bus, triggerManager);
        healer.SetZone(ZoneType.Battlefield);

        // Simulate the card entering the battlefield via a CardMovedEvent.
        var moveEvent = new CardMovedEvent(healer, ZoneType.Hand, ZoneType.Battlefield);
        bus.Publish(moveEvent);

        // Resolve all pending triggered abilities.
        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            item?.Resolve();
        }

        alice.LifeTotal.Should().Be(23,
            "entering the battlefield via the bus gains controller 3 life end-to-end");
    }
}
