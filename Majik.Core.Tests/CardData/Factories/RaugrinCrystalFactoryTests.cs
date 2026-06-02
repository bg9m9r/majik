using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RaugrinCrystalFactory"/> — the Modern Horizons 3
/// Jeskai "Crystal" mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add {U}, {R}, or {W}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Mirrors <see cref="RaugrinTriomeFactoryTests"/>. Covers:
/// - Identity (Artifact, mana cost {3}).
/// - Three "{T}: Add" mana abilities producing {U}, {R}, {W} (CR 605.1),
///   each gated on the artifact being untapped.
/// - Cycling ability shape (ManaCostCost {2} + DiscardSelfCost via the shared
///   <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycling: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class RaugrinCrystalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void RaugrinCrystal_HasThreeManaAbilities_ProducingURW()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Raugrin Crystal", _alice);
        var mana = artifact.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "Crystal taps for {U}, {R}, or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void RaugrinCrystal_ManaAbilities_GatedOnUntapped()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Raugrin Crystal", _alice);
        var blue = artifact.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        // CR 605.1 — the tap ({T}) is the mana ability's cost; while the
        // artifact is untapped the slot is active, and tapping it disables it.
        blue.CanActivate().Should().BeTrue("untapped artifact can tap for mana");
        artifact.Tap();
        blue.CanActivate().Should().BeFalse("a tapped artifact cannot tap again");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void RaugrinCrystal_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Raugrin Crystal", _alice);
        var cycling = artifact.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = {2} mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Cycling {2} charges 2 generic mana");
        manaCost.Blue.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.White.Should().Be(0);
    }

    [Fact]
    public void RaugrinCrystal_HasCyclingKeywordMarker()
    {
        var artifact = (Artifact)NamedCardFactory.Create("Raugrin Crystal", _alice);
        artifact.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // End-to-end cycling — pays {2}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void RaugrinCrystal_Cycling_EndToEnd_PaysTwoDiscardsSelfDrawsOne()
    {
        var topCard = new Card("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var crystal = RaugrinCrystalFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(crystal);
        crystal.SetZone(ZoneType.Hand);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var cycling = crystal.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        crystal.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(crystal);
    }
}
