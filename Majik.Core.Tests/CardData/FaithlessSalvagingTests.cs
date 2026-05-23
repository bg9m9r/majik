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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FaithlessSalvagingFactory"/>.
///
/// Card: Faithless Salvaging — Sorcery {1}{R} (Phyrexia: All Will Be One).
///   "Discard a card, then draw a card.
///    Flashback—Discard a creature card."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Resolve: discard 1 + draw 1 (in that order); net hand size unchanged
///     when the hand had ≥1 starting card and a library card is available.
///   - Flashback cast: same resolve effect; flashback's
///     <see cref="DiscardACreatureCardAdditionalCost"/> rider only pays when
///     a creature card is in hand; post-resolve exiles the spell card
///     (CR 702.34b).
///   - Empty library: discard still happens, draw flags the SBA loss.
/// </summary>
public class FaithlessSalvagingTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FaithlessSalvaging_Identity()
    {
        var c = FaithlessSalvagingFactory.Create(_alice);

        c.Name.Should().Be("Faithless Salvaging");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FaithlessSalvaging()
    {
        var card = NamedCardFactory.Create("Faithless Salvaging", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Faithless Salvaging");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlashbackCost_IsZeroManaPlusDiscardCreatureRider()
    {
        // Flashback cost is non-mana ("Discard a creature card"), so the
        // mana portion is zero and the discard ships as an additional cost
        // rider (mirrors Cabal Therapy's Sacrifice-a-creature flashback).
        var fb = FaithlessSalvagingFactory.BuildFlashbackCost();
        fb.AlternativeManaCost.Should().Be(ManaCost.Zero);
        fb.Description.Should().Contain("Flashback");

        var additional = FaithlessSalvagingFactory.BuildFlashbackAdditionalCosts();
        additional.Should().ContainSingle()
            .Which.Should().BeOfType<DiscardACreatureCardAdditionalCost>();
        additional[0].Description.Should().Be("discard a creature card");
    }

    // -----------------------------------------------------------------------
    // Resolve: discard 1, then draw 1.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DiscardsOne_ThenDrawsOne_NetHandSizeUnchanged()
    {
        // Starting hand: 1 card. Library: 1 card. Discard removes the
        // existing hand card; draw refills with the library card. Net hand
        // size: 1.
        var inHand = SeedHandCard(_alice, "InHand");
        var top = SeedLibraryCard(_alice, "Top");

        var effects = FaithlessSalvagingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        // The starting hand card was discarded; the library card replaced it.
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(inHand);
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        inHand.Zone.Should().Be(ZoneType.Graveyard);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_FromEmptyHand_DiscardNoOp_StillDrawsOne()
    {
        // No starting hand → discard is a clean no-op (CR 701.16a "up to 1"
        // semantics). Draw still fires.
        var top = SeedLibraryCard(_alice, "Top");

        var effects = FaithlessSalvagingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EmptyLibrary_DiscardsThenFlagsSbaLoss()
    {
        // Hand has one card; library empty. Discard fires (card → graveyard);
        // draw underflows and flags MarkTriedToDrawFromEmptyLibrary
        // (CR 704.5b / CR 120.3).
        var inHand = SeedHandCard(_alice, "InHand");

        var effects = FaithlessSalvagingFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(inHand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the draw hit an empty library — SBA flag must be set");
    }

    // -----------------------------------------------------------------------
    // Flashback rider — "Discard a creature card."
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackRider_CannotPay_WhenNoCreatureCardInHand()
    {
        // Hand has a non-creature card only — the discard-a-creature-card
        // rider cannot be paid (CR 117.1).
        var nonCreature = new Card("Non-Creature", "");
        nonCreature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(nonCreature);
        nonCreature.SetZone(ZoneType.Hand);

        var rider = (DiscardACreatureCardAdditionalCost)
            FaithlessSalvagingFactory.BuildFlashbackAdditionalCosts()[0];

        rider.CanPay(_alice).Should().BeFalse();
        rider.Pay(_alice).Should().BeFalse();
        rider.Discarded.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().Contain(nonCreature);
    }

    [Fact]
    public void FlashbackRider_Pays_ByDiscardingFirstCreatureCardInHand()
    {
        // Mixed hand: a non-creature + a creature. The rider picks the
        // first creature card in hand (deterministic v1 policy) and moves
        // it to the graveyard.
        var nonCreature = new Card("Non-Creature", "");
        nonCreature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(nonCreature);
        nonCreature.SetZone(ZoneType.Hand);

        var creature = new Creature("Goblin Guide", "{R}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);

        var rider = (DiscardACreatureCardAdditionalCost)
            FaithlessSalvagingFactory.BuildFlashbackAdditionalCosts()[0];

        rider.CanPay(_alice).Should().BeTrue();
        rider.Pay(_alice).Should().BeTrue();
        rider.Discarded.Should().BeSameAs(creature);
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(creature);
        _alice.Zones.Hand.GetCards().Should().Contain(nonCreature);
    }

    [Fact]
    public void FlashbackCast_FromGraveyard_AppliesResolveEffect_ThenExiles()
    {
        // Faithless Salvaging in Alice's graveyard. Library has one card
        // (to be drawn). Hand has a creature card to feed the flashback
        // rider + a non-creature to feed the resolve-time discard.
        var fs = FaithlessSalvagingFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(fs);
        fs.SetZone(ZoneType.Graveyard);

        var resolveDiscard = new Card("InHandResolve", "");
        resolveDiscard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(resolveDiscard);
        resolveDiscard.SetZone(ZoneType.Hand);

        var creature = new Creature("Memnite", "{0}", 1, 1);
        creature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);

        var top = SeedLibraryCard(_alice, "Top");

        // Sanity: flashback alt-cost legal here.
        var fb = FaithlessSalvagingFactory.BuildFlashbackCost();
        fb.CanCastFor(fs, _alice).Should().BeTrue();
        fb.AlternativeManaCost.Should().Be(ManaCost.Zero);

        // Pay the additional discard-a-creature-card rider first (this is
        // what SpellCastFlow does at announcement time, before resolution).
        var rider = (DiscardACreatureCardAdditionalCost)
            FaithlessSalvagingFactory.BuildFlashbackAdditionalCosts()[0];
        rider.Pay(_alice).Should().BeTrue();
        rider.Discarded.Should().BeSameAs(creature);

        // Run the printed resolve effect.
        foreach (var e in FaithlessSalvagingFactory.BuildResolveEffect(_alice)) e.Execute();

        // Then flashback's post-resolve hook fires — card exiles from
        // graveyard (CR 702.34b).
        fb.OnResolved(fs, _alice);

        // Faithless Salvaging is in exile.
        fs.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(fs);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(fs);

        // Rider discarded the creature into graveyard.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(creature);

        // Resolve-time discard sent the non-creature hand card to the
        // graveyard; the library card was drawn into hand.
        resolveDiscard.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(resolveDiscard);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.34 — flashback is only castable from graveyard.
        var fs = FaithlessSalvagingFactory.Create(_alice);
        fs.SetZone(ZoneType.Hand);

        var fb = FaithlessSalvagingFactory.BuildFlashbackCost();
        fb.CanCastFor(fs, _alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }
}
