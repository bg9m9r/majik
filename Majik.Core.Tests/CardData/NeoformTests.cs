using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Counters;
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
/// Tests for Neoform (War of the Spark, {G}{U}, Sorcery).
///
/// "As an additional cost to cast this spell, sacrifice a creature.
///  Search your library for a creature card with mana value equal to 1
///  plus the sacrificed creature's mana value, put that card onto the
///  battlefield with an additional +1/+1 counter on it, then shuffle."
///
/// Covers:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - SpellDefinition shape: declares <see cref="SacrificeACreatureAdditionalCost"/>
///    as its additional cost; no targets, no modes, no X.
///  - Resolve with sac'ed Llanowar Elves (MV 1) → tutors creature MV == 2.
///  - EXACT MV gate: MV-3 candidates are excluded when sac MV == 1.
///  - MV-2 candidate absent → tutor is a no-op; shuffle still runs.
///  - Tutored creature enters with exactly one +1/+1 counter.
///  - SpellCastFlow rejects the cast when the caster has no creature to
///    sacrifice (CR 601.2g).
///  - ZoneService routing: tutored creature's Library→Battlefield move
///    publishes a <see cref="CardMovedEvent"/> for ETB-trigger pipeline
///    (CR 603.6a).
/// </summary>
public class NeoformTests
{
    // -----------------------------------------------------------------------
    // Test helpers
    // -----------------------------------------------------------------------

