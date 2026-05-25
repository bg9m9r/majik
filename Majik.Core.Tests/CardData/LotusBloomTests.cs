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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LotusBloomFactory"/>.
///
/// Lotus Bloom (Time Spiral, no printed mana cost — Suspend 3—{0}):
///   "Suspend 3—{0}
///    {T}, Sacrifice Lotus Bloom: Add three mana of any one color."
///
/// Covers:
/// - Card identity (Artifact, no printed mana cost, non-legendary).
/// - NamedCardFactory dispatch.
/// - Hand cast restriction (only castable via Suspend / cast-from-exile).
/// - Suspend alt-cost shape (3 time counters, {0} mana cost).
/// - Five WUBRG mana abilities, each producing three mana of that colour.
/// - Activation taps the Bloom AND moves it to its owner's graveyard
///   (CR 701.16 — sacrifice as part of the activation cost).
/// - Sibling abilities become un-activatable once the Bloom has been
///   sacrificed.
/// </summary>
public class LotusBloomTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void LotusBloom_IsArtifact_NoPrintedManaCost_NonLegendary()
    {
        var bloom = LotusBloomFactory.Create(_alice);

        bloom.Name.Should().Be("Lotus Bloom");
        bloom.HasType(CardType.Artifact).Should().BeTrue();
        bloom.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Lotus Bloom is NOT legendary");
        bloom.ManaCost.Should().Be("",
            "Lotus Bloom prints with no mana cost — Scryfall mana_cost == \"\"");
        bloom.ManaCostValue.Should().Be(ManaCost.Zero,
            "empty mana cost parses to zero (CR 202.1a)");
        bloom.Owner.Should().BeSameAs(_alice);
        bloom.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LotusBloom()
    {
        var card = NamedCardFactory.Create("Lotus Bloom", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Lotus Bloom");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void LotusBloom_CannotBeCastFromHand_PerCR202_1a()
    {
        var bloom = LotusBloomFactory.Create(_alice);

        bloom.RestrictedCastZones.Should().Contain(ZoneType.Hand,
            "Lotus Bloom has no printed mana cost — only castable via Suspend (CR 117.7c)");
    }

    // --------------------------------------------------------------
    // Suspend alt-cost
    // --------------------------------------------------------------

    [Fact]
    public void BuildSuspendCost_Returns_Suspend3_For_Zero()
    {
        var suspend = LotusBloomFactory.BuildSuspendCost();

        suspend.TimeCounters.Should().Be(3);
        suspend.AlternativeManaCost.Should().Be(ManaCost.Parse("0"));
    }

    // --------------------------------------------------------------
    // Mana ability shape — three mana of one colour per WUBRG
    // --------------------------------------------------------------

    [Fact]
    public void LotusBloom_HasFiveManaAbilities_OnePerColor_EachProducesThree()
    {
        var bloom = LotusBloomFactory.Create(_alice);
        var mas = bloom.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 3
                                     && m.ManaGenerated.TotalValue == 3);
    }

    // --------------------------------------------------------------
    // Activation — taps + sacrifices, produces three of chosen colour
    // --------------------------------------------------------------

    [Fact]
    public void LotusBloom_Activate_ProducesThreeOfChosenColor_AndSacrificesBloom()
    {
        var bloom = LotusBloomFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bloom);

        var mas = bloom.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "bloom is untapped and on the battlefield");
        }

        // Activate the red mode.
        var red = mas.Single(m => m.ManaGenerated.Red == 3);
        var produced = red.Activate();

        produced.Red.Should().Be(3);
        produced.TotalValue.Should().Be(3);

        // Tapped (cost) + sacrificed (additional cost).
        bloom.IsTapped.Should().BeTrue("activation taps the bloom");
        bloom.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the bloom to its owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bloom);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bloom);

        // Sibling abilities are no longer activatable.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "bloom has been sacrificed — no further activations");
        }
    }

    [Fact]
    public void LotusBloom_ActivateViaActivator_CreditsManaPool()
    {
        var bloom = LotusBloomFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bloom);

        var activator = new Majik.Core.Services.ManaAbilityActivator();
        var blueAbility = bloom.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 3);

        _alice.ManaPool.Total.Should().Be(0);

        activator.ActivateManaAbility(blueAbility, _alice);

        _alice.ManaPool.Blue.Should().Be(3);
        _alice.ManaPool.Total.Should().Be(3);
        bloom.Zone.Should().Be(ZoneType.Graveyard);
    }
}
