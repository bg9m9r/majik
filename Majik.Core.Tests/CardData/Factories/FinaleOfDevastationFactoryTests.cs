using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FinaleOfDevastationFactory"/> (War of the Spark,
/// {X}{G}{G}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Search your library and/or graveyard for a creature card with mana
///    value X or less and put it onto the battlefield. If you search your
///    library this way, shuffle. If X is 10 or more, creatures you control
///    get +X/+X and gain haste until end of turn."
///
/// Coverage:
///  - Identity (Sorcery {X}{G}{G}) + NamedCardFactory dispatch.
///  - Resolve at X=2 → tutors a creature card (any colour) with mv ≤ 2 from
///    the library onto the battlefield; out-of-range creatures untouched.
///  - A graveyard creature with mv ≤ X is a legal find (and/or graveyard).
///  - No legal creature → no-op (still shuffles the library — CR 701.20a).
///  - Library searched → library shuffles (CR 701.20a).
///  - Live ZoneService → CardMovedEvent fires for the tutored creature
///    (CR 603.6a — ETB triggers fire).
///  - X &lt; 10 → no anthem applied.
///  - X &ge; 10 → creatures the caster controls get +X/+X and gain Haste
///    until end of turn (CR 613.1c Layer 7c + Layer 6); opponents untouched.
/// </summary>
[Trait("Color", "G")]
public class FinaleOfDevastationFactoryTests
{
    private static ChosenSpellParams Choose(int? x) =>
        new(ModeIndex: null, X: x,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell, int? x)
    {
        foreach (var fx in spell.EffectFactory(Choose(x)))
        {
            fx.Execute();
        }
    }

    private static Creature MakeCreatureInLibrary(string name, string manaCost, Player owner)
    {
        var c = new Creature(name, manaCost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Creature MakeCreatureInGraveyard(string name, string manaCost, Player owner)
    {
        var c = new Creature(name, manaCost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        return c;
    }

    // ── Shape / dispatch ─────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = FinaleOfDevastationFactory.Create(owner);

        card.Name.Should().Be("Finale of Devastation");
        card.ManaCost.Should().Be("{X}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_DispatchesFinaleOfDevastation()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Finale of Devastation", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Finale of Devastation");
        card.ManaCost.Should().Be("{X}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // ── Resolve: tutor library → battlefield (any colour) ────────────────────

    [Fact]
    public void Resolve_XEquals2_TutorsCreatureWithManaValue2OrLess_OntoBattlefield()
    {
        var caster = new Player("Alice", 20);
        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        // Unlike Green Sun's Zenith, Finale finds ANY creature card (no colour
        // restriction) — a black creature is a legal find.
        var carnophage = MakeCreatureInLibrary("Carnophage", "B", caster);     // mv 1
        var giant = MakeCreatureInLibrary("Giant", "4GGG", caster);            // mv 7 — out

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod), x: 2);

        var bf = caster.Zones.Battlefield.GetCards().ToList();
        bf.Should().ContainSingle();
        bf[0].Should().BeSameAs(carnophage);
        caster.Zones.Library.GetCards().Should().Contain(giant);
    }

    [Fact]
    public void Resolve_GraveyardCreatureWithLowManaValue_IsLegalFind()
    {
        // "Search your library and/or graveyard" — a creature card in the
        // graveyard with mv ≤ X can be put onto the battlefield.
        var caster = new Player("Alice", 20);
        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        var yardElf = MakeCreatureInGraveyard("Llanowar Elves", "G", caster);  // mv 1

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod), x: 3);

        caster.Zones.Battlefield.GetCards().Should().Contain(yardElf);
        caster.Zones.Graveyard.GetCards().Should().NotContain(yardElf);
        yardElf.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_NoLegalCreature_NoOp_StillShufflesLibrary()
    {
        var caster = new Player("Alice", 20);
        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        // Only an over-cost creature in the library.
        var giant = MakeCreatureInLibrary("Giant", "4GGG", caster);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        var shuffles = new List<LibraryShuffledEvent>();
        var bus = new EventBus();
        bus.Subscribe<LibraryShuffledEvent>(shuffles.Add);
        EventBusRegistry.Set(caster, bus);
        try
        {
            Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod), x: 1);
        }
        finally
        {
            EventBusRegistry.Clear();
        }

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().Contain(giant);
        // CR 701.20a — library searched, so it shuffles even on no find.
        shuffles.Should().Contain(e => e.Reason == FinaleOfDevastationFactory.ShuffleReason);
    }

    // ── ETB trigger fires via live ZoneService ───────────────────────────────

    [Fact]
    public void Resolve_WithLiveZoneService_PublishesCardMovedEventForTutoredCreature()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var caster = new Player("Alice", 20);

        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        var etbBear = new Creature("ETB Drawer", "1G", 1, 1);
        etbBear.SetOwner(caster);
        etbBear.SetController(caster);
        caster.Zones.Library.AddCard(etbBear);
        etbBear.SetZone(ZoneType.Library);

        var moved = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(moved.Add);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod, zones), x: 2);

        etbBear.Zone.Should().Be(ZoneType.Battlefield);
        etbBear.Controller.Should().BeSameAs(caster);
        moved.Should().Contain(e =>
            ReferenceEquals(e.Card, etbBear)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield);
    }

    // ── Anthem clause: only when X ≥ 10 ──────────────────────────────────────

    [Fact]
    public void Resolve_XLessThan10_NoAnthemApplied()
    {
        var caster = new Player("Alice", 20);
        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        var effects = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = caster,
            Controller = caster,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        caster.Zones.Battlefield.AddCard(bear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod), x: 9);

        // X = 9 < 10 → no pump, no haste.
        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(bear).Should().BeFalse();
    }

    [Fact]
    public void Resolve_XEquals10OrMore_PumpsAndGrantsHasteToControlledCreatures_OpponentsUntouched()
    {
        var caster = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var fod = FinaleOfDevastationFactory.Create(caster);
        caster.Zones.Hand.AddCard(fod);
        fod.SetZone(ZoneType.Hand);

        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = caster,
            Controller = caster,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        caster.Zones.Battlefield.AddCard(bear);

        var bobBear = new Creature("Bob's Bear", "1G", 2, 2)
        {
            Owner = bob,
            Controller = bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        bob.Zones.Battlefield.AddCard(bobBear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        // X = 10 → +10/+10 and Haste to Alice's creatures only.
        Resolve(FinaleOfDevastationFactory.BuildSpellDefinition(caster, fod), x: 10);

        bear.GetPower().Should().Be(12);
        bear.GetToughness().Should().Be(12);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        bobBear.GetPower().Should().Be(2);
        bobBear.GetToughness().Should().Be(2);
        CombatAbilities.HasHaste(bobBear).Should().BeFalse();
    }
}
