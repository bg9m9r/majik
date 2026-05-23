using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Dauthi Voidwalker (Modern Horizons 2, {1}{B}).
///
/// Covers:
///  - Card identity + NamedCardFactory dispatch (Dauthi Rogue, 3/2, Shadow).
///  - Opponent's creature dying → exiled with void counter under Voidwalker
///    (not in graveyard).
///  - Opponent's hand→graveyard (discard-shape move) → same replacement
///    fires (the oracle says "from anywhere").
///  - Activated ability {2}, {T}, Remove a void counter from an exiled
///    card: removes the counter; the same card is castable for free via
///    <see cref="CastFromExileAlternativeCost"/>.
///  - Post-removal state: exile zone still holds the card; void-counter
///    pile is empty.
///  - Controller's own card going to graveyard is NOT replaced.
/// </summary>
public class DauthiVoidwalkerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Card identity + dispatch ──────────────────────────────────────────

    [Fact]
    public void DauthiVoidwalker_IsCreature_DauthiRogue_3_2_AtCost1B()
    {
        var dauthi = DauthiVoidwalkerFactory.Create(_alice);

        dauthi.Name.Should().Be("Dauthi Voidwalker");
        dauthi.ManaCost.Should().Be("{1}{B}");
        dauthi.HasType(CardType.Creature).Should().BeTrue();
        dauthi.HasSubtype(CardSubtype.Dauthi).Should().BeTrue();
        dauthi.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        dauthi.BasePower.Should().Be(3);
        dauthi.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void DauthiVoidwalker_HasShadow()
    {
        var dauthi = DauthiVoidwalkerFactory.Create(_alice);

        var keywords = dauthi.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Shadow");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DauthiVoidwalker()
    {
        var card = NamedCardFactory.Create("Dauthi Voidwalker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dauthi Voidwalker");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dauthi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Shadow");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // ── Replacement effect — opponent's grave-bound card → exile ──────────

    [Fact]
    public void OpponentsCreatureDying_IsExiledWithVoidCounterInsteadOfGraveyard()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (dauthi, _) = DauthiVoidwalkerFactory.Create(_alice, rep);
        PutOnBattlefield(dauthi, _alice);

        // Bob's creature on the battlefield → moved to graveyard (mimics
        // SBA dying after lethal damage / destroy).
        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(_bob);
        PutOnBattlefield(bobCreature, _bob);

        zones.MoveCardTo(bobCreature, ZoneType.Graveyard);

        // The replacement rewrote Graveyard → Exile.
        bobCreature.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobCreature);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCreature);

        var state = DauthiVoidwalkerFactory.GetState(dauthi);
        state.Should().NotBeNull();
        state!.HasVoidCounter(bobCreature).Should().BeTrue(
            "the dying card was stamped with a void counter under Dauthi Voidwalker");
        state.VoidCounterCount.Should().Be(1);
    }

    [Fact]
    public void OpponentsDiscard_HandToGraveyard_IsExiledWithVoidCounter()
    {
        // Oracle says "from ANYWHERE" — hand → graveyard (e.g. discard
        // from Thoughtseize / Liliana) should also fire the replacement.
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (dauthi, _) = DauthiVoidwalkerFactory.Create(_alice, rep);
        PutOnBattlefield(dauthi, _alice);

        var bobSpell = new Sorcery("Thoughtseize", "{B}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobSpell);
        bobSpell.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobSpell, ZoneType.Graveyard);

        bobSpell.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobSpell);
        _bob.Zones.Exile.GetCards().Should().Contain(bobSpell);

        var state = DauthiVoidwalkerFactory.GetState(dauthi);
        state!.HasVoidCounter(bobSpell).Should().BeTrue();
    }

    [Fact]
    public void ControllersOwnCardGoingToGraveyard_IsNotReplaced()
    {
        // Replacement is opponent-only — Alice's own card going to her
        // graveyard should NOT be exiled.
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (dauthi, _) = DauthiVoidwalkerFactory.Create(_alice, rep);
        PutOnBattlefield(dauthi, _alice);

        var aliceCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceCreature.SetOwner(_alice);
        PutOnBattlefield(aliceCreature, _alice);

        zones.MoveCardTo(aliceCreature, ZoneType.Graveyard);

        aliceCreature.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCreature);
        _alice.Zones.Exile.GetCards().Should().NotContain(aliceCreature);

        DauthiVoidwalkerFactory.GetState(dauthi)!.VoidCounterCount.Should().Be(0);
    }

    // ── Activated ability shape + behavior ─────────────────────────────────

    [Fact]
    public void ActivatedAbility_HasManaTapAndVoidCounterCosts()
    {
        var dauthi = DauthiVoidwalkerFactory.Create(_alice);
        var ability = dauthi.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3,
            "{2} + tap + remove-a-void-counter");
        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        ability.Costs.OfType<RemoveVoidCounterCost>().Should().HaveCount(1);
    }

    [Fact]
    public void RemoveVoidCounterCost_CanPay_GatesOnPileNonEmpty()
    {
        var (dauthi, _) = DauthiVoidwalkerFactory.Create(_alice, new ReplacementBus());
        var ability = dauthi.Abilities.OfType<ActivatedAbility>().Single();
        var counterCost = ability.Costs.OfType<RemoveVoidCounterCost>().Single();

        // Empty pile — can't pay.
        counterCost.CanPay(_alice).Should().BeFalse();

        // Stamp a void counter on a fake exiled card → cost is payable.
        var state = DauthiVoidwalkerFactory.GetState(dauthi)!;
        var exiled = new Sorcery("Doomed Card", "{B}") { Owner = _bob };
        state.AddVoidCounter(exiled);

        counterCost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void ActivatedAbility_Effect_RemovesVoidCounterFromAutopickedCard()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (dauthi, _) = DauthiVoidwalkerFactory.Create(_alice, rep);
        PutOnBattlefield(dauthi, _alice);

        // Bob's instant dies into exile-with-void-counter.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobBolt);
        bobBolt.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bobBolt, ZoneType.Graveyard);

        bobBolt.Zone.Should().Be(ZoneType.Exile);
        var state = DauthiVoidwalkerFactory.GetState(dauthi)!;
        state.HasVoidCounter(bobBolt).Should().BeTrue();

        // Execute the ability's effect — should pull the counter off Bolt.
        var ability = dauthi.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        state.HasVoidCounter(bobBolt).Should().BeFalse();
        state.VoidCounterCount.Should().Be(0);

        // The card stays in exile (the "play it without paying its mana
        // cost" payoff is a separate cast performed by the caller via
        // BuildAlternativeCost). The cast-for-free alt cost is ready.
        bobBolt.Zone.Should().Be(ZoneType.Exile);
        var altCost = DauthiVoidwalkerFactory.BuildAlternativeCost(bobBolt);
        altCost.AlternativeManaCost.IsZero.Should().BeTrue(
            "the play-from-exile permission is free (mana cost = 0)");
        altCost.CanCastFor(bobBolt, _bob).Should().BeTrue(
            "Bolt is in exile and owned by Bob, so the alt cost is legal "
            + "(Voidwalker grants the play permission to ITS controller, but "
            + "the alt-cost itself reads zone + ownership invariants — "
            + "controller-redirect would be layered on top in a full flow)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void PutOnBattlefield(ICard card, Player controller)
    {
        card.SetOwner(card.Owner ?? controller);
        card.SetController(controller);
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
