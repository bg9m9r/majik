using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HazoretTheFerventFactory"/> (Amonkhet,
/// {3}{R}).
///
/// Legendary Creature — God 5/4. Oracle text (verified against Scryfall):
///   "Indestructible, haste
///    Hazoret can't attack or block unless you have one or fewer cards in
///    hand.
///    {2}{R}, Discard a card: Hazoret deals 2 damage to each opponent."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Indestructible + Haste keyword markers.
///   - "Can't attack or block unless one-or-fewer cards in hand"
///     predicate-mode CombatRestrictionEffects (CannotAttack +
///     CannotBlock), gated to Hazoret, evaluated against the controller's
///     live hand size.
///   - {2}{R}, Discard a card activated ability dealing 2 damage to each
///     opponent.
/// </summary>
[Trait("Color", "R")]
public class HazoretTheFerventFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsLegendaryGodShape()
    {
        var hazoret = HazoretTheFerventFactory.Create(_alice);

        hazoret.Should().BeOfType<Creature>();
        hazoret.Name.Should().Be("Hazoret the Fervent");
        hazoret.Power.Should().Be(5);
        hazoret.Toughness.Should().Be(4);
        hazoret.ManaCost.Should().Be("{3}{R}");
        hazoret.ManaCostValue.TotalValue.Should().Be(4);
        hazoret.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        hazoret.HasSubtype(CardSubtype.God).Should().BeTrue();
        hazoret.Owner.Should().BeSameAs(_alice);
        hazoret.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_AttachesIndestructible_AndHaste()
    {
        var hazoret = HazoretTheFerventFactory.Create(_alice);

        var keywords = hazoret.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Indestructible");
        keywords.Should().Contain("Haste");
    }

    // -------------------------------------------------------------------------
    // Can't attack / block unless one or fewer cards in hand
    // -------------------------------------------------------------------------

    [Fact]
    public void TwoOrMoreCardsInHand_HazoretCannotAttackOrBlock()
    {
        var effects = new ContinuousEffectsService();
        var hazoret = HazoretTheFerventFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(hazoret);
        hazoret.SetZone(ZoneType.Battlefield);

        // Two cards in hand — over the threshold.
        for (var i = 0; i < 2; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack)
            .Should().BeTrue("two cards in hand > one — can't attack");
        effects.HasRestriction(hazoret, CombatRestriction.CannotBlock)
            .Should().BeTrue("two cards in hand > one — can't block");
    }

    [Fact]
    public void OneOrFewerCardsInHand_HazoretCanAttackAndBlock()
    {
        var effects = new ContinuousEffectsService();
        var hazoret = HazoretTheFerventFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(hazoret);
        hazoret.SetZone(ZoneType.Battlefield);

        // One card in hand — at the threshold; restriction lifts.
        var c = new Card("Filler", "");
        c.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(c);

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack)
            .Should().BeFalse("one card in hand satisfies 'one or fewer'");
        effects.HasRestriction(hazoret, CombatRestriction.CannotBlock)
            .Should().BeFalse("one card in hand satisfies 'one or fewer'");
    }

    [Fact]
    public void Restriction_DropToOneCard_LiftsImmediately()
    {
        var effects = new ContinuousEffectsService();
        var hazoret = HazoretTheFerventFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(hazoret);
        hazoret.SetZone(ZoneType.Battlefield);

        var c1 = new Card("A", "");
        c1.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(c1);
        var c2 = new Card("B", "");
        c2.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(c2);

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack).Should().BeTrue();

        // Discarding one card drops the hand to 1 — restriction recomputes.
        _alice.Zones.Hand.RemoveCard(c2);

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack)
            .Should().BeFalse("predicate re-reads live hand size every pass");
    }

    [Fact]
    public void Restriction_GatedToHazoretOnly_NotOtherCreatures()
    {
        var effects = new ContinuousEffectsService();
        var hazoret = HazoretTheFerventFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(hazoret);
        hazoret.SetZone(ZoneType.Battlefield);

        // Two cards in hand — Hazoret is locked, but an unrelated creature
        // must be unaffected (the restriction is scoped to Hazoret).
        for (var i = 0; i < 2; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack).Should().BeTrue();
        effects.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("the restriction is scoped to Hazoret only");
    }

    [Fact]
    public void Restriction_SuppressedOffBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var hazoret = HazoretTheFerventFactory.Create(_alice, effects);
        // Not on the battlefield — static restriction is suppressed
        // (CR 603.6e). Two cards in hand would otherwise lock it.
        for (var i = 0; i < 2; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Hand.AddCard(c);
        }

        effects.HasRestriction(hazoret, CombatRestriction.CannotAttack)
            .Should().BeFalse("static restriction functions only on the battlefield");
    }

    // -------------------------------------------------------------------------
    // Activated ability — {2}{R}, Discard a card: 2 damage to each opponent
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_Cost_IsTwoRAndDiscardACard()
    {
        var hazoret = HazoretTheFerventFactory.Create(_alice);
        var ability = hazoret.Abilities.OfType<ActivatedAbility>().Single();

        var manaCost = ability.Costs.OfType<ManaCostCost>().SingleOrDefault();
        manaCost.Should().NotBeNull();
        manaCost!.Cost.TotalValue.Should().Be(3, "2 generic + 1 red = 3");
        manaCost!.Cost.Red.Should().Be(1);

        ability.Costs.OfType<DiscardACardCost>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_DealsTwoToEachOpponent()
    {
        var hazoret = HazoretTheFerventFactory.Create(_alice, opponentsResolver: () => new[] { _bob });

        var bobLifeBefore = _bob.LifeTotal;

        var ability = hazoret.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore - HazoretTheFerventFactory.ActivatedDamage);
    }

    [Fact]
    public void ActivatedAbility_NoOpponentsResolver_NoOp()
    {
        // Single-arg overload — no opponents resolver; the burn finds no
        // opponents (defensive — shape-only tests don't deal damage).
        var hazoret = HazoretTheFerventFactory.Create(_alice);

        var bobLifeBefore = _bob.LifeTotal;

        var ability = hazoret.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }
}
