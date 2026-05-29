using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StaggershockFactory"/> (Rise of the Eldrazi, {2}{R}).
///
/// Staggershock — Instant.
/// Oracle text (verified against Scryfall):
///   "Staggershock deals 2 damage to any target.
///    Rebound (If you cast this spell from your hand, exile it as it
///    resolves. At the beginning of your next upkeep, you may cast this
///    card from exile without paying its mana cost.)"
///
/// Staggershock = Shock's "2 damage to any target" body (CR 115.3 / CR 120.3)
/// composed with the Rebound keyword (CR 702.88) — Shock damage shape +
/// Ephemerate's deferred Rebound marker convention.
///
/// Covers:
/// - Identity ({2}{R} Instant, name, owner / controller) loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - Rebound keyword marker (CR 702.88) — the rider is deferred, but the
///   marker is attached (same convention as <see cref="EphemerateFactory"/>).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 2 damage to a player target (CR 120.3).
/// - Resolve deals 2 damage to a creature target.
/// </summary>
public class StaggershockFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity + markers ────────────────────────────────────────────────────

    [Fact]
    public void Staggershock_Identity_InstantAt2R()
    {
        var card = StaggershockFactory.Create(_alice);

        card.Name.Should().Be("Staggershock");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Staggershock_HasReboundKeywordMarker()
    {
        var card = StaggershockFactory.Create(_alice);

        var keywordNames = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain("Rebound",
            "CR 702.88 — Rebound marker attached even though the rider is deferred");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Staggershock()
    {
        var card = NamedCardFactory.Create("Staggershock", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Staggershock");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void Staggershock_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = StaggershockFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void Staggershock_Resolve_DealsTwoDamageToPlayer()
    {
        var def = StaggershockFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(18, "Staggershock deals 2 damage to any target (CR 120.3)");
    }

    [Fact]
    public void Staggershock_Resolve_DealsTwoDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = StaggershockFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(2, "Staggershock deals 2 damage to target creature");
    }
}
