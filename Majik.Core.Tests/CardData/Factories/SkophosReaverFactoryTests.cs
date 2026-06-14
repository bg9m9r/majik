using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkophosReaverFactory"/> (Theros Beyond Death,
/// {2}{R}).
///
/// Creature — Minotaur Warrior 2/3. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "During your turn, this creature gets +2/+0.
///    Madness {1}{R}"
///
/// Covers the card's UNIQUE non-madness behaviour:
/// - Identity (name, type, mana cost, P/T, Minotaur + Warrior subtypes).
/// - "During your turn, +2/+0" — the conditional Layer 7c static surfaces a
///   4/3 on the controller's turn and a base 2/3 on an opponent's turn
///   (CR 613.3c / CR 611.2c).
/// - NamedCardFactory dispatch.
///
/// Madness {1}{R} is intrinsic (CR 702.35 — MadnessCatalog + the discard
/// funnel cover it) so it is intentionally not tested here.
/// </summary>
[Trait("Color", "R")]
public class SkophosReaverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SkophosReaver_Identity()
    {
        var c = SkophosReaverFactory.Create(_alice);

        c.Name.Should().Be("Skophos Reaver");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Minotaur).Should().BeTrue("Skophos Reaver is a Minotaur");
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue("Skophos Reaver is a Warrior");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SkophosReaver()
    {
        var c = NamedCardFactory.Create("Skophos Reaver", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Skophos Reaver");
        c.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
    }

    [Fact]
    public void SkophosReaver_DuringYourTurn_Gets4_3()
    {
        var svc = new ContinuousEffectsService { ActivePlayer = _alice };
        var c = SkophosReaverFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var chars = svc.Compute(c);
        chars.Power.Should().Be(4, "during your turn it gets +2/+0");
        chars.Toughness.Should().Be(3, "the pump is +2/+0, toughness unchanged");
    }

    [Fact]
    public void SkophosReaver_DuringOpponentsTurn_IsBase2_3()
    {
        var svc = new ContinuousEffectsService { ActivePlayer = _bob };
        var c = SkophosReaverFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var chars = svc.Compute(c);
        chars.Power.Should().Be(2, "outside your turn it is the base 2/3");
        chars.Toughness.Should().Be(3);
    }

    [Fact]
    public void SkophosReaver_PumpLiftsWhenTurnPasses()
    {
        var svc = new ContinuousEffectsService { ActivePlayer = _alice };
        var c = SkophosReaverFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        svc.Compute(c).Power.Should().Be(4);

        // The active player changes to the opponent — the static lifts on the
        // next compute (the gate is re-evaluated live, not registered/revoked).
        svc.ActivePlayer = _bob;
        svc.Compute(c).Power.Should().Be(2);

        // And re-applies when it becomes the controller's turn again.
        svc.ActivePlayer = _alice;
        svc.Compute(c).Power.Should().Be(4);
    }
}
