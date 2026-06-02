using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="NecrogenSpellbombFactory"/> — Artifact {1} with two
/// sacrifice-self activated abilities (Scryfall verified):
///   "{B}, Sacrifice this artifact: Target player discards a card."
///   "{1}, Sacrifice this artifact: Draw a card."
///
/// Mirrors <see cref="AetherSpellbombTests"/> (same sac-spellbomb shape); the
/// colored mode is a targeted discard instead of a creature bounce. The
/// discarding player chooses which card (CR 701.7a) — modelled here with the
/// deterministic first-card pick used by Mind Rot's no-agent fallback.
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller), loaded from
///   <c>necrogen-spellbomb.json</c>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: two <see cref="ActivatedAbility"/>s with the correct costs
///   and (for the discard) one 1..1 "target player" request.
/// - Discard-mode resolution: target player discards a card, spellbomb
///   sacrificed.
/// - Cantrip-mode resolution: controller draws 1, spellbomb sacrificed.
/// - Empty-hand / empty-library edge cases — no crash, sacrifice still occurs.
/// </summary>
public class NecrogenSpellbombTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NecrogenSpellbomb_IsArtifact_WithOneManaCost()
    {
        var bomb = NecrogenSpellbombFactory.Create(_alice);

        bomb.HasType(CardType.Artifact).Should().BeTrue();
        bomb.Name.Should().Be("Necrogen Spellbomb");
        bomb.ManaCost.Should().Be("{1}");
        bomb.Owner.Should().BeSameAs(_alice);
        bomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NecrogenSpellbomb()
    {
        var card = NamedCardFactory.Create("Necrogen Spellbomb", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Necrogen Spellbomb");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void NecrogenSpellbomb_HasTwoActivatedAbilities()
    {
        var bomb = NecrogenSpellbombFactory.Create(_alice);

        bomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DiscardAbility_HasB_AndSacrifice_AndOnePlayerTarget()
    {
        var bomb = NecrogenSpellbombFactory.Create(_alice);

        var discard = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        discard.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("B"),
                "the discard mode costs {B}");
        discard.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the discard mode sacrifices the spellbomb");

        discard.TargetRequests[0].MinTargets.Should().Be(1);
        discard.TargetRequests[0].MaxTargets.Should().Be(1);
        discard.TargetRequests[0].Description.Should().Contain("player");
    }

    [Fact]
    public void DrawAbility_Has1Generic_AndSacrifice_AndNoTargets()
    {
        var bomb = NecrogenSpellbombFactory.Create(_alice);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("1"),
                "the cantrip mode costs {1}");
        draw.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                "the cantrip mode sacrifices the spellbomb");
    }

    // -----------------------------------------------------------------------
    // {B}, sac: target player discards a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Discard_TargetPlayerDiscardsACard_AndSacrificesSpellbomb()
    {
        // Bob holds two cards; Alice activates {B}, sac targeting Bob.
        var inHand1 = new Card("Hand Card 1", "");
        var inHand2 = new Card("Hand Card 2", "");
        _bob.Zones.Hand.AddCard(inHand1);
        _bob.Zones.Hand.AddCard(inHand2);
        inHand1.SetZone(ZoneType.Hand);
        inHand2.SetZone(ZoneType.Hand);

        var bomb = NecrogenSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var discard = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        discard.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        discard.Resolve();

        // Exactly one card discarded from Bob's hand to his graveyard.
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(1);
        _bob.Zones.Graveyard.GetCards().Single().Zone.Should().Be(ZoneType.Graveyard);

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Discard_TargetSelf_Works()
    {
        // A player may target themselves (the discard is not opponent-only).
        var card = new Card("Alice Hand Card", "");
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var bomb = NecrogenSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var discard = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        discard.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _alice },
        });

        discard.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Discard_EmptyHand_NoCrash_ButStillSacrifices()
    {
        // Target player has no cards — CR 701.7c (can't discard what you
        // don't have). No crash; sacrifice still resolves.
        var bomb = NecrogenSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var discard = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 1);

        discard.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        discard.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // {1}, sac: draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Cantrip_DrawsACard_AndSacrificesSpellbomb()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bomb = NecrogenSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Cantrip_EmptyLibrary_NoDraw_ButStillSacrifices()
    {
        var bomb = NecrogenSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        var draw = bomb.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count == 0);

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(bomb);
        bomb.Zone.Should().Be(ZoneType.Graveyard);
    }
}
