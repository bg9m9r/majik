using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Violent Outburst (Alara Reborn, {1}{R}{G}, Instant).
///
/// Covers:
/// - Identity (name, type, cost, mana value).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Cascade trigger fires on cast and routes through
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 3 — proving
///   <b>cascade is type-agnostic</b> (Violent Outburst is an instant; the
///   trigger structure is identical to Crashing Footfalls' sorcery cast).
/// - Resolve effect (creatures-you-control +1/+0 and haste until EOT).
/// - Cascade discovery — registered in
///   <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>.
/// </summary>
public class ViolentOutburstTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = ViolentOutburstFactory.Create(_alice);

        card.Name.Should().Be("Violent Outburst");
        card.ManaCost.Should().Be("{1}{R}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        card.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ViolentOutburst()
    {
        var card = NamedCardFactory.Create("Violent Outburst", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Violent Outburst");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Card_HasCascadeTriggeredAbility()
    {
        var card = ViolentOutburstFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Violent Outburst prints one triggered ability — Cascade.");
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV3_InstantSpeed()
    {
        // Cascade audit — verify the trigger fires the same way for an
        // INSTANT cascade source as it does for the sorcery (Crashing
        // Footfalls). Library setup: Mountain (land, bottomed) then Lava
        // Spike (MV 1, eligible — strictly less than 3).
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var spike = new Sorcery("Lava Spike", "{R}");
        spike.SetOwner(_alice);

        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(spike);
        spike.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = ViolentOutburstFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(spike);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);

        spike.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void CascadeTrigger_ActiveZones_IncludesStack_ForInstantCascade()
    {
        // The cascade trigger needs to be active in the Stack zone so it
        // fires while Violent Outburst (an Instant) is on the stack as a
        // spell — same posture as Crashing Footfalls / Living End. This
        // is the audit that confirms cascade-on-instant works.
        var card = ViolentOutburstFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Stack);
    }

    [Fact]
    public void Resolve_PumpsAndGrantsHasteToControllersCreatures()
    {
        // Alice controls a Grizzly Bears (2/2) and a Goblin (1/1).
        // Bob controls an opposing Goblin — should NOT be pumped.
        var effects = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var aliceGoblin = new Creature("Aliced Goblin", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(aliceGoblin);

        var bobGoblin = new Creature("Bob's Goblin", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _bob.Zones.Battlefield.AddCard(bobGoblin);

        // Resolve Violent Outburst.
        var def = ViolentOutburstFactory.BuildSpellDefinition(_alice);
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        foreach (var e in def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            e.Execute();
        }

        // Alice's creatures: +1/+0 and gain Haste.
        bear.GetPower().Should().Be(3, "Grizzly Bears gets +1/+0");
        bear.GetToughness().Should().Be(2, "+0 toughness — Violent Outburst is +1/+0, not +1/+1");
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        aliceGoblin.GetPower().Should().Be(2);
        aliceGoblin.GetToughness().Should().Be(1);
        CombatAbilities.HasHaste(aliceGoblin).Should().BeTrue();

        // Bob's creature: unaffected.
        bobGoblin.GetPower().Should().Be(1, "Bob's Goblin is not Alice's creature");
        CombatAbilities.HasHaste(bobGoblin).Should().BeFalse();
    }

    [Fact]
    public void Resolve_NoCreatures_NoOp()
    {
        // Empty battlefield — should be a clean no-op.
        var def = ViolentOutburstFactory.BuildSpellDefinition(_alice);

        var act = () =>
        {
            foreach (var e in def.EffectFactory(new ChosenSpellParams(
                ModeIndex: null, X: null,
                Targets: System.Array.Empty<IReadOnlyList<object>>(),
                Mana: ManaPayment.Empty)))
            {
                e.Execute();
            }
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Resolve_CreatureWithoutActiveEffects_NoOpsCleanly()
    {
        // Shape-only safety — a creature with no ContinuousEffectsService
        // wired should not throw. Mirrors Violent Urge's defensive guard.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            // ActiveEffects intentionally null.
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var act = () => ViolentOutburstFactory.ApplyPumpAndHaste(_alice);
        act.Should().NotThrow();

        // Power/toughness unchanged.
        bear.GetPower().Should().Be(2);
    }

    [Fact]
    public void CascadeTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        // Sanity — trigger condition is keyed on this card's own
        // SpellCastEvent (not on any cast of any spell).
        var card = ViolentOutburstFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.Should().BeOfType<Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Domain.DomainEvents.SpellCastEvent>>();

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);

        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Domain.DomainEvents.SpellCastEvent>)trigger.Condition;
        cond.Matches(new Majik.Core.Domain.DomainEvents.SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new Majik.Core.Domain.DomainEvents.SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_ViolentOutburst()
    {
        var card = ViolentOutburstFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Violent Outburst is registered in the cascade ship list so the "
            + "bot's bidding heuristic sees it as a cascade card.");
    }
}
