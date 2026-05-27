using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NightsWhisperFactory"/>.
///
/// Card: Night's Whisper — Sorcery {1}{B} (Fifth Dawn).
///   "You draw two cards and you lose 2 life."
///
/// Covers:
/// - Identity ({1}{B} black Sorcery, correct name/owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: caster draws exactly 2 cards (library shrinks by 2).
/// - Resolve: caster loses exactly 2 life (CR 119.3).
/// - Resolve on empty library: stamps the SBA flag (CR 704.5b), life
///   still drains 2.
/// </summary>
public class NightsWhisperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NightsWhisper_Identity()
    {
        var card = NightsWhisperFactory.Create(_alice);

        card.Name.Should().Be("Night's Whisper");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue("Night's Whisper is a Sorcery");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NightsWhisper_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Night's Whisper", _alice);

        card.Should().BeOfType<Sorcery>("Night's Whisper is a Sorcery");
        card.Name.Should().Be("Night's Whisper");
        card.ManaCost.Should().Be("{1}{B}");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw 2, lose 2
    // -----------------------------------------------------------------------

    [Fact]
    public void NightsWhisper_Resolve_DrawsTwoCards_LosesTwoLife()
    {
        var alice = new Player("Alice", 20);

        // Seed library with three known cards.
        var c1 = new Card("Card1", "");
        var c2 = new Card("Card2", "");
        var c3 = new Card("Card3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        foreach (var effect in NightsWhisperFactory.BuildResolveEffect(alice))
            effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "Night's Whisper draws exactly 2 cards (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(1,
            "two cards left the top of the library");
        alice.LifeTotal.Should().Be(18,
            "caster loses 2 life (CR 119.3)");
    }

    [Fact]
    public void NightsWhisper_Resolve_EmptyLibrary_StampsLossFlag_LifeStillLost()
    {
        var alice = new Player("Alice", 20);
        // Library intentionally empty.

        var act = () =>
        {
            foreach (var effect in NightsWhisperFactory.BuildResolveEffect(alice))
                effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
        alice.LifeTotal.Should().Be(18,
            "life loss happens regardless of draw success (CR 119.3)");
    }
}
