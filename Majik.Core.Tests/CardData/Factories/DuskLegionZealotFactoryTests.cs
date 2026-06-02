using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DuskLegionZealotFactory"/>
/// (Rivals of Ixalan, {1}{B}).
///
/// Creature — Vampire Soldier 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, you draw a card and you lose 1 life."
///
/// Covers:
///   - Identity (Creature, Vampire Soldier subtypes, {1}{B}, black, 1/1,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - The single ETB trigger: structural shape + the ordered draw-then-
///     lose-life effects (CR 603.6 / CR 119.3).
/// </summary>
[Trait("Color", "B")]
public class DuskLegionZealotFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void DuskLegionZealot_Identity_CreatureVampireSoldier_1_1_Black1B()
    {
        var zealot = DuskLegionZealotFactory.Create(_alice);

        zealot.Name.Should().Be("Dusk Legion Zealot");
        zealot.HasType(CardType.Creature).Should().BeTrue();
        zealot.ManaCost.Should().Be("{1}{B}");
        zealot.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(zealot).Should().Contain(ManaColor.Black);
        zealot.Power.Should().Be(1);
        zealot.Toughness.Should().Be(1);
        zealot.Subtypes.Should().Contain(CardSubtype.Vampire);
        zealot.Subtypes.Should().Contain(CardSubtype.Soldier);
        zealot.Owner.Should().BeSameAs(_alice);
        zealot.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DuskLegionZealot_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Dusk Legion Zealot", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dusk Legion Zealot");
    }

    // -------------------------------------------------------------------------
    // ETB trigger — draw a card and lose 1 life (CR 603.6 / CR 119.3)
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_IsStructurallyPresent_BattlefieldActive()
    {
        var zealot = DuskLegionZealotFactory.Create(_alice);

        var triggers = zealot.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Dusk Legion Zealot prints one triggered ability — the ETB draw-and-lose-life.");
        triggers[0].Source.Should().BeSameAs(zealot);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void EtbTrigger_DrawsOneCard_AndLosesOneLife()
    {
        // CR 603.6 — "When this creature enters, you draw a card and you lose
        // 1 life." The two effects resolve in printed order.
        var alice = new Player("Alice", 20);
        var top = SeedLibraryCard(alice, "Top1");
        SeedLibraryCard(alice, "Top2"); // remains in library

        var zealot = DuskLegionZealotFactory.Create(alice);
        var trigger = zealot.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        // Draw a card (CR 120.2): top of library moves to hand.
        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly one card was drawn off the top");

        // You lose 1 life (CR 119.3).
        alice.LifeTotal.Should().Be(19,
            "Dusk Legion Zealot's ETB makes its controller lose exactly 1 life");
    }
}
