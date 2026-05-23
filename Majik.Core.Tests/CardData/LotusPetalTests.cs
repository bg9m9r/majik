using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LotusPetalFactory"/>.
///
/// Lotus Petal — Artifact {0}.
///   "{T}, Sacrifice Lotus Petal: Add one mana of any color."
///
/// Covers:
/// - Card identity (Artifact, mana cost {0}, non-legendary).
/// - NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG).
/// - Activation taps Lotus Petal AND moves it to its owner's graveyard
///   (CR 701.16 — sacrifice as part of the activation cost).
/// - Per-colour mana generation routes correctly into the controller's
///   mana pool.
/// - Sibling abilities become un-activatable once the petal has been
///   sacrificed (CanActivate returns false — petal is no longer on the
///   battlefield).
/// </summary>
public class LotusPetalTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void LotusPetal_IsArtifact_ZeroCost_NonLegendary()
    {
        var petal = LotusPetalFactory.Create(_alice);

        petal.Name.Should().Be("Lotus Petal");
        petal.HasType(CardType.Artifact).Should().BeTrue("Lotus Petal is an Artifact");
        petal.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Lotus Petal is NOT legendary (distinguishes it from the Mox / Lotus cycle)");
        petal.ManaCost.Should().Be("{0}");
        petal.Owner.Should().BeSameAs(_alice);
        petal.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LotusPetal()
    {
        var card = NamedCardFactory.Create("Lotus Petal", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Lotus Petal");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        card.ManaCost.Should().Be("{0}");
    }

    // --------------------------------------------------------------
    // Mana ability shape — one per WUBRG
    // --------------------------------------------------------------

    [Fact]
    public void LotusPetal_HasFiveManaAbilities_OnePerColor()
    {
        var petal = LotusPetalFactory.Create(_alice);
        var mas = petal.Abilities.OfType<ManaAbility>().ToList();

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

    // --------------------------------------------------------------
    // Activation — produces chosen colour, taps + sacrifices petal
    // --------------------------------------------------------------

    [Fact]
    public void LotusPetal_Activate_ProducesChosenColor_AndSacrificesPetal()
    {
        var petal = LotusPetalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(petal);

        // All five abilities are activatable while petal is on battlefield + untapped.
        var mas = petal.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "petal is untapped and on the battlefield");
        }

        // Activate the green option.
        var green = mas.Single(m => m.ManaGenerated.Green == 1);
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        // Petal is tapped (cost) AND moved to owner's graveyard (sacrifice).
        petal.IsTapped.Should().BeTrue("activation taps the petal");
        petal.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the petal from battlefield to its owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(petal,
            "petal has left the battlefield");
        _alice.Zones.Graveyard.GetCards().Should().Contain(petal,
            "petal is now in its owner's graveyard");

        // Sibling abilities are no longer activatable — petal is off the battlefield.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "petal has been sacrificed — no further activations possible");
        }
    }

    // --------------------------------------------------------------
    // ManaAbilityActivator path — pool gets credited
    // --------------------------------------------------------------

    [Fact]
    public void LotusPetal_ActivateViaActivator_CreditsManaPoolWithChosenColor()
    {
        var petal = LotusPetalFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(petal);

        var activator = new Majik.Core.Services.ManaAbilityActivator();
        var blueAbility = petal.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        _alice.ManaPool.Total.Should().Be(0);

        activator.ActivateManaAbility(blueAbility, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Total.Should().Be(1);
        petal.Zone.Should().Be(ZoneType.Graveyard);
    }
}
