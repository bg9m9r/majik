using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DryadArborFactory"/> — Dryad Arbor (Future Sight),
/// the only printed Land Creature with no mana cost.
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "(This land isn't a spell, it's affected by summoning sickness, and it
///    has \"{T}: Add {G}.\")"
///
/// The reminder text simply describes the consequences of its type line +
/// printed mana ability; the only modelled behaviour is the intrinsic
/// {T}: Add {G} (CR 605.1) plus the dual Land+Creature identity, the
/// Forest+Dryad subtypes, the 1/1 body (CR 208), and the green colour
/// indicator (CR 202.2c — it is green despite having no mana cost).
/// </summary>
public class DryadArborTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DryadArbor_IsLandCreature_ForestDryad_1_1()
    {
        var card = DryadArborFactory.Create(_alice);

        card.Name.Should().Be("Dryad Arbor");
        card.Should().BeOfType<Creature>("primary type is Creature so it is summoning-sick (CR 302.6)");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().Contain(CardSubtype.Forest);
        card.Subtypes.Should().Contain(CardSubtype.Dryad);
        card.Supertypes.Should().BeEmpty("Dryad Arbor is a nonbasic land, not basic");

        var creature = (Creature)card;
        creature.Power.Should().Be(1);
        creature.Toughness.Should().Be(1);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DryadArbor_IsGreen_ViaColorIndicator()
    {
        // CR 202.2c — Dryad Arbor has no mana cost but a green colour
        // indicator, so it is green.
        var card = DryadArborFactory.Create(_alice);

        CardColors.GetColors(card).Should().BeEquivalentTo(new[] { ManaColor.Green });
    }

    [Fact]
    public void DryadArbor_IsSummoningSick_OnEntry_ManaAbilityGated()
    {
        // CR 302.6 / 605.3a — because Dryad Arbor is a creature, its
        // "{T}: Add {G}" mana ability cannot be activated the turn it enters
        // (no haste). This is what the reminder text's "it's affected by
        // summoning sickness" describes; the engine enforces it automatically.
        var card = DryadArborFactory.Create(_alice);

        card.HasSummoningSickness.Should().BeTrue();

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse();
    }

    [Fact]
    public void DryadArbor_TapForGreen_OnceSummoningSicknessClears()
    {
        var card = DryadArborFactory.Create(_alice);
        // Simulate the controller having controlled it since their most
        // recent turn began (CR 302.6) — the untap step clears the flag.
        card.ClearSummoningSickness();

        var mana = card.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        produced.Green.Should().Be(1);
        produced.Generic.Should().Be(0);
        card.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DryadArbor()
    {
        var card = NamedCardFactory.Create("Dryad Arbor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dryad Arbor");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }
}
