using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.30 — Echo. Reusable <see cref="EchoHelper"/>: at the controller's
/// upkeep, pay the echo cost or sacrifice the permanent — once, on the first
/// upkeep after it comes under control.
/// </summary>
public class EchoHelperTests
{
    private static Creature OnBattlefield(Player owner)
    {
        var card = new Creature("Echo Creature", "{1}{R}", 2, 2);
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }

    [Fact]
    public void Echo_Pays_WhenAffordableAndChosen_KeepsPermanent()
    {
        var alice = new Player("Alice", 20);
        var card = OnBattlefield(alice);
        var echo = ManaCost.Parse("{2}{R}");
        var debt = new EchoHelper.EchoDebt { Pending = true };

        alice.AddManaToPool(ManaCost.Parse("{2}{R}"));

        EchoHelper.ResolveEcho(card, echo, willPay: _ => true, debt);

        card.Zone.Should().Be(ZoneType.Battlefield, "echo paid → permanent stays");
        alice.Zones.Battlefield.GetCards().Should().Contain(card);
        debt.Pending.Should().BeFalse("the echo debt is satisfied this upkeep");
    }

    [Fact]
    public void Echo_Sacrifices_WhenDeclined()
    {
        var alice = new Player("Alice", 20);
        var card = OnBattlefield(alice);
        var echo = ManaCost.Parse("{2}{R}");
        var debt = new EchoHelper.EchoDebt { Pending = true };

        alice.AddManaToPool(ManaCost.Parse("{2}{R}"));

        EchoHelper.ResolveEcho(card, echo, willPay: _ => false, debt);

        card.Zone.Should().Be(ZoneType.Graveyard, "declined echo → sacrificed");
        alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        debt.Pending.Should().BeFalse();
    }

    [Fact]
    public void Echo_Sacrifices_WhenCannotAfford()
    {
        var alice = new Player("Alice", 20);
        var card = OnBattlefield(alice);
        var echo = ManaCost.Parse("{2}{R}");
        var debt = new EchoHelper.EchoDebt { Pending = true };

        // No mana in pool — even if the agent "wants" to pay, it can't.
        EchoHelper.ResolveEcho(card, echo, willPay: _ => true, debt);

        card.Zone.Should().Be(ZoneType.Graveyard, "can't pay echo → sacrificed");
        debt.Pending.Should().BeFalse();
    }

    [Fact]
    public void Echo_OnlyChargesOnce()
    {
        var alice = new Player("Alice", 20);
        var card = OnBattlefield(alice);
        var echo = ManaCost.Parse("{2}{R}");
        var debt = new EchoHelper.EchoDebt { Pending = true };

        alice.AddManaToPool(ManaCost.Parse("{2}{R}"));
        EchoHelper.ResolveEcho(card, echo, willPay: _ => true, debt); // pays
        alice.ManaPool.Generic.Should().Be(0);

        // Second upkeep: debt already cleared → no further charge, no sacrifice.
        alice.AddManaToPool(ManaCost.Parse("{2}{R}"));
        EchoHelper.ResolveEcho(card, echo, willPay: _ => false, debt);

        card.Zone.Should().Be(ZoneType.Battlefield, "echo only fires the first upkeep");
        alice.ManaPool.Generic.Should().Be(2, "no mana spent on the second upkeep");
    }

    [Fact]
    public void Echo_UpkeepTrigger_RegistersWithInterveningIf()
    {
        var alice = new Player("Alice", 20);
        var card = OnBattlefield(alice);

        var trigger = EchoHelper.AttachTo(card, ManaCost.Parse("{1}{R}"));

        trigger.Should().NotBeNull();
        card.Abilities.OfType<TriggeredAbility>().Should().Contain(trigger,
            "the echo upkeep trigger is attached to the card shape");
    }
}
