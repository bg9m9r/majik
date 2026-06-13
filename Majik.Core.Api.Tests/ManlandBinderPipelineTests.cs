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
    // QUOTED ABILITY ON ANIMATE — "becomes a … creature with \"Whenever this
    // creature attacks, …\"". The quoted attack trigger is granted via a
    // GrantAbilityEffect scoped to the animation (ExpiresAtEndOfTurn), reusing
    // the same effect shapes the standalone Restless attack triggers use.
    // Closes the manland-quoted-ability-on-animate deferral.
    // -----------------------------------------------------------------------

    private const string DenOfTheBugbearOracle =
        "If you control two or more other lands, this land enters tapped.\n" +
        "{T}: Add {R}.\n" +
        "{3}{R}: Until end of turn, this land becomes a 3/2 red Goblin creature " +
        "with \"Whenever this creature attacks, create a 1/1 red Goblin creature " +
        "token that's tapped and attacking.\" It's still a land.";

    [Fact]
    public void Prod_DenOfTheBugbear_AnimateGrantsQuotedAttackTrigger_MintsGoblinToken()
    {
        // The quoted "Whenever this creature attacks, create a 1/1 red Goblin
        // token …" rider must bind in prod. Before activation there is NO
        // attack trigger on the land (the quoted ability is granted only while
        // animated). After activating the animate ability, the granted trigger
        // appears; firing it mints exactly one Goblin token.
        var repo = new FakeCardRepo();
        repo.Add("Den of the Bugbear", "Land", oracleText: DenOfTheBugbearOracle, colors: "R");
        var land = new Land("Den of the Bugbear", supertypes: null, subtypes: null);

        var (facade, live) = BuildThroughProd(land, repo);
        var alice = facade.Alice;
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Animate ability binds; the quoted trigger is NOT yet on the card
        // (granted only while animated, CR 613.1f).
        live.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the quoted attack trigger is granted only while animated");
        var animate = live.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        foreach (var e in animate.Effects) e.Execute();

        // Animation upgraded the land to a 3/2 Goblin AND granted the trigger.
        var cc = facade.ContinuousEffects.Compute((Permanent)land)
            .Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(3);
        cc.Toughness.Should().Be(2);
        cc.Subtypes.Should().Contain(CardSubtype.Goblin);

        var trigger = live.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the quoted attack trigger is granted while animated").Subject;

        var goblinsBefore = alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Goblin");
        foreach (var e in trigger.Effects) e.Execute();
        alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Goblin")
            .Should().Be(goblinsBefore + 1,
                "the granted attack trigger mints exactly one 1/1 red Goblin token");

        // CR 514.2 — the grant expires at end of turn with the animation; the
        // trigger is revoked.
        facade.ContinuousEffects.ExpireEndOfTurn();
        live.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the granted trigger is revoked when the animation expires");
    }

    private const string RagingRavineOracle =
        "This land enters tapped.\n" +
        "{T}: Add {R} or {G}.\n" +
        "{2}{R}{G}: Until end of turn, this land becomes a 3/3 red and green " +
        "Elemental creature with \"Whenever this creature attacks, put a +1/+1 " +
        "counter on it.\" It's still a land.";

    [Fact]
    public void Prod_RagingRavine_AnimateGrantsQuotedAttackTrigger_SelfCounter()
    {
        // The quoted "Whenever this creature attacks, put a +1/+1 counter on
        // it." rider grants a self-counter trigger while animated.
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
            "the quoted self-counter attack trigger is granted while animated").Subject;
        trigger.TargetRequests.Should().BeEmpty("\"on it\" is non-targeted self-reference");

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        foreach (var e in trigger.Effects) e.Execute();
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the granted attack trigger puts a +1/+1 counter on the animated land itself");
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
