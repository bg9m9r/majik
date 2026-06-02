using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OfOneMindFactory"/>.
///
/// Card: Of One Mind — Sorcery {2}{U} (Modern Horizons).
///   "This spell costs {2} less to cast if you control a Human creature and a
///    non-Human creature.
///    Draw two cards."
///
/// Covers:
///   - Identity (name, Sorcery type, mana cost {2}{U}, blue, CMC 3,
///     owner/controller). Base shape is materialised from the embedded JSON
///     definition via <see cref="CardDefinitionLoader.FromEmbeddedResource"/>.
///   - NamedCardFactory dispatch returns a Sorcery carrying the conditional
///     <see cref="CostReductionAbility"/> (CR 117.7).
///   - Conditional {2} reduction (CR 117.7) — applies only when the caster
///     controls BOTH a Human creature AND a non-Human creature; floor-at-zero
///     (CR 117.7c). The {U} pip survives the reduction (only generic reduces).
///   - Resolve effect draws two cards from top of library (CR 121.1).
///   - Empty library mid-resolve flags the SBA-driven loss (CR 704.5b).
/// </summary>
[Trait("Color", "U")]
public class OfOneMindFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutCreatureOnBattlefield(Player owner, string name, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "{0}", 1, 1, subtypes: subtypes);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }

    private ICard SeedLibraryCard(string name)
    {
        var c = new Sorcery(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void OfOneMind_HasSorceryShape_Blue_AtCost2U()
    {
        var card = OfOneMindFactory.Create(_alice);

        card.Name.Should().Be("Of One Mind");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void OfOneMind_DispatchesViaNamedCardFactory_CarriesCostReducer()
    {
        var card = NamedCardFactory.Create("Of One Mind", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Of One Mind");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the conditional {2}-less cost reducer is attached");
    }

    // -------------------------------------------------------------------------
    // Conditional cost reduction (CR 117.7)
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduction_NoCreatures_FullPrintedCost()
    {
        var card = OfOneMindFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(2, "no Human + non-Human pair → no reduction");
        effective.Blue.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Reduction_OnlyHuman_NoReduction()
    {
        var card = OfOneMindFactory.Create(_alice);
        PutCreatureOnBattlefield(_alice, "Human Soldier", CardSubtype.Human, CardSubtype.Soldier);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(2,
            "a Human creature alone does not satisfy the 'and a non-Human creature' clause");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Reduction_OnlyNonHuman_NoReduction()
    {
        var card = OfOneMindFactory.Create(_alice);
        PutCreatureOnBattlefield(_alice, "Goblin", CardSubtype.Goblin);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(2,
            "a non-Human creature alone does not satisfy the 'a Human creature and' clause");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Reduction_HumanAndNonHuman_TwoLess_BluePipSurvives()
    {
        var card = OfOneMindFactory.Create(_alice);
        PutCreatureOnBattlefield(_alice, "Human Soldier", CardSubtype.Human, CardSubtype.Soldier);
        PutCreatureOnBattlefield(_alice, "Goblin", CardSubtype.Goblin);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(0, "{2} reduced by {2} → {0} (CR 117.7)");
        effective.Blue.Should().Be(1, "the {U} pip is unaffected (CR 117.7c — only generic reduces)");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Reduction_HumanThatIsAlsoNonHumanCreaturePair_OneCreatureEach()
    {
        // A single Human creature cannot satisfy both halves of the clause —
        // it is a Human, never a non-Human. Two distinct creatures are needed.
        var card = OfOneMindFactory.Create(_alice);
        PutCreatureOnBattlefield(_alice, "Lone Human", CardSubtype.Human);

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(2,
            "one Human creature cannot also be the required non-Human creature");
    }

    // -------------------------------------------------------------------------
    // Resolve: draw two cards
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsExactlyTwoCards_LibraryShrinksByTwo()
    {
        var l1 = SeedLibraryCard("L1");
        var l2 = SeedLibraryCard("L2");
        SeedLibraryCard("L3"); // remains in library

        var effects = OfOneMindFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { l1, l2 });
        l1.Zone.Should().Be(ZoneType.Hand);
        l2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsLossSba_DoesNotThrow()
    {
        var act = () =>
        {
            var effects = OfOneMindFactory.BuildResolveEffect(_alice);
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library stamps the SBA loss flag (CR 704.5b)");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
