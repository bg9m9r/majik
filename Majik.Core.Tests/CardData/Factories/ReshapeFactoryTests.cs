using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Tests for Reshape (Darksteel, {X}{U}{U}, Sorcery).
///
/// Oracle text (Scryfall verified):
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Search your library for an artifact card with mana value X or less,
///    put it onto the battlefield, then shuffle."
///
/// Structurally Reshape is Whir of Invention's resolve body (the X-bounded
/// artifact tutor → battlefield, then shuffle — CR 701.19a / CR 701.20a)
/// with the cost helper swapped for a MANDATORY "sacrifice an artifact"
/// additional cost (CR 601.2f), the artifact analogue of Bone Splinters.
///
/// Coverage (unique behaviour only — dispatch + well-formedness is covered
/// for every implemented card by CardFactoryContractTests):
/// - Identity ({X}{U}{U} Sorcery) — single exact mana-cost / type assert.
/// - Mandatory sacrifice-an-artifact additional cost declared (CR 601.2f).
/// - Resolve at X=3 tutors an artifact with mv ≤ 3 onto the battlefield.
/// - Resolve at X=0 only accepts mv-0 artifacts.
/// - Out-of-range artifacts left untouched (no-op when nothing eligible).
/// - Non-artifact cards in library are never tutored.
/// - Live ZoneService → CardMovedEvent published so ETB triggers fire
///   (CR 603.6a).
/// </summary>
[Trait("Color", "U")]
public class ReshapeFactoryTests
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

    // ── Shape / identity ─────────────────────────────────────────────────────

    [Fact]
    public void Identity_TypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = ReshapeFactory.Create(owner);

        card.Name.Should().Be("Reshape");
        card.ManaCost.Should().Be("{X}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    // ── Mandatory additional cost (CR 601.2f) ────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_DeclaresMandatorySacrificeAnArtifactAdditionalCost()
    {
        var caster = new Player("Alice", 20);

        var spell = ReshapeFactory.BuildSpellDefinition(caster);

        // CR 601.2f — the printed "As an additional cost to cast this spell,
        // sacrifice an artifact" rider is declared on the definition so the
        // cast flow pays it and gates legality (CR 601.2g) on the caster
        // controlling an artifact.
        spell.AdditionalCosts.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactAdditionalCost>();
    }

    [Fact]
    public void SacrificeAnArtifactAdditionalCost_CannotPayWithNoArtifact_PayableWithOne()
    {
        var caster = new Player("Alice", 20);
        var cost = new SacrificeAnArtifactAdditionalCost();

        // No artifact on the battlefield → CR 601.2g illegal cast.
        cost.CanPay(caster).Should().BeFalse();

        var widget = new Artifact("Widget", "{1}");
        widget.SetOwner(caster);
        widget.SetController(caster);
        caster.Zones.Battlefield.AddCard(widget);
        widget.SetZone(ZoneType.Battlefield);

        cost.CanPay(caster).Should().BeTrue();
        cost.Pay(caster).Should().BeTrue();
        // Sacrificed artifact moved to its owner's graveyard.
        widget.Zone.Should().Be(ZoneType.Graveyard);
        cost.Sacrificed.Should().BeSameAs(widget);
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

        Resolve(ReshapeFactory.BuildSpellDefinition(caster), x: 3);

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

        Resolve(ReshapeFactory.BuildSpellDefinition(caster), x: 0);

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

        Resolve(ReshapeFactory.BuildSpellDefinition(caster), x: 2);

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

        Resolve(ReshapeFactory.BuildSpellDefinition(caster), x: 3);

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

        Resolve(ReshapeFactory.BuildSpellDefinition(caster, zones), x: 2);

        etbArtifact.Zone.Should().Be(ZoneType.Battlefield);
        etbArtifact.Controller.Should().BeSameAs(caster);
        moved.Should().ContainSingle(e =>
            ReferenceEquals(e.Card, etbArtifact)
            && e.FromZone == ZoneType.Library
            && e.ToZone == ZoneType.Battlefield);
    }
}
