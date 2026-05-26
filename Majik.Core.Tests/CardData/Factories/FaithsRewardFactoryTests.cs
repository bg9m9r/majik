using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FaithsRewardFactory"/> (Mirrodin Besieged, {3}{W}).
///
/// Card: Faith's Reward — Instant {3}{W}.
/// Oracle: "Return to the battlefield all permanent cards in your
/// graveyard that were put there from the battlefield this turn."
///
/// Covers:
/// - Identity (Instant, {3}{W}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve with no TurnState wired → no-op (shape-only path).
/// - Resolve: returns a creature that died this turn (recorded by
///   <see cref="TurnState.RecordPermanentMovedToGraveyard"/>) to the
///   battlefield under the caster's control.
/// - Resolve: does NOT return a card that's not a permanent card
///   (defence-in-depth — instants/sorceries can't actually be on the
///   battlefield, but the filter is correct).
/// - Resolve: does NOT return a card that was NOT recorded as moving
///   BF→Graveyard this turn (e.g. discarded directly from hand).
/// - Resolve: does NOT return cards owned by other players (CR 404.1
///   — "your graveyard" filter).
/// </summary>
public class FaithsRewardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FaithsReward_Identity()
    {
        var fr = FaithsRewardFactory.Create(_alice);

        fr.Name.Should().Be("Faith's Reward");
        fr.ManaCost.Should().Be("{3}{W}");
        fr.HasType(CardType.Instant).Should().BeTrue();
        fr.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FaithsReward_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Faith's Reward", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Faith's Reward");
    }

    [Fact]
    public void FaithsReward_Resolve_NoTurnStateWired_IsNoOp()
    {
        // Shape-only path: BuildSpellDefinition's resolve body when the
        // turnState callback returns null does not move anything.
        var deadBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        deadBear.SetOwner(_alice);
        deadBear.SetController(_alice);
        deadBear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(deadBear);

        var def = FaithsRewardFactory.BuildSpellDefinition(_alice, () => null);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        deadBear.Zone.Should().Be(ZoneType.Graveyard,
            "without TurnState wiring the resolve body is a no-op");
    }

    [Fact]
    public void FaithsReward_Resolve_ReturnsPermanentCardMovedThisTurn()
    {
        var ts = new TurnState();

        // Simulate Alice's creature dying this turn.
        var deadBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        deadBear.SetOwner(_alice);
        deadBear.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(deadBear);
        deadBear.SetZone(ZoneType.Graveyard);

        ts.RecordPermanentMovedToGraveyard(_alice, deadBear);

        var def = FaithsRewardFactory.BuildSpellDefinition(_alice, () => ts);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        deadBear.Zone.Should().Be(ZoneType.Battlefield,
            "Faith's Reward returns permanents that moved BF→Graveyard this turn");
        _alice.Zones.Battlefield.GetCards().Should().Contain(deadBear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(deadBear);
    }

    [Fact]
    public void FaithsReward_Resolve_SkipsCardOwnedByAnotherPlayer()
    {
        var ts = new TurnState();

        // A card Bob owns sits in his graveyard — Alice's Faith's Reward
        // shouldn't reach it (CR 404.1 — "your graveyard" filter).
        var bobsBear = new Creature("Bob's Bears", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(bobsBear);
        bobsBear.SetZone(ZoneType.Graveyard);

        // The ledger keys by owner — recording with Bob as former
        // controller (and Bob as owner) puts it in Bob's bucket, not
        // Alice's.
        ts.RecordPermanentMovedToGraveyard(_bob, bobsBear);

        var def = FaithsRewardFactory.BuildSpellDefinition(_alice, () => ts);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        bobsBear.Zone.Should().Be(ZoneType.Graveyard,
            "Faith's Reward only returns cards from the caster's graveyard");
    }

    [Fact]
    public void FaithsReward_Resolve_NoRecordedCards_NoOp()
    {
        var ts = new TurnState();
        // Alice has a creature in her graveyard but NOTHING was recorded
        // as moving BF→Graveyard this turn (e.g. milled into graveyard
        // pre-turn, or discarded from hand).
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var def = FaithsRewardFactory.BuildSpellDefinition(_alice, () => ts);
        var picks = new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        var effects = def.EffectFactory(picks);

        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "no card was recorded as moving BF→Graveyard this turn → nothing to return");
    }

    [Fact]
    public void IsPermanentCard_ClassifiesCorrectly()
    {
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var inst = new Instant("Lightning Bolt", "{R}");
        var sorc = new Sorcery("Wrath of God", "{2}{W}{W}");

        FaithsRewardFactory.IsPermanentCard(creature).Should().BeTrue();
        FaithsRewardFactory.IsPermanentCard(inst).Should().BeFalse(
            "instants are not permanent cards (CR 110.4a)");
        FaithsRewardFactory.IsPermanentCard(sorc).Should().BeFalse(
            "sorceries are not permanent cards (CR 110.4a)");
    }
}
