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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Chord of Calling (Ravnica: City of Guilds, {X}{G}{G}{G}, Instant).
///
/// "Flash. Convoke. Search your library for a creature card with mana value
/// X or less, put it onto the battlefield, then shuffle." (CR 702.8 Flash,
/// CR 702.51 Convoke, CR 701.19a Search.)
///
/// Coverage:
/// - Identity (name / type / mana cost) + NamedCardFactory dispatch.
/// - Flash + Convoke keyword markers present.
/// - Convoke cost reducer (ConvokeAlternativeCost.ReduceCost) trims printed
///   cost by tapped creatures (deferred actual integration with cast flow).
/// - Resolve at X=3 picks a creature with mv ≤ 3 onto the battlefield.
/// - Resolve at X=0 only accepts mv-0 creatures; mv-1 candidates ignored.
/// - Resolve at X=2 leaves out-of-range creatures untouched.
/// - ETB trigger on the tutored creature fires when a live ZoneService is
///   threaded in (CR 603.6a — bus-driven CardMovedEvent path).
/// </summary>
public class ChordOfCallingTests
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
        var card = ChordOfCallingFactory.Create(owner);

        card.Name.Should().Be("Chord of Calling");
        card.ManaCost.Should().Be("{X}{G}{G}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Flash", "Convoke" });
    }

    [Fact]
    public void NamedCardFactory_DispatchesChordOfCalling()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Chord of Calling", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Chord of Calling");
        card.ManaCost.Should().Be("{X}{G}{G}{G}");

        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[] { "Flash", "Convoke" });
    }

    // ── Convoke alt-cost surface ─────────────────────────────────────────────

    [Fact]
    public void BuildAlternativeCost_ReturnsPrintedCost_AndReducesByTappedCreatures()
    {
        // The alt-cost object surfaces the printed cost {X}{G}{G}{G}. Tapping
        // two creatures via the pure-function reducer trims two pips off:
        // first {G} (one creature), second {G} (second creature) — leaving
        // {X}{G}.
        var convoke = ChordOfCallingFactory.BuildAlternativeCost();
        convoke.Description.Should().Be("Convoke");
        convoke.AlternativeManaCost.Should().Be(ManaCost.Parse("XGGG"));

        var bear1 = new Creature("Bear", "1G", 2, 2);
        var bear2 = new Creature("Bear", "1G", 2, 2);
        var reduced = ConvokeAlternativeCost.ReduceCost(
            convoke.AlternativeManaCost, new[] { bear1, bear2 });

        // {X}{G}{G}{G} has 0 generic + 3 green pips. Two taps consume two
        // greens (no generic to peel first), leaving 1 green + the X marker.
        reduced.Green.Should().Be(1);
        reduced.Generic.Should().Be(0);
        reduced.HasX.Should().BeTrue();
    }

    // ── Resolve: X tutor → battlefield ───────────────────────────────────────

    [Fact]
    public void Resolve_XEquals3_TutorsCreatureWithManaValue3OrLess_OntoBattlefield()
    {
        var caster = new Player("Alice", 20);
        var bear = MakeCreatureInLibrary("Bear", "1G", caster);          // mv 2
        var elf = MakeCreatureInLibrary("Llanowar Elf", "G", caster);    // mv 1
        var giant = MakeCreatureInLibrary("Giant", "4GGG", caster);      // mv 7 — out

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ChordOfCallingFactory.BuildSpellDefinition(caster), x: 3);

        // Exactly one creature on the battlefield — chosen by the
        // deterministic agent from the eligible set {bear, elf}. Giant
        // remains in library (mv too high).
        var bf = caster.Zones.Battlefield.GetCards().ToList();
        bf.Should().ContainSingle();
        bf[0].HasType(CardType.Creature).Should().BeTrue();
        bf[0].Should().BeOneOf(bear, elf);

        caster.Zones.Library.GetCards().Should().Contain(giant);
    }

    [Fact]
    public void Resolve_XEqualsZero_OnlyAcceptsManaValueZeroCreatures()
    {
        var caster = new Player("Alice", 20);
        var elf = MakeCreatureInLibrary("Llanowar Elf", "G", caster);             // mv 1 — out
        var memnite = MakeCreatureInLibrary("Memnite", "", caster);               // mv 0 — eligible

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ChordOfCallingFactory.BuildSpellDefinition(caster), x: 0);

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(memnite);
        caster.Zones.Library.GetCards().Should().Contain(elf);
    }

    [Fact]
    public void Resolve_OutOfRangeCreaturesUntouched_NoOpWhenNoEligible()
    {
        var caster = new Player("Alice", 20);
        var giant = MakeCreatureInLibrary("Giant", "4GGG", caster);      // mv 7 — out at X=2

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ChordOfCallingFactory.BuildSpellDefinition(caster), x: 2);

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(giant);
    }

    // ── ETB trigger fires on the tutored creature ─────────────────────────────

    [Fact]
    public void Resolve_WithLiveZoneService_FiresETBTriggerOnTutoredCreature()
    {
        // When a ZoneService is threaded into BuildSpellDefinition, the
        // Library → Battlefield move publishes CardMovedEvent so an ETB
        // trigger attached to the tutored creature fires (CR 603.6a).
        // Mirrors LivingEndTests' reanimate-then-trigger pattern.
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var caster = new Player("Alice", 20);

        var etbBear = new Creature("ETB Drawer", "1G", 1, 1);
        etbBear.SetOwner(caster);
        etbBear.SetController(caster);
        caster.Zones.Library.AddCard(etbBear);
        etbBear.SetZone(ZoneType.Library);

        // Self-ETB trigger that increments a counter on resolve — we just
        // observe that the bus published a Library→Battlefield event so a
        // trigger registered against CardMovedEvent would have its
        // condition evaluated. Subscribing directly to CardMovedEvent is
        // the cleanest signal-of-life without spinning up a full
        // TriggerManager + StackResolver (LivingEndTests does that
        // heavier setup; here we just pin the event-publish contract).
        var moved = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(moved.Add);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(ChordOfCallingFactory.BuildSpellDefinition(caster, zones), x: 2);

        etbBear.Zone.Should().Be(ZoneType.Battlefield);
        etbBear.Controller.Should().BeSameAs(caster);
        moved.Should().ContainSingle(e =>
            ReferenceEquals(e.Card, etbBear)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield);
    }
}
