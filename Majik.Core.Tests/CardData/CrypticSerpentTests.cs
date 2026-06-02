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
/// Tests for <see cref="CrypticSerpentFactory"/>.
///
/// Cryptic Serpent (Hour of Devastation, {5}{U}{U}):
///   Creature — Serpent 6/5.
///   This spell costs {1} less to cast for each instant and sorcery card in
///   your graveyard.
///
/// Covers:
///   - Card identity (Serpent 6/5, {5}{U}{U}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Cost reduction at 0 / 4 / 5 / 8 instants+sorceries in the caster's
///     graveyard (floor at the two blue pips per CR 117.7c).
///   - Non-instant/sorcery cards in graveyard do not reduce.
/// </summary>
public class CrypticSerpentTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CrypticSerpent_Identity()
    {
        var serpent = CrypticSerpentFactory.Create(_alice);

        serpent.Name.Should().Be("Cryptic Serpent");
        serpent.ManaCost.Should().Be("{5}{U}{U}");
        serpent.HasType(CardType.Creature).Should().BeTrue();
        serpent.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        serpent.BasePower.Should().Be(6);
        serpent.BaseToughness.Should().Be(5);
        serpent.Owner.Should().BeSameAs(_alice);
        serpent.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CrypticSerpent_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Cryptic Serpent", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Cryptic Serpent");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Serpent).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(6);
        ((Creature)card).BaseToughness.Should().Be(5);

        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "graveyard-count cost reducer");
    }

    // -----------------------------------------------------------------------
    // Cost reduction (CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void CrypticSerpent_EmptyGraveyard_PaysFullCost()
    {
        // 0 instants / sorceries in graveyard → no reduction. Pays
        // {5}{U}{U}: generic = 5, U pips = 2.
        var serpent = CrypticSerpentFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(serpent, _alice);

        effective.Generic.Should().Be(5);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void CrypticSerpent_FourInstantsOrSorceriesInGraveyard_ReducesGenericBy4()
    {
        // 4 instants / sorceries in graveyard → reduction by 4 generic.
        // Pays {1}{U}{U}: generic = 1, U pips = 2.
        var serpent = CrypticSerpentFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 2);

        var effective = CostReduction.GetEffectiveCost(serpent, _alice);

        effective.Generic.Should().Be(1);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void CrypticSerpent_FiveInstantsOrSorceriesInGraveyard_ReducesToUU()
    {
        // 5 in graveyard → reduction = 5 generic. Pays {U}{U}.
        var serpent = CrypticSerpentFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 2);

        var effective = CostReduction.GetEffectiveCost(serpent, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void CrypticSerpent_EightInstantsOrSorceriesInGraveyard_FloorsAtColouredPips()
    {
        // 8 → reduction = 8 generic. Printed generic = 5; floors at 0.
        // Coloured pips untouched (CR 117.7c) — still pays {U}{U}.
        var serpent = CrypticSerpentFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 4);

        var effective = CostReduction.GetEffectiveCost(serpent, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(2);
    }

    [Fact]
    public void CrypticSerpent_NonInstantSorceryGraveyardCards_DoNotReduce()
    {
        var serpent = CrypticSerpentFactory.Create(_alice);
        AddToGraveyard(_alice, new Creature("Bear A", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }));

        var effective = CostReduction.GetEffectiveCost(serpent, _alice);

        effective.Generic.Should().Be(5,
            "non-instant/sorcery cards don't trigger Cryptic Serpent's reduction");
        effective.Blue.Should().Be(2);
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
