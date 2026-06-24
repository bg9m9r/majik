using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Disturbing Mirth (Duskmourn: House of Horror, {B}{R}).
///
/// Oracle (Scryfall-verified, embedded seed):
///   "When this enchantment enters, you may sacrifice another enchantment or
///    creature. If you do, draw two cards.
///    When you sacrifice this enchantment, manifest dread."
///
/// Coverage (unique behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
/// - Identity: {B}{R} Enchantment shape.
/// - ETB optional sacrifice → draw two: auto-takes the upside with no agent,
///   sacrifices an eligible creature/enchantment, draws two (CR 117.5/120.2).
/// - ETB "another" excludes Disturbing Mirth itself (CR 109.2): a lone
///   Disturbing Mirth on the battlefield finds nothing to sacrifice → no draw.
/// - Self-sacrifice manifest-dread trigger fires on the live bus only when
///   THIS card is sacrificed (CR 603.6b / 701.16 / 701.59).
/// </summary>
[Trait("Color", "M")]
public class DisturbingMirthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasEnchantmentShape()
    {
        var mirth = DisturbingMirthFactory.Create(_alice);

        mirth.Should().BeOfType<Enchantment>();
        mirth.Name.Should().Be("Disturbing Mirth");
        mirth.HasType(CardType.Enchantment).Should().BeTrue();
        mirth.ManaCost.Should().Be("{B}{R}");
        mirth.ManaCostValue.TotalValue.Should().Be(2);
        mirth.Owner.Should().BeSameAs(_alice);
        mirth.Controller.Should().BeSameAs(_alice);

        // Two triggers attached: ETB + self-sacrifice.
        mirth.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // ETB — you may sacrifice another enchantment or creature; draw two
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbEffect_SacrificesAnEligibleCreature_AndDrawsTwo()
    {
        var mirth = DisturbingMirthFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mirth);
        mirth.SetZone(ZoneType.Battlefield);

        // A creature on the battlefield is an eligible sacrifice.
        var fodder = new Creature("Fodder", "{1}{B}", 1, 1);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        // Stock the library so the draw two has cards to take.
        for (int i = 0; i < 3; i++)
        {
            var c = new Card($"Stuffer-{i}", "{0}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var etb = EtbTrigger(mirth);
        // No agent registered → "you may" auto-takes the upside (sac + draw).
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder,
            "the eligible creature was sacrificed (CR 701.16).");
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 2,
            "If you do, draw two cards (CR 120.2).");
    }

    [Fact]
    public void EtbEffect_OnlyDisturbingMirth_DrawsNothing_AnotherExcludesSelf()
    {
        // Lone Disturbing Mirth on the battlefield — "another" (CR 109.2)
        // excludes itself, so there is nothing eligible to sacrifice; the
        // optional cost can't be paid → no draw (CR 120.2).
        var mirth = DisturbingMirthFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mirth);
        mirth.SetZone(ZoneType.Battlefield);

        for (int i = 0; i < 2; i++)
        {
            var c = new Card($"Stuffer-{i}", "{0}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var etb = EtbTrigger(mirth);
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(mirth,
            "\"another\" excludes Disturbing Mirth — it cannot sacrifice itself (CR 109.2).");
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore,
            "no other creature/enchantment → \"If you do\" fails → no draw (CR 120.2).");
    }

    // -----------------------------------------------------------------------
    // Self-sacrifice — manifest dread
    // -----------------------------------------------------------------------

    [Fact]
    public void SelfSacrificeTrigger_LiveBus_FiresOnlyWhenThisCardIsSacrificed()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mirth = DisturbingMirthFactory.Create(_alice, triggers, zones: null, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(mirth);
        mirth.SetZone(ZoneType.Battlefield);

        // A different permanent's sacrifice must NOT fire Disturbing Mirth's
        // self-sacrifice trigger (it scopes to SacrificedCard == this card).
        var other = new Creature("Other", "{1}{R}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);

        Majik.Core.Primitives.Fx.Sacrifice(other, _alice, bus);
        triggers.PendingCount.Should().Be(0,
            "sacrificing another permanent does not fire \"When you sacrifice THIS enchantment\".");

        // Sacrificing Disturbing Mirth itself fires the manifest-dread trigger.
        Majik.Core.Primitives.Fx.Sacrifice(mirth, _alice, bus);
        triggers.PendingCount.Should().Be(1,
            "sacrificing this enchantment surfaces the manifest-dread trigger (CR 603.6b).");
    }

    [Fact]
    public void SelfSacrificeEffect_ResolvesManifestDread()
    {
        // CR 701.59 — the trigger's effect runs real manifest dread for the
        // card's controller: top of Alice's library becomes a face-down 2/2
        // ManifestedCreature; the second-from-top goes to her graveyard.
        var mirth = DisturbingMirthFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mirth);
        mirth.SetZone(ZoneType.Battlefield);

        var topCard = new Creature("Top Card Creature", "{1}{G}", 3, 3);
        topCard.SetOwner(_alice);
        var secondCard = new Card("Second Card", "{R}");
        secondCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var libraryBefore = _alice.Zones.Library.GetCards().Count();
        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        var selfSac = mirth.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<PermanentSacrificedEvent>);
        foreach (var e in selfSac.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Count().Should().Be(libraryBefore - 2,
            "manifest dread looks at + consumes top 2 of library.");
        _alice.Zones.Graveyard.GetCards().Should().Contain(secondCard,
            "second-of-two looked-at card goes to graveyard.");
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore + 1,
            "manifested wrapper joins the battlefield as a face-down 2/2.");

        var wrapper = _alice.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.UnderlyingCard.Should().BeSameAs(topCard);
    }

    private static TriggeredAbility EtbTrigger(Enchantment mirth) =>
        mirth.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);
}
