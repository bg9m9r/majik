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
/// Tests for <see cref="NihilSpellbombFactory"/> — Artifact {B} with one
/// activated ability and a dies trigger:
///   "{T}, Sacrifice Nihil Spellbomb: Exile all cards from target player's graveyard."
///   "When Nihil Spellbomb is put into a graveyard from the battlefield, you
///    may pay {B}. If you do, draw a card."
///
/// Covers:
/// - Card identity (Artifact, {B}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one ActivatedAbility + one TriggeredAbility.
/// - Activated ability: exiles all cards from target player's graveyard and
///   sacrifices the spellbomb.
/// - Dies trigger: draws a card when {B} is available in mana pool.
/// - Dies trigger: does NOT draw when no {B} is available.
/// </summary>
public class NihilSpellbombTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NihilSpellbomb_IsArtifact_WithBlackManaCost()
    {
        var spellbomb = NihilSpellbombFactory.Create(_alice);

        spellbomb.HasType(CardType.Artifact).Should().BeTrue();
        spellbomb.Name.Should().Be("Nihil Spellbomb");
        spellbomb.ManaCost.Should().Be("{B}");
        spellbomb.Owner.Should().BeSameAs(_alice);
        spellbomb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NihilSpellbomb()
    {
        var card = NamedCardFactory.Create("Nihil Spellbomb", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Nihil Spellbomb");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void NihilSpellbomb_HasOneActivatedAbility_AndOneDiesTrigger()
    {
        var spellbomb = NihilSpellbombFactory.Create(_alice);

        spellbomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        spellbomb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_HasTapAndSacrificeCosts_AndOnePlayerTarget()
    {
        var spellbomb = NihilSpellbombFactory.Create(_alice);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the exile mode costs {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the exile mode sacrifices the spellbomb");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("player");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_ExilesAllCards_FromTargetPlayerGraveyard_AndSacrificesSpellbomb()
    {
        // Bob has two cards in graveyard. Both should be exiled.
        var card1 = new Card("Spell 1", "{1}");
        var card2 = new Card("Spell 2", "{2}");
        _bob.Zones.Graveyard.AddCard(card1);
        card1.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(card2);
        card2.SetZone(ZoneType.Graveyard);

        var spellbomb = NihilSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        // Both of Bob's graveyard cards are now in Exile.
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "the entire target player graveyard is exiled");
        _bob.Zones.Exile.GetCards().Should().Contain(card1);
        _bob.Zones.Exile.GetCards().Should().Contain(card2);
        card1.Zone.Should().Be(ZoneType.Exile);
        card2.Zone.Should().Be(ZoneType.Exile);

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(spellbomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(spellbomb);
        spellbomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ActivatedAbility_EmptyGraveyard_StillSacrificesSpellbomb()
    {
        // Bob's graveyard is empty — exile is a no-op but sacrifice still occurs.
        var spellbomb = NihilSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        ability.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();

        // Spellbomb still sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(spellbomb);
        spellbomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — draws when {B} is available
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_DrawsACard_WhenBlackManaIsAvailable()
    {
        // Place spellbomb on the battlefield, then move it to the graveyard
        // to simulate "dies" (CR 700.4). The dies trigger auto-pays {B}.
        var spellbomb = NihilSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        // Give Alice {B} in her mana pool.
        _alice.AddManaToPool(ManaCost.Parse("{B}"));

        // Put a card on top of Alice's library.
        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        // Simulate Nihil Spellbomb dying (Battlefield → Graveyard).
        _alice.Zones.Battlefield.RemoveCard(spellbomb);
        _alice.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);

        var diesTrigger = spellbomb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        // {B} was paid, card was drawn.
        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the dies trigger draws a card when {B} is paid");
        topCard.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void DiesTrigger_DoesNotDraw_WhenNoBlackManaAvailable()
    {
        // Spellbomb dies but Alice has no {B} — no draw.
        var spellbomb = NihilSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        // No mana added — pool is empty.
        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        // Simulate dying.
        _alice.Zones.Battlefield.RemoveCard(spellbomb);
        _alice.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);

        var diesTrigger = spellbomb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        // No draw — mana was insufficient.
        _alice.Zones.Hand.GetCards().Should().NotContain(topCard,
            "the dies trigger does not draw without {B} in the mana pool");
        _alice.Zones.Library.GetCards().Should().Contain(topCard);
    }
}
