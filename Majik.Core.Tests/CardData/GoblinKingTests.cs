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
/// Unit tests for <see cref="GoblinKingFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Goblin subtype, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - LordStaticEffect: other controller-Goblins get +1/+1 + Mountainwalk.
/// - <c>allPlayers: true</c> — opponent's Goblins ALSO get +1/+1 +
///   Mountainwalk (Lord of Atlantis shape — printed text has no
///   "you control" qualifier).
/// - includeSelf: false — Goblin King doesn't self-buff its own +1/+1.
/// - Two Goblin Kings buff each other (each is "Other" relative to the
///   other's static).
/// - LTB lifts the bonus.
/// - Non-Goblin creature is NOT pumped (subtype gate).
/// </summary>
public class GoblinKingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GoblinKing_Identity()
    {
        var c = GoblinKingFactory.Create(_alice);

        c.Name.Should().Be("Goblin King");
        c.ManaCost.Should().Be("{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinKing_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin King", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin King");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
    }

    [Fact]
    public void GoblinKing_BuffsOtherControllerGoblin_Plus1Plus1AndMountainwalk()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var king = GoblinKingFactory.Create(_alice, svc);
        king.Zone = ZoneType.Battlefield;
        king.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2,
            "other Goblins get +1/+1 from Goblin King (1 → 2 power).");
        otherGoblin.GetToughness().Should().Be(2);

        svc.Compute(otherGoblin).Keywords
            .Should().Contain("Mountainwalk",
                "Goblin King grants Mountainwalk to other Goblins (CR 702.14b).");
    }

    [Fact]
    public void GoblinKing_AllPlayers_BuffsOpponentGoblin()
    {
        // Lord of Atlantis shape — the printed oracle text has NO "you
        // control" rider, so opposing Goblins ALSO get +1/+1 +
        // Mountainwalk. This is the canonical Goblin King distinction
        // from Goblin Chieftain (which is "you control").
        var svc = new ContinuousEffectsService();

        var oppGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var king = GoblinKingFactory.Create(_alice, svc);
        king.Zone = ZoneType.Battlefield;
        king.ActiveEffects = svc;

        oppGoblin.GetPower().Should().Be(2,
            "Goblin King is all-players (Lord of Atlantis shape) — opponent's Goblins also get +1/+1.");
        oppGoblin.GetToughness().Should().Be(2);
        svc.Compute(oppGoblin).Keywords
            .Should().Contain("Mountainwalk",
                "all-Goblins includes opponent's Goblins.");
    }

    [Fact]
    public void GoblinKing_DoesNotPump_NonGoblin()
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

        var king = GoblinKingFactory.Create(_alice, svc);
        king.Zone = ZoneType.Battlefield;
        king.ActiveEffects = svc;

        bear.GetPower().Should().Be(2,
            "Goblin King only buffs creatures matching the Goblin subtype.");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords
            .Should().NotContain("Mountainwalk",
                "non-Goblin creatures don't get the granted Mountainwalk.");
    }

    [Fact]
    public void GoblinKing_DoesNotSelfPump()
    {
        // includeSelf: false — Goblin King's own +1/+1 static doesn't
        // stack on itself.
        var svc = new ContinuousEffectsService();

        var king = GoblinKingFactory.Create(_alice, svc);
        king.Zone = ZoneType.Battlefield;
        king.ActiveEffects = svc;

        king.GetPower().Should().Be(2, "Goblin King doesn't self-buff via 'Other Goblins'.");
        king.GetToughness().Should().Be(2);
        svc.Compute(king).Keywords
            .Should().NotContain("Mountainwalk",
                "a lone Goblin King doesn't grant Mountainwalk to itself.");
    }

    [Fact]
    public void TwoGoblinKings_BuffEachOther()
    {
        // Each King's static says "Other Goblin creatures" — the OTHER
        // King is "other" relative to this one. So two Kings stack
        // +1/+1 on each other and each grants Mountainwalk to the other.
        var svc = new ContinuousEffectsService();

        var king1 = GoblinKingFactory.Create(_alice, svc);
        king1.Zone = ZoneType.Battlefield;
        king1.ActiveEffects = svc;

        var king2 = GoblinKingFactory.Create(_alice, svc);
        king2.Zone = ZoneType.Battlefield;
        king2.ActiveEffects = svc;

        king1.GetPower().Should().Be(3,
            "the OTHER Goblin King's static pumps this one (+1/+1).");
        king1.GetToughness().Should().Be(3);
        king2.GetPower().Should().Be(3);
        king2.GetToughness().Should().Be(3);
        svc.Compute(king1).Keywords
            .Should().Contain("Mountainwalk",
                "each King grants Mountainwalk to the other.");
        svc.Compute(king2).Keywords
            .Should().Contain("Mountainwalk");
    }

    [Fact]
    public void GoblinKing_LTB_LiftsBonusFromOtherGoblin()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var king = GoblinKingFactory.Create(_alice, svc);
        king.Zone = ZoneType.Battlefield;
        king.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2);
        otherGoblin.GetToughness().Should().Be(2);

        king.SetZone(ZoneType.Graveyard);

        otherGoblin.GetPower().Should().Be(1, "bonus lifts on LTB (IsActive battlefield gate).");
        otherGoblin.GetToughness().Should().Be(1);
        svc.Compute(otherGoblin).Keywords
            .Should().NotContain("Mountainwalk",
                "granted Mountainwalk lifts when King leaves the battlefield.");
    }

    [Fact]
    public void GoblinKing_SingleArgOverload_NoStaticRegistered()
    {
        // No ContinuousEffectsService passed — shape-only path. Other
        // Goblins don't get the buff.
        var king = GoblinKingFactory.Create(_alice);
        king.Should().NotBeNull();
        king.Name.Should().Be("Goblin King");
        king.BasePower.Should().Be(2);
        king.BaseToughness.Should().Be(2);
    }
}
