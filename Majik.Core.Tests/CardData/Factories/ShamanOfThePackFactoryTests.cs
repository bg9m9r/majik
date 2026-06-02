using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShamanOfThePackFactory"/> — Creature — Elf Shaman
/// 3/2 at {1}{B}{G} (Magic Origins).
///
/// Oracle: "When this creature enters, target opponent loses life equal to
/// the number of Elves you control."
///
/// Covers:
/// - Identity (Elf Shaman 3/2 at {1}{B}{G}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Single battlefield-active ETB triggered ability.
/// - ETB condition fires on this card moving to the battlefield only.
/// - Resolution: target opponent loses life = controller's Elf count
///   (including the Shaman itself; excluding opponent's Elves).
/// - No chosen target → clean no-op (CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class ShamanOfThePackFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfThePack_Identity_ElfShaman_3_2_At1BG()
    {
        var c = ShamanOfThePackFactory.Create(_alice);

        c.Name.Should().Be("Shaman of the Pack");
        c.ManaCost.Should().Be("{1}{B}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("Shaman of the Pack is an Elf");
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Shaman of the Pack is a Shaman");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ShamanOfThePack_HasSingleBattlefieldActiveEtbTrigger()
    {
        var c = ShamanOfThePackFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Shaman of the Pack has one ETB trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "CR 603.6a — the ETB trigger functions while on the battlefield");
    }

    [Fact]
    public void ShamanOfThePack_EtbTrigger_FiresOnSelfEnteringBattlefieldOnly()
    {
        var c = ShamanOfThePackFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var etb = GetEtbTrigger(c);

        var selfEvt = new Majik.Core.Events.CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(selfEvt).Should().BeTrue(
            "ETB trigger fires when this card moves to the battlefield");

        var other = new Creature("Other", "{1}{G}", 2, 2);
        var otherEvt = new Majik.Core.Events.CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(otherEvt).Should().BeFalse(
            "ETB trigger only fires for this specific card");
    }

    // -----------------------------------------------------------------------
    // Resolution — life loss = controller's Elf count
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_OpponentLosesLifeEqualToControllersElves_IncludingSelf()
    {
        var shaman = ShamanOfThePackFactory.Create(_alice);
        shaman.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(shaman);

        // Two other Elves under Alice's control → 3 Elves total (with Shaman).
        MakeElf(_alice, "Llanowar Elves");
        MakeElf(_alice, "Elvish Mystic");

        var etb = GetEtbTrigger(shaman);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        foreach (var e in etb.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(17,
            "target opponent loses life = Alice's Elves (Shaman + 2) = 3 (CR 119.3)");
        _bob.LifeLostThisTurn.Should().Be(3,
            "the loss feeds spectacle / revolt / lifegain observers (CR 119.3)");
    }

    [Fact]
    public void EtbTrigger_CountsOnlyControllersElves_IgnoresOpponentElves()
    {
        var shaman = ShamanOfThePackFactory.Create(_alice);
        shaman.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(shaman);

        // Bob controls two Elves — must NOT count toward Alice's drain.
        MakeElf(_bob, "Heritage Druid");
        MakeElf(_bob, "Dwynen's Elite");

        var etb = GetEtbTrigger(shaman);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        foreach (var e in etb.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "CR 109.5 — 'Elves you control' counts only Alice's battlefield (just the Shaman = 1)");
    }

    [Fact]
    public void EtbTrigger_NoOtherElvesAndShamanNotOnBattlefield_LosesZero()
    {
        // Shaman not placed on the battlefield → controller controls 0 Elves.
        var shaman = ShamanOfThePackFactory.Create(_alice);

        var etb = GetEtbTrigger(shaman);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        foreach (var e in etb.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            "no Elves controlled → opponent loses 0 life (CR 119.3 / Fx.LoseLife no-ops at 0)");
    }

    [Fact]
    public void EtbTrigger_NoChosenTarget_CleanNoOp()
    {
        var shaman = ShamanOfThePackFactory.Create(_alice);
        shaman.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(shaman);
        MakeElf(_alice, "Llanowar Elves");

        var etb = GetEtbTrigger(shaman);
        // No SetChosenTargets call — ChosenTargets is empty.

        var act = () =>
        {
            foreach (var e in etb.Effects) e.Execute();
        };

        act.Should().NotThrow("no chosen target → clean no-op (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20, "no target → no life change");
        _alice.LifeTotal.Should().Be(20);
    }
}
