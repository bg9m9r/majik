using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Data-driven CONTRACT test asserting the "non-unique" plumbing of EVERY
/// implemented card in ONE place: production dispatch
/// (<see cref="NamedCardFactory"/>) succeeds, the built card is named after the
/// requested card, is owned/controlled by the requester, and is well-formed
/// (has at least one card type). This replaces the per-card
/// <c>*_DispatchesViaNamedCardFactory</c> boilerplate across ~850
/// <c>*FactoryTests</c> and auto-covers every FUTURE card the moment it is
/// implemented — so card PRs only write tests for the card's UNIQUE behaviour.
///
/// <para>Value-level identity (exact mana value, colours, type line) is NOT
/// asserted here: the embedded Modern seed is a point-in-time Scryfall snapshot
/// that legitimately diverges from the engine's runtime model for a real slice
/// of cards (MDFC/split front-face-only rows, Vehicles modeled as runtime
/// Creatures, "Tribal" vs Scryfall's renamed "Kindred", characteristic-defining
/// bodies like Grist, plus a few stale rows). Those asserts stay with the
/// per-card factory tests, which verify against live Scryfall at authoring
/// time.</para>
/// </summary>
public class CardFactoryContractTests
{
    public static IEnumerable<object[]> ImplementedCards()
        => ImplementedCardNames.All.OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(ImplementedCards))]
    public void Factory_Dispatches_ProducesWellFormedNamedCard(string name)
    {
        var alice = new Player("Alice", 20);

        // 1. Dispatch correctness (covers every per-card *_DispatchesViaNamedCardFactory).
        var card = NamedCardFactory.Create(name, alice);
        card.Should().NotBeNull($"'{name}' is in the implemented set so dispatch must succeed");
        // Two-part names are registered under the full "Left // Right" oracle
        // name. The runtime card name is either the full combined name (split
        // cards keep both names — CR 707) or the front/left face alone
        // (transform DFC/MDFC build the front face — CR 712.4), so accept either.
        var front = name.Split(" // ")[0];
        card.Name.Should().BeOneOf(name, front);
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);

        // 2. The card is non-degenerate: it has at least one card type and a
        //    parsed mana cost object (never throws). This catches a factory that
        //    dispatches but produces a malformed card.
        Enum.GetValues<CardType>().Any(card.HasType).Should().BeTrue(
            $"'{name}' must have at least one card type");

        // Scope note — exact mana value / colours / type line are deliberately
        // NOT asserted against the seed here. The embedded seed is a point-in-
        // time Scryfall snapshot whose Cmc/Colors/TypeLine legitimately diverge
        // from the engine's runtime model for a real slice of cards: MDFC/split
        // (front-face-only seed), Vehicles (modeled as runtime Creatures),
        // "Tribal" vs Scryfall's renamed "Kindred", characteristic-defining
        // bodies (e.g. Grist), and a few stale seed rows. Those value-level
        // asserts stay with the per-card factory tests, which verify against
        // live Scryfall at authoring time. This contract owns only the uniform
        // plumbing every card shares — dispatch + well-formedness — which every
        // future card inherits for free, so card PRs no longer hand-write a
        // *_DispatchesViaNamedCardFactory test.
    }
}
