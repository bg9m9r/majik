using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Prod-path verification of the manland (creature-land) animate + attack
/// triggers. Manlands are NEVER routed through their [CardName] factory
/// (GameFacade.BuildDeckCard short-circuits the factory swap for Lands), so
/// every manland's animate ability + attack trigger MUST be bound by the
/// binder chain (<see cref="ManlandBinder"/>) or it is land-dead in real
/// games. These tests build the land through <see cref="GameFacade.Create"/>
/// — the exact production materialization path — and inspect the resulting
/// card's <see cref="ICard.Abilities"/>.
/// </summary>
public class ManlandBinderPipelineTests
{
    private sealed class FakeCardRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _cards =
            new(StringComparer.OrdinalIgnoreCase);

        public CardEntity? GetByName(string name)
            => _cards.TryGetValue(name, out var c) ? c : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(
            string? q, bool io, int l,
            IReadOnlyList<string>? colors = null,
            IReadOnlyList<string>? types = null,
            IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => _cards.ContainsKey(name);
        public void SetImplemented(string n, bool v) => throw new NotImplementedException();

        public void Add(string name, string typeLine, string? oracleText = null,
            string keywordsJson = "[]", string manaCost = "", string colors = "")
        {
            _cards[name] = new CardEntity
            {
                Name = name,
                ScryfallId = Guid.NewGuid().ToString(),
                ManaCost = manaCost,
                TypeLine = typeLine,
                Keywords = keywordsJson,
                OracleText = oracleText,
                Colors = colors,
                Set = "TST",
                CollectorNumber = "1",
                IsImplemented = true,
            };
        }
    }

