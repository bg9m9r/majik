using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RiteOfFlameFactory"/>.
///
/// Rite of Flame (Coldsnap, {R}, Sorcery):
///   "Add {R}{R}. Then add {R} for each card named Rite of Flame in
///    your graveyard."
///
/// Covers:
///   - Card identity (name, sorcery type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with empty graveyard — base output (+2 R, no bonus).
///   - Resolve with N copies of Rite of Flame already in the graveyard —
///     adds +N red mana on top of the base {R}{R} (CR 608.2f — count
///     sampled at resolution; the resolving spell itself is on the
///     stack, not yet in the graveyard).
///   - Non-matching graveyard cards don't contribute to the bonus
///     (name match is exact).
/// </summary>
public class RiteOfFlameTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RiteOfFlame_HasExpectedShape()
    {
        var card = RiteOfFlameFactory.Create(_alice);

        card.Name.Should().Be("Rite of Flame");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RiteOfFlame()
    {
        var card = NamedCardFactory.Create("Rite of Flame", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Rite of Flame");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_EmptyGraveyard_AddsTwoRed()
    {
        _alice.ManaPool.Total.Should().Be(0);
        RiteOfFlameFactory.CountCopiesInGraveyard(_alice).Should().Be(0);

        var effect = RiteOfFlameFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(2);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(2);
    }

    [Fact]
    public void Resolve_OneCopyInGraveyard_AddsThreeRed()
    {
        // CR 608.2f — count is sampled at resolution. With one previously
        // cast Rite of Flame already in the graveyard, the resolving copy
        // produces {R}{R} + {R} = three red.
        SeedRiteOfFlameInGraveyard(1);

        var effect = RiteOfFlameFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(3);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_ThreeCopiesInGraveyard_AddsFiveRed()
    {
        // Graveyard-scaling: {R}{R} base + {R} × 3 = five red.
        SeedRiteOfFlameInGraveyard(3);
        RiteOfFlameFactory.CountCopiesInGraveyard(_alice).Should().Be(3);

        var effect = RiteOfFlameFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(5);
        _alice.ManaPool.Total.Should().Be(5);
    }

    [Fact]
    public void Resolve_OtherNamedCardsInGraveyard_DoNotContribute()
    {
        // Only cards named exactly "Rite of Flame" count toward the
        // graveyard bonus. Other ritual-shaped cards in the graveyard
        // (e.g. Pyretic Ritual, Desperate Ritual) don't trigger the
        // bonus despite their thematic similarity.
        SeedNamed("Pyretic Ritual", 2);
        SeedNamed("Desperate Ritual", 2);
        SeedRiteOfFlameInGraveyard(1);

        RiteOfFlameFactory.CountCopiesInGraveyard(_alice).Should().Be(1);

        var effect = RiteOfFlameFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // Base {R}{R} + 1 from the single Rite of Flame in graveyard.
        _alice.ManaPool.Red.Should().Be(3);
        _alice.ManaPool.Total.Should().Be(3);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SeedRiteOfFlameInGraveyard(int count) =>
        SeedNamed(RiteOfFlameFactory.CardName, count);

    private void SeedNamed(string name, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card(name, "");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
    }
}
