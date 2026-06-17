using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Prod-path (GameFacade.Create) reproduction for the live Badgermole Cub bugs:
///   1. The Earthbend-1 ETB offered ILLEGAL targets (enemy lands + creatures).
///   2. The chosen land got the +1/+1 counter but was NOT animated into a
///      creature.
///
/// These build the card through the full routed prod path
/// (GameFacade.BuildDeckCard -> NamedCardFactory.Create -> OverlayAdditiveBinders)
/// rather than the [CardName] factory alone, which is where the factory-direct
/// unit tests (BadgermoleCubTests) pass but live play failed.
/// </summary>
public class BadgermoleCubProdPathTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private static GameFacade BuildGame()
    {
        // Alice's library: one Badgermole Cub + a couple of Forests so she has a
        // land she controls to earthbend, plus a Dryad Arbor (a Land Creature —
        // an Earthbend-legal "land you control"). Bob gets Forests too (the
        // opponent's lands must NOT be offerable).
        var aliceShells = new List<ICard>
        {
            MakeShell("Badgermole Cub"),
            MakeShell("Forest"),
            MakeShell("Forest"),
            MakeShell("Dryad Arbor"),
        };
        var bobShells = new List<ICard>
        {
            MakeShell("Forest"),
            MakeShell("Forest"),
        };
        return GameFacade.Create("Alice", "Bob", aliceShells, bobShells, cardRepo: Repo);
    }

    // Build the shell exactly as production does (DeckCardShellBuilder picks the
    // primary type and preserves ALL printed types) — so a Land Creature like
    // Dryad Arbor becomes a Creature C# instance carrying the Land type, the
    // shape that exposed Bug A.
    private static ICard MakeShell(string name)
    {
        var e = Repo.GetByName(name)!;
        return Majik.Core.CardData.DeckCardShellBuilder.Build(e);
    }

    private static ICard BuildCubThroughProd(GameFacade facade)
    {
        return facade.Alice.Zones.GetZone(ZoneType.Library).GetCards()
            .First(c => c.Name == "Badgermole Cub");
    }

    [Fact]
    public void ProdCub_HasExactlyOneEarthbendEtbTrigger()
    {
        var facade = BuildGame();
        var cub = BuildCubThroughProd(facade);

        var targeted = cub.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.TargetRequests.Count > 0)
            .ToList();

        targeted.Should().HaveCount(1,
            "the [CardName] factory provides exactly one Earthbend ETB trigger; "
            + "the additive binder must NOT synthesize a duplicate targeted trigger");
    }

    [Fact]
    public void ProdCub_EtbTargetPool_OnlyControllersLands()
    {
        var facade = BuildGame();
        var alice = facade.Alice;
        var bob = facade.Bob;

        // Put one of Alice's Forests and one of Bob's Forests on the battlefield,
        // plus a creature on each side — none of the opponent's permanents, and
        // no creature, may appear as an Earthbend target.
        var aliceForest = MoveToBattlefield(alice, alice, "Forest");
        var bobForest = MoveToBattlefield(bob, bob, "Forest");

        var cub = BuildCubThroughProd(facade);
        var etb = cub.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        var req = etb.TargetRequests[0];

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var candidates = req.ResolveCandidates(ctx);

        candidates.Should().Contain(aliceForest, "you may earthbend a land you control");
        candidates.Should().NotContain(bobForest,
            "Earthbend targets 'a land YOU control' — opponent lands are illegal (CR 115.4)");
        candidates.Should().OnlyContain(c => c is Land,
            "Earthbend targets a land — creatures are never legal targets");
        candidates.Cast<object>().Should().OnlyContain(
            c => ReferenceEquals(((ICard)c).Controller, alice),
            "every offered candidate must be controlled by the cub's controller");
    }

    // -------------------------------------------------------------------
    // Bug A — Dryad Arbor is a Land Creature, built through the prod path
    // as a `Creature` C# instance (its first printed type is Creature). The
    // Earthbend gatherer's `OfType<Land>()` filters on the C# class, so it
    // silently excludes Dryad Arbor even though it IS a "land you control"
    // (its computed/printed types include CardType.Land). Earthbend targets
    // "target land you control" = any permanent whose types include Land.
    // -------------------------------------------------------------------
    [Fact]
    public void ProdCub_EtbTargetPool_OffersDryadArbor_ALandCreature()
    {
        var facade = BuildGame();
        var alice = facade.Alice;
        var bob = facade.Bob;

        var dryad = MoveToBattlefieldAny(alice, alice, "Dryad Arbor");
        // Dryad Arbor is built as a Creature instance in prod, but is a land.
        dryad.Should().BeAssignableTo<Creature>(
            "Dryad Arbor's first printed type is Creature, so the prod shell builder makes it a Creature instance");
        dryad.HasType(CardType.Land).Should().BeTrue("Dryad Arbor is a Land Creature");

        var cub = BuildCubThroughProd(facade);
        var etb = cub.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        var req = etb.TargetRequests[0];

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var candidates = req.ResolveCandidates(ctx);

        candidates.Should().Contain((object)dryad,
            "Dryad Arbor is a land Alice controls — a legal Earthbend target (CR 701.59), "
            + "even though it's built as a Creature C# instance");
    }

    // -------------------------------------------------------------------
    // Bug C (good-to-have) — earthbending Dryad Arbor itself: the counter +
    // animate apply to a land-creature. It is already a creature, so it stays
    // a creature, gains the +1/+1 counter, and gains haste.
    // -------------------------------------------------------------------
    [Fact]
    public void ProdCub_EtbResolution_EarthbendsDryadArbor_CounterAndAnimateApply()
    {
        var facade = BuildGame();
        var alice = facade.Alice;
        var dryad = MoveToBattlefieldAny(alice, alice, "Dryad Arbor");

        var cub = BuildCubThroughProd(facade);
        ((Card)cub).SetController(alice);
        ((Card)cub).SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(cub);

        var etb = cub.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { dryad } });
        foreach (var effect in etb.Effects) effect.Execute();

        dryad.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Earthbend 1 puts a +1/+1 counter on the land creature");

        var svc = dryad.ActiveEffects;
        svc.Should().NotBeNull("the prod build path wires the land-creature's CES");
        var chars = svc!.Compute(dryad);
        chars.Types.Should().Contain(CardType.Creature, "Dryad Arbor is already a creature");
        chars.Types.Should().Contain(CardType.Land, "still a land");
        chars.Keywords.Should().Contain("Haste", "Earthbend grants haste");
    }

    [Fact]
    public void ProdCub_EtbResolution_AnimatesChosenLandIntoCreature()
    {
        var facade = BuildGame();
        var alice = facade.Alice;
        var aliceForest = MoveToBattlefield(alice, alice, "Forest");

        var cub = BuildCubThroughProd(facade);
        // Mirror the live battlefield: the cub itself is on the battlefield.
        ((Card)cub).SetController(alice);
        ((Card)cub).SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(cub);

        var etb = cub.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aliceForest } });
        foreach (var effect in etb.Effects) effect.Execute();

        aliceForest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Earthbend 1 puts a +1/+1 counter on the land");

        // The live CES is what GameFacade wired; read the computed characteristics.
        var svc = aliceForest.ActiveEffects;
        svc.Should().NotBeNull("the prod build path wires the land's CES");
        var chars = svc!.Compute(aliceForest);
        chars.Types.Should().Contain(CardType.Creature,
            "Earthbend animates the land into a 0/0 creature that's still a land");
        chars.Types.Should().Contain(CardType.Land, "still a land");
        chars.Keywords.Should().Contain("Haste", "Earthbend grants haste");

        // It must be a 1/1 (0/0 base + one +1/+1 counter) and surface as a
        // creature to the engine — the symptom was the land got the counter but
        // stayed a non-creature land that could not attack.
        chars.Should().BeOfType<CreatureCharacteristics>(
            "the animated land surfaces on the creature row through Compute");
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(1, "0/0 base + one +1/+1 counter = 1/1");
        cc.Toughness.Should().Be(1);
        aliceForest.IsEffectivelyCreature().Should().BeTrue(
            "the engine treats the earthbent land as a creature (eligible to attack)");
        aliceForest.GetEffectivePower().Should().Be(1,
            "the animated land carries a combat body the engine can read");
    }

    // ---------------------------------------------------------------------
    // CR 608.2b — agent-boundary legality recheck. The remote (human) target
    // prompt must DROP a picked target that is not in the engine-offered legal
    // pool. This is the systemic fix: before it, RemoteAgent.ChooseTargetsAsync
    // shipped no candidate list and accepted any instance id the portal sent.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RemoteAgent_DropsIllegalTarget_NotInOfferedPool()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var myLand = new Land("Forest") { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var oppLand = new Land("Forest") { Owner = bob, Controller = bob, Zone = ZoneType.Battlefield };
        alice.Zones.Battlefield.AddCard(myLand);
        bob.Zones.Battlefield.AddCard(oppLand);

        var lookup = new Dictionary<Guid, ICard> { [myLand.InstanceId] = myLand, [oppLand.InstanceId] = oppLand };
        var agent = new RemoteAgent(alice, id => lookup.GetValueOrDefault(id));

        // "target land you control" — only Alice's land is legal.
        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target land you control",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { myLand },
            Intent: Majik.Core.Cards.BotIntent.None);

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);

        // The client tries to smuggle in the OPPONENT'S land (illegal).
        agent.Submit(new Majik.Core.Api.Commands.ChooseTargetsCommand(
            new[] { oppLand.InstanceId }) { PlayerId = alice.Id });

        var chosen = await task;
        chosen.Should().BeEmpty(
            "an opponent's land is not in the offered 'target land you control' pool — dropped (CR 608.2b)");
    }

    [Fact]
    public async Task RemoteAgent_KeepsLegalTarget_InOfferedPool()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var myLand = new Land("Forest") { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        alice.Zones.Battlefield.AddCard(myLand);

        var lookup = new Dictionary<Guid, ICard> { [myLand.InstanceId] = myLand };
        var agent = new RemoteAgent(alice, id => lookup.GetValueOrDefault(id));

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target land you control",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { myLand },
            Intent: Majik.Core.Cards.BotIntent.None);

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);
        agent.Submit(new Majik.Core.Api.Commands.ChooseTargetsCommand(
            new[] { myLand.InstanceId }) { PlayerId = alice.Id });

        var chosen = await task;
        chosen.Should().ContainSingle().Which.Should().BeSameAs(myLand,
            "a legal pick in the offered pool is preserved");
    }

    [Fact]
    public async Task RemoteAgent_ShipsCandidatePayload_ForRestrictedTargetPrompt()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var myLand = new Land("Forest") { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        alice.Zones.Battlefield.AddCard(myLand);

        var agent = new RemoteAgent(alice, _ => myLand);
        PromptPayload? payloadAtPromptTime = null;
        agent.PromptRequested += _ => payloadAtPromptTime = agent.PendingPayload;

        var req = new Majik.Core.Players.Agents.TargetRequest(
            Description: "target land you control",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { myLand },
            Intent: Majik.Core.Cards.BotIntent.None);

        var stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
        var ctx = new Majik.Core.Game.GameContext(
            alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var task = agent.ChooseTargetsAsync(ctx, req);

        payloadAtPromptTime.Should().NotBeNull(
            "a restricted target prompt ships the legal candidate list to the portal");
        payloadAtPromptTime!.Candidates.Should().ContainSingle()
            .Which.InstanceId.Should().Be(myLand.InstanceId);

        agent.Submit(new Majik.Core.Api.Commands.ChooseTargetsCommand(
            new[] { myLand.InstanceId }) { PlayerId = alice.Id });
        await task;
    }

    private static Land MoveToBattlefield(Player owner, Player controller, string name)
    {
        var lib = owner.Zones.GetZone(ZoneType.Library);
        var land = (Land)lib.GetCards().First(c => c.Name == name);
        lib.RemoveCard(land);
        land.SetController(controller);
        land.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(land);
        return land;
    }

    /// <summary>
    /// Move a prod-built permanent (which may be a Creature C# instance even
    /// though it is a land — Dryad Arbor) from the library to the battlefield.
    /// </summary>
    private static Permanent MoveToBattlefieldAny(Player owner, Player controller, string name)
    {
        var lib = owner.Zones.GetZone(ZoneType.Library);
        var perm = (Permanent)lib.GetCards().First(c => c.Name == name);
        lib.RemoveCard(perm);
        perm.SetController(controller);
        perm.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(perm);
        return perm;
    }
}
