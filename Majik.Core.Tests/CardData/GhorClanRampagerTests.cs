using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GhorClanRampagerFactory"/>.
///
/// Ghor-Clan Rampager (Gatecrash / Modern Horizons, {2}{R}{G}):
///   Creature — Beast 4/4.
///   Trample
///   Bloodrush — {R}{G}, Discard Ghor-Clan Rampager: Target attacking
///   creature gets +4/+4 and gains trample until end of turn.
///
/// Covers:
///   - Card identity: Beast 4/4, {2}{R}{G}, MV 4, owner / controller.
///   - NamedCardFactory dispatch.
///   - Trample keyword marker attached (CR 702.19).
///   - Bloodrush cost shape: {R}{G} + DiscardSelfCost (hand-gated).
///   - Bloodrush DiscardSelfCost: payable in hand, rejected outside hand.
///   - Bloodrush target request: 1..1 "attacking creature".
///   - Bloodrush resolve: target gets +4/+4 and gains Trample until EOT.
///   - Bloodrush resolve: granted Trample + pump expire at end of turn
///     (CR 514.2).
///   - Bloodrush resolve: fizzles when target left the battlefield (CR 608.2b).
/// </summary>
public class GhorClanRampagerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GhorClanRampager_Identity()
    {
        var card = GhorClanRampagerFactory.Create(_alice);

        card.Name.Should().Be("Ghor-Clan Rampager");
        card.ManaCost.Should().Be("{2}{R}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GhorClanRampager_ManaValue_Is_4()
    {
        var card = GhorClanRampagerFactory.Create(_alice);

        // {2}{R}{G} = 2 generic + 1 red + 1 green = MV 4 (CR 202.3).
        card.ManaCostValue.TotalValue.Should().Be(4, "{2}{R}{G} has mana value 4");
        card.ManaCostValue.Red.Should().Be(1, "one {R} pip");
        card.ManaCostValue.Green.Should().Be(1, "one {G} pip");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GhorClanRampager_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ghor-Clan Rampager", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ghor-Clan Rampager");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(4);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Trample");

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Bloodrush is the single activated ability");
    }

    // -----------------------------------------------------------------------
    // Trample (CR 702.19)
    // -----------------------------------------------------------------------

    [Fact]
    public void GhorClanRampager_HasTrampleKeyword()
    {
        var card = GhorClanRampagerFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Trample", "CR 702.19 — Trample on the body");

        CombatAbilities.HasTrample(card).Should().BeTrue(
            "the printed Trample marker surfaces through CombatAbilities");
    }

    // -----------------------------------------------------------------------
    // Bloodrush — {R}{G}, Discard this card (hand-gated)
    // -----------------------------------------------------------------------

    private static ActivatedAbility Bloodrush(Creature card) =>
        card.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void Bloodrush_CostShape_IsRGAndDiscardSelf()
    {
        var card = GhorClanRampagerFactory.Create(_alice);
        var br = Bloodrush(card);

        br.Costs.Should().HaveCount(2);
        br.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = br.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(0, "Bloodrush costs {R}{G}: no generic");
        manaCost.Red.Should().Be(1, "Bloodrush costs {R}{G}: 1 red");
        manaCost.Green.Should().Be(1, "Bloodrush costs {R}{G}: 1 green");
    }

    [Fact]
    public void Bloodrush_DiscardSelfCost_PayableWhenInHand_RejectedElsewhere()
    {
        var card = GhorClanRampagerFactory.Create(_alice);
        var br = Bloodrush(card);
        var discardCost = br.Costs.OfType<DiscardSelfCost>().Single();

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        discardCost.CanPay(_alice).Should().BeTrue(
            "Bloodrush is activated from the hand by discarding the card");

        _alice.Zones.Hand.RemoveCard(card);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        discardCost.CanPay(_alice).Should().BeFalse(
            "Bloodrush cannot be activated from outside the hand");
    }

    [Fact]
    public void Bloodrush_TargetRequest_IsSingleAttackingCreature()
    {
        var card = GhorClanRampagerFactory.Create(_alice);
        var br = Bloodrush(card);

        br.TargetRequests.Should().HaveCount(1);
        var tr = br.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("attacking");
        tr.Intent.HasAny(BotIntent.CombatTrick).Should().BeTrue(
            "Bloodrush is a combat trick");
    }

    // -----------------------------------------------------------------------
    // Bloodrush resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Bloodrush_Resolve_TargetGetsPlus4Plus4AndTrample()
    {
        var card = GhorClanRampagerFactory.Create(_alice);
        var attacker = NewBattlefieldCreatureWithEffects(_alice, "Bear", "{1}{G}");

        // Pre-conditions: vanilla 2/2 with no trample.
        attacker.Power.Should().Be(2);
        attacker.Toughness.Should().Be(2);
        CombatAbilities.HasTrample(attacker).Should().BeFalse();

        var br = Bloodrush(card);
        br.SetChosenTargets(new[] { new object[] { attacker } });
        br.Resolve();

        // +4/+4 ⇒ 6/6; Trample granted (Layer 6 keyword grant).
        attacker.Power.Should().Be(6, "+4/+4 pump (CR 613.1c)");
        attacker.Toughness.Should().Be(6, "+4/+4 pump (CR 613.1c)");
        CombatAbilities.HasTrample(attacker).Should().BeTrue(
            "Bloodrush grants Trample until end of turn (CR 702.19)");
    }

    [Fact]
    public void Bloodrush_Resolve_PumpAndTrampleExpireAtEndOfTurn_CR514()
    {
        var continuous = new ContinuousEffectsService();
        var card = GhorClanRampagerFactory.Create(_alice);
        var attacker = NewBattlefieldCreature(_alice, "Bear", "{1}{G}");
        attacker.ActiveEffects = continuous;

        var br = Bloodrush(card);
        br.SetChosenTargets(new[] { new object[] { attacker } });
        br.Resolve();

        attacker.Power.Should().Be(6);
        CombatAbilities.HasTrample(attacker).Should().BeTrue();

        // CR 514.2 — cleanup step removes "until end of turn" effects.
        continuous.ExpireEndOfTurn();

        attacker.Power.Should().Be(2, "+4/+4 pump expires at end of turn");
        attacker.Toughness.Should().Be(2);
        CombatAbilities.HasTrample(attacker).Should().BeFalse(
            "granted Trample expires at end of turn (CR 514.2)");
    }

    [Fact]
    public void Bloodrush_Resolve_FizzlesWhenTargetLeftBattlefield_CR608()
    {
        // CR 608.2b — target no longer on the battlefield at resolution → no-op.
        var card = GhorClanRampagerFactory.Create(_alice);
        var gone = new Creature("Vanished Bear", "{1}{G}", 2, 2);
        gone.SetOwner(_alice);
        gone.ActiveEffects = new ContinuousEffectsService();
        gone.SetZone(ZoneType.Graveyard); // moved off battlefield before resolve
        _alice.Zones.Graveyard.AddCard(gone);

        var br = Bloodrush(card);
        br.SetChosenTargets(new[] { new object[] { gone } });
        br.Resolve();

        gone.Power.Should().Be(2, "CR 608.2b — target left the battlefield; no pump");
        CombatAbilities.HasTrample(gone).Should().BeFalse(
            "CR 608.2b — no Trample granted when the target has left the battlefield");
    }

    [Fact]
    public void Bloodrush_Resolve_CanTargetOpponentsAttacker()
    {
        // Oracle reads "target attacking creature" — not controller-restricted.
        var card = GhorClanRampagerFactory.Create(_alice);
        var bobAttacker = NewBattlefieldCreatureWithEffects(_bob, "Goblin", "{R}");

        var br = Bloodrush(card);
        br.SetChosenTargets(new[] { new object[] { bobAttacker } });
        br.Resolve();

        bobAttacker.Power.Should().Be(6, "Bloodrush can target any attacking creature");
        CombatAbilities.HasTrample(bobAttacker).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewBattlefieldCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature NewBattlefieldCreatureWithEffects(Player owner, string name, string cost)
    {
        var c = NewBattlefieldCreature(owner, name, cost);
        c.ActiveEffects = new ContinuousEffectsService();
        return c;
    }
}
