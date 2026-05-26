using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MirariWakeFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller wiring).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Anthem (+1/+1) to controller's creatures via
///   <see cref="ControllerCreatureAnthemEffect"/>.
/// - Opponent's creatures untouched.
/// - LTB lifts the bonus.
/// - Two copies stack additively.
///
/// Mana-tap doubling is deferred (see factory xmldoc) — not covered here.
/// </summary>
public class MirariWakeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MirarisWake_Identity()
    {
        var card = MirariWakeFactory.Create(_alice);

        card.Name.Should().Be("Mirari's Wake");
        card.ManaCost.Should().Be("{3}{G}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MirarisWake_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mirari's Wake", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Mirari's Wake");
    }

    [Fact]
    public void MirarisWake_BuffsControllersCreatures_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(3,
            "Mirari's Wake gives all creatures you control +1/+1 (2→3).");
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void MirarisWake_DoesNotPump_OpponentCreatures()
    {
        var svc = new ContinuousEffectsService();

        var oppBear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        oppBear.GetPower().Should().Be(2,
            "Mirari's Wake is scoped to controller's creatures (CR 109.5 — 'you').");
        oppBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MirarisWake_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake = MirariWakeFactory.Create(_alice, svc);
        wake.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(3);

        // Mirari's Wake LTB → IsActive gate falls (CR 613).
        wake.SetZone(ZoneType.Graveyard);

        bear.GetPower().Should().Be(2, "bonus lifts on LTB");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void TwoMirarisWakes_StackAdditively()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var wake1 = MirariWakeFactory.Create(_alice, svc);
        wake1.Zone = ZoneType.Battlefield;

        var wake2 = MirariWakeFactory.Create(_alice, svc);
        wake2.Zone = ZoneType.Battlefield;

        bear.GetPower().Should().Be(4, "two Mirari's Wakes stack: 2 base + 1 + 1 = 4.");
        bear.GetToughness().Should().Be(4);
    }
}
