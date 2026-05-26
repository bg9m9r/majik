using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Scion of Oona (Lorwyn, {1}{U}).
///
/// Covers:
///   - Identity (name, type, Faerie + Soldier subtypes, 1/1, mana cost).
///   - NamedCardFactory dispatch.
///   - Flash + Flying keyword markers.
///   - LordStaticEffect: other controller-Faeries get +1/+1 + Shroud.
///   - Non-Faerie controller creature NOT pumped.
///   - Opponent's Faerie NOT pumped (controller-scoped).
///   - Scion itself does NOT gain Shroud from its own static.
///   - LTB lifts the bonus (effect's IsActive gate falls when source leaves).
///   - Two Scions stack (+2/+2; Shroud idempotent).
/// </summary>
public class ScionOfOonaTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ScionOfOona_Identity()
    {
        var c = ScionOfOonaFactory.Create(_alice);

        c.Name.Should().Be("Scion of Oona");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void ScionOfOona_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Scion of Oona", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Scion of Oona");
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    [Fact]
    public void ScionOfOona_BuffsOtherControllerFaerie_Plus1Plus1AndShroud()
    {
        var svc = new ContinuousEffectsService();

        var otherFaerie = new Creature("Pestermite", "{2}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Faerie })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var scion = ScionOfOonaFactory.Create(_alice, svc);
        scion.Zone = ZoneType.Battlefield;
        scion.ActiveEffects = svc;

        otherFaerie.GetPower().Should().Be(3, "other Faeries get +1/+1 (2 → 3).");
        otherFaerie.GetToughness().Should().Be(2);

        svc.Compute(otherFaerie).Keywords.Should().Contain("Shroud",
            "CR 613.1f — granted Shroud keyword.");
    }

    [Fact]
    public void ScionOfOona_DoesNotPump_NonFaerie()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var scion = ScionOfOonaFactory.Create(_alice, svc);
        scion.Zone = ZoneType.Battlefield;
        scion.ActiveEffects = svc;

        bear.GetPower().Should().Be(2,
            "Scion only buffs creatures matching the Faerie subtype.");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Should().NotContain("Shroud");
    }

    [Fact]
    public void ScionOfOona_DoesNotPump_OpponentFaerie()
    {
        var svc = new ContinuousEffectsService();

        var oppFaerie = new Creature("Pestermite", "{2}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Faerie })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var scion = ScionOfOonaFactory.Create(_alice, svc);
        scion.Zone = ZoneType.Battlefield;
        scion.ActiveEffects = svc;

        oppFaerie.GetPower().Should().Be(2,
            "Scion's static is scoped to its controller's Faeries (CR 109.5).");
        oppFaerie.GetToughness().Should().Be(1);
        svc.Compute(oppFaerie).Keywords.Should().NotContain("Shroud");
    }

    [Fact]
    public void ScionOfOona_DoesNotSelfPump_OrSelfGrantShroud()
    {
        // includeSelf: false — Scion's own +1/+1 + Shroud static doesn't
        // touch itself. Critically, Scion stays a legal removal target.
        var svc = new ContinuousEffectsService();

        var scion = ScionOfOonaFactory.Create(_alice, svc);
        scion.Zone = ZoneType.Battlefield;
        scion.ActiveEffects = svc;

        scion.GetPower().Should().Be(1, "Scion doesn't self-buff via 'Other Faeries'.");
        scion.GetToughness().Should().Be(1);
        svc.Compute(scion).Keywords.Should().NotContain("Shroud",
            "Scion itself stays a legal removal target — canonical play pattern.");
    }

    [Fact]
    public void ScionOfOona_LTB_LiftsBonusFromOtherFaerie()
    {
        var svc = new ContinuousEffectsService();

        var otherFaerie = new Creature("Pestermite", "{2}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Faerie })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var scion = ScionOfOonaFactory.Create(_alice, svc);
        scion.Zone = ZoneType.Battlefield;
        scion.ActiveEffects = svc;

        otherFaerie.GetPower().Should().Be(3);
        svc.Compute(otherFaerie).Keywords.Should().Contain("Shroud");

        // Scion dies — LordStaticEffect.IsActive() short-circuits when
        // the source isn't on the battlefield (CR 613).
        scion.SetZone(ZoneType.Graveyard);

        otherFaerie.GetPower().Should().Be(2, "bonus lifts on LTB");
        otherFaerie.GetToughness().Should().Be(1);
        svc.Compute(otherFaerie).Keywords.Should().NotContain("Shroud",
            "granted Shroud lifts when Scion leaves the battlefield.");
    }

    [Fact]
    public void TwoScions_StackPower_ShroudIdempotent()
    {
        var svc = new ContinuousEffectsService();

        var otherFaerie = new Creature("Pestermite", "{2}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Faerie })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var scion1 = ScionOfOonaFactory.Create(_alice, svc);
        scion1.Zone = ZoneType.Battlefield;
        scion1.ActiveEffects = svc;

        var scion2 = ScionOfOonaFactory.Create(_alice, svc);
        scion2.Zone = ZoneType.Battlefield;
        scion2.ActiveEffects = svc;

        otherFaerie.GetPower().Should().Be(4,
            "two Scions stack +1/+1 — 2 base + 2 from two lords = 4.");
        otherFaerie.GetToughness().Should().Be(3);
        svc.Compute(otherFaerie).Keywords.Should().Contain("Shroud",
            "Shroud keyword grant is idempotent (HashSet semantics).");

        // Each Scion buffs the OTHER Scion + grants it Shroud
        // (includeSelf: false only excludes self vs self).
        scion1.GetPower().Should().Be(2);
        scion2.GetPower().Should().Be(2);
        svc.Compute(scion1).Keywords.Should().Contain("Shroud",
            "the other Scion's static grants Shroud to this one.");
    }
}
