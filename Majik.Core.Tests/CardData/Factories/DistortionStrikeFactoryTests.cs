using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DistortionStrikeFactory"/> (Rise of the Eldrazi, {U}).
///
/// Distortion Strike — Sorcery.
/// Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 until end of turn and can't be blocked
///    this turn.
///    Rebound (If you cast this spell from your hand, exile it as it
///    resolves. At the beginning of your next upkeep, you may cast this
///    card from exile without paying its mana cost.)"
///
/// Distortion Strike = Defiant Strike's +1/+0 pump body (CR 613.1g / CR 514.2)
/// composed with a single-target can't-be-blocked-this-turn restriction
/// (CR 509.1c / CR 702.x — Earthshaker Khenra / Rogue's Passage convention) and
/// the deferred Rebound keyword marker (CR 702.88 — Staggershock convention).
///
/// Covers:
/// - Identity ({U} Sorcery, name, owner/controller, color) loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - Rebound keyword marker (CR 702.88) — the rider is deferred, but the marker
///   is attached (same convention as <see cref="StaggershockFactory"/>).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "target creature" request, no X.
/// - Resolve pumps the target creature +1/+0 (CR 613.1g / CR 514.2).
/// - Resolve grants the target can't-be-blocked this turn (CR 509.1c / CR 702.x).
/// - Both grants expire at end of turn (CR 514.2).
/// - Illegal target (creature no longer on the battlefield) → both clauses
///   no-op (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class DistortionStrikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity + markers ────────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_Identity_SorceryAtU()
    {
        var card = DistortionStrikeFactory.Create(_alice);

        card.Name.Should().Be("Distortion Strike");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void DistortionStrike_HasReboundKeywordMarker()
    {
        var card = DistortionStrikeFactory.Create(_alice);

        var keywordNames = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain("Rebound",
            "CR 702.88 — Rebound marker attached even though the rider is deferred");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = DistortionStrikeFactory.BuildSpellDefinition(resolver: x => x);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    // ── Pump ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_Resolve_TargetGetsPlusOnePlusZero()
    {
        var bear = BuildBear(_bob);

        var def = DistortionStrikeFactory.BuildSpellDefinition(resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute();

        bear.GetPower().Should().Be(3, "Distortion Strike grants +1 power (CR 613.1g)");
        bear.GetToughness().Should().Be(2, "Distortion Strike grants +0 toughness");
    }

    // ── Can't be blocked ──────────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_Resolve_TargetCantBeBlockedThisTurn()
    {
        var bear = BuildBear(_bob);
        var svc = bear.ActiveEffects!;

        var def = DistortionStrikeFactory.BuildSpellDefinition(resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute();

        // CR 509.1c / CR 702.x — the target can't be blocked this turn.
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Distortion Strike grants can't-be-blocked-this-turn (CR 509.1c)");
    }

    // ── End-of-turn expiry ────────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_Grants_ExpireAtEndOfTurn()
    {
        var bear = BuildBear(_bob);
        var svc = bear.ActiveEffects!;

        var def = DistortionStrikeFactory.BuildSpellDefinition(resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute();

        bear.GetPower().Should().Be(3);
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue();

        // CR 514.2 — "until end of turn" / "this turn" effects expire in cleanup.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2, "the pump expires at end of turn (CR 514.2)");
        bear.GetToughness().Should().Be(2);
        svc.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "the can't-be-blocked grant expires at end of turn (CR 514.2)");
    }

    // ── Illegal target ────────────────────────────────────────────────────────

    [Fact]
    public void DistortionStrike_TargetNotOnBattlefield_BothClausesNoOp()
    {
        // Creature is in the graveyard at resolution (CR 608.2b — the spell
        // fizzles; neither clause applies).
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        var def = DistortionStrikeFactory.BuildSpellDefinition(resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(dead))) effect.Execute();

        dead.GetPower().Should().Be(2, "the pump no-ops off the battlefield (CR 608.2b)");
        dead.ActiveEffects!.HasRestriction(dead, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("the can't-be-blocked grant no-ops off the battlefield (CR 608.2b)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ChosenSpellParams Chosen(object target) => new(
        ModeIndex: null,
        X:         null,
        Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
        Mana:      ManaPayment.Empty);

    private static Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }
}
