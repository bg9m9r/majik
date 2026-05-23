using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MoxOpalFactory"/>.
///
/// Mox Opal — Legendary Artifact {0}.
/// "Metalcraft — {T}: Add one mana of any color. Activate only if you
///  control three or more artifacts." (CR 702.95)
///
/// Covers:
/// - Card identity (Legendary Artifact, mana cost {0}).
/// - NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG).
/// - Per-colour mana generation.
/// - Metalcraft gating: 2 artifacts → no, 3 artifacts (incl. self) → yes,
///   4+ artifacts → yes.
/// - Opponent artifacts do not count.
/// </summary>
public class MoxOpalTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_IsLegendaryArtifact_ZeroCost()
    {
        var mox = MoxOpalFactory.Create(_alice);

        mox.Name.Should().Be("Mox Opal");
        mox.HasType(CardType.Artifact).Should().BeTrue("Mox Opal is an Artifact");
        mox.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Mox Opal is Legendary");
        mox.ManaCost.Should().Be("{0}");
        mox.Owner.Should().BeSameAs(_alice);
        mox.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MoxOpal()
    {
        var card = NamedCardFactory.Create("Mox Opal", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mox Opal");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // --------------------------------------------------------------
    // Mana ability shape
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_HasFiveManaAbilities_OnePerColor()
    {
        var mox = MoxOpalFactory.Create(_alice);
        var mas = mox.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        // Each generates exactly one pip of its respective colour.
        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Metalcraft gate — off
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_CannotActivate_WhenControllerHasTwoArtifactsTotal()
    {
        // Two artifacts on the battlefield: Mox Opal itself + one other.
        // Metalcraft requires THREE.
        var mox = MoxOpalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        var other = new Artifact("Random Artifact A", "{1}");
        other.SetOwner(_alice);
        other.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "Metalcraft is inactive — controller has only 2 artifacts");
        }
    }

    // --------------------------------------------------------------
    // Metalcraft gate — on (3 artifacts, including Mox itself)
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_CanActivate_WithThreeArtifacts_IncludingSelf_AndGeneratesChosenColor()
    {
        var mox = MoxOpalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        var a1 = new Artifact("Artifact One", "{1}");
        a1.SetOwner(_alice);
        a1.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a1);

        var a2 = new Artifact("Artifact Two", "{1}");
        a2.SetOwner(_alice);
        a2.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(a2);

        var mas = mox.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "Metalcraft active — 3 artifacts on controller's battlefield");
        }

        // Pick the blue option, activate, verify mana + tap.
        var blue = mas.Single(m => m.ManaGenerated.Blue == 1);
        var produced = blue.Activate();

        produced.Blue.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        mox.IsTapped.Should().BeTrue("activating tapped Mox Opal");

        // After tapping, all five abilities are no longer activatable
        // (tap gate, independent of Metalcraft).
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse("Mox Opal is tapped");
        }
    }

    // --------------------------------------------------------------
    // Metalcraft gate — still on with 4+ artifacts
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_CanActivate_WithFourArtifacts()
    {
        var mox = MoxOpalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        for (var i = 0; i < 3; i++)
        {
            var a = new Artifact($"Artifact {i}", "{1}");
            a.SetOwner(_alice);
            a.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(a);
        }

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeTrue(
                "4 artifacts on controller's battlefield satisfies Metalcraft");
        }
    }

    // --------------------------------------------------------------
    // Opponent artifacts do not count
    // --------------------------------------------------------------

    [Fact]
    public void MoxOpal_OpponentsArtifactsDoNotCountTowardMetalcraft()
    {
        var mox = MoxOpalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // One artifact on Alice's side (the Mox itself), two on Bob's.
        // From Alice's perspective: only 1 artifact controlled → no Metalcraft.
        for (var i = 0; i < 2; i++)
        {
            var bobArt = new Artifact($"Bob Artifact {i}", "{1}");
            bobArt.SetOwner(_bob);
            bobArt.SetController(_bob);
            _bob.Zones.Battlefield.AddCard(bobArt);
        }

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "opponent's artifacts do not count toward Alice's Metalcraft");
        }
    }
}
