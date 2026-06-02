using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the COMBINED split card factory <see cref="WearTearFactory"/>
/// (Wear // Tear, Dragon's Maze, {1}{R} // {W}). Both faces are Instants.
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   Wear {1}{R} — Instant: "Destroy target artifact.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///   Tear {W}    — Instant: "Destroy target enchantment.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///
/// Split cards present each half as its own castable face (CR 712.2 — a split
/// card has two faces on one card; the caster picks one face to cast, and only
/// that face's cost / effect applies). This combined factory mirrors the
/// two-face posture of <see cref="FireIceFactory"/> / <see cref="BoomBustFactory"/>:
/// the combined card name "Wear // Tear" is the <c>[CardName]</c> dispatch key
/// (matching the embedded seed row), the card SHAPE is built from the embedded
/// JSON definition (<c>wear-tear.json</c>), and each face's resolve-time
/// <see cref="Game.SpellDefinition"/> is delegated to the already-implemented
/// single-half factories (<see cref="WearFactory"/> / <see cref="TearFactory"/>),
/// which carry the destroy-artifact / destroy-enchantment behaviour.
///
/// Fuse (CR 702.102) lets a player cast BOTH halves from hand as one split
/// spell. The engine has no split-cast / fuse cast surface yet (Fire // Ice and
/// Boom // Bust share this gap), so the combined object carries the front
/// (Wear) face's {1}{R} cost; the Fuse keyword is informational only.
///
/// Covers:
///   - Combined card identity (Instant, combined name, red, front Wear cost).
///   - <see cref="NamedCardFactory"/> dispatch for the combined name.
///   - Wear face delegation — destroy target artifact → graveyard (CR 701.7).
///   - Tear face delegation — destroy target enchantment → graveyard.
/// </summary>
[Trait("Color", "R")]
public class WearTearCombinedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ────────────────────────────────────────────────

    [Fact]
    public void WearTear_IsInstant_WithWearFrontFaceCost()
    {
        var card = WearTearFactory.Create(_alice);

        card.Name.Should().Be("Wear // Tear");
        card.HasType(CardType.Instant).Should().BeTrue();
        // The combined card carries the front (Wear) face mana cost.
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WearTear_IsRed()
    {
        var card = WearTearFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    // ── Wear face — destroy artifact (delegated to WearFactory) ─────────────

    [Fact]
    public void WearFace_DestroysTargetArtifact_MovesToGraveyard()
    {
        var artifact = new Artifact("Sol Ring", "{1}")
        { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = WearTearFactory.BuildWearDefinition(resolver: x => x);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");

        Resolve(def, artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Wear destroys target artifact (CR 701.7)");
    }

    // ── Tear face — destroy enchantment (delegated to TearFactory) ──────────

    [Fact]
    public void TearFace_DestroysTargetEnchantment_MovesToGraveyard()
    {
        var enchantment = new Enchantment("Sylvan Library", "{1}{G}")
        { Owner = _bob, Controller = _bob };
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var def = WearTearFactory.BuildTearDefinition(resolver: x => x);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("enchantment");

        Resolve(def, enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Tear destroys target enchantment (CR 701.7)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void Resolve(SpellDefinition def, ICard target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
