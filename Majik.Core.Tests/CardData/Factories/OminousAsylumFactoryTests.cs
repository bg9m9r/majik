using System.Linq;
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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OminousAsylumFactory"/> — Ominous Asylum (Duskmourn:
/// House of Horror, B/R surveil land). Oracle text (Scryfall-verified
/// 2026-06-24):
///   "This land enters tapped.
///    {T}: Add {B} or {R}.
///    {4}, {T}: Surveil 1. (Look at the top card of your library. You may put
///    it into your graveyard.)"
///
/// Covers ONLY the card's unique behaviour: the two {B}/{R} mana abilities and
/// the repeatable "{4}, {T}: Surveil 1" ACTIVATED ability (distinct from the
/// Murders at Karlov Manor surveil-land cycle, which surveils once on ETB).
/// Dispatch + well-formedness are already asserted for every implemented card by
/// CardFactoryContractTests. Enters-tapped (CR 614.1c) is applied by
/// EntersTappedBinder on the production load path, not by the named factory.
/// </summary>
[Trait("Color", "M")] // B/R dual land — multicolour for test sharding.
public class OminousAsylumFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land Create() => OminousAsylumFactory.Create(_alice);

    [Fact]
    public void OminousAsylum_Identity_IsLandWithNoManaCost()
    {
        var land = Create();

        land.Name.Should().Be("Ominous Asylum");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // ----- {T}: Add {B} or {R} — two single-colour mana abilities (CR 605.1a) -

    [Fact]
    public void OminousAsylum_HasManaAbility_ForBlack()
    {
        var land = Create();

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                      && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void OminousAsylum_HasManaAbility_ForRed()
    {
        var land = Create();

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                      && m.ManaGenerated.Black == 0);
    }

    // ----- {4}, {T}: Surveil 1 — repeatable activated ability (CR 701.42) -----

    [Fact]
    public void OminousAsylum_HasOneActivatedSurveilAbility()
    {
        var land = Create();

        // Exactly one non-mana activated ability (the surveil one).
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void OminousAsylum_SurveilAbility_CostsFourGenericPlusTap()
    {
        var land = Create();
        var surveil = land.Abilities.OfType<ActivatedAbility>().Single();

        // {4} mana + {T}: two cost components.
        surveil.Costs.Should().HaveCount(2);
        surveil.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.Generic.Should().Be(4);
        // The {T} component is an AdditionalCost.Tap(self) (Primitives.Costs.TapSelf).
        surveil.Costs.OfType<AdditionalCost>().Should().ContainSingle();
    }

    [Fact]
    public void OminousAsylum_SurveilEffect_PutsTopCardInGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = OminousAsylumFactory.Create(alice);
        var surveil = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in surveil.Effects) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Graveyard);
    }
}
