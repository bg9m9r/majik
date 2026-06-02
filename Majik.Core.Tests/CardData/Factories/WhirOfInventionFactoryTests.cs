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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Whir of Invention (Aether Revolt, {X}{U}{U}{U}, Instant).
///
/// Oracle text (Scryfall verified):
///   "Improvise (Your artifacts can help cast this spell. Each artifact you
///    tap after you're done activating mana abilities pays for {1}.)
///    Search your library for an artifact card with mana value X or less,
///    put it onto the battlefield, then shuffle."
///
/// Structurally Whir of Invention is Chord of Calling with two swaps:
/// the tutor type is <b>artifact</b> instead of creature, and the cost
/// helper is <b>Improvise</b> (CR 702.127) instead of Convoke (CR 702.51).
/// The cast-flow Improvise primitive is reused verbatim from
/// <see cref="ImproviseAdditionalCost"/> (the Kappa Cannoneer rail).
///
/// Coverage:
/// - Identity (name / type / mana cost) + NamedCardFactory dispatch.
/// - Improvise keyword marker present.
/// - Improvise cost helper (BuildAdditionalCost) reduces generic by the tap
///   count via ImproviseAdditionalCost.ApplyTo (CR 702.127 — generic only).
/// - Resolve at X=3 tutors an artifact with mv ≤ 3 onto the battlefield.
/// - Resolve at X=0 only accepts mv-0 artifacts.
/// - Out-of-range artifacts left untouched (no-op when nothing eligible).
/// - Non-artifact cards in library are never tutored.
/// - Live ZoneService → CardMovedEvent published so ETB triggers fire
///   (CR 603.6a).
/// </summary>
[Trait("Color", "U")]
public class WhirOfInventionFactoryTests
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

    private static Artifact MakeArtifactInLibrary(string name, string manaCost, Player owner)
    {
        var a = new Artifact(name, manaCost);
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Library.AddCard(a);
        a.SetZone(ZoneType.Library);
        return a;
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
        var card = WhirOfInventionFactory.Create(owner);

        card.Name.Should().Be("Whir of Invention");
        card.ManaCost.Should().Be("{X}{U}{U}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Improvise");
    }
    // ── Improvise cost surface ───────────────────────────────────────────────

    [Fact]
    public void BuildAdditionalCost_TapsArtifacts_ReducesGenericPips()
    {
        // {X}{U}{U}{U} has 0 generic + 3 blue pips. Improvise only pays
        // generic (CR 702.127) — with no generic to reduce, ApplyTo leaves
        // the cost unchanged. We verify the reduction primitive against a
        // cost that DOES carry generic so the {1}-per-tap fold is observable.
        var owner = new Player("Alice", 20);
        var card = WhirOfInventionFactory.Create(owner);

        var widget1 = new Artifact("Widget", "{1}");
        var widget2 = new Artifact("Widget", "{1}");
        var cost = WhirOfInventionFactory.BuildAdditionalCost(
            card, new[] { (Permanent)widget1, widget2 });

        cost.ReductionAmount.Should().Be(2);

        // CR 702.127 — generic reduced by 2, blue pips preserved.
        var reduced = cost.ApplyTo(ManaCost.Parse("3UUU"));
        reduced.Generic.Should().Be(1);
        reduced.Blue.Should().Be(3);
    }

    // ── Resolve: X tutor → battlefield ───────────────────────────────────────

    [Fact]
    public void Resolve_XEquals3_TutorsArtifactWithManaValue3OrLess_OntoBattlefield()
    {
        var caster = new Player("Alice", 20);
        var sol = MakeArtifactInLibrary("Sol Ring", "{1}", caster);          // mv 1
        var signet = MakeArtifactInLibrary("Signet", "{2}", caster);         // mv 2
        var colossus = MakeArtifactInLibrary("Colossus", "{11}", caster);    // mv 11 — out

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WhirOfInventionFactory.BuildSpellDefinition(caster), x: 3);

        var bf = caster.Zones.Battlefield.GetCards().ToList();
        bf.Should().ContainSingle();
        bf[0].HasType(CardType.Artifact).Should().BeTrue();
        bf[0].Should().BeOneOf(sol, signet);

        caster.Zones.Library.GetCards().Should().Contain(colossus);
    }

    [Fact]
    public void Resolve_XEqualsZero_OnlyAcceptsManaValueZeroArtifacts()
    {
        var caster = new Player("Alice", 20);
        var ornithopter = MakeArtifactInLibrary("Ornithopter", "", caster);  // mv 0 — eligible
        var sol = MakeArtifactInLibrary("Sol Ring", "{1}", caster);          // mv 1 — out

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WhirOfInventionFactory.BuildSpellDefinition(caster), x: 0);

        caster.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(ornithopter);
        caster.Zones.Library.GetCards().Should().Contain(sol);
    }

    [Fact]
    public void Resolve_OutOfRangeArtifactsUntouched_NoOpWhenNoEligible()
    {
        var caster = new Player("Alice", 20);
        var colossus = MakeArtifactInLibrary("Colossus", "{11}", caster);    // mv 11 — out at X=2

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WhirOfInventionFactory.BuildSpellDefinition(caster), x: 2);

        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(colossus);
    }

    [Fact]
    public void Resolve_NeverTutorsNonArtifactCard()
    {
        var caster = new Player("Alice", 20);
        var creature = MakeCreatureInLibrary("Bear", "1G", caster);          // mv 2 but not an artifact

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WhirOfInventionFactory.BuildSpellDefinition(caster), x: 3);

        // The creature is in mv range but is NOT an artifact — it must stay
        // in the library (CR 701.19a — search restricted by the named
        // characteristic "artifact card").
        caster.Zones.Battlefield.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(creature);
    }

    // ── ETB trigger fires on the tutored artifact ─────────────────────────────

    [Fact]
    public void Resolve_WithLiveZoneService_PublishesCardMovedEventForTutoredArtifact()
    {
        // When a ZoneService is threaded into BuildSpellDefinition, the
        // Library → Battlefield move publishes CardMovedEvent so an ETB
        // trigger attached to the tutored artifact fires (CR 603.6a).
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var caster = new Player("Alice", 20);

        var etbArtifact = new Artifact("ETB Widget", "{2}");
        etbArtifact.SetOwner(caster);
        etbArtifact.SetController(caster);
        caster.Zones.Library.AddCard(etbArtifact);
        etbArtifact.SetZone(ZoneType.Library);

        var moved = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(moved.Add);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WhirOfInventionFactory.BuildSpellDefinition(caster, zones), x: 2);

        etbArtifact.Zone.Should().Be(ZoneType.Battlefield);
        etbArtifact.Controller.Should().BeSameAs(caster);
        moved.Should().ContainSingle(e =>
            ReferenceEquals(e.Card, etbArtifact)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield);
    }
}
