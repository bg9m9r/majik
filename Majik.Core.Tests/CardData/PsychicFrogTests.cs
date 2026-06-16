using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PsychicFrogFactory"/> (Modern Horizons 3,
/// {U}{B}).
///
/// Oracle text:
///   "Whenever this creature deals combat damage to a player or
///    planeswalker, draw a card.
///    Discard a card: Put a +1/+1 counter on this creature.
///    Exile three cards from your graveyard: This creature gains flying
///    until end of turn."
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Frog subtype, NO printed Flying).
/// - NamedCardFactory dispatch.
/// - Combat-damage-to-a-player trigger draws a card; does NOT fire on damage
///   to a creature.
/// - "Discard a card: +1/+1 counter" cost shape + payment + counter.
/// - "Exile three from graveyard: gain flying EOT" cost guard + flying grant.
/// </summary>
public class PsychicFrogTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ─────────────────────────────────────────────────

    [Fact]
    public void PsychicFrog_Identity()
    {
        var frog = PsychicFrogFactory.Create(_alice);

        frog.Name.Should().Be("Psychic Frog");
        frog.ManaCost.Should().Be("{U}{B}");
        frog.HasType(CardType.Creature).Should().BeTrue();
        frog.HasSubtype(CardSubtype.Frog).Should().BeTrue("Psychic Frog is a Frog");
        frog.BasePower.Should().Be(1);
        frog.BaseToughness.Should().Be(2);
        frog.Owner.Should().BeSameAs(_alice);
        frog.Controller.Should().BeSameAs(_alice);

        // No PRINTED Flying — Flying is only granted by the third ability.
        frog.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Flying",
                "Psychic Frog has no printed Flying — it must pay the exile-3 ability for it");
    }

    [Fact]
    public void PsychicFrog_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Psychic Frog", _alice);

        card.Should().BeOfType<Creature>("Psychic Frog is a Creature");
        card.Name.Should().Be("Psychic Frog");
        card.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage draw trigger is attached");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "discard-pump + exile-3-flying activated abilities are wired");
    }

    // ── Combat-damage trigger — draw a card ─────────────────────────────────

    [Fact]
    public void PsychicFrog_CombatDamageToPlayer_DrawsACard()
    {
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        var top = new Creature("Top", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var trigger = frog.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(frog, _bob, 3);

        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "Psychic Frog dealing combat damage to a player matches the trigger");

        foreach (var e in trigger.Effects) e.Execute();

        // Exactly ONE card drawn (not "draw N") — the new oracle is "draw a card".
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void PsychicFrog_CombatDamageToCreature_DoesNotFire()
    {
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };

        var trigger = frog.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(frog, (ICard)blocker, 1);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "combat damage to a (non-planeswalker) creature does not match");
    }

    // ── Discard a card: +1/+1 counter ───────────────────────────────────────

    [Fact]
    public void PsychicFrog_DiscardPump_HasDiscardACardCost_AndNoManaCost()
    {
        var frog = PsychicFrogFactory.Create(_alice);

        var pump = frog.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardACardCost>().Any());
        pump.Costs.OfType<DiscardACardCost>().Should().ContainSingle();
        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Psychic Frog's discard-pump has no mana cost");
        pump.RebindSafe.Should().BeTrue("the pump reads ctx.Source for Agatha re-home");
    }

    [Fact]
    public void PsychicFrog_DiscardPump_DiscardsACard_AndAdds_PlusOne_Counter()
    {
        var frog = PsychicFrogFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        var fodder = new Creature("Fodder", "1G", 1, 1) { Owner = _alice };
        _alice.Zones.Hand.AddCard(fodder);
        fodder.SetZone(ZoneType.Hand);

        var pump = frog.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<DiscardACardCost>().Any());

        var discardCost = pump.Costs.OfType<DiscardACardCost>().Single();
        discardCost.CanPay(_alice).Should().BeTrue();
        frog.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        discardCost.Pay(_alice);
        foreach (var effect in pump.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(fodder);
        frog.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the activated ability places a +1/+1 counter");

        discardCost.CanPay(_alice).Should().BeFalse(
            "empty hand → \"discard a card\" cannot be paid (CR 117.1)");
    }

    // ── Exile three from graveyard: gain flying EOT ─────────────────────────

    [Fact]
    public void PsychicFrog_ExileThree_GrantsFlyingUntilEndOfTurn()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var frog = PsychicFrogFactory.Create(_alice, triggers: null, replacements: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        // Seed three cards in the graveyard to pay the cost.
        for (var i = 0; i < 3; i++)
        {
            var g = new Creature($"Grave{i}", "1G", 1, 1) { Owner = _alice };
            _alice.Zones.Graveyard.AddCard(g);
            g.SetZone(ZoneType.Graveyard);
        }

        var flyingAbility = frog.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<DiscardACardCost>().Any());
        flyingAbility.RebindSafe.Should().BeTrue();

        Majik.Core.Combat.CombatAbilities.HasFlying(frog).Should().BeFalse(
            "no flying before activation");

        foreach (var effect in flyingAbility.Effects) effect.Execute();

        // Three graveyard cards exiled (CR 601.2g cost), flying granted.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty("the three cards paid the cost");
        _alice.Zones.Exile.GetCards().Should().HaveCount(3);
        Majik.Core.Combat.CombatAbilities.HasFlying(frog).Should().BeTrue(
            "the ability grants flying until end of turn");
    }

    [Fact]
    public void PsychicFrog_ExileThree_ShortGraveyard_DoesNothing()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);
        var frog = PsychicFrogFactory.Create(_alice, triggers: null, replacements: null, effects: effects);
        _alice.Zones.Battlefield.AddCard(frog);
        frog.SetZone(ZoneType.Battlefield);

        // Only two cards — cost can't be paid (CR 601.2g).
        for (var i = 0; i < 2; i++)
        {
            var g = new Creature($"Grave{i}", "1G", 1, 1) { Owner = _alice };
            _alice.Zones.Graveyard.AddCard(g);
            g.SetZone(ZoneType.Graveyard);
        }

        var flyingAbility = frog.Abilities.OfType<ActivatedAbility>()
            .Single(a => !a.Costs.OfType<DiscardACardCost>().Any());

        foreach (var effect in flyingAbility.Effects) effect.Execute();

        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2, "fewer than three → no exile");
        Majik.Core.Combat.CombatAbilities.HasFlying(frog).Should().BeFalse(
            "the cost wasn't paid → no flying");
    }
}
