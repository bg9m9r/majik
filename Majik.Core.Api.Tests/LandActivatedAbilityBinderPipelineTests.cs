using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Prod-path verification of the generic utility-land activated-ability binder
/// (<see cref="LandActivatedAbilityBinder"/>). Lands are NEVER routed through
/// their [CardName] factory (GameFacade.BuildDeckCard gates the instance-swap on
/// !shell.HasType(Land)), so a utility land's scry / draw / counter / token /
/// damage / return / destroy ability is DEAD in real games unless the binder
/// chain binds it. Every test builds the land through <see cref="GameFacade.Create"/>
/// — the exact production materialization path — and inspects / executes the
/// bound <see cref="ICard.Abilities"/>. (A factory-direct test would NOT prove
/// the ability fires in prod; only this path counts — v1-deferrals #12.)
/// </summary>
public class LandActivatedAbilityBinderPipelineTests
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

    /// <summary>Build <paramref name="land"/> through the production deck-build
    /// path and return the live (bound, in-place) instance.</summary>
    private static (GameFacade facade, ICard live) BuildThroughProd(Land land, FakeCardRepo repo)
    {
        repo.Add("Forest", "Basic Land — Forest", oracleText: "({T}: Add {G}.)");

        var deck = new List<ICard> { land };
        for (var i = 1; i < 60; i++)
            deck.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));

        var facade = GameFacade.Create("Alice", "Bob", deck, new List<ICard>(), cardRepo: repo);
        return (facade, land);
    }

    private static GameContext Ctx(GameFacade facade) =>
        new(facade.Alice, new[] { facade.Alice, facade.Bob }, facade.Alice, 1,
            StepStateType.PreCombatMain, facade.LiveStack);

    /// <summary>Run the ability's TargetRequests through the real collection
    /// pipeline with the given agent, stamp ChosenTargets, then execute the
    /// effect — the path AbilityActivator drives in prod.</summary>
    private static void CollectAndExecute(
        ActivatedAbility ability, GameContext ctx, IPlayerAgent? agent)
    {
        var collected = TargetCollection.CollectAsync(
            ability.TargetRequests, ability.Source as ICard, ctx, agent).GetAwaiter().GetResult();
        ability.SetChosenTargets(collected);
        foreach (var e in ability.Effects)
            e.ExecuteAsync(ResolutionContext.For(ctx.Self, agent, ctx, null))
                .GetAwaiter().GetResult();
    }

    private static void OnBattlefield(GameFacade facade, Land land)
    {
        facade.Alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    // ======================================================================
    // 1. SCRY — Castle Vantress
    // ======================================================================

    private const string CastleVantressOracle =
        "This land enters tapped unless you control an Island.\n" +
        "{T}: Add {U}.\n" +
        "{2}{U}{U}, {T}: Scry 2.";

    [Fact]
    public void Prod_CastleVantress_BindsScryActivatedAbility()
    {
        var repo = new FakeCardRepo();
        repo.Add("Castle Vantress", "Land", oracleText: CastleVantressOracle, colors: "U");
        var land = new Land("Castle Vantress", null, null);

        var (_, live) = BuildThroughProd(land, repo);

        var scry = live.Abilities.OfType<ActivatedAbility>()
            .Where(a => a.Costs.OfType<ManaCostCost>().Any())
            .ToList();
        scry.Should().ContainSingle("the {2}{U}{U}, {T}: Scry 2 ability binds in prod");
        scry[0].Costs.OfType<AdditionalCost>().Should().NotBeEmpty("the {T} tap cost is added");
    }

    [Fact]
    public async Task Prod_CastleVantress_Scry_ReordersTopOfLibrary()
    {
        var repo = new FakeCardRepo();
        repo.Add("Castle Vantress", "Land", oracleText: CastleVantressOracle, colors: "U");
        var land = new Land("Castle Vantress", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);

        var libBefore = facade.Alice.Zones.Library.GetCards().Count();
        var scry = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        // Agent that decides "keep the top two on top" — proves the prod path
        // routes the scry decision through the agent (the bound effect awaits it).
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: facade.Alice.Zones.Library.GetCards().Take(2).ToList()));

        foreach (var e in scry.Effects)
            await e.ExecuteAsync(ResolutionContext.For(facade.Alice, agent, Ctx(facade), null));

        // Scry doesn't change library SIZE (no draw); it proves the effect ran
        // without throwing on the prod path.
        facade.Alice.Zones.Library.GetCards().Count().Should().Be(libBefore);
    }

    // ======================================================================
    // 2. DRAW — Sea Gate Wreckage (hand-empty restriction)
    // ======================================================================

    private const string SeaGateWreckageOracle =
        "{T}: Add {C}. ({C} represents colorless mana.)\n" +
        "{2}{C}, {T}: Draw a card. Activate only if you have no cards in hand.";

    [Fact]
    public void Prod_SeaGateWreckage_BindsDraw_WithHandEmptyRestriction()
    {
        var repo = new FakeCardRepo();
        repo.Add("Sea Gate Wreckage", "Land", oracleText: SeaGateWreckageOracle, colors: "");
        var land = new Land("Sea Gate Wreckage", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        // Ensure the hand is non-empty so the "no cards in hand" restriction blocks.
        var inHand = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        inHand.SetOwner(alice);
        alice.Zones.Hand.AddCard(inHand); inHand.SetZone(ZoneType.Hand);

        var draw = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        draw.CanActivateNow().Should().BeFalse("the hand is non-empty, the restriction blocks activation");

        // Empty the hand → restriction now allows activation.
        foreach (var c in alice.Zones.Hand.GetCards().ToList())
        {
            alice.Zones.Hand.RemoveCard(c);
        }
        draw.CanActivateNow().Should().BeTrue("with an empty hand the restriction permits activation");

        var handBefore = alice.Zones.Hand.GetCards().Count();
        foreach (var e in draw.Effects) e.Execute();
        alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1, "exactly one card is drawn");
    }

    // ======================================================================
    // 3a. +1/+1 counter on EACH creature you control — Gavony Township
    // ======================================================================

    private const string GavonyTownshipOracle =
        "{T}: Add {C}.\n" +
        "{2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control.";

    [Fact]
    public void Prod_GavonyTownship_CountersEachCreatureYouControl()
    {
        var repo = new FakeCardRepo();
        repo.Add("Gavony Township", "Land", oracleText: GavonyTownshipOracle, colors: "");
        var land = new Land("Gavony Township", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var mine = new Creature("Bear", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        mine.SetOwner(alice); mine.SetController(alice);
        alice.Zones.Battlefield.AddCard(mine); mine.SetZone(ZoneType.Battlefield);

        // Bob's creature must NOT get a counter ("you control").
        var theirs = new Creature("Ogre", "{R}", 3, 3, null, new[] { CardSubtype.Ogre });
        theirs.SetOwner(facade.Bob); theirs.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(theirs); theirs.SetZone(ZoneType.Battlefield);

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in ability.Effects) e.Execute();

        mine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        theirs.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0, "'you control' excludes the opponent's creature");
    }

    // ======================================================================
    // 3b. +1/+1 counters on TARGET creature — Cave of Temptation (agent-chosen)
    // ======================================================================

    private const string CaveOfTemptationOracle =
        "{T}: Add {C}.\n" +
        "{1}, {T}: Add one mana of any color.\n" +
        "{4}, {T}, Sacrifice this land: Put two +1/+1 counters on target creature. Activate only as a sorcery.";

    [Fact]
    public void Prod_CaveOfTemptation_CountersTheAgentChosenCreature()
    {
        var repo = new FakeCardRepo();
        repo.Add("Cave of Temptation", "Land", oracleText: CaveOfTemptationOracle, colors: "");
        var land = new Land("Cave of Temptation", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var first = new Creature("Bear A", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        var second = new Creature("Bear B", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        foreach (var c in new[] { first, second })
        {
            c.SetOwner(alice); c.SetController(alice);
            alice.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        }

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target creature");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { second });
        CollectAndExecute(ability, Ctx(facade), agent);

        second.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "TWO +1/+1 counters go on the AGENT-CHOSEN creature");
        first.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the non-chosen creature is untouched (not first-eligible)");
    }

    // ======================================================================
    // 4. TOKEN — Castle Ardenvale (1/1 white Human)
    // ======================================================================

    private const string CastleArdenvaleOracle =
        "This land enters tapped unless you control a Plains.\n" +
        "{T}: Add {W}.\n" +
        "{2}{W}{W}, {T}: Create a 1/1 white Human creature token.";

    [Fact]
    public void Prod_CastleArdenvale_CreatesHumanToken()
    {
        var repo = new FakeCardRepo();
        repo.Add("Castle Ardenvale", "Land", oracleText: CastleArdenvaleOracle, colors: "W");
        var land = new Land("Castle Ardenvale", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var creaturesBefore = alice.Zones.Battlefield.GetCards().OfType<Creature>().Count();
        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in ability.Effects) e.Execute();

        var creatures = alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        creatures.Count.Should().Be(creaturesBefore + 1, "one token created");
        var token = creatures.Single(c => c.HasSubtype(CardSubtype.Human));
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.IsToken.Should().BeTrue();
    }

    // ======================================================================
    // 5a. DAMAGE to each opponent — Ramunap Ruins
    // ======================================================================

    private const string RamunapRuinsOracle =
        "{T}: Add {C}.\n" +
        "{T}, Pay 1 life: Add {R}.\n" +
        "{2}{R}{R}, {T}, Sacrifice a Desert: This land deals 2 damage to each opponent.";

    [Fact]
    public async Task Prod_RamunapRuins_Deals2ToEachOpponent()
    {
        var repo = new FakeCardRepo();
        repo.Add("Ramunap Ruins", "Land — Desert", oracleText: RamunapRuinsOracle, colors: "");
        var land = new Land("Ramunap Ruins", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);

        var bobLifeBefore = facade.Bob.LifeTotal;
        var aliceLifeBefore = facade.Alice.LifeTotal;

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in ability.Effects)
            await e.ExecuteAsync(ResolutionContext.For(facade.Alice, null, Ctx(facade), null));

        facade.Bob.LifeTotal.Should().Be(bobLifeBefore - 2, "the opponent takes 2 damage");
        facade.Alice.LifeTotal.Should().Be(aliceLifeBefore, "the controller is not an opponent of themselves");
    }

    // ======================================================================
    // 5b. GAIN LIFE — Phyrexia's Core (typed-sac rider deferred)
    // ======================================================================

    private const string PhyrexiasCoreOracle =
        "{T}: Add {C}.\n" +
        "{1}, {T}, Sacrifice an artifact: You gain 1 life.";

    [Fact]
    public void Prod_PhyrexiasCore_Gains1Life()
    {
        var repo = new FakeCardRepo();
        repo.Add("Phyrexia's Core", "Land", oracleText: PhyrexiasCoreOracle, colors: "");
        var land = new Land("Phyrexia's Core", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var lifeBefore = facade.Alice.LifeTotal;

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in ability.Effects) e.Execute();

        facade.Alice.LifeTotal.Should().Be(lifeBefore + 1);
    }

    // ======================================================================
    // 5c. DAMAGE to any target — Barbarian Ring (Threshold ability-word prefix)
    // ======================================================================

    private const string BarbarianRingOracle =
        "{T}: Add {R}. This land deals 1 damage to you.\n" +
        "Threshold — {R}, {T}, Sacrifice this land: It deals 2 damage to any target. Activate only if there are seven or more cards in your graveyard.";

    [Fact]
    public void Prod_BarbarianRing_DealsDamageToAgentChosenTarget()
    {
        var repo = new FakeCardRepo();
        repo.Add("Barbarian Ring", "Land", oracleText: BarbarianRingOracle, colors: "R");
        var land = new Land("Barbarian Ring", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);

        var bobLifeBefore = facade.Bob.LifeTotal;
        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("any target");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { facade.Bob });
        CollectAndExecute(ability, Ctx(facade), agent);

        facade.Bob.LifeTotal.Should().Be(bobLifeBefore - 2,
            "the Threshold-prefixed 'deals 2 damage to any target' binds + hits the agent's pick");
    }

    // ======================================================================
    // 6. RETURN FROM GRAVEYARD — Buried Ruin (agent-chosen artifact)
    // ======================================================================

    private const string BuriedRuinOracle =
        "{T}: Add {C}.\n" +
        "{2}, {T}, Sacrifice this land: Return target artifact card from your graveyard to your hand.";

    [Fact]
    public void Prod_BuriedRuin_ReturnsAgentChosenArtifactFromGraveyard()
    {
        var repo = new FakeCardRepo();
        repo.Add("Buried Ruin", "Land", oracleText: BuriedRuinOracle, colors: "");
        var land = new Land("Buried Ruin", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var art1 = new Artifact("Bauble A", "{0}");
        var art2 = new Artifact("Bauble B", "{0}");
        foreach (var a in new[] { art1, art2 })
        {
            a.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(a); a.SetZone(ZoneType.Graveyard);
        }

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.TargetRequests.Should().ContainSingle();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { art2 });
        CollectAndExecute(ability, Ctx(facade), agent);

        alice.Zones.Hand.GetCards().Should().Contain(art2, "the AGENT-CHOSEN artifact returns to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(art1, "the non-chosen artifact stays in the graveyard");
    }

    // ======================================================================
    // 7. DESTROY TARGET LAND — Ghost Quarter (agent-chosen)
    // ======================================================================

    private const string GhostQuarterOracle =
        "{T}: Add {C}.\n" +
        "{T}, Sacrifice this land: Destroy target land. Its controller may search their library for a basic land card, put it onto the battlefield, then shuffle.";

    [Fact]
    public void Prod_GhostQuarter_DestroysAgentChosenLand()
    {
        var repo = new FakeCardRepo();
        repo.Add("Ghost Quarter", "Land", oracleText: GhostQuarterOracle, colors: "");
        var land = new Land("Ghost Quarter", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);

        var bobLand = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        bobLand.SetOwner(facade.Bob); bobLand.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(bobLand); bobLand.SetZone(ZoneType.Battlefield);

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target land");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bobLand });
        CollectAndExecute(ability, Ctx(facade), agent);

        facade.Bob.Zones.Battlefield.GetCards().Should().NotContain(bobLand,
            "the AGENT-CHOSEN land is destroyed");
        facade.Bob.Zones.Graveyard.GetCards().Should().Contain(bobLand);
    }

    [Fact]
    public void Prod_TectonicEdge_BindsNonbasicLandDestroy()
    {
        const string oracle =
            "{T}: Add {C}.\n" +
            "{1}, {T}, Sacrifice this land: Destroy target nonbasic land. Activate only if an opponent controls four or more lands.";
        var repo = new FakeCardRepo();
        repo.Add("Tectonic Edge", "Land", oracleText: oracle, colors: "");
        var land = new Land("Tectonic Edge", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);

        // A BASIC land is NOT a legal target (nonbasic-only); a nonbasic IS.
        var basic = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        basic.SetOwner(facade.Bob); basic.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(basic); basic.SetZone(ZoneType.Battlefield);
        var nonbasic = new Land("Mishra's Factory", null, null);
        nonbasic.SetOwner(facade.Bob); nonbasic.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(nonbasic); nonbasic.SetZone(ZoneType.Battlefield);

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        var candidates = ability.TargetRequests[0].CandidateGatherer!(Ctx(facade));
        candidates.Should().Contain(nonbasic);
        candidates.Should().NotContain(basic, "a basic land is not a legal target for a 'nonbasic land' destroy");
    }

    // ======================================================================
    // 7b. SEARCH-FOR-BASIC (Panorama cycle) — reuses the fetch effect path
    // ======================================================================

    [Fact]
    public async Task Prod_BantPanorama_SacFetchesNamedBasicTapped()
    {
        // The {1}-cost three-named-basic Panorama form is NOT covered by
        // OracleLandActivatedAbilityBinder (that handles the two-basic +
        // "Pay 1 life" fetchland and the any-basic / no-mana sac-fetch forms),
        // so it binds HERE.
        const string oracle =
            "{T}: Add {C}.\n" +
            "{1}, {T}, Sacrifice this land: Search your library for a basic Forest, Plains, or Island card, put it onto the battlefield tapped, then shuffle.";
        var repo = new FakeCardRepo();
        repo.Add("Bant Panorama", "Land", oracleText: oracle, colors: "");
        var land = new Land("Bant Panorama", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        // Library already holds Forests from the deck-build; ensure at least one.
        var bfBefore = alice.Zones.Battlefield.GetCards().OfType<Land>()
            .Count(l => l.HasSubtype(CardSubtype.Forest));

        var fetch = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>().Count() >= 2);

        // ScriptedAgent's ChooseLibraryPickAsync returns the first candidate;
        // the library is all-Forest so the fetch picks a basic Forest.
        var agent = new ScriptedAgent();

        foreach (var e in fetch.Effects)
            await e.ExecuteAsync(ResolutionContext.For(alice, agent, Ctx(facade), null));

        alice.Zones.Graveyard.GetCards().Should().Contain(land, "the Panorama sacrifices itself");
        var fetched = alice.Zones.Battlefield.GetCards().OfType<Land>()
            .Where(l => l.HasSubtype(CardSubtype.Forest)).ToList();
        fetched.Count.Should().Be(bfBefore + 1, "a basic Forest was fetched onto the battlefield");
        fetched.Last().IsTapped.Should().BeTrue("the fetched basic enters tapped");
    }

    // ======================================================================
    // 8. CHANNEL — deferred family (discard-from-hand activation seam)
    // ======================================================================

    // ======================================================================
    // 11. MASS until-EOT KEYWORD GRANT — Vault of the Archangel
    // "{2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink
    // until end of turn." (CR 613.1c Layer 6 / CR 514.2 cleanup expiry)
    // ======================================================================

    private const string VaultOfTheArchangelOracle =
        "{T}: Add {C}.\n" +
        "{2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink until end of turn.";

    [Fact]
    public void Prod_VaultOfTheArchangel_GrantsDeathtouchAndLifelinkToYourCreatures()
    {
        var repo = new FakeCardRepo();
        repo.Add("Vault of the Archangel", "Land", oracleText: VaultOfTheArchangelOracle, colors: "");
        var land = new Land("Vault of the Archangel", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var mine = new Creature("Bear", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        mine.SetOwner(alice); mine.SetController(alice);
        alice.Zones.Battlefield.AddCard(mine); mine.SetZone(ZoneType.Battlefield);

        // Bob's creature must NOT gain the keywords ("Creatures you control").
        var theirs = new Creature("Ogre", "{R}", 3, 3, null, new[] { CardSubtype.Ogre });
        theirs.SetOwner(facade.Bob); theirs.SetController(facade.Bob);
        facade.Bob.Zones.Battlefield.AddCard(theirs); theirs.SetZone(ZoneType.Battlefield);

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in ability.Effects) e.Execute();

        mine.HasEffectiveKeyword("Deathtouch").Should().BeTrue(
            "the controller's creature gains deathtouch until end of turn");
        mine.HasEffectiveKeyword("Lifelink").Should().BeTrue(
            "the controller's creature gains lifelink until end of turn");
        theirs.HasEffectiveKeyword("Deathtouch").Should().BeFalse(
            "'Creatures you control' excludes the opponent's creature");
        theirs.HasEffectiveKeyword("Lifelink").Should().BeFalse(
            "'Creatures you control' excludes the opponent's creature");
    }

    // ======================================================================
    // 12. COUNT-LINKED TREASURE TOKEN — Treasure Vault
    // "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens."
    // The token count is read from the per-activation X ledger
    // (ResolutionContext.ChosenX, GAP 2) — CR 111.10 Treasure tokens.
    // ======================================================================

    private const string TreasureVaultOracle =
        "{T}: Add {C}.\n" +
        "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens.";

    [Fact]
    public async Task Prod_TreasureVault_CreatesXTreasureTokens()
    {
        var repo = new FakeCardRepo();
        repo.Add("Treasure Vault", "Land", oracleText: TreasureVaultOracle, colors: "");
        var land = new Land("Treasure Vault", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var artifactsBefore = alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Artifact)).Count();

        // The {X}{X}, {T}, Sacrifice ability — distinguished from the bare
        // {T}: Add {C} mana ability by carrying a ManaCostCost ({X}{X}).
        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        // X = 3 chosen at activation; the resolution effect reads it off
        // ResolutionContext.ChosenX (the same ledger TurnDriver stamps in prod).
        foreach (var e in ability.Effects)
            await e.ExecuteAsync(ResolutionContext.For(alice, null, Ctx(facade), null, chosenX: 3));

        var treasures = alice.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(c => c.HasType(CardType.Artifact) && c.HasSubtype(CardSubtype.Treasure))
            .ToList();
        treasures.Count.Should().Be(3, "X = 3 ⇒ three Treasure tokens created");
        treasures.Should().OnlyContain(t => t.IsToken, "each created Treasure is a token");
        alice.Zones.Battlefield.GetCards().Where(c => c.HasType(CardType.Artifact)).Count()
            .Should().Be(artifactsBefore + 3);
    }

    [Fact]
    public void Prod_TreasureVault_XLedgerDefaultsToZero_NoTokens()
    {
        var repo = new FakeCardRepo();
        repo.Add("Treasure Vault", "Land", oracleText: TreasureVaultOracle, colors: "");
        var land = new Land("Treasure Vault", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        var ability = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());

        // No ChosenX threaded (legacy/shape path) ⇒ X defaults to 0, a legal
        // but useless activation: zero Treasures minted.
        foreach (var e in ability.Effects) e.Execute();

        alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Artifact) && c.HasSubtype(CardSubtype.Treasure))
            .Should().Be(0, "X defaults to 0 when no ChosenX is threaded");
    }

    // ======================================================================
    // 13. RICHER-SHAPE TOKEN — Mirrex's Phyrexian Mite
    // "{3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature token
    //  with toxic 1 and \"This token can't block.\""
    // The simple-token parser rejects the artifact-creature + toxic + can't-block
    // riders, so this binds via the dedicated Mite recognizer (CR 111.1 / 111.4).
    // ======================================================================

    private const string MirrexOracle =
        "{T}: Add {C}.\n" +
        "{T}: Add one mana of any color. Activate only if this land entered this turn.\n" +
        "{3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature token " +
        "with toxic 1 and \"This token can't block.\" " +
        "(Players dealt combat damage by it also get a poison counter.)";

    [Fact]
    public void Prod_Mirrex_CreatesPhyrexianMiteToken_ArtifactCreature_ToxicCantBlock()
    {
        var repo = new FakeCardRepo();
        repo.Add("Mirrex", "Land — Sphere", oracleText: MirrexOracle, colors: "");
        var land = new Land("Mirrex", null, new[] { CardSubtype.Sphere });

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        // The {3}, {T} token ability — distinguished from the two mana abilities
        // (which are ManaAbility, not ActivatedAbility).
        var ability = live.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var e in ability.Effects) e.Execute();

        var mite = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Single(c => c.Name == "Phyrexian Mite");
        mite.IsToken.Should().BeTrue();
        mite.Power.Should().Be(1);
        mite.Toughness.Should().Be(1);
        mite.HasType(CardType.Artifact).Should().BeTrue("CR 111.1 — Mite is an artifact creature");
        mite.HasType(CardType.Creature).Should().BeTrue();
        mite.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        mite.HasSubtype(CardSubtype.Mite).Should().BeTrue();
        mite.GetEffectiveColors().Should().BeEmpty("a colorless token");
        // toxic 1 + can't-block markers (CR 702.180 / CR 509.1a).
        mite.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "toxic" && k.Arg == 1);
        mite.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "CantBlock");
        // CR 509.1a — the Mite can't be declared as a blocker.
        Majik.Core.Combat.CombatAbilities.HasCantBlock(mite).Should().BeTrue();
    }

    // ======================================================================
    // 14. ATTACK-RIDER DELAYED TOKEN TRIGGER — Dalkovan Encampment
    // "{2}{W}, {T}: Whenever you attack this turn, create two 1/1 red Warrior
    //  creature tokens that are tapped and attacking. Sacrifice them at the
    //  beginning of the next end step."
    // Activating the ability installs a one-turn "whenever you attack" trigger
    // that mints two 1/1 red Warriors per combat (CR 508.1f / CR 603.7).
    // ======================================================================

    private const string DalkovanOracle =
        "This land enters tapped unless you control a Swamp or a Mountain.\n" +
        "{T}: Add {W}.\n" +
        "{2}{W}, {T}: Whenever you attack this turn, create two 1/1 red Warrior " +
        "creature tokens that are tapped and attacking. Sacrifice them at the " +
        "beginning of the next end step.";

    [Fact]
    public async Task Prod_DalkovanEncampment_AttackRider_InstallsTrigger_MintsWarriorsOnAttack()
    {
        var repo = new FakeCardRepo();
        repo.Add("Dalkovan Encampment", "Land", oracleText: DalkovanOracle, colors: "");
        var land = new Land("Dalkovan Encampment", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;

        // The {2}{W}, {T} attack-rider ability — the only ActivatedAbility (the
        // {T}: Add {W} line is a ManaAbility).
        var ability = live.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();

        // No attack-rider trigger registered yet (the activated ability installs
        // it only on resolution).
        var triggers = facade.Triggers;

        // Activate: resolve the ability ⇒ install the this-turn attack trigger
        // with the live TriggerManager.
        foreach (var e in ability.Effects) e.Execute();

        var warriorsBefore = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.Name == "Warrior");

        // A creature you control is declared as an attacker (CR 508.1f) ⇒ the
        // installed "whenever you attack this turn" trigger fires.
        var attacker = new Creature("Bear", "{1}{G}", 2, 2);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.SetZone(ZoneType.Battlefield);

        triggers.EvaluateTriggers(
            new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(attacker, facade.Bob));
        triggers.PendingCount.Should().Be(1,
            "a creature Alice controls attacked while the rider is live");

        // Drain the pending trigger onto the stack and resolve it (CR 603.3).
        await triggers.PutPendingTriggersOnStackAsync(
            alice, new Dictionary<Player, IPlayerAgent>(), Ctx(facade));
        while (facade.LiveStack.Pop() is { } obj)
            await obj.ResolveAsync(null, Ctx(facade));

        var warriors = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Warrior").ToList();
        warriors.Count.Should().Be(warriorsBefore + 2, "two 1/1 red Warriors per attack");
        warriors.Should().OnlyContain(w => w.IsToken && w.Power == 1 && w.Toughness == 1);
        warriors.Should().OnlyContain(w =>
            w.GetEffectiveColors().Contains(Majik.Core.ValueObjects.ManaColor.Red));
    }

    [Fact]
    public async Task Prod_DalkovanEncampment_AttackRider_SacrificesWarriorTokens_AtNextEndStep()
    {
        var repo = new FakeCardRepo();
        repo.Add("Dalkovan Encampment", "Land", oracleText: DalkovanOracle, colors: "");
        var land = new Land("Dalkovan Encampment", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;
        var triggers = facade.Triggers;

        // Activate the {2}{W}, {T} ability ⇒ install the this-turn attack rider.
        var ability = live.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Declare an attacker Alice controls ⇒ the rider mints two 1/1 Warriors.
        var attacker = new Creature("Bear", "{1}{G}", 2, 2);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.SetZone(ZoneType.Battlefield);

        triggers.EvaluateTriggers(
            new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(attacker, facade.Bob));
        await triggers.PutPendingTriggersOnStackAsync(
            alice, new Dictionary<Player, IPlayerAgent>(), Ctx(facade));
        while (facade.LiveStack.Pop() is { } obj)
            await obj.ResolveAsync(null, Ctx(facade));

        var warriors = alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Warrior").ToList();
        warriors.Should().HaveCount(2, "two Warriors minted before the end step");

        // CR 603.7 — "Sacrifice them at the beginning of the next end step."
        // Firing the End-step StepStartedEvent drains the delayed sacrifice
        // trigger; resolving it moves the tokens to the graveyard.
        triggers.EvaluateTriggers(new StepStartedEvent(StepStateType.End, alice));
        await triggers.PutPendingTriggersOnStackAsync(
            alice, new Dictionary<Player, IPlayerAgent>(), Ctx(facade));
        while (facade.LiveStack.Pop() is { } obj)
            await obj.ResolveAsync(null, Ctx(facade));

        // The sacrificed tokens are off the battlefield (CR 701.16). SBA 704.5d
        // (CR 111.7) then removes them entirely — they cease to exist.
        var sba = new Majik.Core.Rules.StateBasedActions(
            facade.EventBus, null, triggers);
        sba.CheckStateBasedActions(
            new[] { alice, facade.Bob },
            alice.Zones.Battlefield.GetCards());

        alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Warrior").Should().BeEmpty(
                "the two Warrior tokens are sacrificed at the next end step");
        alice.Zones.Graveyard.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Warrior").Should().BeEmpty(
                "tokens cease to exist in the graveyard via SBA 704.5d");
    }

    // ======================================================================
    // CHANNEL cost-reduction rider — Boseiju, Who Endures (CR 118.9)
    // "This ability costs {1} less to activate for each legendary creature you
    // control." Channel binds in prod (BindChannel: ManaCostCost +
    // DiscardSelfCost); the rider makes the mana cost a self-reducing
    // DynamicGenericReductionManaCost. This is the deferral being paid down.
    // ======================================================================

    private const string BoseijuWhoEnduresOracle =
        "{T}: Add {G}.\n" +
        "Channel — {1}{G}, Discard this card: Destroy target artifact, enchantment, " +
        "or nonbasic land an opponent controls. That player may search their library " +
        "for a land card with a basic land type, put it onto the battlefield, then shuffle. " +
        "This ability costs {1} less to activate for each legendary creature you control.";

    [Fact]
    public void Prod_BoseijuWhoEndures_ChannelBindsDynamicCostReduction()
    {
        var repo = new FakeCardRepo();
        repo.Add("Boseiju, Who Endures", "Legendary Land",
            oracleText: BoseijuWhoEnduresOracle, colors: "G");
        var land = new Land("Boseiju, Who Endures", new[] { CardSupertype.Legendary }, null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;

        // The Channel ability binds in prod with a discard-self cost AND a
        // self-reducing mana cost (the rider seam).
        var channel = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardSelfCost>().Any());
        var dyn = channel.Costs.OfType<DynamicGenericReductionManaCost>().Single();
        dyn.BaseCost.Generic.Should().Be(1);
        dyn.BaseCost.Green.Should().Be(1);

        // No legendary creatures → full {1}{G}.
        dyn.EffectiveCost(alice).Generic.Should().Be(1);

        // Two legendary creatures Alice controls → {1}{G} reduced to {G}
        // (clamps the generic at 0, never touches the colored pip).
        for (var i = 0; i < 2; i++)
        {
            var legend = new Creature($"Legend{i}", "{G}", 1, 1,
                supertypes: new[] { CardSupertype.Legendary }) { Owner = alice, Controller = alice };
            alice.Zones.Battlefield.AddCard(legend);
        }
        var effective = dyn.EffectiveCost(alice);
        effective.Generic.Should().Be(0, "{1}{G} reduced by 2 legendary creatures, clamped at 0");
        effective.Green.Should().Be(1, "the colored {G} pip is never reduced (CR 118.9)");

        // CanPay reflects the reduction: floating just {G} suffices now.
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("G"));
        dyn.CanPay(alice).Should().BeTrue("cost reduced to {G}, covered by the floated green");
    }

    // ======================================================================
    // 13b. CHANNEL "may search" follow-up — Boseiju, Who Endures
    // "Destroy target … nonbasic land an opponent controls. THAT PLAYER may
    // search their library for a land card with a basic land type, put it onto
    // the battlefield, then shuffle." (CR 701.19a / 701.20a). The optional
    // search is a binder-bound resolution-time "you may" — the destroyed
    // land's controller is OFFERED the search (decline = no fetch), exactly
    // the seam pinned by the binder-bound-optional-you-may deferral.
    // ======================================================================

    [Fact]
    public async Task Prod_BoseijuWhoEndures_Channel_DestroyedControllerMaySearchBasicLandType_Accept()
    {
        var repo = new FakeCardRepo();
        repo.Add("Boseiju, Who Endures", "Legendary Land",
            oracleText: BoseijuWhoEnduresOracle, colors: "G");
        var land = new Land("Boseiju, Who Endures", new[] { CardSupertype.Legendary }, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var alice = facade.Alice;
        var bob = facade.Bob;

        // Bob (the destroyed land's controller) controls a nonbasic land to be
        // the destroy target, and has a basic-land-TYPE card in his library
        // (a basic Swamp) he may fetch onto the battlefield.
        var bobNonbasic = new Land("Steam Vents", null, new[] { CardSubtype.Island, CardSubtype.Mountain });
        bobNonbasic.SetOwner(bob); bobNonbasic.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobNonbasic); bobNonbasic.SetZone(ZoneType.Battlefield);

        var bobSwamp = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        bobSwamp.SetOwner(bob);
        bob.Zones.Library.AddCard(bobSwamp); bobSwamp.SetZone(ZoneType.Library);

        var channel = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardSelfCost>().Any());

        var bobBasicsBefore = bob.Zones.Battlefield.GetCards().OfType<Land>()
            .Count(l => l.HasSupertype(CardSupertype.Basic));

        // Bob's agent accepts the optional search and picks the basic Swamp
        // (ScriptedAgent.ChooseLibraryPickAsync returns candidates[0]).
        AgentRegistry.Set(bob, new ScriptedAgent());

        try
        {
            // Alice's agent chooses the target (Bob's nonbasic land).
            var aliceAgent = new ScriptedAgent();
            aliceAgent.QueueTargets(new object[] { bobNonbasic });
            CollectAndExecute(channel, Ctx(facade), aliceAgent);
        }
        finally
        {
            AgentRegistry.Remove(bob);
        }

        bob.Zones.Graveyard.GetCards().Should().Contain(bobNonbasic,
            "the targeted nonbasic land is destroyed");
        bob.Zones.Battlefield.GetCards().OfType<Land>()
            .Count(l => l.HasSupertype(CardSupertype.Basic)).Should().Be(bobBasicsBefore + 1,
            "Bob accepted the optional 'may search' and put a basic-land-type card onto the battlefield");
        bob.Zones.Battlefield.GetCards().Should().Contain(bobSwamp,
            "the fetched Swamp entered Bob's battlefield");
    }

    [Fact]
    public async Task Prod_BoseijuWhoEndures_Channel_DestroyedControllerDeclinesSearch_NoFetch()
    {
        var repo = new FakeCardRepo();
        repo.Add("Boseiju, Who Endures", "Legendary Land",
            oracleText: BoseijuWhoEnduresOracle, colors: "G");
        var land = new Land("Boseiju, Who Endures", new[] { CardSupertype.Legendary }, null);

        var (facade, live) = BuildThroughProd(land, repo);
        OnBattlefield(facade, land);
        var bob = facade.Bob;

        var bobNonbasic = new Land("Steam Vents", null, new[] { CardSubtype.Island, CardSubtype.Mountain });
        bobNonbasic.SetOwner(bob); bobNonbasic.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobNonbasic); bobNonbasic.SetZone(ZoneType.Battlefield);

        // Bob's library has NO basic-land-TYPE card to find — the search reveals
        // no eligible candidate, so nothing is fetched (CR 701.20a — declining /
        // finding nothing is legal, the library still shuffles). This exercises
        // the optional half's no-fetch branch without a crash.
        var channel = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardSelfCost>().Any());

        var bobBattlefieldBefore = bob.Zones.Battlefield.GetCards().Count();
        var libBefore = bob.Zones.Library.GetCards().Count();

        AgentRegistry.Set(bob, new ScriptedAgent());
        try
        {
            var aliceAgent = new ScriptedAgent();
            aliceAgent.QueueTargets(new object[] { bobNonbasic });
            CollectAndExecute(channel, Ctx(facade), aliceAgent);
        }
        finally
        {
            AgentRegistry.Remove(bob);
        }

        bob.Zones.Graveyard.GetCards().Should().Contain(bobNonbasic,
            "the targeted nonbasic land is still destroyed");
        // Bob's battlefield gained nothing (the destroyed land left, nothing
        // fetched) — net effect: no basic-land-type card entered.
        bob.Zones.Battlefield.GetCards().OfType<Land>()
            .Count(l => l.HasSupertype(CardSupertype.Basic)).Should().Be(0,
            "no basic-land-type card was available to fetch");
        // The library is still shuffled (CR 701.20a) but the count is unchanged
        // when nothing is fetched.
        bob.Zones.Library.GetCards().Count().Should().Be(libBefore);
    }

    // ======================================================================
    // SACRIFICE-COST BUS — Treasure Vault (self-sac utility land).
    //   "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens."
    // Verifies the PRODUCTION event bus (threaded by DeckCardBuilder into the
    // land binders) reaches the self-sacrifice cost so paying it publishes a
    // PermanentSacrificedEvent (CR 701.16a) — the sac-cost-bus-land-and-binder
    // -payers deferral. A factory-direct test would not prove this; lands route
    // via the binder chain, not their [CardName] factory.
    // ======================================================================

    [Fact]
    public void Prod_TreasureVault_SacCost_PublishesPermanentSacrificedEvent()
    {
        var repo = new FakeCardRepo();
        repo.Add("Treasure Vault", "Artifact Land", oracleText: TreasureVaultOracle, colors: "");
        var land = new Land("Treasure Vault", null, null);

        var (facade, live) = BuildThroughProd(land, repo);
        // The sacrifice cost gates on Controller == payer (CR 701.16a); ensure the
        // live land is owned/controlled by Alice on her battlefield.
        ((Permanent)live).SetOwner(facade.Alice);
        ((Permanent)live).SetController(facade.Alice);
        OnBattlefield(facade, land);

        var seen = new List<PermanentSacrificedEvent>();
        facade.EventBus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        var sacCost = live.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Sacrifice);

        // Pay the self-sacrifice leg in isolation — the bus-bearing cost.
        sacCost.Pay(facade.Alice);

        live.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == live
                && ev.SacrificingPlayer == facade.Alice
                && !ev.WasToken);
    }
}
