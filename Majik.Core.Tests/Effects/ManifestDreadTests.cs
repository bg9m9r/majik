using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Tests for the manifest dread primitive (CR 701.59) shipped alongside
/// Abhorrent Oculus's real upkeep trigger.
///
/// Coverage:
/// - Top two of library: one manifested face-down 2/2 on battlefield,
///   one to graveyard.
/// - Face-down P/T override (CR 708.2 — 2/2 regardless of underlying
///   printed P/T).
/// - Face-down ability suppression (CR 708.2 — face-down permanent has
///   no abilities other than the face-up activation).
/// - Creature-underlying: turn-face-up activated ability exists with
///   the underlying creature's printed mana cost.
/// - Activate turn-face-up: wrapper leaves battlefield, underlying
///   creature takes its place with native characteristics.
/// - Non-creature underlying: no turn-face-up ability granted.
/// - Empty library: clean no-op.
/// - One-card library: that card is manifested, no graveyard step.
/// </summary>
public class ManifestDreadTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Resolve_TopOfLibraryIsManifested_SecondGoesToGraveyard()
    {
        // Top of library = first added (Zone.AddCard appends, index 0
        // is the top).
        var top = new Creature("Top", "{2}{G}", 3, 3);
        var second = new Card("Second", "{1}{R}");
        top.SetOwner(_alice);
        second.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Library.AddCard(second);

        var wrapper = ManifestDreadEffect.Resolve(_alice);

        wrapper.Should().NotBeNull();
        wrapper!.UnderlyingCard.Should().BeSameAs(top);
        _alice.Zones.Battlefield.GetCards().Should().Contain(wrapper);
        _alice.Zones.Graveyard.GetCards().Should().Contain(second);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ManifestedCreature_HasFaceDownTwoTwoOverride_RegardlessOfUnderlying()
    {
        // Underlying is a 5/5 — wrapper should still report 2/2 while
        // face-down.
        var underlying = new Creature("Big Underlying", "{4}{G}", 5, 5);
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.Power.Should().Be(2, "CR 708.2 — face-down creatures are 2/2");
        wrapper.Toughness.Should().Be(2);
    }

    [Fact]
    public void ManifestedCreature_FaceDown_HasNoEffectiveAbilitiesBeyondFaceUpActivation()
    {
        // Underlying creature has a printed Flying keyword; the wrapper
        // does NOT inherit any of its abilities while face-down
        // (CR 708.2).
        var underlying = new Creature("Underlying With Flying", "{1}{U}", 2, 2);
        underlying.AddAbility(new KeywordAbility("Flying", underlying, _alice));
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        // Wrapper has its own FaceDownActivatedAbility for turn-face-up;
        // the underlying creature's Flying does NOT leak through.
        wrapper.EffectiveAbilities.Should().OnlyContain(a => a is FaceDownActivatedAbility);
        wrapper.EffectiveAbilities.OfType<KeywordAbility>().Should().BeEmpty(
            "face-down creatures have no abilities other than the face-up activation");
    }

    [Fact]
    public void ManifestedCreature_CreatureUnderlying_GrantsTurnFaceUpAbility()
    {
        var underlying = new Creature("Underlying Creature", "{2}{B}", 3, 3);
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        var faceUpAbility = wrapper.Abilities.OfType<FaceDownActivatedAbility>().Single();
        faceUpAbility.Costs.Should().HaveCount(1,
            "the turn-face-up cost is the underlying creature's printed mana cost");
    }

    [Fact]
    public void ManifestedCreature_NonCreatureUnderlying_NoTurnFaceUpAbility()
    {
        var sorcery = new Sorcery("Some Sorcery", "{1}{R}");
        sorcery.SetOwner(_alice);
        _alice.Zones.Library.AddCard(sorcery);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        wrapper.UnderlyingCard.Should().BeSameAs(sorcery);
        wrapper.Abilities.OfType<FaceDownActivatedAbility>().Should().BeEmpty(
            "CR 701.59c — turn-face-up granted only if the card is a creature");
    }

    [Fact]
    public void TurnFaceUp_SwapsWrapperForUnderlyingOnBattlefield()
    {
        var underlying = new Creature("Underlying", "{2}{B}", 3, 3);
        underlying.SetOwner(_alice);
        _alice.Zones.Library.AddCard(underlying);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        var flipped = wrapper.TryTurnFaceUp();

        flipped.Should().BeSameAs(underlying);
        wrapper.IsFaceDown.Should().BeFalse();
        _alice.Zones.Battlefield.GetCards().Should().Contain(underlying);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(wrapper);
        underlying.Zone.Should().Be(ZoneType.Battlefield);
        // Native characteristics restored — the underlying creature has
        // its printed P/T accessible via its own GetPower / GetToughness.
        ((Creature)underlying).Power.Should().Be(3);
        ((Creature)underlying).Toughness.Should().Be(3);
        ((Creature)underlying).Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TurnFaceUp_NonCreatureUnderlying_NoOpReturnsNull()
    {
        var sorcery = new Sorcery("Some Sorcery", "{2}");
        sorcery.SetOwner(_alice);
        _alice.Zones.Library.AddCard(sorcery);

        var wrapper = ManifestDreadEffect.Resolve(_alice)!;

        var flipped = wrapper.TryTurnFaceUp();

        flipped.Should().BeNull("non-creature underlying cards cannot be turned face up");
        wrapper.IsFaceDown.Should().BeTrue("flip refused — wrapper stays face-down");
        _alice.Zones.Battlefield.GetCards().Should().Contain(wrapper);
    }

    [Fact]
    public void Resolve_EmptyLibrary_IsCleanNoOp()
    {
        var wrapper = ManifestDreadEffect.Resolve(_alice);

        wrapper.Should().BeNull();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_OneCardLibrary_ManifestsThatCard_NoGraveyardStep()
    {
        var only = new Creature("Only Card", "{1}{B}", 1, 1);
        only.SetOwner(_alice);
        _alice.Zones.Library.AddCard(only);

        var wrapper = ManifestDreadEffect.Resolve(_alice);

        wrapper.Should().NotBeNull();
        wrapper!.UnderlyingCard.Should().BeSameAs(only);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "one-card library: nothing left to send to the graveyard");
        _alice.Zones.Battlefield.GetCards().Should().Contain(wrapper);
    }
}
