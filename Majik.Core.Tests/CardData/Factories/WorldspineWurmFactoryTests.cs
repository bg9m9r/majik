using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WorldspineWurmFactory"/>.
///
/// Worldspine Wurm (Return to Ravnica, {8}{G}{G}{G}). Creature — Wurm 15/15.
/// Oracle text (verified against Scryfall):
///   "Trample
///    When this creature dies, create three 5/5 green Wurm creature tokens
///    with trample.
///    When Worldspine Wurm is put into a graveyard from anywhere, shuffle it
///    into its owner's library."
///
/// Coverage:
/// - Identity (name, type, subtype, cost, mv, green, P/T, owner/controller).
/// - NamedCardFactory dispatch (covered by the shared dispatcher suite).
/// - Trample marker (CR 702.19).
/// - Dies trigger (CR 603.6c / 700.4): create three 5/5 green Wurm tokens
///   with Trample.
/// - Put-into-graveyard-from-anywhere trigger (CR 603.6c): shuffle Worldspine
///   Wurm itself into its owner's library; fires from any origin zone.
/// </summary>
[Trait("Color", "G")]
public class WorldspineWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ────────────────────────────────────────────────────────

    [Fact]
    public void WorldspineWurm_Identity()
    {
        var c = WorldspineWurmFactory.Create(_alice);

        c.Name.Should().Be("Worldspine Wurm");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.ManaCost.Should().Be("{8}{G}{G}{G}");
        c.ManaCostValue.TotalValue.Should().Be(11);
        c.BasePower.Should().Be(15);
        c.BaseToughness.Should().Be(15);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Trample ─────────────────────────────────────────────────────────

    [Fact]
    public void WorldspineWurm_HasTrample()
    {
        var c = WorldspineWurmFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "CR 702.19 — Worldspine Wurm has trample.");
        CombatAbilities.HasTrample(c).Should().BeTrue();
    }

    // ── Dies trigger — three 5/5 green Wurm tokens with trample ──────────

    // The dies trigger and the shuffle trigger both use
    // EventTriggerCondition<CardMovedEvent>; the dies trigger is the one that
    // does NOT fire on a Hand → Graveyard move (it gates on FromZone ==
    // Battlefield, CR 700.4), whereas the "from anywhere" shuffle trigger does.
    private static TriggeredAbility DiesTrigger(Card card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t =>
            t.Condition.Matches(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard), t)
            && !t.Condition.Matches(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard), t));

    private static TriggeredAbility ShuffleTrigger(Card card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t =>
            t.Condition.Matches(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard), t));

    [Fact]
    public void DiesTrigger_FiresOnBattlefieldToGraveyard()
    {
        var card = WorldspineWurmFactory.Create(_alice);
        var dies = DiesTrigger(card);

        // CR 700.4 — "dies" is battlefield → graveyard.
        dies.Condition
            .Matches(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard), dies)
            .Should().BeTrue();
        // Does not fire on a non-battlefield → graveyard move (that is the
        // separate "from anywhere" shuffle trigger, not "dies").
        dies.Condition
            .Matches(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard), dies)
            .Should().BeFalse();
    }

    [Fact]
    public void DiesTrigger_CreatesThreeWurmTokens()
    {
        var card = WorldspineWurmFactory.Create(_alice);
        card.SetController(_alice);
        var dies = DiesTrigger(card);

        foreach (var e in dies.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Wurm")
            .ToList();

        tokens.Should().HaveCount(3, "the dies trigger creates three Wurm tokens.");
        tokens.Should().OnlyContain(t => t.BasePower == 5 && t.BaseToughness == 5,
            "each token is 5/5.");
        tokens.Should().OnlyContain(t => t.HasSubtype(CardSubtype.Wurm));
        tokens.Should().OnlyContain(t => CardColors.GetColors(t).Contains(ManaColor.Green),
            "each token is green (CR 111.4).");
        tokens.Should().OnlyContain(t => CombatAbilities.HasTrample(t),
            "CR 702.19 — each token has trample.");
    }

    // ── Put-into-graveyard-from-anywhere — shuffle itself in ────────────

    [Fact]
    public void ShuffleTrigger_ActiveFromAnywhere()
    {
        var card = WorldspineWurmFactory.Create(_alice);
        var shuffle = ShuffleTrigger(card);

        // "from anywhere" — fires on a graveyard arrival regardless of origin.
        foreach (var from in new[]
                 {
                     ZoneType.Battlefield, ZoneType.Hand, ZoneType.Library,
                     ZoneType.Stack, ZoneType.Exile,
                 })
        {
            shuffle.Condition
                .Matches(new CardMovedEvent(card, from, ZoneType.Graveyard), shuffle)
                .Should().BeTrue($"the trigger fires when Worldspine Wurm enters a graveyard from {from}.");
        }

        // Does NOT fire when moving to a non-graveyard zone.
        shuffle.Condition
            .Matches(new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Exile), shuffle)
            .Should().BeFalse();
    }

    [Fact]
    public void ShuffleTrigger_MovesItselfFromGraveyardIntoLibrary()
    {
        var card = WorldspineWurmFactory.Create(_alice);
        // The Wurm itself sits in the graveyard; an unrelated card also sits
        // there and must stay (only Worldspine Wurm is shuffled in).
        card.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(card);

        var bystander = new Instant("Bystander", "{1}");
        bystander.SetOwner(_alice);
        bystander.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bystander);

        var shuffle = ShuffleTrigger(card);

        foreach (var e in shuffle.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Contain(card,
            "Worldspine Wurm is shuffled into its owner's library.");
        card.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card,
            "the Wurm leaves the graveyard.");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bystander,
            "only Worldspine Wurm itself is shuffled in — other graveyard cards stay.");
    }
}
