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
/// Unit tests for <see cref="CourtHomunculusFactory"/>.
///
/// Court Homunculus (Conflux, {W}). Artifact Creature — Homunculus 1/1.
/// Oracle text: "This creature gets +1/+1 as long as you control another
/// artifact."
///
/// Mirrors <see cref="ArdentRecruitFactoryTests"/> — same Layer-7c
/// conditional self-pump shape, but the threshold is "another artifact"
/// (one other artifact, CR 109.5 — the +1/+1 source excludes itself).
///
/// Covers:
/// - Identity (name, types Artifact + Creature, P/T 1/1, Homunculus
///   subtype, mana cost {W}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Conditional self-pump (Layer 7c):
///   - alone on battlefield → 1/1 (it is itself an artifact, but "another
///     artifact" excludes self).
///   - one other artifact → 2/2.
///   - re-evaluates dynamically as the other artifact ETBs / LTBs.
///   - only the controller's artifacts count.
/// - Helper predicate (ControlsAnotherArtifact).
/// </summary>
public class CourtHomunculusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Artifact NewArtifact(Player owner, string name = "Bauble")
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }

    [Fact]
    public void CourtHomunculus_Identity()
    {
        var ch = CourtHomunculusFactory.Create(_alice);

        ch.Name.Should().Be("Court Homunculus");
        ch.ManaCost.Should().Be("{W}");
        ch.HasType(CardType.Creature).Should().BeTrue();
        ch.HasType(CardType.Artifact).Should().BeTrue();
        ch.HasSubtype(CardSubtype.Homunculus).Should().BeTrue();
        ch.BasePower.Should().Be(1);
        ch.BaseToughness.Should().Be(1);
        ch.Owner.Should().BeSameAs(_alice);
        ch.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CourtHomunculus_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Court Homunculus", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Court Homunculus");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Homunculus).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    private (Creature ch, ContinuousEffectsService effects) NewHomunculusOnBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var ch = CourtHomunculusFactory.Create(_alice, effects, bus);
        zones.MoveCard(ch, ZoneType.Library, ZoneType.Battlefield, _alice);
        ch.ActiveEffects = effects;
        return (ch, effects);
    }

    [Fact]
    public void ConditionalPump_AloneOnBattlefield_StaysOneOne()
    {
        // Court Homunculus is itself an artifact, but "another artifact"
        // (CR 109.5) excludes itself — alone it has no other artifact.
        var (ch, _) = NewHomunculusOnBattlefield();
        ch.Power.Should().Be(1);
        ch.Toughness.Should().Be(1);
    }

    [Fact]
    public void ConditionalPump_OneOtherArtifact_TwoTwo()
    {
        var (ch, _) = NewHomunculusOnBattlefield();
        NewArtifact(_alice, "A1");

        ch.Power.Should().Be(2, "1 + 1 for controlling another artifact");
        ch.Toughness.Should().Be(2);
    }

    [Fact]
    public void ConditionalPump_TwoOtherArtifacts_NoExtraStacking_TwoTwo()
    {
        var (ch, _) = NewHomunculusOnBattlefield();
        NewArtifact(_alice, "A1");
        NewArtifact(_alice, "A2");

        ch.Power.Should().Be(2, "+1/+1 is a flat bonus, not per-artifact");
        ch.Toughness.Should().Be(2);
    }

    [Fact]
    public void ConditionalPump_DynamicallyReevaluates_OnArtifactComingAndGoing()
    {
        var (ch, _) = NewHomunculusOnBattlefield();
        ch.Power.Should().Be(1);

        // Another artifact arrives → bonus flips on.
        var a1 = NewArtifact(_alice, "A1");
        ch.Power.Should().Be(2);
        ch.Toughness.Should().Be(2);

        // It leaves → bonus flips off.
        _alice.Zones.Battlefield.RemoveCard(a1);
        a1.SetZone(ZoneType.Graveyard);
        ch.Power.Should().Be(1);
        ch.Toughness.Should().Be(1);
    }

    [Fact]
    public void ConditionalPump_OpponentsArtifactsDoNotCount()
    {
        var (ch, _) = NewHomunculusOnBattlefield();
        NewArtifact(_bob, "B1");
        NewArtifact(_bob, "B2");

        ch.Power.Should().Be(1,
            "the condition reads 'artifacts YOU control', not opponent's");
        ch.Toughness.Should().Be(1);
    }

    [Fact]
    public void ConditionalPump_HelperPredicate()
    {
        var ch = CourtHomunculusFactory.Create(_alice);
        ch.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ch);

        // Court Homunculus itself is an artifact, but "another" excludes it.
        CourtHomunculusFactory.ControlsAnotherArtifact(_alice, ch).Should().BeFalse();

        NewArtifact(_alice, "A1");
        CourtHomunculusFactory.ControlsAnotherArtifact(_alice, ch).Should().BeTrue();
    }
}
