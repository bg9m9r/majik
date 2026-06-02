using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GingerbruteFactory"/>.
///
/// Gingerbrute (Throne of Eldraine, {1}). Artifact Creature — Food Golem
/// 1/1. Oracle text (verified against Scryfall):
///   "Haste (This creature can attack and {T} as soon as it comes under
///    your control.)
///    {1}: This creature can't be blocked this turn except by creatures
///    with haste.
///    {2}, {T}, Sacrifice this creature: You gain 3 life."
///
/// Coverage:
/// - Identity (name, types Artifact + Creature, Food + Golem subtypes,
///   {1}, 1/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - Haste keyword marker (CR 702.10).
/// - {1} evasion ability: structural shape ({1} mana cost only) +
///   resolution registers an EOT "can't be blocked except by haste"
///   restriction (CR 509.1b) so a non-haste blocker is illegal but a
///   haste blocker is legal; the restriction expires at end of turn.
/// - {2},{T},Sacrifice: gain 3 life — built from the embedded JSON
///   (mana + tap_self + sacrifice_self costs, gain_life_self effect).
/// </summary>
[Trait("Color", "C")]
public class GingerbruteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeBear(Player owner, params string[] keywords)
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        foreach (var kw in keywords)
        {
            c.AddAbility(new KeywordAbility(kw, c, owner));
        }
        return c;
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void Gingerbrute_Identity()
    {
        var c = GingerbruteFactory.Create(_alice);

        c.Name.Should().Be("Gingerbrute");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Food).Should().BeTrue();
        c.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        c.ManaCost.Should().Be("{1}");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Haste ───────────────────────────────────────────────────────────

    [Fact]
    public void Gingerbrute_HasHaste()
    {
        var c = GingerbruteFactory.Create(_alice);

        CombatAbilities.HasHaste(c).Should().BeTrue(
            "Gingerbrute prints Haste (CR 702.10).");
    }

    // ── Sacrifice-for-life ability (from JSON) ──────────────────────────

    [Fact]
    public void Gingerbrute_HasSacrificeForLifeAbility()
    {
        var c = GingerbruteFactory.Create(_alice);

        // {2}, {T}, Sacrifice this creature: You gain 3 life.
        var sacAbility = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(ac => ac.CostType == AdditionalCostType.Sacrifice));

        sacAbility.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed {2} mana cost");
        sacAbility.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap,
                "the printed {T} cost");
        sacAbility.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Sacrifice,
                "the printed sacrifice cost");
    }

    [Fact]
    public void SacrificeAbility_GainsThreeLife()
    {
        var c = GingerbruteFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var sacAbility = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(ac => ac.CostType == AdditionalCostType.Sacrifice));

        var before = _alice.LifeTotal;
        foreach (var effect in sacAbility.Effects) effect.Execute();

        (_alice.LifeTotal - before).Should().Be(3,
            "the printed effect gains 3 life (CR 119.3).");
    }

    // ── {1}: can't be blocked except by haste ───────────────────────────

    [Fact]
    public void Gingerbrute_HasEvasionAbility_OneGenericCost()
    {
        var c = GingerbruteFactory.Create(_alice);

        // The evasion ability has exactly one cost: {1} mana, no tap/sac.
        var evasion = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.Count == 1
                && a.Costs[0] is ManaCostCost);

        evasion.Costs.Should().ContainSingle().Which
            .Should().BeOfType<ManaCostCost>();
    }

    [Fact]
    public void EvasionAbility_BlocksNonHasteCreature_AllowsHasteCreature()
    {
        var effects = new ContinuousEffectsService();
        var c = GingerbruteFactory.Create(_alice, effects);
        c.SetZone(ZoneType.Battlefield);

        var evasion = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.Count == 1 && a.Costs[0] is ManaCostCost);

        // Resolve the {1} ability.
        foreach (var effect in evasion.Effects) effect.Execute();

        var vanillaBlocker = MakeBear(_bob);
        var hasteBlocker = MakeBear(_bob, "Haste");

        BlockLegality.CanBlock(hasteBlocker, c, out _).Should().BeTrue(
            "a creature with haste may block Gingerbrute (CR 509.1b).");
        BlockLegality.CanBlock(vanillaBlocker, c, out _).Should().BeFalse(
            "a creature without haste can't block Gingerbrute after the {1} ability resolves.");
    }

    [Fact]
    public void EvasionAbility_RestrictionExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var c = GingerbruteFactory.Create(_alice, effects);
        c.SetZone(ZoneType.Battlefield);

        var evasion = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.Count == 1 && a.Costs[0] is ManaCostCost);
        foreach (var effect in evasion.Effects) effect.Execute();

        // "this turn" — the restriction lifts at end of turn (CR 514.2).
        effects.ExpireEndOfTurn();

        var vanillaBlocker = MakeBear(_bob);
        BlockLegality.CanBlock(vanillaBlocker, c, out _).Should().BeTrue(
            "the can't-be-blocked restriction is until end of turn only.");
    }

    [Fact]
    public void EvasionAbility_NoEffectsService_DoesNotThrow()
    {
        var c = GingerbruteFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var evasion = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.Count == 1 && a.Costs[0] is ManaCostCost);

        var act = () => { foreach (var e in evasion.Effects) e.Execute(); };
        act.Should().NotThrow("the shape-only path no-ops without a continuous-effects service.");
    }
}
