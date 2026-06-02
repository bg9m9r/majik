using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ChromaticSphereFactory"/>.
///
/// Chromatic Sphere — Artifact {1} (Mirrodin).
///   "{1}, {T}, Sacrifice this artifact: Add one mana of any color.
///    Draw a card."
///
/// Near-identical to Chromatic Star ({1},{T},Sac: add any colour) — the
/// only behavioural difference is WHEN the cantrip resolves: the Sphere
/// draws as part of activating its ability (CR 605.1a — the ability adds
/// mana, has no target, isn't a loyalty ability, so it IS a mana ability;
/// the draw resolves with it and never uses the stack), whereas the Star
/// draws on a leaves-the-battlefield trigger. The Sphere therefore has NO
/// LTB trigger; the draw is folded into the activation closure.
///
/// Covers:
/// - Identity (Artifact, {1}) + NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG) — same fan-out as Chromatic Star.
/// - No triggered ability (the cantrip is on activation, not on LTB).
/// - Activation requires {1} in the pool (CanActivate gate) and is illegal
///   when the pool can't pay it.
/// - Activating one colour ability: pays {1}, taps + sacrifices the sphere,
///   credits one mana of the chosen colour, and draws a card.
/// - Sibling colour abilities un-activatable once sacrificed.
/// </summary>
[Trait("Color", "C")]
public class ChromaticSphereTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticSphere_IsArtifact_OneCost()
    {
        var sphere = ChromaticSphereFactory.Create(_alice);

        sphere.Name.Should().Be("Chromatic Sphere");
        sphere.HasType(CardType.Artifact).Should().BeTrue();
        sphere.ManaCost.Should().Be("{1}");
        sphere.Owner.Should().BeSameAs(_alice);
        sphere.Controller.Should().BeSameAs(_alice);
    }
    // --------------------------------------------------------------
    // Ability shape — 5 mana abilities, no triggers
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticSphere_HasFiveManaAbilities_OnePerColor()
    {
        var sphere = ChromaticSphereFactory.Create(_alice);
        var mas = sphere.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

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

    [Fact]
    public void ChromaticSphere_HasNoTriggeredAbility()
    {
        var sphere = ChromaticSphereFactory.Create(_alice);
        sphere.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the Sphere cantrips on activation, not on a leaves-the-battlefield trigger");
    }

    // --------------------------------------------------------------
    // Activation gate — requires {1} in the pool
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticSphere_CannotActivate_WithoutOneGenericInPool()
    {
        var sphere = ChromaticSphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        foreach (var ma in sphere.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "the {1} activation cost can't be paid from an empty pool");
        }
    }

    // --------------------------------------------------------------
    // Mana ability activation — pay {1}, tap, produce, sacrifice, draw
    // --------------------------------------------------------------

    [Fact]
    public void ChromaticSphere_Activate_PaysOne_ProducesColor_Sacrifices_AndDraws()
    {
        // A card to draw on top of the library.
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var sphere = ChromaticSphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        // Pay-for: {1} available in the pool.
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var mas = sphere.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "sphere is untapped, on the battlefield, with {1} in the pool");
        }

        // Activate the green option.
        var green = mas.Single(m => m.ManaGenerated.Green == 1);
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        // {1} was consumed paying the activation cost.
        _alice.ManaPool.Generic.Should().Be(0,
            "the {1} activation cost is deducted from the pool");

        sphere.IsTapped.Should().BeTrue("activation taps the sphere");
        sphere.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the sphere from battlefield to owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(sphere);
        _alice.Zones.Graveyard.GetCards().Should().Contain(sphere);

        // The cantrip drew a card as part of the activation.
        _alice.Zones.Hand.GetCards().Should().Contain(top, "the Sphere cantrips on activation");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        // Sibling colour abilities are now un-activatable.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "sphere has been sacrificed — no further activations possible");
        }
    }
}
