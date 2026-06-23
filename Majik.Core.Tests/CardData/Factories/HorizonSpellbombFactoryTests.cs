using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for <see cref="HorizonSpellbombFactory"/> — Artifact {1} (Mirrodin /
/// reprints). Oracle (Scryfall, verified):
///   "{2}, {T}, Sacrifice this artifact: Search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle.
///    When this artifact is put into a graveyard from the battlefield, you may
///    pay {G}. If you do, draw a card."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - Ability shape: one ActivatedAbility ({2}+tap+sac, no targets) + one
///     dies TriggeredAbility.
///   - Activated ability resolution: tutors ONE basic land into hand and
///     sacrifices the spellbomb.
///   - Dies trigger: draws a card when {G} is available; no draw otherwise.
/// </summary>
[Trait("Color", "C")]
public class HorizonSpellbombFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HorizonSpellbomb_HasOneActivatedAbility_AndOneDiesTrigger()
    {
        var spellbomb = HorizonSpellbombFactory.Create(_alice);

        spellbomb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        spellbomb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_HasManaTapAndSacrificeCosts_AndNoTargets()
    {
        var spellbomb = HorizonSpellbombFactory.Create(_alice);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>()
            .Should().Contain(c => c.Cost.Generic == 2,
                "the tutor mode costs {2}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the tutor mode costs {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the tutor mode sacrifices the spellbomb");

        // Searching your own library has no targets.
        ability.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution — tutor one basic to hand + sacrifice
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_TutorsOneBasicIntoHand_AndSacrificesSpellbomb()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // A second basic so we exercise "search for A basic" (singular) — only
        // ONE should move to hand.
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var spellbomb = HorizonSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        // Exactly one basic moved to hand.
        var hand = _alice.Zones.Hand.GetCards().ToList();
        hand.Count.Should().Be(startHand + 1,
            "Horizon Spellbomb searches for A (one) basic land and puts it into hand");
        hand.OfType<Land>().Single().Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().Contain(c => c is Land,
            "only one of the two basics is taken");

        // Spellbomb has been sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(spellbomb);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(spellbomb);
        spellbomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void ActivatedAbility_NoBasicsInLibrary_MovesNoCard_ButStillSacrifices()
    {
        var bog = new Land("Bojuka Bog"); // nonbasic
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var startHand = _alice.Zones.Hand.GetCards().Count();

        var spellbomb = HorizonSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        var ability = spellbomb.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand,
            "no basic land in library → nothing put into hand");
        _alice.Zones.Library.GetCards().Should().Contain(bog);

        // Sacrifice still occurs.
        _alice.Zones.Graveyard.GetCards().Should().Contain(spellbomb);
        spellbomb.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — may pay {G} to draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_DrawsACard_WhenGreenManaIsAvailable()
    {
        var spellbomb = HorizonSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        // Give Alice {G} in her mana pool.
        _alice.AddManaToPool(ManaCost.Parse("{G}"));

        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        // Simulate Horizon Spellbomb dying (Battlefield → Graveyard).
        _alice.Zones.Battlefield.RemoveCard(spellbomb);
        _alice.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);

        var diesTrigger = spellbomb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the dies trigger draws a card when {G} is paid");
        topCard.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void DiesTrigger_DoesNotDraw_WhenNoGreenManaAvailable()
    {
        var spellbomb = HorizonSpellbombFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Battlefield);

        // No mana added — pool is empty.
        var topCard = new Card("Top of Library", "");
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        _alice.Zones.Battlefield.RemoveCard(spellbomb);
        _alice.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);

        var diesTrigger = spellbomb.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(topCard,
            "the dies trigger does not draw without {G} in the mana pool");
        _alice.Zones.Library.GetCards().Should().Contain(topCard);
    }
}
