using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GenerousVisitorFactory"/>.
///
/// Card: Generous Visitor (Theros Beyond Death, {G}). Creature — Spirit 1/1.
///   "Whenever you cast an enchantment spell, put a +1/+1 counter on
///    target creature."
///
/// Covers:
/// - Identity ({G} 1/1 Spirit).
/// - NamedCardFactory dispatch.
/// - Trigger condition gating (enchantment cast by controller fires;
///   creature spell, opponent's enchantment, and non-enchantment spells
///   don't).
/// - Resolution applies a +1/+1 counter to the chosen target creature.
/// - Resolve-time recheck (CR 608.2b) — non-creature / off-battlefield
///   target is a silent no-op.
/// </summary>
public class GenerousVisitorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GenerousVisitor_Identity()
    {
        var c = GenerousVisitorFactory.Create(_alice);

        c.Name.Should().Be("Generous Visitor");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GenerousVisitor_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Generous Visitor", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Generous Visitor");
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        ((Creature)c).Power.Should().Be(1);
        ((Creature)c).Toughness.Should().Be(1);
    }

    [Fact]
    public void GenerousVisitor_HasOneTriggeredAbility_WithTargetCreature()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);

        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
        trigger.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void TriggerCondition_FiresOnControllerEnchantmentCast()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        // Alice casts a plain enchantment.
        var enchCard = new Enchantment("Plain Enchantment", "{2}");
        enchCard.SetOwner(_alice);
        enchCard.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(enchCard, _alice);

        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, ability: null!).Should().BeTrue();
    }

    [Fact]
    public void TriggerCondition_FiresOnControllerAuraCast()
    {
        // Auras carry the Enchantment card type (CR 303.1) — should fire
        // the same as plain enchantments.
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        var aura = new Enchantment(
            "Test Aura", "{1}{W}", subtypes: new[] { CardSubtype.Aura });
        aura.SetOwner(_alice);
        aura.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(aura, _alice);

        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, ability: null!).Should().BeTrue();
    }

    [Fact]
    public void TriggerCondition_DoesNotFireOnControllerCreatureCast()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(bears, _alice);

        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, ability: null!).Should().BeFalse(
            "Generous Visitor's trigger gates on enchantment-type only");
    }

    [Fact]
    public void TriggerCondition_DoesNotFireOnOpponentEnchantmentCast()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        var ench = new Enchantment("Opponent's Enchantment", "{2}");
        ench.SetOwner(_bob);
        ench.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(ench, _bob);

        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, ability: null!).Should().BeFalse(
            "the trigger reads 'whenever YOU cast'");
    }

    [Fact]
    public void Resolve_PlacesPlusOnePlusOneCounter_OnTargetCreature()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        bears.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Generous Visitor places one +1/+1 counter on the target creature");
    }

    [Fact]
    public void Resolve_NonCreatureTarget_IsSilentNoOp()
    {
        var visitor = GenerousVisitorFactory.Create(_alice);
        var trigger = visitor.Abilities.OfType<TriggeredAbility>().Single();

        // An artifact target — not a creature.
        var widget = new Artifact("Random Widget", "{2}");
        widget.SetOwner(_alice);
        widget.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(widget);
        widget.SetZone(ZoneType.Battlefield);

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { widget },
        });

        foreach (var effect in trigger.Effects) effect.Execute();

        widget.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "non-creature target fails resolve-time recheck (CR 608.2b)");
    }
}
