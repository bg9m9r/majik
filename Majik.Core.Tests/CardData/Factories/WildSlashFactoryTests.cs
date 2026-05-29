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
/// Unit tests for <see cref="WildSlashFactory"/> (Fate Reforged, {R}).
///
/// Oracle text (verified against Scryfall 2026-05):
///   "Ferocious — If you control a creature with power 4 or greater,
///    damage can't be prevented this turn.
///    Wild Slash deals 2 damage to any target."
///
/// Covers:
/// - Identity ({R} Instant, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 2 damage to a player target.
/// - Resolve body routes creature damage through
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// - Resolve body routes planeswalker damage through loyalty removal (CR 306.7).
/// - Ferocious checker: true iff caster controls a power-4+ creature.
/// - Ferocious rider does not alter the base 2-damage outcome (the
///   prevention-suppression clause is a documented v1 no-op; see factory).
/// </summary>
public class WildSlashFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WildSlash_Identity_InstantAtR()
    {
        var card = WildSlashFactory.Create(_alice);

        card.Name.Should().Be("Wild Slash");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildSlash_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Wild Slash", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Wild Slash");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WildSlash_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = WildSlashFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve body — damage
    // -----------------------------------------------------------------------

    [Fact]
    public void WildSlash_Resolve_DealsTwoDamageToPlayer()
    {
        var def = WildSlashFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(18, "Wild Slash deals 2 damage to any target");
    }

    [Fact]
    public void WildSlash_Resolve_DealsTwoDamageToCreature()
    {
        // Use a 0/3 creature so 2 damage is not lethal — verifies the damage
        // marker is applied without an SBA wipe interfering.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 3,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = WildSlashFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        bear.Damage.Should().Be(2, "Wild Slash deals 2 damage to target creature");
    }

    [Fact]
    public void WildSlash_Resolve_DealsTwoDamageToPlaneswalker_ViaLoyaltyRemoval()
    {
        var pw = new Planeswalker("Chandra, Torch of Defiance", "{2}{R}{R}", 4,
            Array.Empty<CardSupertype>(),
            new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = WildSlashFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { pw },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        // CR 306.7 — damage to a planeswalker removes loyalty counters.
        pw.Loyalty.Should().Be(2,
            "Wild Slash deals 2 damage → planeswalker loses 2 loyalty (4−2=2)");
        _bob.LifeTotal.Should().Be(20,
            "damage to a planeswalker does not reduce its controller's life total");
    }

    // -----------------------------------------------------------------------
    // Ferocious checker (CR 702.105b analog) — power-4+ creature control
    // -----------------------------------------------------------------------

    [Fact]
    public void Ferocious_True_WhenCasterControlsPowerFourPlusCreature()
    {
        var beast = new Creature("Watchwolf", "{G}{W}", 4, 4);
        beast.SetOwner(_alice);
        beast.SetController(_alice);
        beast.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(beast);

        var ferocious = WildSlashFactory.BuildFerociousChecker(_alice);

        ferocious().Should().BeTrue(
            "Alice controls a creature with power ≥ 4 (CR 702.105b analog)");
    }

    [Fact]
    public void Ferocious_False_WhenNoPowerFourPlusCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var ferocious = WildSlashFactory.BuildFerociousChecker(_alice);

        ferocious().Should().BeFalse(
            "Alice's only creature has power 2 (< 4) — ferocious is not met");
    }

    // -----------------------------------------------------------------------
    // Ferocious rider does not change the base damage outcome
    // -----------------------------------------------------------------------

    [Fact]
    public void WildSlash_FerociousActive_StillDealsTwoDamage()
    {
        // Ferocious met → "damage can't be prevented this turn" is a
        // documented v1 no-op (no global prevention-suppression infra exists;
        // see WildSlashFactory / SkullcrackFactory). The base 2 damage is
        // unaffected by the rider's presence either way.
        var def = WildSlashFactory.BuildSpellDefinition(
            resolver: x => x,
            ferociousChecker: () => true);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Ferocious rider is a no-op in v1; base 2 damage still applies");
    }
}
