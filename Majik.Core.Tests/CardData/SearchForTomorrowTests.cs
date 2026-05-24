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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Search for Tomorrow (Time Spiral, {2}{G}, Sorcery).
///
/// "Search your library for a basic land card, put it onto the battlefield,
///  then shuffle your library. Suspend 2—{G}."
///
/// Covers:
///   - Card shape (name, type, mana cost).
///   - NamedCardFactory dispatch.
///   - Resolve puts a basic land onto the battlefield untapped.
///   - Resolve does not pick nonbasic lands (Tron lands must stay in library).
///   - Resolve is a no-op when the library has no basic land.
///   - Suspend cost constants: 2 counters, {G} cost.
///   - Suspend mechanics: paying {G} from hand exiles card with 2 time counters.
///   - Full suspend cycle: two upkeep ticks then auto-cast for free, land enters.
/// </summary>
public class SearchForTomorrowTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    private static Land MakeBasicLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    private static Land MakeNonbasicLand(string name, Player owner)
    {
        var land = new Land(name, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // -------------------------------------------------------------------------
    // Card shape
    // -------------------------------------------------------------------------

    [Fact]
    public void SearchForTomorrow_IsSorcery_AtCost2G()
    {
        var card = SearchForTomorrowFactory.Create(_alice);

        card.Name.Should().Be("Search for Tomorrow");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SearchForTomorrow()
    {
        var card = NamedCardFactory.Create("Search for Tomorrow", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Search for Tomorrow");
        card.ManaCost.Should().Be("{2}{G}");
        card.Owner.Should().Be(_alice);
    }

    // -------------------------------------------------------------------------
    // Resolve effect — basic land → battlefield untapped
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_BasicLand_EntersBattlefieldUntapped()
    {
        // Library holds a Forest (basic). Resolve should move it to
        // the battlefield in the untapped state.
        var caster = new Player("A", 20);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SearchForTomorrowFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().BeEmpty();

        // Land must enter untapped (unlike Primeval Titan / Path to Exile targets).
        var placed = caster.Zones.Battlefield.GetCards().First() as Permanent;
        placed.Should().NotBeNull();
        placed!.IsTapped.Should().BeFalse("Search for Tomorrow puts the land onto the battlefield untapped");
    }

    [Fact]
    public void Resolve_PicksFirstBasicLand_WhenMultiplePresent()
    {
        var caster = new Player("A", 20);
        var mountain = MakeBasicLand("Mountain", caster, CardSubtype.Mountain);
        var island = MakeBasicLand("Island", caster, CardSubtype.Island);
        caster.Zones.Library.AddCard(mountain);
        caster.Zones.Library.AddCard(island);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SearchForTomorrowFactory.BuildSpellDefinition(caster));

        // First match goes to battlefield, second stays in library.
        caster.Zones.Battlefield.GetCards().Should().HaveCount(1);
        caster.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_DoesNotPickNonbasicLand()
    {
        // Urza's Mine (nonbasic) must stay — predicate is "basic land".
        var caster = new Player("A", 20);
        var urzasMine = MakeNonbasicLand("Urza's Mine", caster);
        var forest = MakeBasicLand("Forest", caster, CardSubtype.Forest);
        // Put nonbasic first so a buggy "any land" predicate would pick it.
        caster.Zones.Library.AddCard(urzasMine);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SearchForTomorrowFactory.BuildSpellDefinition(caster));

        // Forest enters battlefield; Urza's Mine stays in library.
        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Urza's Mine");
    }

    [Fact]
    public void Resolve_NoBasicLandInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2);
        grizzly.SetOwner(caster);
        grizzly.SetController(caster);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(SearchForTomorrowFactory.BuildSpellDefinition(caster));

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Suspend constants
    // -------------------------------------------------------------------------

    [Fact]
    public void SuspendCost_IsGreenWithTwoCounters()
    {
        var suspend = SearchForTomorrowFactory.BuildSuspendCost();

        suspend.TimeCounters.Should().Be(2);
        suspend.AlternativeManaCost.Should().Be(ManaCost.Parse("{G}"));
        suspend.Description.Should().Contain("2");
    }

    // -------------------------------------------------------------------------
    // Suspend mechanics
    // -------------------------------------------------------------------------

    [Fact]
    public void Suspend_PayG_ExilesWithTwoTimeCounters()
    {
        var sft = SearchForTomorrowFactory.Create(_alice);
        sft.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sft);

        var registry = new SuspendedCardRegistry((_, _) => { });
        var suspend = SearchForTomorrowFactory.BuildSuspendCost();

        suspend.ApplySuspend(sft, _alice, registry);

        sft.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(sft);
        registry.TimeCountersOn(sft).Should().Be(2);
    }

    [Fact]
    public void Suspend_CannotSuspendFromNonHandZone()
    {
        var sft = SearchForTomorrowFactory.Create(_alice);
        sft.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(sft);

        var registry = new SuspendedCardRegistry((_, _) => { });
        var suspend = SearchForTomorrowFactory.BuildSuspendCost();

        // CR 702.62b — suspend is only available from the hand.
        suspend.CanCastFor(sft, _alice).Should().BeFalse();
        var act = () => suspend.ApplySuspend(sft, _alice, registry);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Suspend_FullCycle_TwoUpkeepTicks_ThenCastsForFree_LandEntersBattlefield()
    {
        // Setup: Search for Tomorrow in Alice's hand. A Forest in library.
        var sft = SearchForTomorrowFactory.Create(_alice);
        sft.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sft);

        var forest = MakeBasicLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Library.AddCard(forest);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        (ICard Card, Player Owner)? ready = null;
        var registry = new SuspendedCardRegistry(_bus, (card, owner) =>
            ready = (card, owner));

        // Pay {G}: exile with 2 time counters.
        var suspend = SearchForTomorrowFactory.BuildSuspendCost();
        suspend.ApplySuspend(sft, _alice, registry);

        sft.Zone.Should().Be(ZoneType.Exile);
        registry.TimeCountersOn(sft).Should().Be(2);

        // First upkeep: counter ticks from 2 → 1, card stays suspended.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        registry.IsTracked(sft).Should().BeTrue("still has 1 counter");
        ready.Should().BeNull("ready fires only when last counter is removed");
        registry.TimeCountersOn(sft).Should().Be(1);

        // Second upkeep: counter ticks from 1 → 0; ready callback fires.
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        registry.IsTracked(sft).Should().BeFalse("all counters removed");
        ready.Should().NotBeNull("ready callback should have fired on last counter removal");
        sft.Zone.Should().Be(ZoneType.Exile, "still in exile until free cast moves it");

        // Drive the free cast (zero-mana alternative cost, CR 702.62d).
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);

        var freeCast = new CastFromExileAlternativeCost(
            "Suspend resolved (CR 702.62d)", ManaCost.Parse("0"));

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 2, PhaseStateType.Upkeep, stack);

        var freeAgent = new ScriptedAgent();
        freeAgent.QueueMana(ManaPayment.Empty);

        var spell = await flow.CastAsync(
            ready!.Value.Owner, ready.Value.Card,
            SearchForTomorrowFactory.BuildSpellDefinition(_alice),
            freeAgent, ctx,
            alternativeCost: freeCast);

        sft.Zone.Should().Be(ZoneType.Stack);

        // Resolve: Forest enters Alice's battlefield untapped.
        spell.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Forest");
        var placed = _alice.Zones.Battlefield.GetCards().First() as Permanent;
        placed!.IsTapped.Should().BeFalse();
    }
}
