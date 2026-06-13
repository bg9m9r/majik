using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LeylineBindingFactory"/> — Enchantment {5}{W}.
///
///   "Flash
///    Domain — This spell costs {1} less to cast for each basic land type
///    among lands you control.
///    When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield."
///
/// Leyline Binding is the "Oblivion Ring" exile-until-leaves template
/// (CR 701.21) on a Flash (CR 702.8) body with a Domain (CR 702.16 /
/// CR 117.7) cost reducer — the same backbone as
/// <see cref="CastOutFactory"/>.
///
/// Covers:
/// - Card identity (Enchantment, {5}{W}; NOT an Aura).
/// - NamedCardFactory dispatch.
/// - Flash keyword marker present (CR 702.8).
/// - Domain cost reduction (CR 702.16 / CR 117.7): 0/3/5 basic types,
///   floor preserving the single coloured W pip (CR 117.7c).
/// - ETB exile + LTB return O-Ring pair:
///     * ETB exiles a target nonland permanent an opponent controls.
///     * ETB rejects lands (CR 608.2b) + controller-side permanents.
///     * LTB returns the exiled card under its owner's control (CR 110.2).
/// </summary>
public class LeylineBindingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void AddBasic(Player owner, CardSubtype subtype, string name)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_IsEnchantment_WithFiveGenericOneWhite()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        lb.Name.Should().Be("Leyline Binding");
        lb.HasType(CardType.Enchantment).Should().BeTrue();
        lb.IsAura.Should().BeFalse("the current Leyline Binding is a plain Enchantment, not an Aura");
        lb.ManaCost.Should().Be("{5}{W}");
        lb.Owner.Should().BeSameAs(_alice);
        lb.Controller.Should().BeSameAs(_alice);
        lb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LeylineBinding()
    {
        var lb = NamedCardFactory.Create("Leyline Binding", _alice);

        lb.Should().BeOfType<Enchantment>();
        lb.Name.Should().Be("Leyline Binding");
        lb.ManaCost.Should().Be("{5}{W}");
    }

    [Fact]
    public void LeylineBinding_HasFlashKeyword()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        lb.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Leyline Binding has Flash (CR 702.8)");
    }

    // -----------------------------------------------------------------------
    // Domain cost reduction (CR 702.16 / CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_NoBasicTypes_PaysFullFiveGenericOneWhite()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(5, "no basic land types → no Domain reduction");
        effective.White.Should().Be(1, "the single coloured W pip is untouched (CR 117.7c)");
    }

    [Fact]
    public void LeylineBinding_ThreeBasicTypes_ReducesGenericByThree()
    {
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(2, "{5} generic − {3} for three basic land types");
        effective.White.Should().Be(1, "coloured pips never reduce (CR 117.7c)");
    }

    [Fact]
    public void LeylineBinding_AllFiveBasicTypes_CollapsesToSingleWhite()
    {
        // The canonical "Leyline Binding turn-2 for {W}" case.
        var lb = LeylineBindingFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Swamp, "Swamp");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        var effective = CostReduction.GetEffectiveCost(lb, _alice);

        effective.Generic.Should().Be(0,
            "{5} generic − {5} for all five basic land types floors at zero");
        effective.White.Should().Be(1,
            "CR 117.7c — Domain only reduces generic mana; the W pip remains");
    }

    [Fact]
    public void LeylineBinding_DomainReducer_IsExactlyOnePerBasicType()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        var reducer = lb.Abilities.OfType<CostReductionAbility>().Single();
        reducer.TotalReducer.Should().NotBeNull("Domain uses the whole-reducer shape");

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        reducer.TotalReducer!(_alice).Should().Be(3,
            "Domain returns 1 × number of distinct basic land types (CR 702.16)");
    }

    // -----------------------------------------------------------------------
    // O-Ring exile-until-leaves (CR 701.21 / 603.6 / 610.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void LeylineBinding_Etb_ExilesOpponentPermanent()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        etb.Resolve();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void LeylineBinding_Etb_RejectsLandTarget()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        etb.Resolve();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter (CR 608.2b)");
    }

    [Fact]
    public void LeylineBinding_Etb_RejectsControllerOwnPermanent()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        etb.Resolve();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents ('an opponent controls', CR 109.5)");
    }

    [Fact]
    public void LeylineBinding_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        etb.Resolve();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        ltb.Resolve();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void LeylineBinding_Ltb_NoOpWhenNothingExiled()
    {
        var lb = LeylineBindingFactory.Create(_alice);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        var ltb = lb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        ltb.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Live prod-path: ETB exile resolves through the bus + TriggerManager +
    // the async targeted-trigger drain that PROMPTS the agent (CR 603.3).
    //
    // The earlier tests poke the ETB ability directly (SetChosenTargets +
    // Resolve). This case mirrors GriefTests' end-to-end posture instead: it
    // registers Leyline Binding's abilities with a live TriggerManager, fires
    // the ETB via a real CardMovedEvent (enter battlefield), then drains the
    // pending trigger through PutPendingTriggersOnStackAsync so the engine's
    // shared targeting pipeline (TargetCollection.CollectAsync) GATHERS the
    // legal candidates and prompts the agent — the exact path the live
    // GameFacade build uses for an Enchantment ETB trigger.
    //
    // Without a candidate gatherer on the exile_until_leaves verb the agent is
    // offered ZERO targets and the ETB silently fizzles in prod — the gap this
    // covers.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LeylineBinding_Etb_ResolvesThroughLiveAgentPrompt_ExilesOpponentPermanent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, new ZoneService(bus));

        // Live Leyline Binding on the battlefield, abilities bus-registered.
        var lb = LeylineBindingFactory.Create(_alice, triggers);
        lb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(lb);

        // Bob's nonland permanent (the only legal ETB target — "an opponent
        // controls") plus one of Alice's own creatures that must NOT be offered.
        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        // Fire the ETB through the bus exactly as a real enter would.
        bus.Publish(new CardMovedEvent(lb, ZoneType.Stack, ZoneType.Battlefield));
        triggers.PendingCount.Should().Be(1, "ETB exile trigger fired on enter");

        // Candidate-aware agent: it picks FROM the candidates the engine
        // gathered + offered, so an empty pool (no gatherer) yields no pick.
        var agent = new PickOpponentPermanentAgent(_bob);
        var agents = new Dictionary<Player, IPlayerAgent> { [_alice] = agent };
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, stack);

        await triggers.PutPendingTriggersOnStackAsync(_alice, agents, ctx);

        // The engine offered exactly the opponent's nonland permanent — not
        // Alice's own Bird ("an opponent controls", CR 109.5).
        agent.OfferedCandidates.Should().ContainSingle()
            .Which.Should().BeSameAs(bobsCreature);

        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "the ETB exile resolved end-to-end through the live agent prompt");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "Alice's own permanent was never a legal candidate");
    }

    /// <summary>
    /// Picks the named opponent's permanent out of the candidates the engine's
    /// targeting pipeline GATHERED and offered (captured for assertion), proving
    /// the candidate gatherer surfaced the legal target rather than the agent
    /// reaching past an empty pool.
    /// </summary>
    private sealed class PickOpponentPermanentAgent : DelegatingAgent
    {
        private readonly Player _opponent;
        public IReadOnlyList<object> OfferedCandidates { get; private set; } =
            System.Array.Empty<object>();

        public PickOpponentPermanentAgent(Player opponent) => _opponent = opponent;

        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request,
            System.Threading.CancellationToken ct = default)
        {
            OfferedCandidates = request.LegalCandidates;
            var pick = request.LegalCandidates
                .OfType<Permanent>()
                .FirstOrDefault(p => ReferenceEquals(p.Controller, _opponent));
            IReadOnlyList<object> result = pick is null
                ? System.Array.Empty<object>()
                : new object[] { pick };
            return Task.FromResult(result);
        }
    }
}
