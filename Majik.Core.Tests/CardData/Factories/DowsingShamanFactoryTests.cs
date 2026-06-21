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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DowsingShamanFactory"/> (Champions of Kamigawa,
/// verified against Scryfall):
///   "{2}{G}, {T}: Return target enchantment card from your graveyard to your
///    hand."
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - The recursion ability's cost shape ({2}{G} + Tap self) + RebindSafe.
/// - The recursion ability resolving against a chosen enchantment card (returns
///   it to hand; rejects a non-enchantment; fizzles on a vanished target).
/// - The candidate gatherer scopes to the controller's own graveyard, filtered
///   to enchantment cards.
/// </summary>
public class DowsingShamanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility RecursionAbility(Creature shaman) =>
        shaman.Abilities.OfType<ActivatedAbility>().Single();

    private static void ResolveWithTarget(ActivatedAbility ability, ICard target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.ResolveAsync(agent: null, game: null).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void DowsingShaman_Identity()
    {
        var c = DowsingShamanFactory.Create(_alice);

        c.Name.Should().Be("Dowsing Shaman");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(4);
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue("Dowsing Shaman is a Centaur");
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue("Dowsing Shaman is a Shaman");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{4}{G}");
    }

    [Fact]
    public void DowsingShaman_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Dowsing Shaman", _alice);

        c.Should().BeOfType<Creature>("Dowsing Shaman is a Creature");
        c.Name.Should().Be("Dowsing Shaman");
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    [Fact]
    public void DowsingShaman_RecursionAbility_HasManaAndTapCosts_AndRebindSafe()
    {
        var shaman = DowsingShamanFactory.Create(_alice);
        var ability = RecursionAbility(shaman);

        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("G"), "{2}{G}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap, "{T}");
        ability.RebindSafe.Should().BeTrue(
            "the recursion effect reads ResolutionContext.Source's controller + its " +
            "{T} cost re-homes via AdditionalCost.RebindSource, so Agatha's Soul " +
            "Cauldron can re-home the REAL ability to a counter-bearing bearer (CR 707.2)");

        var req = ability.TargetRequests.Should().ContainSingle().Subject;
        req.Description.Should().Contain("enchantment card from your graveyard");
    }

    [Fact]
    public void DowsingShaman_Recursion_ReturnsChosenEnchantmentToHand()
    {
        var shaman = DowsingShamanFactory.Create(_alice);
        var ability = RecursionAbility(shaman);

        var aura = new Enchantment("Spirit Loop", "1W");
        aura.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(aura);
        aura.SetZone(ZoneType.Graveyard);

        ResolveWithTarget(ability, aura);

        aura.Zone.Should().Be(ZoneType.Hand, "the chosen enchantment card returns to hand (CR 701.20)");
        _alice.Zones.Hand.GetCards().Should().Contain(aura);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(aura);
    }

    [Fact]
    public void DowsingShaman_Recursion_RejectsNonEnchantmentTarget()
    {
        var shaman = DowsingShamanFactory.Create(_alice);
        var ability = RecursionAbility(shaman);

        // A non-enchantment card in the graveyard — CR 608.2b re-check fizzles.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        ResolveWithTarget(ability, bolt);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "a non-enchantment card is not a legal target and the ability fizzles (CR 608.2b)");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
    }

    [Fact]
    public void DowsingShaman_Recursion_GathererScopesToControllerGraveyardEnchantments()
    {
        var bob = new Player("Bob", 20);
        var shaman = DowsingShamanFactory.Create(_alice);
        var ability = RecursionAbility(shaman);

        var aura = new Enchantment("Spirit Loop", "1W");
        aura.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(aura);
        aura.SetZone(ZoneType.Graveyard);

        // An enchantment in BOB's graveyard — NOT a candidate ("your graveyard").
        var enemyAura = new Enchantment("Enemy Loop", "1B");
        enemyAura.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(enemyAura);
        enemyAura.SetZone(ZoneType.Graveyard);

        // A non-enchantment in Alice's graveyard — excluded by the type filter.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

        var candidates = ability.TargetRequests.Single().CandidateGatherer!(ctx);

        candidates.Should().Contain(aura, "the controller's graveyard enchantment is a candidate");
        candidates.Should().NotContain((object)enemyAura, "'your graveyard' excludes the opponent's");
        candidates.Should().NotContain((object)bolt, "a non-enchantment card is excluded");
    }

    // -----------------------------------------------------------------------
    // agatha-mother-of-runes-style-controller-scoped-candidate-gatherer-tail —
    // the recursion candidate gatherer is a re-homeable ControllerScopedGatherer
    // (not a closure capturing the authoring controller), so Agatha's Soul
    // Cauldron re-homing the REAL ability onto a counter-bearing bearer
    // (CR 707.2 / 613.1f) re-scopes "your graveyard" onto the BEARER's
    // controller. Before the migration, RebindController no-op'd on the plain
    // closure gatherer and the re-homed ability still enumerated the ORIGINAL
    // (exiled) controller's graveyard.
    // -----------------------------------------------------------------------
    [Fact]
    public void DowsingShaman_Recursion_RebindToBearer_GathererScopesToBearerControllerGraveyard()
    {
        var bob = new Player("Bob", 20);
        var shaman = DowsingShamanFactory.Create(_alice);
        var ability = RecursionAbility(shaman);

        // An enchantment in ALICE's graveyard (the originally-authored controller).
        var aliceAura = new Enchantment("Spirit Loop", "1W");
        aliceAura.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(aliceAura);
        aliceAura.SetZone(ZoneType.Graveyard);

        // An enchantment in BOB's graveyard (the bearer's controller).
        var bobAura = new Enchantment("Bob Loop", "1B");
        bobAura.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(bobAura);
        bobAura.SetZone(ZoneType.Graveyard);

        // Agatha's Soul Cauldron re-homes the ability onto a Bob-controlled bearer.
        var bearer = new Creature("Counter Bear", "{1}{G}", 2, 2);
        bearer.SetOwner(bob);
        bearer.ChangeController(bob);
        bob.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        var rehomed = ability.RebindTo(bearer, bob);

        var ctx = new Majik.Core.Game.GameContext(
            self: bob,
            allPlayers: new[] { _alice, bob },
            activePlayer: bob,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());

        var candidates = rehomed.TargetRequests.Single().CandidateGatherer!(ctx);

        candidates.Should().Contain(bobAura,
            "the re-homed gatherer scopes to the BEARER's controller's graveyard (Bob's)");
        candidates.Should().NotContain((object)aliceAura,
            "the originally-authored Alice's graveyard is no longer enumerated after re-home");
    }
}
