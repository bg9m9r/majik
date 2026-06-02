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
/// Unit tests for <see cref="PhyrexianRagerFactory"/>
/// (Mirrodin Besieged, {2}{B}).
///
/// Creature — Phyrexian Horror 2/2. Oracle text (verified against Scryfall):
///   "When this creature enters, you draw a card and you lose 1 life."
///
/// Covers:
///   - Identity (Creature, Phyrexian Horror subtypes, {2}{B}, black, 2/2,
///     owner/controller).
///   - NamedCardFactory dispatch.
///   - The single ETB trigger: structural shape + the ordered draw-then-
///     lose-life effects (CR 603.6 / CR 119.3).
/// </summary>
[Trait("Color", "B")]
public class PhyrexianRagerFactoryTests
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
    public void PhyrexianRager_Identity_CreaturePhyrexianHorror_2_2_Black2B()
    {
        var rager = PhyrexianRagerFactory.Create(_alice);

        rager.Name.Should().Be("Phyrexian Rager");
        rager.HasType(CardType.Creature).Should().BeTrue();
        rager.ManaCost.Should().Be("{2}{B}");
        rager.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(rager).Should().Contain(ManaColor.Black);
        rager.Power.Should().Be(2);
        rager.Toughness.Should().Be(2);
        rager.Subtypes.Should().Contain(CardSubtype.Phyrexian);
        rager.Subtypes.Should().Contain(CardSubtype.Horror);
        rager.Owner.Should().BeSameAs(_alice);
        rager.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhyrexianRager_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Phyrexian Rager", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Phyrexian Rager");
    }

    // -------------------------------------------------------------------------
    // ETB trigger — draw a card and lose 1 life (CR 603.6 / CR 119.3)
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_IsStructurallyPresent_BattlefieldActive()
    {
        var rager = PhyrexianRagerFactory.Create(_alice);

        var triggers = rager.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Phyrexian Rager prints one triggered ability — the ETB draw-and-lose-life.");
        triggers[0].Source.Should().BeSameAs(rager);
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

        var rager = PhyrexianRagerFactory.Create(alice);
        var trigger = rager.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        // Draw a card (CR 120.2): top of library moves to hand.
        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly one card was drawn off the top");

        // You lose 1 life (CR 119.3).
        alice.LifeTotal.Should().Be(19,
            "Phyrexian Rager's ETB makes its controller lose exactly 1 life");
    }
}
