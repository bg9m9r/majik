using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GoblinTombRaiderFactory"/>.
///
/// Goblin Tomb Raider (Ixalan, {R}). Creature — Goblin Pirate 1/2. Oracle
/// text (verified against Scryfall 2026-06):
///   "As long as you control an artifact, this creature gets +1/+0 and has
///    haste."
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/2, Goblin/Pirate subtypes, mana
///   cost {R}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Artifact-conditional self-pump + Haste grant (Layer 7c + Layer 6):
///   - 0 artifacts → 1/2, no haste.
///   - 1 artifact → 2/2 (+1/+0) and haste.
///   - Bonus is flat (not per-artifact).
///   - Dynamically re-evaluates as an artifact ETBs / LTBs.
///   - Only the controller's artifacts count.
/// - Helper predicate (ControlsArtifact).
/// </summary>
[Trait("Color", "R")]
public class GoblinTombRaiderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Artifact NewArtifact(Player owner, string name = "Mox")
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(owner);
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }

    [Fact]
    public void GoblinTombRaider_Identity()
    {
        var raider = GoblinTombRaiderFactory.Create(_alice);

        raider.Name.Should().Be("Goblin Tomb Raider");
        raider.ManaCost.Should().Be("{R}");
        raider.HasType(CardType.Creature).Should().BeTrue();
        raider.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        raider.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        raider.BasePower.Should().Be(1);
        raider.BaseToughness.Should().Be(2);
        raider.Owner.Should().BeSameAs(_alice);
        raider.Controller.Should().BeSameAs(_alice);
    }

    private Creature NewRaiderOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var raider = GoblinTombRaiderFactory.Create(_alice, effects, bus);
        zones.MoveCard(raider, ZoneType.Library, ZoneType.Battlefield, _alice);
        raider.ActiveEffects = effects;
        return raider;
    }

    [Fact]
    public void Artifact_ZeroArtifacts_StaysOneTwo_NoHaste()
    {
        var raider = NewRaiderOnBattlefield();

        raider.Power.Should().Be(1);
        raider.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(raider).Should().BeFalse(
            "no artifact controlled → no haste");
    }

    [Fact]
    public void Artifact_OneArtifact_ActivatesBonus_TwoTwo_AndHaste()
    {
        var raider = NewRaiderOnBattlefield();
        NewArtifact(_alice);

        raider.Power.Should().Be(2, "1 + 1 artifact bonus");
        raider.Toughness.Should().Be(2, "+1/+0 leaves toughness unchanged");
        CombatAbilities.HasHaste(raider).Should().BeTrue(
            "an artifact is controlled → haste");
    }

    [Fact]
    public void Artifact_TwoArtifacts_NoExtraStacking()
    {
        var raider = NewRaiderOnBattlefield();
        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");

        raider.Power.Should().Be(2, "+1/+0 is a flat bonus, not per-artifact");
        raider.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(raider).Should().BeTrue();
    }

    [Fact]
    public void Artifact_DynamicallyReevaluates_OnArtifactComingAndGoing()
    {
        var raider = NewRaiderOnBattlefield();

        // No artifact yet.
        raider.Power.Should().Be(1);
        CombatAbilities.HasHaste(raider).Should().BeFalse();

        // An artifact arrives → bonus + haste flip on. The bystander artifact is
        // added via raw zone ops (no ActiveEffects wired), so invalidate the
        // layer-system cache explicitly via Clear() — production's
        // CardMovedEvent does this.
        var artifact = NewArtifact(_alice);
        raider.ActiveEffects!.Clear();
        raider.Power.Should().Be(2);
        raider.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(raider).Should().BeTrue();

        // Last artifact leaves → bonus + haste flip off.
        _alice.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        raider.ActiveEffects!.Clear();
        raider.Power.Should().Be(1);
        raider.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(raider).Should().BeFalse();
    }

    [Fact]
    public void Artifact_OpponentsArtifactsDoNotCount()
    {
        var raider = NewRaiderOnBattlefield();
        NewArtifact(_bob, "B1");

        raider.Power.Should().Be(1,
            "the bonus reads 'YOU control an artifact', not opponent's");
        raider.Toughness.Should().Be(2);
        CombatAbilities.HasHaste(raider).Should().BeFalse();
    }

    [Fact]
    public void ControlsArtifact_HelperPredicate()
    {
        GoblinTombRaiderFactory.ControlsArtifact(_alice).Should().BeFalse();

        NewArtifact(_alice);
        GoblinTombRaiderFactory.ControlsArtifact(_alice).Should().BeTrue();
    }
}
