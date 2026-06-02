using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KozileksPredatorFactory"/> (Rise of the
/// Eldrazi, {3}{G}).
///
/// Creature — Eldrazi Drone 3/3 (green). Oracle text (verified against
/// Scryfall):
///   "When this creature enters, create two 0/1 colorless Eldrazi Spawn
///    creature tokens. They have "Sacrifice this token: Add {C}.""
///
/// Covers:
///   - Identity (Eldrazi Drone 3/3 at {3}{G}, green, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - One ETB trigger attached structurally on the shape-only path.
///   - ETB effect mints TWO 0/1 colorless Eldrazi Spawn tokens under the
///     controller, each carrying a sac-for-{C} mana ability.
/// </summary>
[Trait("Color", "G")]
public class KozileksPredatorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void KozileksPredator_Identity()
    {
        var c = KozileksPredatorFactory.Create(_alice);

        c.Name.Should().Be("Kozilek's Predator");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KozileksPredator_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kozilek's Predator", _alice);

        c.Should().BeOfType<Creature>("Kozilek's Predator is a Creature instance");
        c.Name.Should().Be("Kozilek's Predator");
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    [Fact]
    public void KozileksPredator_HasOneEtbTrigger()
    {
        var c = KozileksPredatorFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB two-Spawn-token trigger is attached");
    }

    [Fact]
    public void EtbEffect_CreatesTwoEldraziSpawnUnderController()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = KozileksPredatorFactory.Create(_alice, zones, triggers);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        var spawns = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Spawn))
            .ToList();

        spawns.Should().HaveCount(2,
            "the ETB effect creates two Eldrazi Spawn tokens (CR 111.10)");

        foreach (var spawn in spawns)
        {
            spawn.Name.Should().Be("Eldrazi Spawn");
            spawn.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
            spawn.BasePower.Should().Be(0);
            spawn.BaseToughness.Should().Be(1);
            CardColors.GetColors(spawn).Should().BeEmpty(
                "Eldrazi Spawn tokens are colorless (CR 111.10)");

            // "Sacrifice this token: Add {C}." — wired as a ManaAbility (sac
            // cost deferred, same posture as Treasure/Food, see TokenFactory).
            spawn.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
                "each Spawn carries the Add {C} mana ability");
        }
    }
}
