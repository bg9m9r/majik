using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinChieftainFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Goblin + Warrior subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Printed Haste keyword on Chieftain itself (CR 702.10).
/// - LordStaticEffect: other controller-Goblins get +1/+1 + Haste.
/// - Controller's own non-Goblin creature is NOT pumped.
/// - Opponent's Goblin is NOT pumped (controller-scoped).
/// - LTB lifts the bonus (effect's IsActive gate falls when source
///   leaves the battlefield).
/// - Chieftain itself doesn't double-stack +1/+1 from its own static
///   (includeSelf: false).
/// </summary>
public class GoblinChieftainTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GoblinChieftain_Identity()
    {
        var c = GoblinChieftainFactory.Create(_alice);

        c.Name.Should().Be("Goblin Chieftain");
        c.ManaCost.Should().Be("{1}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinChieftain_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Chieftain", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Chieftain");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void GoblinChieftain_HasPrintedHaste()
    {
        var c = GoblinChieftainFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Haste",
            "CR 702.10 — Haste is the first printed keyword on Goblin Chieftain.");

        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    [Fact]
    public void GoblinChieftain_BuffsOtherControllerGoblin_Plus1Plus1AndHaste()
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

        var chieftain = GoblinChieftainFactory.Create(_alice, svc);
        chieftain.Zone = ZoneType.Battlefield;
        chieftain.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2,
            "other Goblins controlled by Chieftain's controller get +1/+1 (1 → 2 power).");
        otherGoblin.GetToughness().Should().Be(2);

        CombatAbilities.HasHaste(otherGoblin).Should().BeTrue(
            "Other Goblins gain Haste from Chieftain's static.");
    }

    [Fact]
    public void GoblinChieftain_DoesNotPump_NonGoblin()
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

        var chieftain = GoblinChieftainFactory.Create(_alice, svc);
        chieftain.Zone = ZoneType.Battlefield;
        chieftain.ActiveEffects = svc;

        bear.GetPower().Should().Be(2,
            "Chieftain only buffs creatures matching the Goblin subtype.");
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "non-Goblin creatures don't get the granted Haste.");
    }

    [Fact]
    public void GoblinChieftain_DoesNotPump_OpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = new Creature("Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var chieftain = GoblinChieftainFactory.Create(_alice, svc);
        chieftain.Zone = ZoneType.Battlefield;
        chieftain.ActiveEffects = svc;

        oppGoblin.GetPower().Should().Be(1,
            "Chieftain's static is scoped to its controller's Goblins (CR 109.5 — 'you').");
        oppGoblin.GetToughness().Should().Be(1);
        CombatAbilities.HasHaste(oppGoblin).Should().BeFalse(
            "opponent's Goblins don't get the granted Haste.");
    }

    [Fact]
    public void GoblinChieftain_DoesNotSelfPump_PlusOnePlusOne()
    {
        // includeSelf: false — Chieftain's own +1/+1 static doesn't stack on
        // itself. Its OWN Haste comes from the printed keyword, not the
        // static.
        var svc = new ContinuousEffectsService();

        var chieftain = GoblinChieftainFactory.Create(_alice, svc);
        chieftain.Zone = ZoneType.Battlefield;
        chieftain.ActiveEffects = svc;

        chieftain.GetPower().Should().Be(2, "Chieftain doesn't self-buff via 'Other Goblins'.");
        chieftain.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GoblinChieftain_LTB_LiftsBonusFromOtherGoblin()
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

        var chieftain = GoblinChieftainFactory.Create(_alice, svc);
        chieftain.Zone = ZoneType.Battlefield;
        chieftain.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(2);
        otherGoblin.GetToughness().Should().Be(2);

        // Chieftain dies — LordStaticEffect.IsActive() short-circuits when
        // the source isn't on the battlefield (CR 613).
        chieftain.SetZone(ZoneType.Graveyard);

        otherGoblin.GetPower().Should().Be(1, "bonus lifts on LTB");
        otherGoblin.GetToughness().Should().Be(1);
        CombatAbilities.HasHaste(otherGoblin).Should().BeFalse(
            "granted Haste lifts when Chieftain leaves the battlefield.");
    }

    [Fact]
    public void TwoChieftains_StackPower_HasteIdempotent()
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

        var chief1 = GoblinChieftainFactory.Create(_alice, svc);
        chief1.Zone = ZoneType.Battlefield;
        chief1.ActiveEffects = svc;

        var chief2 = GoblinChieftainFactory.Create(_alice, svc);
        chief2.Zone = ZoneType.Battlefield;
        chief2.ActiveEffects = svc;

        otherGoblin.GetPower().Should().Be(3,
            "two Chieftains stack +1/+1 — 1 base + 2 from two lords = 3.");
        otherGoblin.GetToughness().Should().Be(3);
        CombatAbilities.HasHaste(otherGoblin).Should().BeTrue(
            "Haste keyword grant is idempotent — set semantics, second grant is a no-op.");

        // Each Chieftain still buffs the OTHER Chieftain (includeSelf:
        // false only excludes self vs self).
        chief1.GetPower().Should().Be(3, "the other Chieftain's static still applies to this one.");
        chief2.GetPower().Should().Be(3);
    }
}
