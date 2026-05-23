using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
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
/// Tests for Eldritch Evolution (Eldritch Moon, {1}{G}, Sorcery).
///
/// "As an additional cost to cast this spell, sacrifice a creature.
///  Search your library for a creature card with mana value less than or
///  equal to the sacrificed creature's mana value plus 2, put it onto the
///  battlefield, then shuffle. Exile Eldritch Evolution."
///
/// Covers:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - SpellDefinition shape: declares <see cref="SacrificeACreatureAdditionalCost"/>
///    as its additional cost.
///  - Resolve with sacrificed 2/2 Bear (mv 2) → tutors creature mv ≤ 4.
///  - Resolve with sacrificed Llanowar Elves (mv 1) → tutor cap = 3
///    excludes mv-4 candidates.
///  - SpellCastFlow rejects the cast when the controller has no creature
///    to sacrifice (CR 601.2g — additional cost can't be paid).
///  - After resolve: Eldritch Evolution moves to its owner's exile zone;
///    the tutored creature lands on the battlefield with a ZoneService-
///    published <see cref="CardMovedEvent"/> so ETB triggers fire (CR 603.6a).
/// </summary>
public class EldritchEvolutionTests
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
        // Mirror SpellCastFlow's CR 601.2f payment order: the cost picks
        // the first eligible creature on the battlefield, so wire the
        // battlefield such that `toSac` is the first match.
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
        var card = EldritchEvolutionFactory.Create(owner);

        card.Name.Should().Be("Eldritch Evolution");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EldritchEvolution()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Eldritch Evolution", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Eldritch Evolution");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_DeclaresSacrificeACreatureAdditionalCost()
    {
        var caster = new Player("Alice", 20);
        var card = EldritchEvolutionFactory.Create(caster);

        var def = EldritchEvolutionFactory.BuildSpellDefinition(caster, card);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeACreatureAdditionalCost>(
                "Eldritch Evolution prints 'As an additional cost to cast this spell, sacrifice a creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — tutor cap = sacrificed.mv + 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_SacBearMv2_TutorsCreatureMv4OrLess()
    {
        // Sac a 2/2 Bear (mv 2) → tutor cap = 4. Library holds a mv-4
        // creature (Hill Giant), a mv-2 creature (Bear), and a mv-5
        // creature (Serra Angel). DeterministicBotAgent picks the first
        // eligible candidate; the cap predicate accepts both mv-2 and
        // mv-4 entries but excludes the mv-5 one. Wire the library so the
        // mv-4 Hill Giant is first to prove the predicate accepts it.
        var caster = new Player("Alice", 20);
        var card = EldritchEvolutionFactory.Create(caster);
        caster.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var bearOnBattlefield = MakeCreature("Sac Bear", "1G", 2, 2, caster);

        var hillGiant = MakeCreature("Hill Giant", "3R", 3, 3, caster);
        var libBear = MakeCreature("Lib Bear", "1G", 2, 2, caster);
        var serraAngel = MakeCreature("Serra Angel", "3WW", 4, 4, caster);
        caster.Zones.Library.AddCard(hillGiant);
        caster.Zones.Library.AddCard(libBear);
        caster.Zones.Library.AddCard(serraAngel);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, bearOnBattlefield);
        var def = EldritchEvolutionFactory.BuildSpellDefinition(caster, card);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().Contain(hillGiant,
            "Hill Giant mv 4 ≤ (sac.mv 2 + 2) — eligible and first in candidates list");
        hillGiant.Zone.Should().Be(ZoneType.Battlefield);
        hillGiant.Controller.Should().Be(caster);
        caster.Zones.Library.GetCards().Should().NotContain(hillGiant);
        caster.Zones.Library.GetCards().Should().Contain(serraAngel,
            "Serra Angel mv 5 > cap 4 — excluded by the cap predicate");
        card.Zone.Should().Be(ZoneType.Exile, "self-exile rider runs after the tutor");
        caster.Zones.Exile.GetCards().Should().Contain(card);
    }

    [Fact]
    public void Resolve_SacLlanowarElvesMv1_TutorCapIs3_ExcludesMv4Candidates()
    {
        // Sac a Llanowar Elves (mv 1) → tutor cap = 3. Library: Hill
        // Giant (mv 4 — excluded) and Grizzly Bears (mv 2 — eligible).
        // The mv-4 candidate is wired first to prove the predicate REJECTS
        // it (the engine then falls through to the mv-2 Bears).
        var caster = new Player("Alice", 20);
        var card = EldritchEvolutionFactory.Create(caster);
        caster.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var llanowar = MakeCreature("Llanowar Elves", "G", 1, 1, caster);

        var hillGiant = MakeCreature("Hill Giant", "3R", 3, 3, caster);
        var grizzly = MakeCreature("Grizzly Bears", "1G", 2, 2, caster);
        caster.Zones.Library.AddCard(hillGiant);
        caster.Zones.Library.AddCard(grizzly);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, llanowar);
        var def = EldritchEvolutionFactory.BuildSpellDefinition(caster, card);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        caster.Zones.Battlefield.GetCards().Should().Contain(grizzly,
            "Grizzly Bears mv 2 ≤ cap 3 — the only eligible candidate");
        caster.Zones.Library.GetCards().Should().Contain(hillGiant,
            "Hill Giant mv 4 > cap (1 + 2) = 3 — must remain in library");
        caster.Zones.Library.GetCards().Should().NotContain(grizzly);
        card.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Cast-time: no creature to sacrifice → cast rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoCreatureToSacrifice()
    {
        // CR 601.2g — if any additional cost can't be paid, the cast is
        // illegal. SpellCastFlow throws and the spell never hits the
        // stack. Note the SacrificeACreatureAdditionalCost is required
        // at cast time even if the library has no candidates: the cost
        // predicate is independent of the resolve effect.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = EldritchEvolutionFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Library has a tutor target, but Alice controls no creatures —
        // the additional cost can't be paid.
        var libCreature = MakeCreature("Some Bear", "1G", 2, 2, alice);
        alice.Zones.Library.AddCard(libCreature);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.Main, stack);

        var def = EldritchEvolutionFactory.BuildSpellDefinition(alice, card);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice a creature*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        card.Zone.Should().Be(ZoneType.Hand);
        libCreature.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Exile.GetCards().Should().NotContain(card);
    }

    // -----------------------------------------------------------------------
    // End-to-end via ZoneService: ETB-trigger routing + self-exile
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RoutesTutoredCreatureThroughZoneService_PublishesEtbEvent()
    {
        // CR 603.6a — ETB triggers fire off the CardMovedEvent published
        // by ZoneService.MoveCard. Eldritch Evolution must route the
        // tutored creature's library→battlefield move through
        // ZoneService so any ETB triggers on the picked creature fire.
        // The card itself also moves to exile via the same service.
        var bus = new EventBus();
        var zones = new ZoneService(eventBus: bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var caster = new Player("Alice", 20);
        var card = EldritchEvolutionFactory.Create(caster);
        caster.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var sacBear = MakeCreature("Sac Bear", "1G", 2, 2, caster);

        var tutored = MakeCreature("Hill Giant", "3R", 3, 3, caster);
        caster.Zones.Library.AddCard(tutored);
        tutored.SetZone(ZoneType.Library);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var paid = PaySacrificeOf(caster, sacBear);
        var def = EldritchEvolutionFactory.BuildSpellDefinition(caster, card, zones);
        foreach (var fx in def.EffectFactory(ParamsWithPaidSac(paid))) fx.Execute();

        // The tutored creature is now on the battlefield, and the
        // ZoneService published a CardMovedEvent (Library → Battlefield)
        // — the ETB-trigger pipeline subscribes to this exact event.
        tutored.Zone.Should().Be(ZoneType.Battlefield);
        tutored.Controller.Should().Be(caster);
        movedEvents.Should().Contain(e =>
                ReferenceEquals(e.Card, tutored)
                && e.FromZone == ZoneType.Library
                && e.ToZone == ZoneType.Battlefield,
            "CR 603.6a — ETB triggers fire off ZoneService's CardMovedEvent");

        // Eldritch Evolution itself moves to exile (printed rider —
        // CR 608.2 override of the default sorcery-to-graveyard).
        card.Zone.Should().Be(ZoneType.Exile);
        caster.Zones.Exile.GetCards().Should().Contain(card);
        movedEvents.Should().Contain(e =>
                ReferenceEquals(e.Card, card)
                && e.ToZone == ZoneType.Exile,
            "self-exile move also routes through ZoneService when the service is wired");
    }
}
