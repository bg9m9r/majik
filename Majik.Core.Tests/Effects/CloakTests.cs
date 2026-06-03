using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Tests for the Cloak keyword-action primitive (CR 702.168), shipped to
/// unblock the Cloak cluster (Cryptic Coat).
///
/// Cloak is the sibling of Manifest (CR 701.31): a card is put onto the
/// battlefield face down as a 2/2 creature — the difference is that the
/// cloaked permanent additionally has <b>ward {2}</b> (CR 702.168a /
/// CR 708.4). Like manifest, it can be turned face up any time for its
/// mana cost if it's a creature card (CR 708.6).
///
/// Coverage mirrors <see cref="ManifestDreadTests"/>:
/// - Top of library cloaked as a face-down 2/2 on the battlefield.
/// - Face-down P/T override (CR 708.2 — 2/2 regardless of underlying P/T).
/// - Ward {2} is the cloaked permanent's only "static" ability while
///   face-down (CR 702.168a — ward is part of the cloak definition, so it
///   survives the CR 708.2 ability-suppression that hides the underlying
///   card's printed abilities).
/// - Creature-underlying: turn-face-up activated ability with the
///   underlying creature's printed mana cost.
/// - Turn face up: wrapper leaves, underlying creature takes its slot.
/// - Non-creature underlying: no turn-face-up ability granted.
/// - Empty library: clean no-op.
/// </summary>
public class CloakTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Cloak_TopOfLibrary_BecomesFaceDownTwoTwoOnBattlefield()
    {
        var top = new Creature("Top", "{2}{G}", 5, 5);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var wrapper = CloakEffect.Cloak(_alice);

        wrapper.Should().NotBeNull();
        wrapper!.UnderlyingCard.Should().BeSameAs(top);
        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.Power.Should().Be(2, "CR 708.2 — face-down creatures are 2/2");
        wrapper.Toughness.Should().Be(2);
        _alice.Zones.Battlefield.GetCards().Should().Contain(wrapper);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Cloak_FaceDownPermanent_HasWardTwo()
    {
        var top = new Creature("Top", "{1}{U}", 3, 3);
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);

        var wrapper = CloakEffect.Cloak(_alice)!;

        // CR 702.168a — a cloaked permanent has ward {2}. Even though
        // CR 708.2 hides the underlying card's printed abilities, ward is
        // part of the cloak definition, so it is one of the face-down
        // permanent's abilities and is surfaced by EffectiveAbilities.
        var ward = wrapper.EffectiveAbilities
            .OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Ward");
        ward.Should().NotBeNull("a cloaked permanent has ward {2}");
        ward!.Arg.Should().Be(2);
    }

    [Fact]
    public void Cloak_FaceDown_DoesNotLeakUnderlyingPrintedAbilities()
    {
        // Underlying has Flying; the wrapper must NOT expose it while
        // face-down (CR 708.2). Only the cloak ward + turn-face-up show.
        var underlying = new Creature("Flier", "{1}{U}", 2, 2);
        underlying.AddAbility(new KeywordAbility("Flying", underlying, _alice));
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = CloakEffect.Cloak(_alice)!;

        wrapper.EffectiveAbilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "CR 708.2 — face-down permanents do not have the underlying card's printed abilities");
    }

    [Fact]
    public void Cloak_CreatureUnderlying_GrantsTurnFaceUpForPrintedManaCost()
    {
        var underlying = new Creature("Underlying", "{2}{B}", 4, 4);
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = CloakEffect.Cloak(_alice)!;

        var faceUp = wrapper.Abilities.OfType<FaceDownActivatedAbility>().Single();
        faceUp.Costs.Should().HaveCount(1,
            "CR 708.6 — turn face up for the underlying creature's printed mana cost");
    }

    [Fact]
    public void Cloak_TurnFaceUp_SwapsWrapperForUnderlying()
    {
        var underlying = new Creature("Underlying", "{2}{B}", 4, 4);
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = CloakEffect.Cloak(_alice)!;
        var flipped = wrapper.TryTurnFaceUp();

        flipped.Should().BeSameAs(underlying);
        wrapper.IsFaceDown.Should().BeFalse();
        _alice.Zones.Battlefield.GetCards().Should().Contain(underlying);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wrapper);
        ((Creature)underlying).Power.Should().Be(4);
    }

    [Fact]
    public void Cloak_NonCreatureUnderlying_NoTurnFaceUpAbility()
    {
        var sorcery = new Sorcery("Some Sorcery", "{1}{R}");
        sorcery.SetOwner(_alice);
        _alice.Zones.Library.AddCard(sorcery);

        var wrapper = CloakEffect.Cloak(_alice)!;

        wrapper.UnderlyingCard.Should().BeSameAs(sorcery);
        wrapper.Abilities.OfType<FaceDownActivatedAbility>().Should().BeEmpty(
            "CR 708.6 — turn-face-up only granted if the underlying card is a creature");
        // ...but it still has ward {2} (the cloak ward does not depend on
        // the underlying card being a creature).
        wrapper.EffectiveAbilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Ward");
    }

    [Fact]
    public void Cloak_EmptyLibrary_IsCleanNoOp()
    {
        var wrapper = CloakEffect.Cloak(_alice);

        wrapper.Should().BeNull();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
