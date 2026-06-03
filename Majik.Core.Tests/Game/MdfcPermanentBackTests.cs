using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 712.3 — modal PERMANENT back face. An MDFC whose BACK is a permanent
/// (artifact / creature / enchantment) can be cast AS that permanent: the
/// chosen back enters the battlefield as that face, no transform (CR 712.4).
/// Flagship: Birgi, God of Storytelling // Harnfel, Horn of Bounty
/// (Legendary Creature 3/3 // Legendary Artifact).
/// </summary>
public class MdfcPermanentBackTests : IDisposable
{
    public MdfcPermanentBackTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private readonly Player _alice = new("Alice", 20);

    private static GameContext Ctx(Player self) =>
        new(self, new[] { self }, self, 1,
            PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack(new EventBus()));

    // ------------------------------------------------------------------
    // Card model — the front carries a castable PERMANENT back face.
    // ------------------------------------------------------------------

    [Fact]
    public void Birgi_Front_IsLegendaryCreatureGod_3_3()
    {
        var birgi = BirgiGodOfStorytellingFactory.Create(_alice);

        birgi.Name.Should().Be("Birgi, God of Storytelling");
        birgi.HasType(CardType.Creature).Should().BeTrue();
        birgi.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        birgi.BasePower.Should().Be(3);
        birgi.BaseToughness.Should().Be(3);
    }

    [Fact]
    public void Birgi_Front_OffersCastablePermanentBack_Harnfel()
    {
        var birgi = BirgiGodOfStorytellingFactory.Create(_alice);

        birgi.MdfcState.Should().NotBeNull();
        birgi.MdfcState!.CanCastEitherFace.Should().BeTrue();
        var back = birgi.MdfcState!.CastableBackFace!;
        back.Should().NotBeNull();
        back.IsLand.Should().BeFalse("Harnfel is a nonland permanent, not a land");
        back.IsPermanent.Should().BeTrue("Harnfel is an artifact permanent back");
        back.Name.Should().Be("Harnfel, Horn of Bounty");
    }

    [Fact]
    public void Harnfel_BackCard_IsLegendaryArtifact()
    {
        var birgi = BirgiGodOfStorytellingFactory.Create(_alice);
        var harnfel = birgi.MdfcState!.CastableBackFace!.BuildCard(_alice);

        harnfel.Name.Should().Be("Harnfel, Horn of Bounty");
        harnfel.HasType(CardType.Artifact).Should().BeTrue();
        harnfel.HasType(CardType.Creature).Should().BeFalse();
    }

    [Fact]
    public void NamedFactory_Dispatches_Birgi_WithCastablePermanentBack()
    {
        var card = NamedCardFactory.Create("Birgi, God of Storytelling", _alice);

        card.Should().BeOfType<Creature>();
        var birgi = (Creature)card;
        birgi.MdfcState!.CastableBackFace!.IsPermanent.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // ResolveFaceAsync — choosing the back returns the permanent face.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveFace_ChoosingBack_ReturnsHarnfelPermanentFace()
    {
        var birgi = BirgiGodOfStorytellingFactory.Create(_alice);
        var agent = new ScriptedAgent();
        agent.QueueChoiceIndex(1); // back

        var chosen = await MdfcCastFlow.ResolveFaceAsync(birgi, _alice, agent, Ctx(_alice));

        chosen.Should().NotBeNull();
        chosen!.IsPermanent.Should().BeTrue();
        chosen.IsLand.Should().BeFalse();
        chosen.Name.Should().Be("Harnfel, Horn of Bounty");
    }

    // ------------------------------------------------------------------
    // Integration — casting the back face enters Harnfel as an artifact.
    // ------------------------------------------------------------------

    [Fact]
    public async Task TurnDriver_CastBirgi_ChoosingBack_EntersHarnfelAsArtifact()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var ces = new ContinuousEffectsService(bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        var birgi = BirgiGodOfStorytellingFactory.Create(alice);
        birgi.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(birgi);

        // Float the back-face cost so the cast is payable.
        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{4}{R}"));

        foreach (var p in players)
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                c.SetZone(ZoneType.Library);
                p.Zones.Library.AddCard(c);
            }

        var inner = new ScriptedAgent();
        inner.QueueChoiceIndex(1); // BACK face (Harnfel)
        var aliceAgent = new MainPhaseCastAgent(inner, birgi, alice);
        AgentRegistry.Set(alice, aliceAgent);

        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 20; i++) bobAgent.QueuePriority(PriorityAction.Pass);
        AgentRegistry.Set(bob, bobAgent);

        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent> { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: ces,
            eventBus: bus);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        var harnfel = alice.Zones.Battlefield.GetCards()
            .SingleOrDefault(c => c.Name == "Harnfel, Horn of Bounty");
        harnfel.Should().NotBeNull("choosing the back face enters Harnfel as the artifact permanent");
        harnfel!.HasType(CardType.Artifact).Should().BeTrue();
        alice.Zones.Battlefield.GetCards().Should().NotContain(c => c.Name == "Birgi, God of Storytelling",
            "the front Birgi face never enters — only the chosen back permanent does");
        ((Permanent)harnfel).ActiveEffects.Should().NotBeNull(
            "DispatchCast wires ActiveEffects onto the permanent back so its body computes");
    }

    // ------------------------------------------------------------------
    // Harnfel's activated ability — "Discard a card: Exile the top two
    // cards of your library. You may play those cards this turn."
    // (CR 602.1 / 118.9 / 514.2) — temporary-play-permission-expiry.
    // ------------------------------------------------------------------

    private static Card LibraryCard(Player owner, string name, string cost = "{1}{R}")
    {
        var c = new Card(name, cost);
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    [Fact]
    public void Harnfel_HasDiscardExileTopTwoActivatedAbility()
    {
        var harnfel = BirgiGodOfStorytellingFactory.BuildHarnfel(_alice);

        harnfel.Abilities.OfType<ActivatedAbility>().Should().ContainSingle()
            .Which.Costs.Should().ContainSingle(c => c is Majik.Core.Costs.DiscardACardCost,
                "the ability's cost is 'Discard a card'");
    }

    [Fact]
    public void Harnfel_Activate_ExilesTopTwo_GrantsPlayThisTurn()
    {
        var harnfel = BirgiGodOfStorytellingFactory.BuildHarnfel(_alice);
        var top1 = LibraryCard(_alice, "Top1", "{R}");
        var top2 = LibraryCard(_alice, "Top2", "{1}{R}");
        var top3 = LibraryCard(_alice, "Top3", "{2}{R}");

        var ability = harnfel.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 });
        _alice.Zones.Exile.GetCards().Should().NotContain(top3);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { top3 });

        // Both exiled cards become legal cast sources from exile for Alice.
        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        new Majik.Core.Costs.ExileCastAlternativeCost("harnfel", top1.RuntimeExileCastCost!)
            .CanCastFor(top1, _alice).Should().BeTrue();
    }

    [Fact]
    public void Harnfel_PlayPermission_ExpiresAtEndOfTurn()
    {
        var bus = new EventBus();
        var harnfel = BirgiGodOfStorytellingFactory.BuildHarnfel(_alice, bus);
        var top1 = LibraryCard(_alice, "Top1", "{R}");
        var top2 = LibraryCard(_alice, "Top2", "{1}{R}");

        var ability = harnfel.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // "This turn" — the FIRST Cleanup the controller owns revokes BOTH
        // grants under one shared subscription (CR 514.2).
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        top1.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the play permission does not linger past 'this turn'");
        top2.RuntimeExileCastAllowedCaster.Should().BeNull();
    }
}