    /// <summary>
    /// Build <paramref name="land"/> through the production deck-build path and
    /// return the live (bound) instance from Alice's library. For Lands the
    /// shell instance is bound in place, so the same reference is returned.
    /// </summary>
    private static (GameFacade facade, ICard live) BuildThroughProd(
        Land land, FakeCardRepo repo)
    {
        repo.Add("Forest", "Basic Land — Forest", oracleText: "({T}: Add {G}.)");

        var deck = new List<ICard> { land };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var facade = GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);
        return (facade, land);
    }

    private const string TarPitOracle =
        "This land enters tapped.\n" +
        "{T}: Add {U} or {B}.\n" +
        "{1}{U}{B}: Until end of turn, this land becomes a 3/2 blue and black " +
        "Elemental creature. It's still a land. It can't be blocked this turn.";

    private const string ColonnadeOracle =
        "This land enters tapped.\n" +
        "{T}: Add {W} or {U}.\n" +
        "{3}{W}{U}: Until end of turn, this land becomes a 4/4 white and blue " +
        "Elemental creature with flying and vigilance. It's still a land.";

    private const string RestlessReefOracle =
        "This land enters tapped.\n" +
        "{T}: Add {U} or {B}.\n" +
        "{2}{U}{B}: Until end of turn, this land becomes a 4/4 blue and black " +
        "Shark creature with deathtouch. It's still a land.\n" +
        "Whenever this land attacks, target player mills four cards.";

    private const string RestlessVentsOracle =
        "This land enters tapped.\n" +
        "{T}: Add {B} or {R}.\n" +
        "{1}{B}{R}: Until end of turn, this land becomes a 2/3 black and red " +
        "Insect creature with menace. It's still a land.\n" +
        "Whenever this land attacks, you may discard a card. If you do, draw a card.";

    // -----------------------------------------------------------------------
    // ANIMATE — bound via the prod binder chain
    // -----------------------------------------------------------------------

    [Fact]
    public void Prod_CreepingTarPit_HasAnimateActivatedAbility()
    {
        var repo = new FakeCardRepo();
        repo.Add("Creeping Tar Pit", "Land", oracleText: TarPitOracle, colors: "U,B");
        var land = new Land("Creeping Tar Pit", supertypes: null, subtypes: null);

        var (_, live) = BuildThroughProd(land, repo);

        // {1}{U}{B} animate ability.
        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<ManaCostCost>().Any())
            .ToList();
        animate.Should().ContainSingle("the {1}{U}{B} animate ability is bound in prod");

        // Mana ability present too (binder chain still binds {T}: Add {U} or {B}).
        live.Abilities.OfType<IManaAbility>().Should().NotBeEmpty();
    }

    [Fact]
    public void Prod_CreepingTarPit_Animate_BecomesCreatureStillLand()
    {
        var repo = new FakeCardRepo();
        repo.Add("Creeping Tar Pit", "Land", oracleText: TarPitOracle, colors: "U,B");
        var land = new Land("Creeping Tar Pit", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var chars = facade.ContinuousEffects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land, "It's still a land");
        chars.Types.Should().Contain(CardType.Creature, "animate adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
    }

    [Fact]
    public void Prod_CelestialColonnade_Animate_GrantsFlyingAndVigilance_4_4()
    {
        var repo = new FakeCardRepo();
        repo.Add("Celestial Colonnade", "Land", oracleText: ColonnadeOracle, colors: "W,U");
        var land = new Land("Celestial Colonnade", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var chars = facade.ContinuousEffects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
        chars.Keywords.Should().Contain("Flying");
        chars.Keywords.Should().Contain("Vigilance");
        var cc = chars.Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(4);
        cc.Toughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // ATTACK TRIGGER — bound via the prod binder chain (Restless cycle)
    // -----------------------------------------------------------------------

    [Fact]
    public void Prod_RestlessVents_HasRummageAttackTrigger_AndAnimates()
    {
        var repo = new FakeCardRepo();
        repo.Add("Restless Vents", "Land", oracleText: RestlessVentsOracle, colors: "B,R");
        var land = new Land("Restless Vents", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // The non-targeted rummage attack trigger IS bound in prod.
        live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the 'Whenever this land attacks, you may discard a card. If you do, draw a card' trigger is bound in prod");
        live.Abilities.OfType<ActivatedAbility>()
            .Count(a => a.Costs.OfType<ManaCostCost>().Any())
            .Should().Be(1, "the animate ability is also bound");

        // Fire the trigger with a card in hand + library → the rummage discards
        // one card (hand count drops by one before the draw) and then draws one
        // (graveyard count rises by exactly one). We assert on graveyard delta
        // (robust to whatever the deck-build left in hand).
        var gyBefore = alice.Zones.Graveyard.GetCards().Count();
        var toDiscard = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        toDiscard.SetOwner(alice);
        alice.Zones.Hand.AddCard(toDiscard);
        toDiscard.SetZone(ZoneType.Hand);

        var trigger = live.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        alice.Zones.Graveyard.GetCards().Count().Should().Be(gyBefore + 1,
            "the rummage discards exactly one card (proving the prod-bound trigger fires)");
    }

    [Fact]
    public void Prod_RestlessReef_TargetedTrigger_IsDeferred_ButAnimates()
    {
        // Restless Reef's "mill TARGET player" trigger needs an agent target
        // prompt — deferred. The animate ability (4/4 Shark, deathtouch) still
        // binds in prod; only the targeted attack trigger is a no-op.
        var repo = new FakeCardRepo();
        repo.Add("Restless Reef", "Land", oracleText: RestlessReefOracle, colors: "U,B");
        var land = new Land("Restless Reef", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        live.Abilities.OfType<ActivatedAbility>()
            .Count(a => a.Costs.OfType<ManaCostCost>().Any())
            .Should().Be(1, "the animate ability binds even though the trigger is deferred");
        live.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the TARGETED mill trigger is deferred (needs an agent target prompt)");

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();
        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(4);
        cc.Toughness.Should().Be(4);
        cc.Keywords.Should().Contain("Deathtouch");
        cc.Subtypes.Should().Contain(CardSubtype.Shark);
    }
}
