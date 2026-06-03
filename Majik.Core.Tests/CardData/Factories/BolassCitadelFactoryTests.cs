using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Bolas's Citadel (War of the Spark, {3}{B}{B}{B}) — the
/// cast-from-top + pay-life-equal-to-mana-value alt cost (CR 118.9) and the
/// {T}, Sacrifice ten nonland permanents drain.
/// </summary>
public class BolassCitadelFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, ContinuousEffectsService effects) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService(bus);
        return (zones, effects);
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    private T PutOnTop<T>(T card) where T : ICard
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    [Fact]
    public void Identity_LegendaryArtifact_At3BBB()
    {
        var citadel = BolassCitadelFactory.Create(_alice);

        citadel.Name.Should().Be("Bolas's Citadel");
        citadel.ManaCost.Should().Be("{3}{B}{B}{B}");
        citadel.HasType(CardType.Artifact).Should().BeTrue();
        citadel.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BolassCitadel()
    {
        var card = NamedCardFactory.Create("Bolas's Citadel", _alice);
        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Bolas's Citadel");
    }

    [Fact]
    public void SacrificeAbility_TapAndSacTen()
    {
        var citadel = BolassCitadelFactory.Create(_alice);
        var ability = citadel.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<SacrificeNNonlandPermanentsCost>().Should()
            .ContainSingle(c => c.Count == BolassCitadelFactory.SacrificeCount);
    }

    [Fact]
    public void OnBattlefield_TopNonland_IsCastable_WithMandatoryPayLifeAltCost()
    {
        var (zones, effects) = BuildEngine();
        var citadel = BolassCitadelFactory.Create(_alice, effects);
        var creature = PutOnTop(new Creature("Goblin Bear", "{2}{R}", 2, 2));

        // Not yet on the battlefield — no grant.
        LibraryTopPlayPermissions.MayCastTopCard(_alice, creature).Should().BeFalse();
        LibraryTopPlayPermissions.MandatoryTopCastAltCostFor(_alice, creature).Should().BeNull();

        EnterBattlefield(zones, _alice, citadel);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, creature).Should().BeTrue(
            "Bolas's Citadel's Any grant authorizes casting the top card");
        var alt = LibraryTopPlayPermissions.MandatoryTopCastAltCostFor(_alice, creature);
        alt.Should().BeOfType<PayLifeEqualToManaValueAlternativeCost>(
            "a top-cast under the grant must pay life equal to mana value");
        PayLifeEqualToManaValueAlternativeCost.LifeAmountFor(creature).Should().Be(3);
    }

    [Fact]
    public void OnBattlefield_TopLand_IsPlayable_NoAltCost()
    {
        var (zones, effects) = BuildEngine();
        var citadel = BolassCitadelFactory.Create(_alice, effects);
        var land = PutOnTop(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));

        EnterBattlefield(zones, _alice, citadel);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue(
            "the Any grant covers the land-play half too");
        // A land is played, not cast — no pay-life alt cost applies.
        LibraryTopPlayPermissions.MandatoryTopCastAltCostFor(_alice, land).Should().BeNull();
    }

    [Fact]
    public void LeavesBattlefield_GrantRevoked()
    {
        var (zones, effects) = BuildEngine();
        var citadel = BolassCitadelFactory.Create(_alice, effects);
        var creature = PutOnTop(new Creature("Goblin Bear", "{2}{R}", 2, 2));

        EnterBattlefield(zones, _alice, citadel);
        LibraryTopPlayPermissions.MayCastTopCard(_alice, creature).Should().BeTrue();

        zones.MoveCardTo(citadel, ZoneType.Graveyard, controller: _alice);
        LibraryTopPlayPermissions.MayCastTopCard(_alice, creature).Should().BeFalse(
            "CR 603.6e — the static functions only while its source is on the battlefield");
    }

    [Fact]
    public void DrainAbility_ResolverProvided_EachOpponentLoses10()
    {
        var citadel = BolassCitadelFactory.Create(
            _alice, continuousEffects: null,
            opponentResolver: () => new[] { _bob });
        var ability = citadel.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var e in ability.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(10);
        _alice.LifeTotal.Should().Be(20, "the controller is not an opponent");
    }
}
