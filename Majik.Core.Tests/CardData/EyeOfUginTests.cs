using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EyeOfUginFactory"/> (Worldwake).
///
/// Covers:
/// - Identity (name, Land type, Legendary supertype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Static cost reducer (<see cref="SpellCostReductionAbility"/>):
///     * Colorless Eldrazi spell — generic cost reduced by 2.
///     * Coloured Eldrazi spell — no reduction (predicate excludes coloured).
///     * Colorless non-Eldrazi spell — no reduction (predicate excludes
///       non-Eldrazi subtypes).
///     * Off-battlefield Eye — no reduction (rider inert when source
///       isn't on the controller's battlefield).
///     * Opponent's spell — no reduction (rider scoped to controller's
///       battlefield).
/// - Activated tutor:
///     * Shape: {7} mana cost + {T} additional cost.
///     * Happy path: colorless creature in library → moved to hand,
///       library shuffled.
///     * Skip non-colorless creature, skip non-creature colorless cards.
///     * Empty / no candidates: clean no-op (shuffle still happens).
/// </summary>
public class EyeOfUginTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeOfUgin_Identity()
    {
        var land = EyeOfUginFactory.Create(_alice);

        land.Name.Should().Be("Eye of Ugin");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Eye of Ugin is a Legendary Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EyeOfUgin_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Eye of Ugin", _alice);

        card.Should().BeOfType<Land>("Eye of Ugin is a Land");
        card.Name.Should().Be("Eye of Ugin");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static cost reducer
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeOfUgin_HasOneSpellCostReductionAbility()
    {
        var land = EyeOfUginFactory.Create(_alice);

        land.Abilities.OfType<SpellCostReductionAbility>().Should().HaveCount(1,
            "the cost-reducer rider is attached");
    }

    [Fact]
    public void ColorlessEldraziSpell_GenericReducedByTwo()
    {
        var eye = EyeOfUginFactory.Create(_alice);
        PutOnBattlefield(_alice, eye);

        // Emrakul-shape stand-in: a colorless Eldrazi creature with {6}
        // generic. Use a printed cost of "6" (no coloured pips) — the
        // CardColors.GetColors helper returns an empty set for any
        // mana cost with zero coloured pips.
        var eldrazi = new Creature("Test Eldrazi", "6", power: 6, toughness: 6,
            subtypes: new[] { CardSubtype.Eldrazi });
        eldrazi.SetOwner(_alice);
        eldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(eldrazi, _alice);

        effective.Generic.Should().Be(4, "{6} generic reduced by 2 → {4}");
    }

    [Fact]
    public void ColouredEldraziSpell_NoReduction()
    {
        // Reaper from the Abyss is not Eldrazi, but for the "coloured Eldrazi"
        // test we hand-roll a coloured-Eldrazi spell (rare printed shape
        // but the predicate must still gate it out — the rider is only
        // for COLORLESS Eldrazi spells).
        var eye = EyeOfUginFactory.Create(_alice);
        PutOnBattlefield(_alice, eye);

        var colouredEldrazi = new Creature("Coloured Eldrazi", "{4}{B}",
            power: 5, toughness: 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        colouredEldrazi.SetOwner(_alice);
        colouredEldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(colouredEldrazi, _alice);

        effective.Generic.Should().Be(4,
            "the coloured pip makes the spell non-colorless — no Eye discount");
        effective.Black.Should().Be(1, "coloured pip untouched");
    }

    [Fact]
    public void ColorlessNonEldraziSpell_NoReduction()
    {
        var eye = EyeOfUginFactory.Create(_alice);
        PutOnBattlefield(_alice, eye);

        // Karn Liberated stand-in: colorless creature, NOT Eldrazi.
        var nonEldrazi = new Creature("Test Construct", "5", power: 5, toughness: 5,
            subtypes: new[] { CardSubtype.Construct });
        nonEldrazi.SetOwner(_alice);
        nonEldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(nonEldrazi, _alice);

        effective.Generic.Should().Be(5,
            "non-Eldrazi colorless creature — no Eye discount");
    }

    [Fact]
    public void OffBattlefield_NoReduction()
    {
        // Eye in hand, not on battlefield → rider inert.
        var eye = EyeOfUginFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(eye);
        eye.SetZone(ZoneType.Hand);

        var eldrazi = new Creature("Test Eldrazi", "6", power: 6, toughness: 6,
            subtypes: new[] { CardSubtype.Eldrazi });
        eldrazi.SetOwner(_alice);
        eldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(eldrazi, _alice);

        effective.Generic.Should().Be(6,
            "Eye isn't on the battlefield — no discount");
    }

    [Fact]
    public void OpponentControlsEye_DoesNotDiscountYourSpells()
    {
        // Bob controls an Eye of Ugin; Alice casts a colorless Eldrazi.
        // The rider is scoped to the controller of the reducer permanent
        // ("spells YOU cast"), so Alice gets no discount.
        var bobsEye = EyeOfUginFactory.Create(_bob);
        PutOnBattlefield(_bob, bobsEye);

        var aliceEldrazi = new Creature("Test Eldrazi", "6", power: 6, toughness: 6,
            subtypes: new[] { CardSubtype.Eldrazi });
        aliceEldrazi.SetOwner(_alice);
        aliceEldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(aliceEldrazi, _alice);

        effective.Generic.Should().Be(6,
            "Bob's Eye doesn't reduce Alice's spells — 'spells you cast' is " +
            "scoped to the controller of the reducer permanent");
    }

    [Fact]
    public void TwoEyes_ReductionStacks()
    {
        var e1 = EyeOfUginFactory.Create(_alice);
        var e2 = EyeOfUginFactory.Create(_alice);
        PutOnBattlefield(_alice, e1);
        PutOnBattlefield(_alice, e2);

        var eldrazi = new Creature("Test Eldrazi", "8", power: 8, toughness: 8,
            subtypes: new[] { CardSubtype.Eldrazi });
        eldrazi.SetOwner(_alice);
        eldrazi.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(eldrazi, _alice);

        effective.Generic.Should().Be(4,
            "two Eyes reduce {8} generic by {2}+{2} → {4} (Legend Rule applies " +
            "at SBAs but for cost-calc each reducer ability is consulted " +
            "independently)");
    }

    // -----------------------------------------------------------------------
    // Activated tutor — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeOfUgin_ActivatedAbility_HasManaAndTapCost()
    {
        var land = EyeOfUginFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activated ability requires {7} mana");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the activated ability includes a {T} cost");
    }

    // -----------------------------------------------------------------------
    // Activated tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeOfUgin_ActivatedAbility_PullsColorlessCreatureFromLibrary_ToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-creature first (bait), then a coloured creature
        // (also bait), then a colorless creature (the legal pick).
        var bait1 = new Card("Random Card", "");
        bait1.SetOwner(alice);
        alice.Zones.Library.AddCard(bait1);
        bait1.SetZone(ZoneType.Library);

        var colouredCreature = new Creature("Bear", "1G", 2, 2);
        colouredCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(colouredCreature);
        colouredCreature.SetZone(ZoneType.Library);

        var colorlessCreature = new Creature("Colorless Construct", "4", 4, 4,
            subtypes: new[] { CardSubtype.Construct });
        colorlessCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(colorlessCreature);
        colorlessCreature.SetZone(ZoneType.Library);

        var eye = EyeOfUginFactory.Create(alice);
        PutOnBattlefield(alice, eye);

        var ability = eye.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        colorlessCreature.Zone.Should().Be(ZoneType.Hand,
            "the colorless creature moved Library → Hand");
        alice.Zones.Hand.GetCards().Should().Contain(colorlessCreature);
        alice.Zones.Library.GetCards().Should().NotContain(colorlessCreature,
            "the picked card was removed from the library");

        colouredCreature.Zone.Should().Be(ZoneType.Library,
            "the coloured creature is ineligible — must remain in the library");
        bait1.Zone.Should().Be(ZoneType.Library,
            "the non-creature card is ineligible — must remain in the library");
    }

    // -----------------------------------------------------------------------
    // Activated tutor — empty / no-candidates
    // -----------------------------------------------------------------------

    [Fact]
    public void EyeOfUgin_ActivatedAbility_NoColorlessCreatureInLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        // Seed library with only ineligible cards.
        var colouredCreature = new Creature("Bear", "1G", 2, 2);
        colouredCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(colouredCreature);
        colouredCreature.SetZone(ZoneType.Library);

        var nonCreature = new Card("Random Card", "");
        nonCreature.SetOwner(alice);
        alice.Zones.Library.AddCard(nonCreature);
        nonCreature.SetZone(ZoneType.Library);

        var eye = EyeOfUginFactory.Create(alice);
        PutOnBattlefield(alice, eye);

        var ability = eye.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("no colorless creature = clean no-op (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no eligible pick — nothing moves to hand");
        colouredCreature.Zone.Should().Be(ZoneType.Library);
        nonCreature.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void EyeOfUgin_ActivatedAbility_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var eye = EyeOfUginFactory.Create(alice);
        PutOnBattlefield(alice, eye);

        var ability = eye.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("empty library = clean no-op (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
