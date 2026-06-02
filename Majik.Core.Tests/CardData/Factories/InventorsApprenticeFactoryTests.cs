using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InventorsApprenticeFactory"/>.
///
/// Inventor's Apprentice (Kaladesh, {R}) is a Creature — Human Artificer 1/2.
/// Oracle text (verified against Scryfall 2026-06):
///   "This creature gets +1/+1 as long as you control an artifact."
///
/// Mechanically a sibling of Loam Lion / Kird Ape — the same conditional
/// self-pump shape — differing only in the predicate (control an artifact, a
/// card-TYPE test) and the bonus (+1/+1). These tests mirror
/// <see cref="LoamLionFactoryTests"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/2, Human + Artificer subtypes, mana
///   cost {R}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Artifact-conditional self-pump (Layer 7c):
///   - 0 artifacts → 1/2.
///   - 1 artifact → 2/3 (+1/+1).
///   - 2 artifacts → 2/3 (flat bonus, not per-artifact).
///   - Pump dynamically re-evaluates as an artifact ETBs / LTBs.
///   - Only the controller's artifacts count.
///   - Non-artifact permanents do not trigger the bonus.
///   - The apprentice itself (not an artifact) does not satisfy its own
///     predicate.
/// - Helper predicate (ControlsArtifact).
/// </summary>
[Trait("Color", "R")]
public class InventorsApprenticeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Artifact NewArtifact(Player owner, string name = "Memnite")
    {
        var a = new Artifact(name, "0") { Owner = owner };
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }

    private static Creature NewBear(Player owner, string name = "Grizzly Bears")
    {
        var b = new Creature(name, "1G", 2, 2) { Owner = owner };
        b.SetController(owner);
        b.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(b);
        return b;
    }

    [Fact]
    public void InventorsApprentice_Identity()
    {
        var c = InventorsApprenticeFactory.Create(_alice);

        c.Name.Should().Be("Inventor's Apprentice");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Inventor's Apprentice is a Human Artificer, not an artifact");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    private Creature NewApprenticeOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var c = InventorsApprenticeFactory.Create(_alice, effects, bus);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, _alice);
        c.ActiveEffects = effects;
        return c;
    }

    [Fact]
    public void Artifact_ZeroArtifacts_StaysOneTwo()
    {
        var c = NewApprenticeOnBattlefield();
        c.Power.Should().Be(1, "no artifact controlled; the apprentice is not itself an artifact");
        c.Toughness.Should().Be(2);
    }

    [Fact]
    public void Artifact_OneArtifact_ActivatesBonus_TwoThree()
    {
        var c = NewApprenticeOnBattlefield();
        NewArtifact(_alice);

        c.Power.Should().Be(2, "1 + 1 artifact bonus");
        c.Toughness.Should().Be(3, "2 + 1 artifact bonus");
    }

    [Fact]
    public void Artifact_TwoArtifacts_NoExtraStacking_TwoThree()
    {
        var c = NewApprenticeOnBattlefield();
        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");

        c.Power.Should().Be(2, "+1/+1 is a flat bonus, not per-artifact");
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void Artifact_NonArtifactPermanent_DoesNotActivate()
    {
        var c = NewApprenticeOnBattlefield();
        NewBear(_alice);

        c.Power.Should().Be(1, "a bear is not an artifact");
        c.Toughness.Should().Be(2);
    }

    [Fact]
    public void Artifact_DynamicallyReevaluates_OnArtifactComingAndGoing()
    {
        var c = NewApprenticeOnBattlefield();

        // No artifact yet.
        c.Power.Should().Be(1);

        // An artifact arrives → bonus flips on. The bystander artifact is added
        // via raw zone ops (no ActiveEffects wired), so invalidate the
        // layer-system cache explicitly via Clear() — production's
        // CardMovedEvent does this.
        var artifact = NewArtifact(_alice);
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);

        // Last artifact leaves → bonus flips off.
        _alice.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
    }

    [Fact]
    public void Artifact_OpponentsArtifactsDoNotCount()
    {
        var c = NewApprenticeOnBattlefield();
        NewArtifact(_bob, "Bob's Memnite");

        c.Power.Should().Be(1,
            "the bonus reads 'YOU control an artifact', not opponent's");
        c.Toughness.Should().Be(2);
    }

    [Fact]
    public void Artifact_ArtifactCreatureCounts()
    {
        var c = NewApprenticeOnBattlefield();

        // An artifact creature is still an artifact (CR 301.1) and satisfies
        // the predicate.
        var golem = new Creature("Golem", "0", 3, 3) { Owner = _alice };
        golem.AddCardType(CardType.Artifact);
        golem.SetController(_alice);
        golem.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(golem);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(2, "an artifact creature counts as an artifact you control");
        c.Toughness.Should().Be(3);
    }

    [Fact]
    public void ControlsArtifact_HelperPredicate()
    {
        InventorsApprenticeFactory.ControlsArtifact(_alice).Should().BeFalse();

        NewBear(_alice);
        InventorsApprenticeFactory.ControlsArtifact(_alice).Should().BeFalse(
            "a bear is not an artifact");

        NewArtifact(_alice);
        InventorsApprenticeFactory.ControlsArtifact(_alice).Should().BeTrue();
    }
}
