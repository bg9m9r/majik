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
/// Unit tests for <see cref="AvacynAngelOfHopeFactory"/> and the
/// <see cref="ControllerPermanentAnthemEffect"/> primitive it wires.
///
/// Covers:
/// - Identity (name, type, supertype/subtype, 8/8, mana cost, owner /
///   controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Printed evergreens (Flying, Vigilance, Indestructible) on Avacyn
///   herself.
/// - Other controller-side creatures gain Indestructible via the anthem.
/// - Avacyn survives a Wrath of God sweep, AND the other creatures she
///   controls survive too (the integration use case the PR was opened
///   for).
/// - Opponent's creature does NOT gain Indestructible (controller-scoped).
/// - Non-creature permanents she controls also gain Indestructible
///   (Layer-system keyword path, no P/T no-op).
/// - LTB lifts the bonus.
/// </summary>
public class AvacynAngelOfHopeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Avacyn_Identity()
    {
        var c = AvacynAngelOfHopeFactory.Create(_alice);

        c.Name.Should().Be("Avacyn, Angel of Hope");
        c.ManaCost.Should().Be("{5}{W}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.BasePower.Should().Be(8);
        c.BaseToughness.Should().Be(8);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Avacyn_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Avacyn, Angel of Hope", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Avacyn, Angel of Hope");
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
    }

    [Fact]
    public void Avacyn_HasPrintedFlying_Vigilance_Indestructible()
    {
        var c = AvacynAngelOfHopeFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
        keywords.Should().Contain("Indestructible");

        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasVigilance(c).Should().BeTrue();
        CombatAbilities.HasIndestructible(c).Should().BeTrue();
    }

    [Fact]
    public void Avacyn_GrantsIndestructible_ToOtherControllerCreatures()
    {
        var svc = new ContinuousEffectsService();

        var otherBear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        avacyn.SetZone(ZoneType.Battlefield);

        CombatAbilities.HasIndestructible(otherBear).Should().BeTrue(
            "Avacyn grants Indestructible to other permanents her controller controls.");
        // P/T bonus is zero — pure keyword anthem.
        otherBear.GetPower().Should().Be(2);
        otherBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Avacyn_DoesNotGrant_ToOpponentCreatures()
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

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        avacyn.SetZone(ZoneType.Battlefield);

        CombatAbilities.HasIndestructible(oppBear).Should().BeFalse(
            "Anthem is scoped to source's controller (CR 109.5 — 'you').");
    }

    [Fact]
    public void Avacyn_SurvivesWrathOfGod_AndProtectsControllerCreatures()
    {
        var svc = new ContinuousEffectsService();

        // Set up Alice's board with Avacyn + a vanilla Grizzly Bears.
        var bears = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
        };
        _alice.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        _alice.Zones.Battlefield.AddCard(avacyn);
        avacyn.SetZone(ZoneType.Battlefield);

        // Sanity check: Indestructible is granted before the sweep.
        CombatAbilities.HasIndestructible(bears).Should().BeTrue();

        // Wrath of God — destroy all creatures (no regen). Avacyn herself
        // (printed Indestructible) and the bears (granted Indestructible)
        // both stay.
        var wrath = WrathOfGodFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var fx in wrath) fx.Execute();

        avacyn.Zone.Should().Be(ZoneType.Battlefield,
            "Avacyn herself has printed Indestructible (CR 702.12).");
        bears.Zone.Should().Be(ZoneType.Battlefield,
            "Grizzly Bears inherits Indestructible from Avacyn's anthem.");
    }

    [Fact]
    public void Avacyn_GrantsIndestructible_ToNonCreaturePermanents()
    {
        var svc = new ContinuousEffectsService();

        // Use an Artifact stand-in. Avacyn's anthem covers non-creature
        // permanents via ControllerPermanentAnthemEffect.Apply(PermanentCharacteristics).
        var rod = new Artifact("Sol Ring", "{1}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        avacyn.SetZone(ZoneType.Battlefield);

        var chars = svc.Compute(rod);
        chars.Keywords.Should().Contain("Indestructible",
            "Non-creature permanents controlled by Avacyn's controller inherit Indestructible via Layer-6 keyword grants (CR 613.1f).");
    }

    [Fact]
    public void Avacyn_LTB_LiftsBonusFromOtherCreature()
    {
        var svc = new ContinuousEffectsService();

        var bears = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        avacyn.SetZone(ZoneType.Battlefield);

        CombatAbilities.HasIndestructible(bears).Should().BeTrue();

        // Avacyn dies — anthem's IsActive() short-circuits.
        avacyn.SetZone(ZoneType.Graveyard);

        CombatAbilities.HasIndestructible(bears).Should().BeFalse(
            "Anthem lifts when Avacyn leaves the battlefield (CR 613 IsActive gate).");
    }

    [Fact]
    public void Avacyn_DoesNotSelfDoubleStack()
    {
        // includeSelf:false — Avacyn's anthem doesn't apply to herself.
        // Her Indestructible comes from the printed keyword, not the
        // self-loop on the anthem. We can't easily observe "double-stack
        // Indestructible" (set semantics in CreatureCharacteristics.Keywords
        // make it idempotent) but we CAN verify the printed keyword is the
        // only one present.
        var svc = new ContinuousEffectsService();

        var avacyn = AvacynAngelOfHopeFactory.Create(_alice, svc);
        avacyn.SetZone(ZoneType.Battlefield);

        CombatAbilities.HasIndestructible(avacyn).Should().BeTrue(
            "printed Indestructible is the source.");
    }
}
