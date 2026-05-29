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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using CombatAbilities = Majik.Core.Combat.CombatAbilities;
using MtgCombat = Majik.Core.Combat.Combat;
using Attacker = Majik.Core.Combat.Attacker;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LegionLoyalistFactory"/>.
///
/// Card: Legion Loyalist (Gatecrash, {R}) — Creature — Goblin Soldier 1/1.
///   "Haste
///    Battalion — Whenever this creature and at least two other creatures
///    attack, creatures you control gain first strike and trample until end
///    of turn and can't be blocked by creature tokens this turn."
/// </summary>
public class LegionLoyalistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Vanilla(Player owner, string name, bool token = false)
    {
        var c = new Creature(name, "{1}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = new ContinuousEffectsService();
        if (token) c.MarkAsToken();
        return c;
    }

    private static TriggeredAbility GetBattalionTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    [Fact]
    public void LegionLoyalist_Identity()
    {
        var c = LegionLoyalistFactory.Create(_alice);

        c.Name.Should().Be("Legion Loyalist");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LegionLoyalist_IsRed()
    {
        var c = LegionLoyalistFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Legion Loyalist has an {R} pip in its mana cost");
    }

    [Fact]
    public void LegionLoyalist_ManaValueIsOne()
    {
        var c = LegionLoyalistFactory.Create(_alice);

        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    [Fact]
    public void LegionLoyalist_HasHasteKeywordMarker()
    {
        var c = LegionLoyalistFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue(
                "Legion Loyalist ships with Haste as a KeywordAbility marker (CR 702.10)");
    }

    [Fact]
    public void LegionLoyalist_HasSingleBattalionTrigger()
    {
        var c = LegionLoyalistFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Battalion is wired as a single attack-declared triggered ability");
    }

    [Fact]
    public void LegionLoyalist_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Legion Loyalist", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Legion Loyalist");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // --- Battalion trigger condition (CR 508.1f) -------------------------

    [Fact]
    public void Battalion_Triggers_WhenLoyalistAndTwoOthersAttack()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        var trigger = GetBattalionTrigger(loyalist);

        var combat = new MtgCombat(_alice, _bob);
        combat.AddAttacker(new Attacker(loyalist, _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally One"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Two"), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue(
            "this creature plus two others (≥3 attackers) satisfies Battalion");
    }

    [Fact]
    public void Battalion_DoesNotTrigger_WithOnlyTwoAttackers()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        var trigger = GetBattalionTrigger(loyalist);

        var combat = new MtgCombat(_alice, _bob);
        combat.AddAttacker(new Attacker(loyalist, _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally One"), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "Battalion needs this creature plus at least TWO others (≥3 total)");
    }

    [Fact]
    public void Battalion_DoesNotTrigger_WhenLoyalistNotAmongAttackers()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        var trigger = GetBattalionTrigger(loyalist);

        // Three others attack, but NOT the Loyalist — "this creature ... attack".
        var combat = new MtgCombat(_alice, _bob);
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally One"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Two"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Three"), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "Battalion requires the Loyalist itself to be among the attackers");
    }

    [Fact]
    public void Battalion_DoesNotTrigger_OnOpponentAttacks()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        var trigger = GetBattalionTrigger(loyalist);

        // Bob attacks with three creatures; Alice's Loyalist isn't attacking.
        var combat = new MtgCombat(_bob, _alice);
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin A"), _alice));
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin B"), _alice));
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin C"), _alice));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "CR 109.5 — 'this creature ... attack' keys on the controller's attack");
    }

    // --- Battalion resolution body --------------------------------------

    [Fact]
    public void Battalion_GrantsFirstStrikeAndTrample_ToCreaturesYouControl()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        loyalist.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(loyalist);

        var ally = Vanilla(_alice, "Ally One");
        _alice.Zones.Battlefield.AddCard(ally);

        var oppCreature = Vanilla(_bob, "Enemy Bear");
        _bob.Zones.Battlefield.AddCard(oppCreature);

        var trigger = GetBattalionTrigger(loyalist);
        foreach (var e in trigger.Effects) e.Execute();

        CombatAbilities.HasFirstStrike(loyalist).Should().BeTrue();
        CombatAbilities.HasTrample(loyalist).Should().BeTrue();
        CombatAbilities.HasFirstStrike(ally).Should().BeTrue();
        CombatAbilities.HasTrample(ally).Should().BeTrue();

        CombatAbilities.HasFirstStrike(oppCreature).Should().BeFalse(
            "the grant is scoped to creatures YOU control (CR 109.5)");
        CombatAbilities.HasTrample(oppCreature).Should().BeFalse();
    }

    [Fact]
    public void Battalion_GrantsTokenBlockRestriction_ToCreaturesYouControl()
    {
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);
        loyalist.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(loyalist);

        var ally = Vanilla(_alice, "Ally One");
        _alice.Zones.Battlefield.AddCard(ally);

        var trigger = GetBattalionTrigger(loyalist);
        foreach (var e in trigger.Effects) e.Execute();

        var tokenBlocker = Vanilla(_bob, "Goblin Token", token: true);
        var realBlocker = Vanilla(_bob, "Grizzly Bears");

        // CR 509.1b — a creature token may not block the attacker; a
        // non-token creature may.
        loyalist.ActiveEffects!
            .CanBlockUnderExceptByRestrictions(loyalist, tokenBlocker)
            .Should().BeFalse("creatures can't be blocked by creature tokens this turn");
        loyalist.ActiveEffects!
            .CanBlockUnderExceptByRestrictions(loyalist, realBlocker)
            .Should().BeTrue("non-token creatures may still block");

        ally.ActiveEffects!
            .CanBlockUnderExceptByRestrictions(ally, tokenBlocker)
            .Should().BeFalse("the restriction applies to every creature you control");
    }

    [Fact]
    public void Battalion_Body_NoOp_WhenControllerOffBattlefield()
    {
        // Shape-only construction: the trigger body must not throw when the
        // controller has no creatures with a live ContinuousEffectsService.
        var loyalist = LegionLoyalistFactory.Create(_alice);
        loyalist.SetZone(ZoneType.Battlefield);

        var trigger = GetBattalionTrigger(loyalist);
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow();
    }
}
