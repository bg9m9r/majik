using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpymastersVaultFactory"/>.
///
/// Oracle (Scryfall-confirmed, Modern Horizons 3):
///   "This land enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {B}, {T}: Target creature you control connives X, where X is the
///    number of creatures that died this turn. (Draw X cards, then discard
///    X cards. Put a +1/+1 counter on that creature for each nonland card
///    discarded this way.)"
///
/// Covers:
/// - Card identity (name, Land type, non-legendary, not a Swamp)
/// - Owner and controller assignment
/// - Single {B} mana ability ({T}: Add {B})
/// - The {B},{T} connive activated ability: cost shape, target gathering,
///   and resolution (connive X = creatures died this turn).
/// </summary>
public class SpymastersVaultTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature PlaceCreature(Player controller, string name = "Bear")
    {
        var c = new Creature(name, "{1}{G}", power: 2, toughness: 2)
            { Owner = controller, Controller = controller };
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void AddCardToLibrary(Player player, bool land = false)
    {
        Card card = land
            ? new Land("Forest", subtypes: new[] { CardSubtype.Forest })
            : new Creature("Spell", "{1}", power: 1, toughness: 1);
        card.Owner = player;
        card.Controller = player;
        card.SetZone(ZoneType.Library);
        player.Zones.Library.AddCard(card);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SpymastersVault_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SpymastersVault_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Name.Should().Be("Spymaster's Vault");
    }

    [Fact]
    public void SpymastersVault_IsNotLegendary_NotSwamp()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        // No Swamp subtype — Spymaster's Vault cannot satisfy its own
        // "enters tapped unless you control a Swamp" predicate.
        land.HasSubtype(CardSubtype.Swamp).Should().BeFalse();
    }

    [Fact]
    public void SpymastersVault_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void SpymastersVault_HasExactlyOneManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "only {T}: Add {B} is the mana ability");
    }

    [Fact]
    public void SpymastersVault_ManaAbility_ProducesBlack()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Black.Should().Be(1, "Spymaster's Vault taps for exactly one {B}");
    }

    [Fact]
    public void SpymastersVault_ManaAbility_ProducesOnlyBlack()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Generic.Should().Be(0);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Connive activated ability — {B}, {T}
    // -----------------------------------------------------------------------

    [Fact]
    public void SpymastersVault_HasOneActivatedConniveAbility()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {B},{T} connive ability is the one activated (non-mana) ability");
    }

    [Fact]
    public void ConniveAbility_HasManaAndTapCosts()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "costs are {B} mana + tap");
        ability.Costs.Should().ContainItemsAssignableTo<ManaCostCost>(
            "one cost is the {B} mana cost");
        ability.Costs.Should().Contain(c => c is AdditionalCost,
            "one cost is the {T} tap cost");
    }

    [Fact]
    public void ConniveAbility_TargetsExactlyOneCreatureYouControl()
    {
        var bob = new Player("Bob", 20);
        var land = (Land)SpymastersVaultFactory.Create(_alice);
        land.SetController(_alice);

        var myCreature = PlaceCreature(_alice);
        var oppCreature = PlaceCreature(bob);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);

        req.CandidateGatherer.Should().NotBeNull();
        var candidates = req.CandidateGatherer!(BuildContext(_alice, bob));
        candidates.Should().Contain(myCreature, "Alice's creature is a legal target");
        candidates.Should().NotContain(oppCreature,
            "'creature you control' excludes the opponent's creatures");
    }

    [Fact]
    public void Connive_NoCreaturesDied_IsNoOp()
    {
        var turnState = new TurnState();
        var land = (Land)SpymastersVaultFactory.Create(_alice, turnState);
        land.SetController(_alice);
        var target = PlaceCreature(_alice);
        AddCardToLibrary(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.Effects.Single().Execute();

        // X = 0 → no draw, no counter.
        _alice.Zones.Hand.GetCards().Should().BeEmpty("X = 0 connives nothing");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    [Fact]
    public void Connive_OneCreatureDied_DrawsThenDiscards_NonlandAddsCounter()
    {
        var turnState = new TurnState();
        turnState.RecordCreatureDied(_alice); // 1 creature died this turn.

        var land = (Land)SpymastersVaultFactory.Create(_alice, turnState);
        land.SetController(_alice);
        var target = PlaceCreature(_alice);
        // Library top is a NONLAND so the connive discard is a nonland →
        // +1/+1 counter (CR 701.50).
        AddCardToLibrary(_alice, land: false);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.Effects.Single().Execute();

        // Connive 1: drew the nonland, then discarded it (last in hand) →
        // a nonland was discarded → +1/+1 counter on the target.
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "X = 1 connive discarded a nonland → +1/+1 counter");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(1,
            "the connive draw was discarded");
    }

    private static GameContext BuildContext(params Player[] players)
        => new(
            self: players[0],
            allPlayers: players,
            activePlayer: players[0],
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(),
            landPlayAvailable: true);
}
