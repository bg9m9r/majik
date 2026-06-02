using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MulldrifterFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - Keyword markers — Flying + Evoke (CR 702.9 / CR 702.74).
/// - NamedCardFactory dispatch.
/// - ETB triggered ability draws 2 cards from controller's library.
/// - ETB draws stamp the empty-library SBA flag (CR 704.5b) on shortage.
/// - Evoke sacrifice trigger has the intervening-if reading EvokeWasPaid
///   (CR 702.74b) — the trigger body sacrifices the creature when paid.
/// - Hard-cast posture: EvokeWasPaid stays false → sacrifice trigger
///   never queues (intervening-if drops it at CR 603.4 queue time).
/// </summary>
[Trait("Color", "U")]
public class MulldrifterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Mulldrifter_Identity()
    {
        var c = MulldrifterFactory.Create(_alice);

        c.Name.Should().Be("Mulldrifter");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.ManaCost.Should().Be("{4}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Mulldrifter_HasFlyingAndEvokeMarkers()
    {
        var c = MulldrifterFactory.Create(_alice);

        var keywordNames = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywordNames.Should().Contain(new[] { "Flying", "Evoke" });
    }
    // -----------------------------------------------------------------------
    // ETB triggered ability — draw 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Mulldrifter_EtbTrigger_DrawsTwoCards()
    {
        var alice = new Player("Alice", 20);

        // Seed library with three known cards.
        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        var c3 = new Card("Top3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var mulldrifter = MulldrifterFactory.Create(alice);
        // Two triggered abilities: Evoke-sacrifice + ETB-draw. The ETB-draw
        // is the one with no intervening-if.
        var etbDraw = mulldrifter.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        foreach (var effect in etbDraw.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(2, "draw 2 (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(1, "two cards left the top");
    }

    [Fact]
    public void Mulldrifter_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);
        // Library is empty.

        var mulldrifter = MulldrifterFactory.Create(alice);
        var etbDraw = mulldrifter.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is null);

        var act = () =>
        {
            foreach (var effect in etbDraw.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws (CR 704.5b loss flag is stamped)");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }

    // -----------------------------------------------------------------------
    // Evoke sacrifice intervening-if
    // -----------------------------------------------------------------------

    [Fact]
    public void Mulldrifter_EvokeSacTrigger_HasInterveningIf_ReadsEvokeWasPaid()
    {
        var c = MulldrifterFactory.Create(_alice);

        var sacTrigger = c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is not null);

        // EvokeWasPaid is false by default → intervening-if evaluates false.
        c.EvokeWasPaid.Should().BeFalse();
        sacTrigger.InterveningIf!().Should().BeFalse(
            "CR 603.4 — Evoke sacrifice trigger drops at queue-time when EvokeWasPaid is false");

        // Flip the flag (alt-cost OnResolved simulation) and re-check.
        c.EvokeWasPaid = true;
        sacTrigger.InterveningIf!().Should().BeTrue(
            "CR 702.74b — Evoke sacrifice trigger queues when the alt-cost was paid");
    }

    [Fact]
    public void Mulldrifter_EvokeSacEffect_MovesCreatureToGraveyard_WhenOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var mulldrifter = MulldrifterFactory.Create(alice);

        // Place on battlefield + flip the evoke flag.
        alice.Zones.Battlefield.AddCard(mulldrifter);
        mulldrifter.SetZone(ZoneType.Battlefield);
        mulldrifter.EvokeWasPaid = true;

        var sacTrigger = mulldrifter.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.InterveningIf is not null);

        foreach (var effect in sacTrigger.Effects) effect.Execute();

        mulldrifter.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves the creature to its owner's graveyard");
        alice.Zones.Graveyard.GetCards().Should().Contain(mulldrifter);
        alice.Zones.Battlefield.GetCards().Should().NotContain(mulldrifter);
    }
}
