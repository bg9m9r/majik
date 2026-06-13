using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
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
    public void Prod_CreepingTarPit_Animate_BodyIsBlueAndBlack_RevertsColourlessAtEot()
    {
        // CR 613.1e / Layer 5 — the animate line names "blue and black", so the
        // animated body's effective colour set must be {Blue, Black}. A manland
        // is a colourless Land off the battlefield; before this the body entered
        // colourless (the parsed colour words were dropped).
        var repo = new FakeCardRepo();
        repo.Add("Creeping Tar Pit", "Land", oracleText: TarPitOracle, colors: "U,B");
        var land = new Land("Creeping Tar Pit", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Printed land is colourless before animating.
        facade.ContinuousEffects.Compute((Permanent)land).Colors
            .Should().BeEmpty("a manland is a colourless Land until activated");

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        facade.ContinuousEffects.Compute((Permanent)land).Colors
            .Should().BeEquivalentTo(new[]
            {
                Majik.Core.ValueObjects.ManaColor.Blue,
                Majik.Core.ValueObjects.ManaColor.Black,
            }, "the animate line names \"blue and black\" (CR 613.1e Layer 5)");

        // CR 514.2 — "until end of turn" colour SET expires at cleanup; the land
        // reverts to colourless along with the rest of the animation.
        facade.ContinuousEffects.ExpireEndOfTurn();
        facade.ContinuousEffects.Compute((Permanent)land).Colors
            .Should().BeEmpty("the colour SET expires at end of turn with the animation");
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
        chars.Colors.Should().BeEquivalentTo(new[]
        {
            Majik.Core.ValueObjects.ManaColor.White,
            Majik.Core.ValueObjects.ManaColor.Blue,
        }, "the animate line names \"white and blue\" (CR 613.1e Layer 5)");
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
    public void Prod_RestlessReef_TargetedMillTrigger_BindsAndAnimates()
    {
        // Restless Reef's "mill TARGET player" trigger now binds in prod as a
        // real TargetRequest-carrying TriggeredAbility. The animate ability
        // (4/4 Shark, deathtouch) still binds too.
        var repo = new FakeCardRepo();
        repo.Add("Restless Reef", "Land", oracleText: RestlessReefOracle, colors: "U,B");
        var land = new Land("Restless Reef", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        live.Abilities.OfType<ActivatedAbility>()
            .Count(a => a.Costs.OfType<ManaCostCost>().Any())
            .Should().Be(1, "the animate ability binds");
        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the TARGETED mill trigger is now bound").Subject;
        trigger.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target player");

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

    // -----------------------------------------------------------------------
    // AGENT-CHOSEN targets — the trigger affects the agent's pick, not "first"
    // -----------------------------------------------------------------------

    private const string RestlessBivouacOracle =
        "This land enters tapped.\n" +
        "{T}: Add {R} or {W}.\n" +
        "{1}{R}{W}: This land becomes a 2/2 red and white Ox creature until " +
        "end of turn. It's still a land.\n" +
        "Whenever this land attacks, put a +1/+1 counter on target creature you control.";

    private static GameContext Ctx(GameFacade facade) =>
        new(facade.Alice, new[] { facade.Alice, facade.Bob }, facade.Alice, 1,
            StepStateType.PreCombatMain, facade.LiveStack);

    /// <summary>
    /// Run the trigger's TargetRequests through the real collection pipeline
    /// with the given agent, stamp ChosenTargets, then execute the effect — the
    /// exact path TriggerManager.PutPendingTriggersOnStackAsync drives.
    /// </summary>
    private static void CollectAndExecute(
        TriggeredAbility trigger, GameContext ctx, IPlayerAgent? agent)
    {
        var collected = TargetCollection.CollectAsync(
            trigger.TargetRequests, trigger.Source as ICard, ctx, agent).GetAwaiter().GetResult();
        trigger.SetChosenTargets(collected);
        foreach (var e in trigger.Effects)
            e.ExecuteAsync(ResolutionContext.For(ctx.Self, agent, ctx, null))
                .GetAwaiter().GetResult();
    }

    [Fact]
    public void Prod_RestlessBivouac_AttackTrigger_CountersTheAgentChosenCreature()
    {
        var repo = new FakeCardRepo();
        repo.Add("Restless Bivouac", "Land", oracleText: RestlessBivouacOracle, colors: "R,W");
        var land = new Land("Restless Bivouac", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Two of the controller's creatures: the agent must pick the SECOND
        // (proving the agent's choice is honoured, not first-eligible).
        var first = new Creature("Bear A", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        var second = new Creature("Bear B", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        foreach (var c in new[] { first, second })
        {
            c.SetOwner(alice); c.SetController(alice);
            alice.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        }

        var trigger = live.Abilities.OfType<TriggeredAbility>().Single();
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { second });

        CollectAndExecute(trigger, Ctx(facade), agent);

        second.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counter goes on the AGENT-CHOSEN creature");
        first.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the non-chosen creature is untouched (not first-eligible)");
    }

    [Fact]
    public void Prod_RestlessReef_AttackTrigger_MillsTheAgentChosenPlayer()
    {
        var repo = new FakeCardRepo();
        repo.Add("Restless Reef", "Land", oracleText: RestlessReefOracle, colors: "U,B");
        var land = new Land("Restless Reef", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        var bob = facade.Bob;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Give Bob a known library so the mill is observable.
        for (var i = 0; i < 6; i++)
        {
            var c = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
            c.SetOwner(bob);
            bob.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
        }
        var bobLibBefore = bob.Zones.Library.GetCards().Count();
        var bobGyBefore = bob.Zones.Graveyard.GetCards().Count();

        var trigger = live.Abilities.OfType<TriggeredAbility>().Single();
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bob }); // mill the OPPONENT, agent's pick

        CollectAndExecute(trigger, Ctx(facade), agent);

        bob.Zones.Graveyard.GetCards().Count().Should().Be(bobGyBefore + 4,
            "the AGENT-CHOSEN player mills exactly four cards");
        bob.Zones.Library.GetCards().Count().Should().Be(bobLibBefore - 4);
    }

    private const string RestlessAnchorageOracle =
        "This land enters tapped.\n" +
        "{T}: Add {W} or {U}.\n" +
        "{1}{W}{U}: Until end of turn, this land becomes a 2/3 white and blue " +
        "Bird creature with flying. It's still a land.\n" +
        "Whenever this land attacks, create a Map token.";

    [Fact]
    public void Prod_RestlessAnchorage_CreateMapAttackTrigger_BindsAndAnimates()
    {
        // Restless Anchorage's "create a Map token" attack trigger is a
        // non-targeted self-contained Restless rider — it binds in prod as a
        // simple TriggeredAbility (no TargetRequest). The animate ability
        // (2/3 Bird, flying) binds too.
        var repo = new FakeCardRepo();
        repo.Add("Restless Anchorage", "Land", oracleText: RestlessAnchorageOracle, colors: "W,U");
        var land = new Land("Restless Anchorage", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        live.Abilities.OfType<ActivatedAbility>()
            .Count(a => a.Costs.OfType<ManaCostCost>().Any())
            .Should().Be(1, "the animate ability binds");
        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the 'create a Map token' attack trigger is bound").Subject;
        trigger.TargetRequests.Should().BeEmpty("create-a-Map is non-targeted");

        // Fire the trigger → exactly one Map token appears on the controller's
        // battlefield.
        var mapsBefore = alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map");
        foreach (var e in trigger.Effects) e.Execute();
        alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map")
            .Should().Be(mapsBefore + 1, "the attack trigger mints exactly one Map token");

        // The animate ability still upgrades the land to a 2/3 flier.
        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();
        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(2);
        cc.Toughness.Should().Be(3);
        cc.Keywords.Should().Contain("Flying");
        cc.Subtypes.Should().Contain(CardSubtype.Bird);
    }

    // -----------------------------------------------------------------------
    // Remaining targeted / defender-capturing Restless triggers, end-to-end
    // through the prod binder chain (close manland-targeted-restless-triggers).
    // -----------------------------------------------------------------------

    // Faithful to Scryfall oracle text (Wilds of Eldraine "Restless" cycle).
    private const string RestlessRidgelineOracle =
        "This land enters tapped.\n" +
        "{T}: Add {R} or {G}.\n" +
        "{2}{R}{G}: This land becomes a 3/4 red and green Dinosaur creature " +
        "until end of turn. It's still a land.\n" +
        "Whenever this land attacks, another target attacking creature gets " +
        "+2/+0 until end of turn. Untap that creature.";

    [Fact]
    public void Prod_RestlessRidgeline_AttackTrigger_PumpsAndUntapsTheAgentChosenCreature()
    {
        // "another target attacking creature gets +2/+0 until end of turn.
        // Untap that creature." — 1..1 target over OTHER creatures. Binds in
        // prod with a real TargetRequest; resolution affects the AGENT'S pick.
        var repo = new FakeCardRepo();
        repo.Add("Restless Ridgeline", "Land", oracleText: RestlessRidgelineOracle, colors: "R,G");
        var land = new Land("Restless Ridgeline", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var first = new Creature("Bear A", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        var second = new Creature("Bear B", "{G}", 2, 2, null, new[] { CardSubtype.Bear });
        foreach (var c in new[] { first, second })
        {
            c.SetOwner(alice); c.SetController(alice);
            alice.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        }
        second.Tap(); // tapped (attacking) — the trigger should untap it.

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the targeted pump+untap trigger is bound in prod").Subject;
        trigger.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("another target attacking creature");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { second });
        CollectAndExecute(trigger, Ctx(facade), agent);

        facade.ContinuousEffects.Compute(second).Power.Should().Be(4,
            "the AGENT-CHOSEN creature gets +2/+0");
        second.IsTapped.Should().BeFalse("the chosen creature is untapped");
        facade.ContinuousEffects.Compute(first).Power.Should().Be(2,
            "the non-chosen creature is untouched (not first-eligible)");
    }

    private const string RestlessVinestalkOracle =
        "This land enters tapped.\n" +
        "{T}: Add {G} or {U}.\n" +
        "{3}{G}{U}: Until end of turn, this land becomes a 5/5 green and blue " +
        "Plant creature with trample. It's still a land.\n" +
        "Whenever this land attacks, up to one other target creature has base " +
        "power and toughness 3/3 until end of turn.";

    [Fact]
    public void Prod_RestlessVinestalk_AttackTrigger_SetsBasePTOnAgentChosenCreature()
    {
        // "up to one other target creature has base power and toughness 3/3
        // until end of turn." — 0..1 target over OTHER creatures. Binds in
        // prod; resolution set-bases the AGENT'S pick to 3/3 (CR 613.7b).
        var repo = new FakeCardRepo();
        repo.Add("Restless Vinestalk", "Land", oracleText: RestlessVinestalkOracle, colors: "G,U");
        var land = new Land("Restless Vinestalk", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var hill = new Creature("Hill Giant", "{3}{G}", 3, 3, null, new[] { CardSubtype.Giant });
        var grizzly = new Creature("Grizzly", "{1}{G}", 7, 7, null, new[] { CardSubtype.Bear });
        foreach (var c in new[] { hill, grizzly })
        {
            c.SetOwner(alice); c.SetController(alice);
            alice.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        }

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the up-to-one set-base-P/T trigger is bound in prod").Subject;
        trigger.TargetRequests.Should().ContainSingle()
            .Which.MinTargets.Should().Be(0, "it is \"up to one\"");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { grizzly });
        CollectAndExecute(trigger, Ctx(facade), agent);

        var gc = facade.ContinuousEffects.Compute(grizzly)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        gc.Power.Should().Be(3, "the AGENT-CHOSEN creature's base P/T becomes 3/3");
        gc.Toughness.Should().Be(3);
        facade.ContinuousEffects.Compute(hill).Power.Should().Be(3,
            "the non-chosen creature keeps its printed 3/3 (untouched)");
    }

    private const string RestlessCottageOracle =
        "This land enters tapped.\n" +
        "{T}: Add {B} or {G}.\n" +
        "{2}{B}{G}: This land becomes a 4/4 black and green Horror creature " +
        "until end of turn. It's still a land.\n" +
        "Whenever this land attacks, create a Food token and exile up to one " +
        "target card from a graveyard.";

    [Fact]
    public void Prod_RestlessCottage_AttackTrigger_FoodPlusExilesAgentChosenGraveyardCard()
    {
        // "create a Food token, then exile up to one target card from a
        // graveyard." — Food is unconditional; the exile is the 0..1 target.
        // Binds in prod; resolution mints Food + exiles the AGENT'S pick.
        var repo = new FakeCardRepo();
        repo.Add("Restless Cottage", "Land", oracleText: RestlessCottageOracle, colors: "B,G");
        var land = new Land("Restless Cottage", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        var bob = facade.Bob;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Two cards in Bob's graveyard — the agent picks the second to exile.
        var gy1 = new Creature("Goblin", "{R}", 1, 1, null, new[] { CardSubtype.Goblin });
        var gy2 = new Creature("Wizard", "{U}", 1, 1, null, new[] { CardSubtype.Wizard });
        foreach (var c in new[] { gy1, gy2 })
        {
            c.SetOwner(bob);
            bob.Zones.Graveyard.AddCard(c); c.SetZone(ZoneType.Graveyard);
        }

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the Food+exile trigger is bound in prod").Subject;
        trigger.TargetRequests.Should().ContainSingle()
            .Which.MinTargets.Should().Be(0, "the exile is \"up to one\"");

        var foodBefore = alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Food");
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { gy2 });
        CollectAndExecute(trigger, Ctx(facade), agent);

        alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Food")
            .Should().Be(foodBefore + 1, "the Food token is created unconditionally");
        gy2.Zone.Should().Be(ZoneType.Exile, "the AGENT-CHOSEN graveyard card is exiled");
        gy1.Zone.Should().Be(ZoneType.Graveyard, "the non-chosen card stays in the graveyard");
    }

    private const string RestlessFortressOracle =
        "This land enters tapped.\n" +
        "{T}: Add {W} or {B}.\n" +
        "{2}{W}{B}: This land becomes a 1/4 white and black Nightmare creature " +
        "until end of turn. It's still a land.\n" +
        "Whenever this land attacks, defending player loses 2 life and you " +
        "gain 2 life.";

    [Fact]
    public void Prod_RestlessFortress_DefenderDrain_CapturesDefenderOffCreatureAttacksEvent()
    {
        // The headline of the manland-targeted-restless-triggers deferral:
        // Fortress's drain captures the defending player off the live
        // CreatureAttacksEvent (CR 506.2) — non-targeted. Verify the trigger
        // binds through the prod binder chain (NOT the [CardName] factory,
        // which is land-dead) and, once its condition observes the event,
        // drains the captured defender and the controller gains.
        var repo = new FakeCardRepo();
        repo.Add("Restless Fortress", "Land", oracleText: RestlessFortressOracle, colors: "W,B");
        var land = new Land("Restless Fortress", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        var bob = facade.Bob;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the defender-drain trigger is bound in prod").Subject;
        trigger.TargetRequests.Should().BeEmpty(
            "the drain is non-targeted — the defender is captured off the event");

        var aliceBefore = alice.LifeTotal;
        var bobBefore = bob.LifeTotal;

        // CR 506.2 — the binder's EventTriggerCondition captures the defending
        // player off the live CreatureAttacksEvent as a SIDE EFFECT when the
        // condition runs (same posture as RestlessFortressFactory's unit test).
        // CreatureAttacksEvent.Attacker is typed Creature, so the unit-level
        // event carries a dummy Creature in the attacker slot; the capture of
        // the defender (Bob) is what resolution reads. (Live combat firing the
        // event with the animated land itself as attacker rides the broader
        // "a Land instance is never a Creature" combat-integration gap shared
        // by EVERY Restless attack trigger — out of scope here; the BINDING +
        // defender capture + drain are what this prod-path test asserts.)
        var dummyAttacker = new Creature("dummy", "{0}", 1, 1);
        var ev = new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(
            attacker: dummyAttacker, defendingPlayerOrPlaneswalker: bob);
        trigger.Condition.Matches(ev, trigger);

        foreach (var e in trigger.Effects) e.Execute();

        bob.LifeTotal.Should().Be(bobBefore - 2,
            "the captured defending player loses 2 life");
        alice.LifeTotal.Should().Be(aliceBefore + 2, "you gain 2 life");
    }

    // -----------------------------------------------------------------------
    // GRANTED QUOTED ABILITIES / KEYWORDS ON ANIMATE (close
    // mass-keyword-grant-until-eot). The animate line carries a granted
    // quoted attack trigger ("with \"Whenever this creature attacks, …\"") or
    // a parameterized keyword (ward {N}) or a conditional first strike
    // ("with \"During your turn, this creature has first strike.\""). Before
    // this slice these were dropped by ManlandBinder; now they bind on animate.
    // -----------------------------------------------------------------------

    private const string DenOfTheBugbearOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {R}.\n" +
        "{3}{R}: Until end of turn, this land becomes a 3/2 red Goblin creature " +
        "with \"Whenever this creature attacks, create a 1/1 red Goblin creature " +
        "token that's tapped and attacking.\" It's still a land.";

    [Fact]
    public void Prod_DenOfTheBugbear_Animate_GrantsAttackTokenTrigger()
    {
        // The quoted "Whenever this creature attacks, create a 1/1 red Goblin
        // token" granted ability binds on animate (CR 508.1f). Before animating
        // the land has NO attack trigger; after animating the granted trigger
        // exists and, when fired, mints exactly one Goblin token.
        var repo = new FakeCardRepo();
        repo.Add("Den of the Bugbear", "Land", oracleText: DenOfTheBugbearOracle, colors: "R");
        var land = new Land("Den of the Bugbear", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // No granted attack trigger before animating.
        live.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the quoted attack trigger is granted only on animate, not at bind time");

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        // 3/2 Goblin.
        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(2);
        cc.Subtypes.Should().Contain(CardSubtype.Goblin);

        // The granted attack trigger now exists; firing it mints one Goblin.
        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the granted attack trigger is added on animate").Subject;
        var goblinsBefore = alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Goblin");
        foreach (var e in trigger.Effects) e.Execute();
        alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Goblin")
            .Should().Be(goblinsBefore + 1, "the granted attack trigger mints one 1/1 Goblin token");
    }

    private const string RagingRavineOracle =
        "This land enters tapped.\n" +
        "{T}: Add {R} or {G}.\n" +
        "{2}{R}{G}: Until end of turn, this land becomes a 3/3 red and green " +
        "Elemental creature with \"Whenever this creature attacks, put a +1/+1 " +
        "counter on it.\" It's still a land.";

    [Fact]
    public void Prod_RagingRavine_Animate_GrantsAttackCounterTrigger()
    {
        // The quoted "Whenever this creature attacks, put a +1/+1 counter on it"
        // granted ability binds on animate; firing it adds a counter to the land.
        var repo = new FakeCardRepo();
        repo.Add("Raging Ravine", "Land", oracleText: RagingRavineOracle, colors: "R,G");
        var land = new Land("Raging Ravine", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the granted +1/+1-on-attack trigger is added on animate").Subject;
        trigger.TargetRequests.Should().BeEmpty("the counter goes on the land itself (non-targeted)");

        var before = land.Counters.Count(CounterType.PlusOnePlusOne);
        foreach (var e in trigger.Effects) e.Execute();
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(before + 1,
            "the granted attack trigger puts a +1/+1 counter on the animated land");
    }

    private const string HiveOfTheEyeTyrantOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {B}.\n" +
        "{3}{B}: Until end of turn, this land becomes a 3/3 black Beholder creature " +
        "with menace and \"Whenever this creature attacks, exile target card from " +
        "defending player's graveyard.\" It's still a land.";

    [Fact]
    public void Prod_HiveOfTheEyeTyrant_Animate_GrantsMenaceAndAttackExileTrigger()
    {
        // The simple keyword (menace) AND the quoted "Whenever this creature
        // attacks, exile target card from defending player's graveyard" granted
        // ability both bind on animate. The trigger carries a 1..1 TargetRequest.
        var repo = new FakeCardRepo();
        repo.Add("Hive of the Eye Tyrant", "Land", oracleText: HiveOfTheEyeTyrantOracle, colors: "B");
        var land = new Land("Hive of the Eye Tyrant", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        var bob = facade.Bob;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Keywords.Should().Contain("Menace", "the printed simple keyword binds alongside the quoted rider");
        cc.Subtypes.Should().Contain(CardSubtype.Beholder);

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the granted exile-graveyard attack trigger is added on animate").Subject;
        trigger.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Contain("graveyard");

        // A card in Bob's graveyard; the agent picks it; resolution exiles it.
        var gyCard = new Creature("Goblin", "{R}", 1, 1, null, new[] { CardSubtype.Goblin });
        gyCard.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(gyCard); gyCard.SetZone(ZoneType.Graveyard);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { gyCard });
        CollectAndExecute(trigger, Ctx(facade), agent);

        gyCard.Zone.Should().Be(ZoneType.Exile, "the agent-chosen graveyard card is exiled");
    }

    private const string HallOfStormGiantsOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {U}.\n" +
        "{5}{U}: Until end of turn, this land becomes a 7/7 blue Giant creature " +
        "with ward {3}. It's still a land.";

    [Fact]
    public void Prod_HallOfStormGiants_Animate_GrantsWardKeyword()
    {
        // "ward {3}" is a parameterized keyword the simple-keyword path dropped.
        // It now binds as a Ward keyword marker on animate (CR 702.21).
        var repo = new FakeCardRepo();
        repo.Add("Hall of Storm Giants", "Land", oracleText: HallOfStormGiantsOracle, colors: "U");
        var land = new Land("Hall of Storm Giants", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(7);
        cc.Toughness.Should().Be(7);
        cc.Subtypes.Should().Contain(CardSubtype.Giant);
        cc.Keywords.Should().Contain("Ward", "ward {3} binds as a Ward keyword marker on animate");
    }

    private const string RestlessSpireOracle =
        "This land enters tapped.\n" +
        "{T}: Add {U} or {R}.\n" +
        "{U}{R}: Until end of turn, this land becomes a 2/1 blue and red Elemental " +
        "creature with \"During your turn, this creature has first strike.\" It's " +
        "still a land.\n" +
        "Whenever this land attacks, scry 1.";

    [Fact]
    public void Prod_RestlessSpire_Animate_GrantsFirstStrike()
    {
        // The quoted conditional "During your turn, this creature has first
        // strike" binds as a flat First Strike grant on animate (v1 posture —
        // observationally equivalent: the body only exists during the
        // controller's turn). The printed scry attack trigger also binds.
        var repo = new FakeCardRepo();
        repo.Add("Restless Spire", "Land", oracleText: RestlessSpireOracle, colors: "U,R");
        var land = new Land("Restless Spire", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(2);
        cc.Toughness.Should().Be(1);
        cc.Keywords.Should().Contain("First Strike",
            "the quoted \"During your turn, this creature has first strike\" binds flatly on animate");

        // The intrinsic printed "Whenever this land attacks, scry 1" trigger
        // also binds (non-targeted).
        live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the printed scry attack trigger binds")
            .Which.TargetRequests.Should().BeEmpty();
    }

    private const string CaveOfTheFrostDragonOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {W}.\n" +
        "{4}{W}: This land becomes a 3/4 white Dragon creature with flying until " +
        "end of turn. It's still a land.";

    [Fact]
    public void Prod_CaveOfTheFrostDragon_Animate_GrantsFlying()
    {
        // Simple keyword (flying) — already covered, but verified end-to-end so
        // the card is closed alongside its cycle siblings.
        var repo = new FakeCardRepo();
        repo.Add("Cave of the Frost Dragon", "Land", oracleText: CaveOfTheFrostDragonOracle, colors: "W");
        var land = new Land("Cave of the Frost Dragon", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(4);
        cc.Subtypes.Should().Contain(CardSubtype.Dragon);
        cc.Keywords.Should().Contain("Flying");
    }

    // -----------------------------------------------------------------------
    // EXOTIC ANIMATE BODIES — "all creature types" + X/X (close
    // manland-exotic-animate-shapes). Before this slice ManlandBinder deferred
    // these (no fixed subtype / no digit P/T) so Mutavault / Faceless Haven /
    // Soulstone Sanctuary / Lair of the Hydra were land-dead in real games.
    // -----------------------------------------------------------------------

    private const string MutavaultOracle =
        "{T}: Add {C}.\n" +
        "{1}: This land becomes a 2/2 creature with all creature types until " +
        "end of turn. It's still a land.";

    [Fact]
    public void Prod_Mutavault_Animate_BecomesEveryCreatureType_2_2()
    {
        // "all creature types" (CR 205.3m) — no fixed subtype. Binds via the
        // prod binder chain; the animated body gets every creature subtype the
        // engine models + base 2/2, still a land.
        var repo = new FakeCardRepo();
        repo.Add("Mutavault", "Land", oracleText: MutavaultOracle);
        var land = new Land("Mutavault", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Types.Should().Contain(CardType.Land, "It's still a land");
        cc.Types.Should().Contain(CardType.Creature);
        cc.Power.Should().Be(2);
        cc.Toughness.Should().Be(2);
        // A representative sample of "every creature type".
        cc.Subtypes.Should().Contain(CardSubtype.Goblin);
        cc.Subtypes.Should().Contain(CardSubtype.Elf);
        cc.Subtypes.Should().Contain(CardSubtype.Dragon);
    }

    private const string FacelessHavenOracle =
        "{T}: Add {C}.\n" +
        "{S}{S}{S}: This land becomes a 4/3 creature with vigilance and all " +
        "creature types until end of turn. It's still a land. ({S} can be paid " +
        "with one mana from a snow source.)";

    [Fact]
    public void Prod_FacelessHaven_Animate_EveryCreatureType_Vigilance_4_3()
    {
        // "with vigilance and all creature types" — the simple keyword binds
        // alongside the every-creature-type grant.
        var repo = new FakeCardRepo();
        repo.Add("Faceless Haven", "Snow Land", oracleText: FacelessHavenOracle);
        var land = new Land("Faceless Haven", new[] { CardSupertype.Snow }, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(4);
        cc.Toughness.Should().Be(3);
        cc.Keywords.Should().Contain("Vigilance");
        cc.Subtypes.Should().Contain(CardSubtype.Zombie);
    }

    private const string SoulstoneSanctuaryOracle =
        "{T}: Add {C}.\n" +
        "{4}: This land becomes a 3/3 creature with vigilance and all creature " +
        "types. It's still a land.";

    [Fact]
    public void Prod_SoulstoneSanctuary_Animate_EveryCreatureType_Vigilance_3_3()
    {
        // No "until end of turn" — the animation is permanent (CR 613.1c). The
        // body still gets every creature type + vigilance + base 3/3.
        var repo = new FakeCardRepo();
        repo.Add("Soulstone Sanctuary", "Land", oracleText: SoulstoneSanctuaryOracle);
        var land = new Land("Soulstone Sanctuary", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(3);
        cc.Keywords.Should().Contain("Vigilance");
        cc.Subtypes.Should().Contain(CardSubtype.Human);

        // No "until end of turn" → the animation does NOT expire at cleanup.
        facade.ContinuousEffects.ExpireEndOfTurn();
        facade.ContinuousEffects.Compute((Permanent)land).Types
            .Should().Contain(CardType.Creature,
                "Soulstone Sanctuary's animate has no 'until end of turn' — it is permanent");
    }

    private const string LairOfTheHydraOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {G}.\n" +
        "{X}{G}: Until end of turn, this land becomes an X/X green Hydra " +
        "creature. It's still a land. X can't be 0.";

    [Fact]
    public async Task Prod_LairOfTheHydra_Animate_XX_GreenHydra_Binds()
    {
        // X/X body (CR 613.7b reads the X paid). Binds via the prod binder
        // chain with a variable-X animate ability; the X paid sets the base
        // power/toughness of the green Hydra. Still a land.
        var repo = new FakeCardRepo();
        repo.Add("Lair of the Hydra", "Land", oracleText: LairOfTheHydraOracle, colors: "G");
        var land = new Land("Lair of the Hydra", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle("the {X}{G} animate ability binds in prod").Subject;

        // GAP 2 — the X paid is threaded via ResolutionContext.ChosenX. Drive
        // the effect with X = 5 (the same path AbilityActivator uses).
        var rctx = ResolutionContext.For(alice, agent: null, game: null, chosenTargets: null, chosenX: 5);
        foreach (var e in animate.Effects)
            await e.ExecuteAsync(rctx);

        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Types.Should().Contain(CardType.Land, "It's still a land");
        cc.Subtypes.Should().Contain(CardSubtype.Hydra);
        cc.Power.Should().Be(5, "X/X body reads the X paid (X=5)");
        cc.Toughness.Should().Be(5);
        cc.Colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Green);
    }

    [Fact]
    public void Prod_RestlessReef_AttackTrigger_NoAgent_IsCleanNoOp()
    {
        // No agent registered → CollectAsync resolves the request to an empty
        // pick → the mill no-ops (CR 608.2b). Faithful to the manland posture.
        var repo = new FakeCardRepo();
        repo.Add("Restless Reef", "Land", oracleText: RestlessReefOracle, colors: "U,B");
        var land = new Land("Restless Reef", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        var bob = facade.Bob;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        var bobGyBefore = bob.Zones.Graveyard.GetCards().Count();

        var trigger = live.Abilities.OfType<TriggeredAbility>().Single();
        CollectAndExecute(trigger, Ctx(facade), agent: null);

        bob.Zones.Graveyard.GetCards().Count().Should().Be(bobGyBefore,
            "with no agent the targeted mill is a clean no-op");
    }
}
