using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Green Sun's Zenith (Mirrodin Besieged / Modern Horizons 2,
/// {X}{G}, Sorcery).
///
/// "Search your library for a green creature card with mana value X or
/// less, put it onto the battlefield, then shuffle. Shuffle Green Sun's
/// Zenith into its owner's library." (CR 701.19a Search, CR 105.2a colour
/// from cost pips, CR 608.2c printed self-shuffle override.)
///
/// Coverage:
///  - Identity (Sorcery {X}{G}) + NamedCardFactory dispatch.
///  - Resolve at X=2 → tutors first green creature with mv ≤ 2 onto the
///    battlefield; out-of-range creatures untouched; non-green creature
///    ignored.
///  - No green creature in library → no-op.
///  - After resolution: Green Sun's Zenith ends up in owner's library
///    (NOT graveyard) — printed self-shuffle override.
///  - ETB trigger on the tutored creature fires (CardMovedEvent published
///    via live ZoneService — CR 603.6a).
///  - Multi-target overlap: deterministic agent picks the first hit only.
/// </summary>
public class GreenSunsZenithTests
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

    // ── Shape / dispatch ─────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = GreenSunsZenithFactory.Create(owner);

        card.Name.Should().Be("Green Sun's Zenith");
        card.ManaCost.Should().Be("{X}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_DispatchesGreenSunsZenith()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Green Sun's Zenith", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Green Sun's Zenith");
        card.ManaCost.Should().Be("{X}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // ── Resolve: tutor → battlefield ─────────────────────────────────────────

    [Fact]
    public void Resolve_XEquals2_TutorsFirstGreenCreatureWithManaValue2OrLess_OntoBattlefield()
    {
        var caster = new Player("Alice", 20);
        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        var elf = MakeCreatureInLibrary("Llanowar Elves", "G", caster);    // mv 1, green
        var bear = MakeCreatureInLibrary("Bear", "1G", caster);            // mv 2, green
        var giant = MakeCreatureInLibrary("Giant", "4GGG", caster);        // mv 7, green — out

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz), x: 2);

        // Exactly one creature on the battlefield (deterministic first
        // match — elf was inserted first and is mv 1 ≤ 2).
        var bf = caster.Zones.Battlefield.GetCards().ToList();
        bf.Should().ContainSingle();
        bf[0].Should().BeSameAs(elf);
        // Giant remains in library (mv too high).
        caster.Zones.Library.GetCards().Should().Contain(giant);
        // Bear remains in library (deterministic first-match, not picked).
        caster.Zones.Library.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Resolve_NoGreenCreatureInLibrary_NoOp()
    {
        var caster = new Player("Alice", 20);
        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        // A red creature and a blue creature — neither is green.
        var goblin = MakeCreatureInLibrary("Goblin", "R", caster);
        var merfolk = MakeCreatureInLibrary("Merfolk", "U", caster);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz), x: 5);

        // Battlefield untouched.
        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        // Library still holds both non-green creatures.
        caster.Zones.Library.GetCards().Should().Contain(new ICard[] { goblin, merfolk });
    }

    [Fact]
    public void Resolve_NonGreenCreatureWithLowManaValue_Ignored()
    {
        var caster = new Player("Alice", 20);
        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        // Black 1-drop comes first — must be skipped by the colour filter.
        var blackGuy = MakeCreatureInLibrary("Carnophage", "B", caster);
        var elf = MakeCreatureInLibrary("Llanowar Elves", "G", caster);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz), x: 2);

        // Only the green elf was picked despite the black creature also
        // being mv ≤ X — CR 105.2a colour predicate gates the tutor.
        var bf = caster.Zones.Battlefield.GetCards().ToList();
        bf.Should().ContainSingle().Which.Should().BeSameAs(elf);
        caster.Zones.Library.GetCards().Should().Contain(blackGuy);
    }

    // ── Self-shuffle: GSZ ends up in owner's library ─────────────────────────

    [Fact]
    public void Resolve_AfterResolution_GreenSunsZenithEndsUpInOwnersLibrary()
    {
        var caster = new Player("Alice", 20);
        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        var elf = MakeCreatureInLibrary("Llanowar Elves", "G", caster);
        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz), x: 1);

        // Tutored creature landed on battlefield…
        caster.Zones.Battlefield.GetCards().Should().Contain(elf);
        // …and GSZ shuffled into its owner's library (not graveyard).
        gsz.Zone.Should().Be(ZoneType.Library);
        caster.Zones.Library.GetCards().Should().Contain(gsz);
        caster.Zones.Graveyard.GetCards().Should().NotContain(gsz);
    }

    [Fact]
    public void Resolve_NoLegalTarget_StillShufflesSelfIntoLibrary()
    {
        // CR 701.19a permits declining to find — but the printed self-
        // shuffle clause still runs (it's a separate sentence, not gated
        // on the tutor succeeding).
        var caster = new Player("Alice", 20);
        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        // Empty library — nothing to tutor.
        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz), x: 99);

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        gsz.Zone.Should().Be(ZoneType.Library);
        caster.Zones.Library.GetCards().Should().Contain(gsz);
    }

    // ── ETB trigger fires on the tutored creature ─────────────────────────────

    [Fact]
    public void Resolve_WithLiveZoneService_PublishesCardMovedEventForTutoredCreature()
    {
        // When a ZoneService is threaded into BuildSpellDefinition, the
        // Library → Battlefield move publishes CardMovedEvent so an ETB
        // trigger attached to the tutored creature fires (CR 603.6a).
        // Mirrors ChordOfCallingTests' ETB-trigger pin.
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var caster = new Player("Alice", 20);

        var gsz = GreenSunsZenithFactory.Create(caster);
        caster.Zones.Hand.AddCard(gsz);
        gsz.SetZone(ZoneType.Hand);

        var etbBear = new Creature("ETB Drawer", "1G", 1, 1);
        etbBear.SetOwner(caster);
        etbBear.SetController(caster);
        caster.Zones.Library.AddCard(etbBear);
        etbBear.SetZone(ZoneType.Library);

        var moved = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(moved.Add);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(GreenSunsZenithFactory.BuildSpellDefinition(caster, gsz, zones), x: 2);

        etbBear.Zone.Should().Be(ZoneType.Battlefield);
        etbBear.Controller.Should().BeSameAs(caster);
        moved.Should().Contain(e =>
            ReferenceEquals(e.Card, etbBear)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield);

        // And GSZ also published a move to its owner's library.
        gsz.Zone.Should().Be(ZoneType.Library);
        moved.Should().Contain(e =>
            ReferenceEquals(e.Card, gsz)
            && e.ToZone == ZoneType.Library);
    }
}
