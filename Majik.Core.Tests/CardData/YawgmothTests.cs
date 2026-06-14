using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="YawgmothFactory"/>.
///
/// Card: Yawgmoth, Thran Physician — Legendary Creature — Human Cleric,
/// {2}{B}{B} (2/4). Current Scryfall oracle (verified against the seed):
///   "Protection from Humans
///    Pay 1 life, Sacrifice another creature: Put a -1/-1 counter on up to one
///    target creature and draw a card.
///    {B}{B}, Discard a card: Proliferate."
///
/// The earlier "Each other player loses 1 life and discards a card" rider is no
/// longer printed (and was inert on the routed prod build because it captured a
/// null opponents resolver) — the factory now models the current oracle: an
/// optional "-1/-1 counter on up to one target creature" (CR 115.1b — a
/// MinTargets 0 / MaxTargets 1 <see cref="Majik.Core.Players.Agents.TargetRequest"/>;
/// the controller may decline) followed by "draw a card". These unit tests
/// resolve the ability with no chosen target (the counter half cleanly no-ops),
/// so they exercise the draw; the live optional-target prompt + counter
/// placement is covered by YawgmothMinusCounterLivePlayTests.
/// </summary>
public class YawgmothTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_IsLegendaryCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.HasType(CardType.Creature).Should().BeTrue();
        yawg.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_HasHumanClericSubtypes()
    {
        var yawg = YawgmothFactory.Create(_alice);

        // CR 205.3 — printed "Human Cleric" only; the card is NOT Phyrexian
        // (the seed type line carries no Phyrexian subtype).
        yawg.HasSubtype(CardSubtype.Phyrexian).Should().BeFalse();
        yawg.HasSubtype(CardSubtype.Human).Should().BeTrue();
        yawg.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_IsTwoFour()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.BasePower.Should().Be(2);
        yawg.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void Yawgmoth_OwnerAndControllerAreSet()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.Owner.Should().BeSameAs(_alice);
        yawg.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability count / presence
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_HasExactlyOneActivatedAbility()
    {
        var yawg = YawgmothFactory.Create(_alice);

        yawg.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Costs
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_AbilityCosts_IncludePayOneLife()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<AdditionalCost>()
            .Should().Contain(
                ac => ac.Description.Contains("1 life"),
                "the cost must include 'Pay 1 life'");
    }

    [Fact]
    public void Yawgmoth_AbilityCosts_IncludeSacrificeAnotherCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<SacrificeAnotherCreatureCost>()
            .Should().HaveCount(1, "the cost must include a SacrificeAnotherCreatureCost");
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_CannotPayWhenNoOtherCreature()
    {
        var yawg = YawgmothFactory.Create(_alice);
        // Yawgmoth is NOT yet on battlefield; alice has no creatures.
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeFalse("no other creature on the battlefield");
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_CanPayWhenOtherCreaturePresent()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void Yawgmoth_SacrificeAnotherCreatureCost_PayMovesCreatureToGraveyard()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var fodder = new Creature("Grizzly Bears", "1G", 2, 2);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ab.Costs.OfType<SacrificeAnotherCreatureCost>().Single();
        cost.Pay(_alice);

        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    // -----------------------------------------------------------------------
    // Effects — current oracle (optional -1/-1 target declined here; draw a card)
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_Effect_ControllerDrawsACard()
    {
        // Seed Alice's library with one card.
        var topCard = new Card("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        topCard.SetController(_alice);
        _alice.Zones.Library.AddCard(topCard);

        var yawg = YawgmothFactory.Create(_alice);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "the drawn card should be in Alice's hand");
        _alice.Zones.Library.GetCards().Should().NotContain(topCard, "drawn card should no longer be in library");
    }

    [Fact]
    public void Yawgmoth_Effect_DrawMarksEmptyLibrary()
    {
        // Alice's library is empty — draw should flag TriedToDrawFromEmptyLibrary.
        var yawg = YawgmothFactory.Create(_alice);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 120.3: attempting to draw from an empty library sets the flag");
    }

    [Fact]
    public void Yawgmoth_Effect_DoesNotDrainOpponents_CurrentOracleHasNoEachOpponentClause()
    {
        // The earlier "each other player loses 1 life and discards a card"
        // rider is no longer printed — resolving the ability must NOT touch
        // an opponent's life or hand.
        var bob = new Player("Bob", 20);
        var bobCard = new Card("Lightning Bolt", "{R}");
        bobCard.SetOwner(bob);
        bobCard.SetController(bob);
        bob.Zones.Hand.AddCard(bobCard);

        var yawg = YawgmothFactory.Create(_alice);

        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        bob.LifeTotal.Should().Be(20, "the current oracle has no each-opponent life loss");
        bob.Zones.Hand.GetCards().Should().Contain(bobCard, "the current oracle has no each-opponent discard");
    }

    // -----------------------------------------------------------------------
    // RE-SOURCE-SAFE (agatha-oracle-shape-yawgmoth-pay-life-counter-pump-loop):
    // the activated ability reads its source/controller off the live
    // ResolutionContext, so it is marked RebindSafe and Agatha's Soul Cauldron
    // can re-home the REAL ability (including its multi-leg pay-life +
    // sacrifice-another cost) to a bearer.
    // -----------------------------------------------------------------------

    [Fact]
    public void Yawgmoth_Ability_IsRebindSafe()
    {
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        ab.RebindSafe.Should().BeTrue(
            "every effect reads ResolutionContext.Source / Controller, so the "
            + "ability is sound to re-home via RebindTo (CR 707.2)");
    }

    [Fact]
    public void Yawgmoth_RebindTo_RehomesSacrificeAnotherCost_ExcludesNewSource()
    {
        // The multi-leg cost wrinkle: SacrificeAnotherCreatureCost captures the
        // ability's source ("another creature" = a creature OTHER than the
        // source). After RebindTo(newSource), the rebound cost must exclude the
        // NEW source, not the original Yawgmoth.
        var bob = new Player("Bob", 20);
        var yawg = YawgmothFactory.Create(_alice);
        var ab = yawg.Abilities.OfType<ActivatedAbility>().Single();

        var bearer = new Creature("Counter Bear", "1G", 2, 2);
        bearer.SetOwner(bob);
        bearer.SetController(bob);
        bob.Zones.Battlefield.AddCard(bearer);

        var rebound = ab.RebindTo(bearer, bob);

        var reboundSac = rebound.Costs.OfType<SacrificeAnotherCreatureCost>().Single();

        // The bearer (the new source) is the ONLY creature on Bob's battlefield;
        // "sacrifice another creature" must therefore find NOTHING — the rebound
        // cost excludes the bearer, proving its captured source was re-homed.
        reboundSac.EligibleSacrifices(bob).Should().BeEmpty(
            "the re-homed sacrifice-another cost excludes the NEW source (bearer)");
        reboundSac.CanPay(bob).Should().BeFalse(
            "with only the bearer on the battlefield there is no OTHER creature to sacrifice");

        // The pay-1-life leg passes through unchanged (it captures no permanent).
        rebound.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.Description.Contains("1 life"),
                "the pay-life leg survives the rebind");

        // Provenance preserved.
        rebound.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");
        rebound.Source.Should().BeSameAs(bearer);
    }

    // -----------------------------------------------------------------------
    // PROD-PATH: GameFacade routed build wires the activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectsAwareDispatch_BuildsYawgmothThroughNamedFactory_OnProdPath()
    {
        // Prod dispatch: GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner, effects).
        var effects = new Majik.Core.Effects.ContinuousEffectsService();
        var built = NamedCardFactory.Create("Yawgmoth, Thran Physician", _alice, effects);

        built.Should().BeOfType<Creature>();
        built.Name.Should().Be("Yawgmoth, Thran Physician");
        built.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the prod effects-aware dispatch must route through the "
            + "Create(Player, ContinuousEffectsService) overload");

        // The draw effect runs on the prod-built card.
        var top = new Card("Swamp", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var ab = ((Creature)built).Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the prod-built ability draws a card");
    }
}
