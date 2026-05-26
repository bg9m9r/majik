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
/// Tests for <see cref="TolarianTerrorFactory"/>.
///
/// Tolarian Terror (Dominaria United, {6}{U}{U}):
///   Creature — Serpent 5/5.
///   This spell costs {1} less to cast for each instant and sorcery card in
///   your graveyard.
///   Ward {3}.
///
/// Covers:
///   - Card identity (Serpent 5/5, {6}{U}{U}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Cost reduction at 0 / 4 / 6 / 10 instants+sorceries in the caster's
///     graveyard (floor at the two blue pips per CR 117.7c).
///   - Non-instant/sorcery cards in graveyard do not reduce.
///   - Ward {3} keyword marker attached + <see cref="TolarianTerrorFactory.BuildWardEffect"/>
///     exposes a bound <see cref="Majik.Core.Keywords.WardEffect"/> with
///     the printed {3} cost.
/// </summary>
public class TolarianTerrorTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TolarianTerror_Identity()
    {
        var terror = TolarianTerrorFactory.Create(_alice);

        terror.Name.Should().Be("Tolarian Terror");
        terror.ManaCost.Should().Be("{6}{U}{U}");
        terror.HasType(CardType.Creature).Should().BeTrue();
        terror.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        terror.BasePower.Should().Be(5);
        terror.BaseToughness.Should().Be(5);
        terror.Owner.Should().BeSameAs(_alice);
        terror.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TolarianTerror_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Tolarian Terror", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Tolarian Terror");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);

        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "graveyard-count cost reducer");
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Ward");
    }

    // -----------------------------------------------------------------------
    // Cost reduction (CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void TolarianTerror_EmptyGraveyard_PaysFullCost()
    {
        // 0 instants / sorceries in graveyard → no reduction. Pays
        // {6}{U}{U}: generic = 6, U pips = 2.
        var terror = TolarianTerrorFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(terror, _alice);

        effective.Generic.Should().Be(6);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void TolarianTerror_FourInstantsOrSorceriesInGraveyard_ReducesGenericBy4()
    {
        // 4 instants / sorceries in graveyard → reduction by 4 generic.
        // Pays {2}{U}{U}: generic = 2, U pips = 2.
        var terror = TolarianTerrorFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 2);

        var effective = CostReduction.GetEffectiveCost(terror, _alice);

        effective.Generic.Should().Be(2);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void TolarianTerror_SixInstantsOrSorceriesInGraveyard_ReducesToUU()
    {
        // 6 in graveyard → reduction = 6 generic. Pays {U}{U}.
        var terror = TolarianTerrorFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 3);

        var effective = CostReduction.GetEffectiveCost(terror, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void TolarianTerror_TenInstantsOrSorceriesInGraveyard_FloorsAtColouredPips()
    {
        // 10 → reduction = 10 generic. Printed generic = 6; floors at 0.
        // Coloured pips untouched (CR 117.7c) — still pays {U}{U}.
        var terror = TolarianTerrorFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 5, sorceries: 5);

        var effective = CostReduction.GetEffectiveCost(terror, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void TolarianTerror_NonInstantSorceryGraveyardCards_DoNotReduce()
    {
        var terror = TolarianTerrorFactory.Create(_alice);
        AddToGraveyard(_alice, new Creature("Bear A", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }));

        var effective = CostReduction.GetEffectiveCost(terror, _alice);

        effective.Generic.Should().Be(6,
            "non-instant/sorcery cards don't trigger Tolarian Terror's reduction");
        effective.Blue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Ward {3}
    // -----------------------------------------------------------------------

    [Fact]
    public void TolarianTerror_BuildWardEffect_ExposesPrinted3Cost()
    {
        var terror = TolarianTerrorFactory.Create(_alice);
        var ward = TolarianTerrorFactory.BuildWardEffect(terror);

        ward.Source.Should().BeSameAs(terror);
        ward.Cost.Generic.Should().Be(3,
            "Ward {3} — 3 generic");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    private static void SeedGraveyardWithSpells(Player p, int instants, int sorceries)
    {
        for (var i = 0; i < instants; i++)
        {
            var c = new Instant($"Inst{i}", "{U}");
            AddToGraveyard(p, c);
        }
        for (var i = 0; i < sorceries; i++)
        {
            var c = new Sorcery($"Sorc{i}", "{U}");
            AddToGraveyard(p, c);
        }
    }
}
