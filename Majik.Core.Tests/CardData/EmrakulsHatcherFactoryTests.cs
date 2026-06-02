using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EmrakulsHatcherFactory"/> (Rise of the Eldrazi,
/// {4}{R}). Creature — Eldrazi Drone 3/3:
///   "When this creature enters, create three 0/1 colorless Eldrazi Spawn
///    creature tokens. They have \"Sacrifice this token: Add {C}.\""
///
/// Covers:
/// - Identity (Eldrazi Drone, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB <see cref="TriggeredAbility"/> shape.
/// - ETB-effect execution creates THREE 0/1 colourless Eldrazi Spawn tokens
///   on the controller's battlefield, each with a colourless-producing
///   <see cref="ManaAbility"/>.
/// </summary>
public class EmrakulsHatcherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EmrakulsHatcher_Identity()
    {
        var card = EmrakulsHatcherFactory.Create(_alice);

        card.Name.Should().Be("Emrakul's Hatcher");
        card.ManaCost.Should().Be("{4}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EmrakulsHatcher_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Emrakul's Hatcher", _alice);

        card.Should().BeOfType<Creature>("Emrakul's Hatcher is a Creature instance");
        card.Name.Should().Be("Emrakul's Hatcher");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    [Fact]
    public void EmrakulsHatcher_HasOneEtbTrigger()
    {
        var card = EmrakulsHatcherFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB Spawn-token trigger is attached");
    }

    // -----------------------------------------------------------------------
    // ETB trigger effect — execute and observe the three Spawn landing.
    // -----------------------------------------------------------------------

    [Fact]
    public void EmrakulsHatcher_EtbEffect_CreatesThreeEldraziSpawnUnderController()
    {
        var hatcher = EmrakulsHatcherFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hatcher);
        hatcher.SetZone(ZoneType.Battlefield);

        var trigger = hatcher.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var spawnOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Eldrazi Spawn")
            .ToList();

        spawnOnBoard.Should().HaveCount(3,
            "the ETB effect creates three Eldrazi Spawn tokens");
        spawnOnBoard.Should().OnlyContain(c => c.Power == 0 && c.Toughness == 1,
            "each Spawn is a 0/1 (CR 111.10)");
        spawnOnBoard.Should().OnlyContain(c => c.IsToken);
        spawnOnBoard.Should().OnlyContain(c => c.HasSubtype(CardSubtype.Eldrazi));
        spawnOnBoard.Should().OnlyContain(c => c.HasSubtype(CardSubtype.Spawn));
        spawnOnBoard.Should().OnlyContain(
            c => c.Abilities.OfType<ManaAbility>().Count() == 1,
            "each Spawn ships with \"Sacrifice this token: Add {C}.\" (sac rider deferred)");
    }
}