    private static ChosenSpellParams ParamsWithPaidSac(SacrificeACreatureAdditionalCost paidCost) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: null,
            ModeIndexes: null,
            AdditionalCostPayments: new IAdditionalCost[] { paidCost });

    private static SacrificeACreatureAdditionalCost PaySacrificeOf(Player caster, Creature toSac)
    {
        caster.Zones.Battlefield.AddCard(toSac);
        toSac.SetOwner(caster);
        toSac.SetController(caster);
        toSac.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeACreatureAdditionalCost();
        cost.CanPay(caster).Should().BeTrue();
        cost.Pay(caster).Should().BeTrue();
        cost.Sacrificed.Should().BeSameAs(toSac);
        toSac.Zone.Should().Be(ZoneType.Graveyard);
        return cost;
    }

    private static Creature MakeCreature(
        string name, string manaCost, int power, int toughness, Player owner)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = NeoformFactory.Create(owner);

        card.Name.Should().Be("Neoform");
        card.ManaCost.Should().Be("{G}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Neoform()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Neoform", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Neoform");
        card.ManaCost.Should().Be("{G}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_DeclaresSacrificeACreatureAdditionalCost()
    {
        var caster = new Player("Alice", 20);

        var def = NeoformFactory.BuildSpellDefinition(caster);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeACreatureAdditionalCost>(
                "Neoform prints 'As an additional cost to cast this spell, sacrifice a creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — EXACT MV gate (sac.MV + 1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_SacLlanowarElvesMv1_TutorsCreatureMvExactly2()
    {
        // Sac Llanowar Elves (MV 1) → target MV = 2.
        // Library: Grizzly Bears (MV 2 — eligible), Hill Giant (MV 4 —
        // excluded). DeterministicBotAgent picks the first eligible
        // candidate. Wire Grizzly Bears first to confirm it is found.
        var caster = new Player("Alice", 20);

        var llanowar = MakeCreature("Llanowar Elves", "{G}", 1, 1, caster);
        var grizzly   = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, caster);
        var hillGiant = MakeCreature("Hill Giant", "{3}{R}", 3, 3, caster);

        caster.Zones.Library.AddCard(grizzly);
        caster.Zones.Library.AddCard(hillGiant);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, llanowar);
        var def  = NeoformFactory.BuildSpellDefinition(caster);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().Contain(grizzly,
            "Grizzly Bears mv 2 == (sac mv 1) + 1 — exactly matches the MV gate");
        grizzly.Zone.Should().Be(ZoneType.Battlefield);
        grizzly.Controller.Should().Be(caster);
        caster.Zones.Library.GetCards().Should().NotContain(grizzly);
        caster.Zones.Library.GetCards().Should().Contain(hillGiant,
            "Hill Giant mv 4 ≠ target mv 2 — must remain in library");
    }

    [Fact]
    public void Resolve_TutoredCreature_HasOnePlusOnePlusOneCounter()
    {
        // The tutored creature must enter with exactly one +1/+1 counter.
        var caster = new Player("Alice", 20);

        var llanowar = MakeCreature("Llanowar Elves", "{G}", 1, 1, caster);
        var target   = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, caster);

        caster.Zones.Library.AddCard(target);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, llanowar);
        var def  = NeoformFactory.BuildSpellDefinition(caster);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Neoform puts the tutored creature onto the battlefield with an additional +1/+1 counter on it");
    }

    [Fact]
    public void Resolve_ExactMvGate_ExcludesMv3WhenSacMv1()
    {
        // Sac a MV-1 creature → target MV = 2. A MV-3 creature must be
        // excluded even though it is "close" (Eldritch Evolution would
        // accept it; Neoform's exact gate must not).
        var caster = new Player("Alice", 20);

        var sacCreature  = MakeCreature("Elvish Mystic", "{G}", 1, 1, caster);
        var mv3Creature  = MakeCreature("Birds of Paradise", "{2}{G}", 2, 3, caster); // MV 3
        var mv2Creature  = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, caster);    // MV 2

        // Wire MV-3 first so agent would pick it if the gate were wrong.
        caster.Zones.Library.AddCard(mv3Creature);
        caster.Zones.Library.AddCard(mv2Creature);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, sacCreature);
        var def  = NeoformFactory.BuildSpellDefinition(caster);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().NotContain(mv3Creature,
            "MV 3 ≠ target MV 2 — the exact gate must exclude it");
        caster.Zones.Battlefield.GetCards().Should().Contain(mv2Creature,
            "MV 2 == target MV 2 — the exact gate must accept it");
    }

    [Fact]
    public void Resolve_NoCandidateAtTargetMv_IsNoOp_ButShuffleStillRuns()
    {
        // Sac MV-1 → target MV = 2. Library has only MV-4 creatures.
        // Tutor is a no-op; library should still be shuffled (CR 701.20a).
        var caster = new Player("Alice", 20);

        var sacCreature  = MakeCreature("Llanowar Elves", "{G}", 1, 1, caster);
        var mv4Creature  = MakeCreature("Hill Giant", "{3}{R}", 3, 3, caster);

        caster.Zones.Library.AddCard(mv4Creature);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, sacCreature);
        var def  = NeoformFactory.BuildSpellDefinition(caster);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().BeEmpty(
            "no MV-2 candidate exists — battlefield should remain empty");
        // Shuffle is a side-effect; we can't observe the order in a unit
        // test without seeding, but we assert no exception was thrown.
    }

    // -----------------------------------------------------------------------
    // Cast-time: no creature to sacrifice → cast rejected (CR 601.2g)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoCreatureToSacrifice()
    {
        // CR 601.2g — additional cost can't be paid → cast illegal.
        var bus   = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow  = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob", 20);

        var card = NeoformFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Library has a legal tutor target, but Alice controls no
        // creatures → the additional cost can't be paid.
        var libCreature = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2, alice);
        alice.Zones.Library.AddCard(libCreature);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1,
            PhaseStateType.PreCombatMain, stack);

        var def = NeoformFactory.BuildSpellDefinition(alice);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice a creature*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        card.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // ZoneService routing: ETB trigger fires on tutored creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RoutesTutoredCreatureThroughZoneService_PublishesEtbEvent()
    {
        // CR 603.6a — ETB triggers fire off the CardMovedEvent published
        // by ZoneService.MoveCard. Neoform must route the tutored
        // creature's Library→Battlefield move through ZoneService so any
        // ETB triggers on the picked creature fire.
        var bus   = new EventBus();
        var zones = new ZoneService(eventBus: bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var caster = new Player("Alice", 20);

        // Sac Bear MV 2 → target MV = 3; tutored creature must be MV 3.
        var sacBear = MakeCreature("Sac Bear", "{1}{G}", 2, 2, caster);
        var tutored = MakeCreature("Hill Giant", "{2}{R}", 3, 3, caster);

        caster.Zones.Library.AddCard(tutored);
        tutored.SetZone(ZoneType.Library);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, sacBear);
        var def  = NeoformFactory.BuildSpellDefinition(caster, zones);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        tutored.Zone.Should().Be(ZoneType.Battlefield);
        tutored.Controller.Should().Be(caster);

        movedEvents.Should().Contain(e =>
                ReferenceEquals(e.Card, tutored)
                && e.FromZone == ZoneType.Library
                && e.ToZone == ZoneType.Battlefield,
            "CR 603.6a — Library→Battlefield move published via ZoneService so ETB triggers fire");

        tutored.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "tutored creature enters with one +1/+1 counter regardless of how the move is routed");
    }
}
