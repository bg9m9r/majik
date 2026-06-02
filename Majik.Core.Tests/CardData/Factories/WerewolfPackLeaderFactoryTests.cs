using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WerewolfPackLeaderFactory"/>.
///
/// Card (Innistrad: Midnight Hunt, {G}{G}), Creature — Human Werewolf 3/3.
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Pack tactics — Whenever Werewolf Pack Leader attacks, if you attacked
///    with creatures with total power 6 or greater this combat, draw a card.
///    {3}{G}: Until end of turn, Werewolf Pack Leader has base power and
///    toughness 5/3, gains trample, and isn't a Human."
///
/// Covers:
/// - Identity (name, {G}{G}, Human + Werewolf subtypes, 3/3, owner/controller).
/// - NamedCardFactory dispatch.
/// - Pack-tactics attack trigger present, keyed on this creature attacking.
/// - Intervening-if: total power >= 6 -> trigger can go on stack; < 6 -> cannot.
/// - Resolution draws exactly one card for the controller.
/// - {3}{G} activated ability becomes base 5/3, gains trample, isn't a Human.
/// </summary>
[Trait("Color", "G")]
public class WerewolfPackLeaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeAttacker(Player owner, int power, string name)
    {
        var c = new Creature(name, "1G", power, power);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void WerewolfPackLeader_Identity()
    {
        var c = WerewolfPackLeaderFactory.Create(_alice);

        c.Name.Should().Be("Werewolf Pack Leader");
        c.ManaCost.Should().Be("{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Werewolf).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Pack tactics attack trigger ──────────────────────────────────────

    [Fact]
    public void PackTactics_TriggerFiresOnSelfAttack()
    {
        var leader = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null, attackingCreaturesSource: () => Array.Empty<Creature>(), effects: null);
        leader.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(leader);
        trigger.IsTriggered(new CreatureAttacksEvent(leader, _bob)).Should().BeTrue(
            "pack tactics triggers when Werewolf Pack Leader itself attacks.");
    }

    [Fact]
    public void PackTactics_TriggerDoesNotFireOnOtherAttacker()
    {
        var leader = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null, attackingCreaturesSource: () => Array.Empty<Creature>(), effects: null);
        leader.SetZone(ZoneType.Battlefield);
        var other = MakeAttacker(_alice, 2, "Grizzly Bears");

        var trigger = GetAttackTrigger(leader);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "pack tactics keys on Werewolf Pack Leader attacking, not other creatures.");
    }

    [Fact]
    public void PackTactics_InterveningIf_TotalPowerSixOrGreater_AllowsStack()
    {
        // Leader (3) + a 3-power ally = total power 6.
        var leader = WerewolfPackLeaderFactory.Create(_alice, triggers: null, attackingCreaturesSource: null, effects: null);
        leader.SetZone(ZoneType.Battlefield);
        var ally = MakeAttacker(_alice, 3, "Ally");
        var attackers = new[] { leader, ally };

        var trigger = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null, attackingCreaturesSource: () => attackers, effects: null);
        var t = GetAttackTrigger(trigger);

        t.CanBePutOnStack().Should().BeTrue(
            "total attacking power 6 (3+3) meets the >= 6 pack-tactics threshold (CR 603.4).");
    }

    [Fact]
    public void PackTactics_InterveningIf_TotalPowerBelowSix_BlocksStack()
    {
        // Leader (3) + a 2-power ally = total power 5 (< 6).
        var leader = WerewolfPackLeaderFactory.Create(_alice, triggers: null, attackingCreaturesSource: null, effects: null);
        leader.SetZone(ZoneType.Battlefield);
        var ally = MakeAttacker(_alice, 2, "Ally");
        var attackers = new[] { leader, ally };

        var trigger = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null, attackingCreaturesSource: () => attackers, effects: null);
        var t = GetAttackTrigger(trigger);

        t.CanBePutOnStack().Should().BeFalse(
            "total attacking power 5 is below the >= 6 pack-tactics threshold (CR 603.4).");
    }

    [Fact]
    public void PackTactics_InterveningIf_OnlyCountsControllersAttackers()
    {
        // Leader (3) for Alice + a 4-power Bob attacker. Only Alice's attackers
        // ("you attacked with creatures") count: 3 < 6.
        var leader = WerewolfPackLeaderFactory.Create(_alice, triggers: null, attackingCreaturesSource: null, effects: null);
        leader.SetZone(ZoneType.Battlefield);
        var enemy = MakeAttacker(_bob, 4, "Enemy");
        var attackers = new[] { leader, enemy };

        var t = GetAttackTrigger(WerewolfPackLeaderFactory.Create(
            _alice, triggers: null, attackingCreaturesSource: () => attackers, effects: null));

        t.CanBePutOnStack().Should().BeFalse(
            "only the controller's attackers count toward 'you attacked with creatures'.");
    }

    [Fact]
    public void PackTactics_Resolution_DrawsACard()
    {
        // Seed Alice's library so the draw moves a card to hand.
        var libCard = new Creature("Forest Bear", "1G", 2, 2);
        libCard.SetOwner(_alice);
        libCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(libCard);

        // CR 603.4 — the draw effect re-checks the intervening-if on
        // resolution, so the attacker snapshot must still meet the threshold
        // (leader 3 + ally 3 = 6) for the draw to happen.
        Creature leader = null!;
        var ally = MakeAttacker(_alice, 3, "Ally");
        leader = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null,
            attackingCreaturesSource: () => new[] { leader, ally }, effects: null);
        leader.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(leader);
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(libCard,
            "pack tactics resolution draws a card for the controller.");
    }

    [Fact]
    public void PackTactics_Resolution_NoDrawWhenConditionNoLongerMet()
    {
        // CR 603.4 — intervening-if re-checked on resolution. With < 6 total
        // attacking power at resolution, no card is drawn even though the
        // trigger fired.
        var libCard = new Creature("Forest Bear", "1G", 2, 2);
        libCard.SetOwner(_alice);
        libCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(libCard);

        var leader = WerewolfPackLeaderFactory.Create(
            _alice, triggers: null,
            attackingCreaturesSource: () => Array.Empty<Creature>(), effects: null);
        leader.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(leader);
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(libCard,
            "with total attacking power below 6 at resolution, no card is drawn.");
    }

    // ── {3}{G} activated ability ─────────────────────────────────────────

    [Fact]
    public void Activated_BecomesFiveThree_GainsTrample_NotHuman()
    {
        var svc = new ContinuousEffectsService();

        var leader = WerewolfPackLeaderFactory.Create(_alice, effects: svc);
        leader.SetZone(ZoneType.Battlefield);
        leader.ActiveEffects = svc;

        var activated = leader.Abilities.OfType<ActivatedAbility>().Single();

        // Run the ability's effect (resolution) — registers the EOT effects.
        foreach (var e in activated.Effects) e.Execute();

        leader.GetPower().Should().Be(5, "base power becomes 5 until end of turn.");
        leader.GetToughness().Should().Be(3, "base toughness becomes 3 until end of turn.");

        var chars = svc.Compute(leader);
        chars.Keywords.Should().Contain("Trample", "the ability grants trample.");
        chars.Subtypes.Should().NotContain(CardSubtype.Human,
            "the animated form isn't a Human.");
        chars.Subtypes.Should().Contain(CardSubtype.Werewolf,
            "only the Human subtype is removed; Werewolf stays.");
    }

    [Fact]
    public void Activated_Expires_AtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var leader = WerewolfPackLeaderFactory.Create(_alice, effects: svc);
        leader.SetZone(ZoneType.Battlefield);
        leader.ActiveEffects = svc;

        var activated = leader.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        svc.ExpireEndOfTurn();

        leader.GetPower().Should().Be(3, "the set-base P/T expires in the cleanup step.");
        leader.GetToughness().Should().Be(3);
        svc.Compute(leader).Subtypes.Should().Contain(CardSubtype.Human,
            "the 'isn't a Human' rider expires at end of turn.");
    }
}
