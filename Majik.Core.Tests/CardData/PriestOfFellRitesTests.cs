using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PriestOfFellRitesFactory"/> (current printing,
/// verified against Scryfall + the embedded seed):
///   "{T}, Pay 3 life, Sacrifice this creature: Return target creature card
///    from your graveyard to the battlefield. Activate only as a sorcery.
///    Unearth {3}{W}{B}"
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - The reanimation ability's cost shape (Tap + Pay 3 life + Sacrifice self),
///   sorcery-speed, RebindSafe.
/// - The reanimation ability resolving against a chosen creature card (returns
///   it to the battlefield; rejects an instant; fizzles on a vanished target).
/// - The reanimation ability reads "your graveyard" off the live
///   ResolutionContext.Source — closing the
///   priest-of-fell-rites-exile-from-gy-reanimate-rebind deferral (it re-homes
///   to a bearer via ActivatedAbility.RebindTo).
/// - Unearth {3}{W}{B} shape + resolution.
/// </summary>
public class PriestOfFellRitesTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility ReanimateAbility(Creature priest) =>
        priest.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice));

    private static ActivatedAbility UnearthAbility(Creature priest) =>
        priest.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

    /// <summary>Resolve an activated ability through its async path with the
    /// chosen target threaded, so ResolutionContext.Source = the ability's own
    /// source (the re-source seam the RebindSafe migration relies on).</summary>
    private static void ResolveWithTarget(ActivatedAbility ability, ICard target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.ResolveAsync(agent: null, game: null).AsTask().GetAwaiter().GetResult();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_Identity()
    {
        var c = PriestOfFellRitesFactory.Create(_alice);

        c.Name.Should().Be("Priest of Fell Rites");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Priest of Fell Rites is a Human");
        c.HasSubtype(CardSubtype.Warlock).Should().BeTrue("Priest of Fell Rites is a Warlock");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{W}{B}");
    }

    [Fact]
    public void PriestOfFellRites_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Priest of Fell Rites", _alice);

        c.Should().BeOfType<Creature>("Priest of Fell Rites is a Creature");
        c.Name.Should().Be("Priest of Fell Rites");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Reanimation ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_HasTapPayLifeAndSacrificeCosts()
    {
        var priest = PriestOfFellRitesFactory.Create(_alice);
        var ability = ReanimateAbility(priest);

        var costs = ability.Costs.OfType<AdditionalCost>().ToList();
        costs.Should().Contain(c => c.CostType == AdditionalCostType.Tap, "{T}");
        costs.Should().Contain(c => c.CostType == AdditionalCostType.PayLife, "Pay 3 life");
        costs.Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice, "Sacrifice this creature");
    }

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_IsSorcerySpeedAndRebindSafe()
    {
        var priest = PriestOfFellRitesFactory.Create(_alice);
        var ability = ReanimateAbility(priest);

        ability.IsSorcerySpeed.Should().BeTrue("'Activate only as a sorcery.'");
        ability.RebindSafe.Should().BeTrue(
            "the reanimation effect reads ResolutionContext.Source + its costs " +
            "re-home via AdditionalCost.RebindSource, so Agatha's Soul Cauldron " +
            "can re-home the REAL ability to a counter-bearing bearer (CR 707.2)");
    }

    // -----------------------------------------------------------------------
    // Reanimation ability — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_ReturnsChosenCreatureCardToBattlefield()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        ResolveWithTarget(ability, bear);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "the chosen creature card was reanimated to the controller's battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_NoTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        // No chosen target → resolves as a no-op (CR 608.2b).
        var act = () => ability.ResolveAsync(agent: null, game: null)
            .AsTask().GetAwaiter().GetResult();

        act.Should().NotThrow();
    }

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_RejectsNonCreatureTarget()
    {
        var alice = new Player("Alice", 20);

        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        ResolveWithTarget(ability, bolt);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "an instant is not a creature card — it stays in the graveyard (CR 608.2b)");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
    }

    // -----------------------------------------------------------------------
    // RE-SOURCE-SAFE — Agatha's Soul Cauldron re-home (deferral close).
    // The reanimate ability re-homed to a BEARER (via RebindTo) reanimates from
    // the BEARER's controller's graveyard, reading ResolutionContext.Source.
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_RebindsToBearer_ReadsBearersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // The Priest is exiled (imprinted under Agatha). Build the ability, then
        // re-home it to a bearer Bob controls — the grant mechanism's RebindTo.
        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        var bearer = new Creature("Bearer Beast", "2G", 3, 3);
        bearer.SetOwner(bob);
        bearer.SetController(bob);
        bearer.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bearer);

        var rebound = ability.RebindTo(bearer, bob);
        rebound.Source.Should().BeSameAs(bearer, "re-homed to the BEARER (CR 707.2)");
        rebound.RebindSafe.Should().BeTrue("RebindTo preserves the re-source provenance");

        // A creature card sits in BOB's graveyard (the bearer's controller).
        var zombie = new Creature("Walking Corpse", "1B", 2, 2);
        zombie.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(zombie);
        zombie.SetZone(ZoneType.Graveyard);

        // A decoy in ALICE's graveyard (the exiled Priest's controller) must NOT
        // be the pool the re-homed ability reanimates from.
        var decoy = new Creature("Decoy Ogre", "2R", 3, 3);
        decoy.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(decoy);
        decoy.SetZone(ZoneType.Graveyard);

        ResolveWithTarget(rebound, zombie);

        zombie.Zone.Should().Be(ZoneType.Battlefield,
            "the re-homed ability reanimates from the BEARER's controller's graveyard");
        bob.Zones.Battlefield.GetCards().Should().Contain(zombie);
        zombie.Controller.Should().BeSameAs(bob,
            "the reanimated permanent enters under the BEARER's controller (CR 110.2)");
        decoy.Zone.Should().Be(ZoneType.Graveyard,
            "the exiled Priest's controller's graveyard is untouched");
    }

    // agatha-mother-of-runes-style-controller-scoped-candidate-gatherer-tail —
    // the re-homed candidate gatherer (a ControllerScopedGatherer) enumerates
    // the BEARER's controller's graveyard creature cards, not the exiled
    // Priest's authoring controller's. Before the migration the plain closure
    // gatherer captured the authoring controller and RebindController no-op'd,
    // so a re-homed ability offered the WRONG player's graveyard as candidates.
    [Fact]
    public void PriestOfFellRites_ReanimateAbility_RebindToBearer_GathererScopesToBearerControllerGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        // A creature card in BOB's graveyard (the bearer's controller).
        var bobZombie = new Creature("Walking Corpse", "1B", 2, 2);
        bobZombie.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobZombie);
        bobZombie.SetZone(ZoneType.Graveyard);

        // A decoy in ALICE's graveyard (the exiled Priest's authoring controller).
        var aliceDecoy = new Creature("Decoy Ogre", "2R", 3, 3);
        aliceDecoy.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(aliceDecoy);
        aliceDecoy.SetZone(ZoneType.Graveyard);

        var bearer = new Creature("Bearer Beast", "2G", 3, 3);
        bearer.SetOwner(bob);
        bearer.SetController(bob);
        bearer.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bearer);

        var rebound = ability.RebindTo(bearer, bob);

        var ctx = new Majik.Core.Game.GameContext(
            self: bob,
            allPlayers: new[] { alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

        var candidates = rebound.TargetRequests.Single().CandidateGatherer!(ctx);

        candidates.Should().Contain(bobZombie,
            "the re-homed gatherer scopes to the BEARER's controller's graveyard (Bob's)");
        candidates.Should().NotContain((object)aliceDecoy,
            "the exiled Priest's authoring controller's graveyard is no longer enumerated");
    }

    [Fact]
    public void PriestOfFellRites_ReanimateAbility_RebindToBearer_RehomesTapAndSacrificeCosts()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var ability = ReanimateAbility(priest);

        var bearer = new Creature("Bearer Beast", "2G", 3, 3);
        bearer.SetOwner(bob);
        bearer.SetController(bob);

        var rebound = ability.RebindTo(bearer, bob);

        // STAGE 1 — the Tap + Sacrifice AdditionalCosts now capture the BEARER.
        var tap = rebound.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Tap);
        var sac = rebound.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Sacrifice);

        tap.Description.Should().Contain(bearer.Name,
            "RebindTo re-homes the {T} cost to the bearer (AdditionalCost.RebindSource)");
        sac.Description.Should().Contain(bearer.Name,
            "RebindTo re-homes the sacrifice cost to the bearer");
    }

    // -----------------------------------------------------------------------
    // Unearth {3}{W}{B} — shape + resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_Unearth_HasManaCostAndSorcerySpeed()
    {
        var priest = PriestOfFellRitesFactory.Create(_alice);
        var unearth = UnearthAbility(priest);

        unearth.Costs.OfType<ManaCostCost>().Should().ContainSingle("Unearth {3}{W}{B}");
        unearth.IsSorcerySpeed.Should().BeTrue("'Unearth only as a sorcery.' (CR 702.84a)");
    }

    [Fact]
    public void PriestOfFellRites_Unearth_ReturnsSelfFromGraveyardWithHaste()
    {
        var alice = new Player("Alice", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        alice.Zones.Graveyard.AddCard(priest);
        priest.SetZone(ZoneType.Graveyard);

        var unearth = UnearthAbility(priest);
        foreach (var effect in unearth.Effects) effect.Execute();

        priest.Zone.Should().Be(ZoneType.Battlefield,
            "Unearth returns the card from the graveyard to the battlefield (CR 702.84a)");
        alice.Zones.Battlefield.GetCards().Should().Contain(priest);
        priest.HasSummoningSickness.Should().BeFalse("Unearth grants Haste (CR 702.10)");
    }

    [Fact]
    public void PriestOfFellRites_Unearth_NotInGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(priest);
        priest.SetZone(ZoneType.Battlefield);

        var unearth = UnearthAbility(priest);
        var act = () => { foreach (var effect in unearth.Effects) effect.Execute(); };

        act.Should().NotThrow("Unearth no-ops when the card is not in the graveyard");
        priest.Zone.Should().Be(ZoneType.Battlefield);
    }
}
