using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StokeTheFlamesFactory"/> (Magic 2015, {2}{R}{R}).
///
/// Stoke the Flames — Instant.
/// Oracle text (verified against Scryfall):
///   "Convoke (Your creatures can help cast this spell. Each creature you tap
///    while casting this spell pays for {1} or one mana of that creature's
///    color.)
///    Stoke the Flames deals 4 damage to any target."
///
/// Structurally this is Flame Javelin (4 damage to any target) with Convoke
/// stapled on instead of the twobrid cost — so the resolve body mirrors
/// <see cref="FlameJavelinFactory"/> and the Convoke wiring mirrors
/// <see cref="ChordOfCallingFactory"/> / <see cref="ConclaveTribunalFactory"/>.
///
/// Covers:
/// - Identity ({2}{R}{R} Instant, name, owner/controller) loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - Convoke keyword marker (CR 702.51) present.
/// - BuildAdditionalCost produces a <see cref="ConvokeAdditionalCost"/>.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 4 damage to a player target (CR 120.3).
/// - Resolve deals 4 damage to a creature target.
/// - Resolve removes loyalty from a planeswalker target (CR 306.7).
/// </summary>
[Trait("Color", "R")]
public class StokeTheFlamesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void StokeTheFlames_Identity_InstantAtTwoRR()
    {
        var card = StokeTheFlamesFactory.Create(_alice);

        card.Name.Should().Be("Stoke the Flames");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StokeTheFlames_HasConvokeKeywordMarker()
    {
        // CR 702.51 — Convoke keyword marker (descriptive; cost reduction is
        // surfaced via BuildAdditionalCost). Same inline attach pattern as
        // Chord of Calling / Conclave Tribunal.
        var card = StokeTheFlamesFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Convoke");
    }

    [Fact]
    public void StokeTheFlames_BuildAdditionalCost_BuildsConvokeCost()
    {
        var card = StokeTheFlamesFactory.Create(_alice);

        var addCost = StokeTheFlamesFactory.BuildAdditionalCost(
            card, Array.Empty<Creature>());

        addCost.Should().NotBeNull();
        addCost.Should().BeOfType<ConvokeAdditionalCost>();
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void StokeTheFlames_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = StokeTheFlamesFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void StokeTheFlames_Resolve_DealsFourDamageToPlayer()
    {
        var def = StokeTheFlamesFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(16, "Stoke the Flames deals 4 damage to any target (CR 120.3)");
    }

    [Fact]
    public void StokeTheFlames_Resolve_DealsFourDamageToCreature()
    {
        // 0/5 creature so 4 damage is not lethal — verifies the damage marker
        // is applied without an SBA wipe interfering.
        var wall = new Creature("Wall of Wood", "{G}", 0, 5,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = StokeTheFlamesFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(4, "Stoke the Flames deals 4 damage to target creature");
    }

    [Fact]
    public void StokeTheFlames_Resolve_RemovesLoyaltyFromPlaneswalker()
    {
        // CR 306.7 — damage to a planeswalker becomes loyalty removal.
        // Fx.DealDamageAny routes the planeswalker branch: 4 damage to a
        // 4-loyalty walker leaves it at 0.
        var walker = new Planeswalker("Test Walker", "{2}{B}", 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Liliana });
        walker.SetOwner(_bob);
        walker.SetController(_bob);
        walker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(walker);

        var def = StokeTheFlamesFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { walker } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        walker.Loyalty.Should().Be(0,
            "Stoke the Flames to a 4-loyalty planeswalker removes 4 loyalty counters (CR 306.7)");
    }
}
