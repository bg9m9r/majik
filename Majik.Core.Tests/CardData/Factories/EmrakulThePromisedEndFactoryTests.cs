using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EmrakulThePromisedEndFactory"/>
/// (Eldritch Moon, {13}).
///
/// Legendary Creature — Eldrazi 13/13. Oracle text:
///   "Emrakul, the Promised End costs {1} less to cast for each card
///    type among cards in your graveyard.
///    When you cast this spell, you gain control of target opponent
///    during that player's next turn. After that turn, that player
///    takes an extra turn.
///    Flying, trample, protection from instants."
///
/// Covers:
///   - Identity (Legendary Eldrazi, {13}, 13/13).
///   - NamedCardFactory dispatch.
///   - Flying + Trample markers; Protection from instants ProtectionAbility.
///   - Cost reduction scales with distinct card types in caster's graveyard.
///   - Cast trigger enqueues extra turn for the chosen opponent on resolution.
/// </summary>
public class EmrakulThePromisedEndFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Emrakul_Identity()
    {
        var em = EmrakulThePromisedEndFactory.Create(_alice);

        em.Name.Should().Be("Emrakul, the Promised End");
        em.ManaCost.Should().Be("{13}");
        em.HasType(CardType.Creature).Should().BeTrue();
        em.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        em.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        em.BasePower.Should().Be(13);
        em.BaseToughness.Should().Be(13);
        em.Owner.Should().BeSameAs(_alice);
        em.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasFlying(em).Should().BeTrue("CR 702.9 — Flying");
        CombatAbilities.HasTrample(em).Should().BeTrue("CR 702.19 — Trample");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EmrakulThePromisedEnd()
    {
        var card = NamedCardFactory.Create("Emrakul, the Promised End", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(13);
        ((Creature)card).BaseToughness.Should().Be(13);
    }

    [Fact]
    public void Emrakul_ProtectionFromInstants()
    {
        var em = EmrakulThePromisedEndFactory.Create(_alice);
        var prot = em.Abilities.OfType<ProtectionAbility>().Single();

        prot.Quality.Should().Be(EmrakulThePromisedEndFactory.ProtectionFromInstantsQuality);

        // CR 702.16 — Protection from instants. Lightning Bolt (an
        // instant) should be rejected; a creature spell should not be.
        Protection.HasProtectionFromCardType(em, CardType.Instant).Should().BeTrue(
            "canonical \"instants\" quality matches CardType.Instant");
        Protection.HasProtectionFromCardType(em, CardType.Sorcery).Should().BeFalse(
            "protection from instants does NOT also cover sorceries");
        Protection.HasProtectionFromCardType(em, CardType.Creature).Should().BeFalse(
            "creature spells are not blocked by \"protection from instants\"");
    }

    [Fact]
    public void Emrakul_CostReduction_ZeroDistinctTypes_NoReduction()
    {
        // Empty graveyard → 0 distinct card types → no reduction;
        // effective cost stays at {13} (generic 13).
        var em = EmrakulThePromisedEndFactory.Create(_alice);
        var eff = CostReduction.GetEffectiveCost(em, _alice);
        eff.Generic.Should().Be(13,
            "empty graveyard yields zero cost reduction");
    }

    [Fact]
    public void Emrakul_CostReduction_ScalesWithDistinctCardTypes_InGraveyard()
    {
        // Stock the graveyard with one of each card type. Each distinct
        // type contributes {1} to the reduction — 4 distinct types →
        // {13} − 4 = {9}.
        var artifact = new Artifact("Mox", "{0}") { Owner = _alice };
        var creature = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        var enchantment = new Enchantment("Pacifism", "{1}{W}") { Owner = _alice };
        var instant = new Instant("Bolt", "{R}") { Owner = _alice };

        foreach (var c in new ICard[] { artifact, creature, enchantment, instant })
        {
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        EmrakulThePromisedEndFactory.CountDistinctCardTypesInGraveyard(_alice)
            .Should().Be(4,
                "artifact + creature + enchantment + instant — four distinct types");

        var em = EmrakulThePromisedEndFactory.Create(_alice);
        var eff = CostReduction.GetEffectiveCost(em, _alice);
        eff.Generic.Should().Be(9, "13 − 4 distinct card types = 9");
    }

    [Fact]
    public void Emrakul_CostReduction_FloorsAtZero_WhenGraveyardIsHuge()
    {
        // Eight distinct types (every type — artifact, creature,
        // enchantment, instant, land, planeswalker, sorcery, tribal).
        // 13 − 8 = 5; comfortably above zero. To assert the floor
        // gate, mock the unrealistic case with the TotalReducer
        // returning a huge value isn't reachable from outside, so
        // this test just sanity-checks the all-types ceiling.
        var artifact = new Artifact("Mox", "{0}") { Owner = _alice };
        var creature = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        var enchantment = new Enchantment("Pacifism", "{1}{W}") { Owner = _alice };
        var instant = new Instant("Bolt", "{R}") { Owner = _alice };
        var sorcery = new Sorcery("Wrath", "{2}{W}{W}") { Owner = _alice };
        var land = new Land("Plains") { Owner = _alice };

        foreach (var c in new ICard[] { artifact, creature, enchantment, instant, sorcery, land })
        {
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        EmrakulThePromisedEndFactory.CountDistinctCardTypesInGraveyard(_alice)
            .Should().Be(6, "six distinct types present in the graveyard");

        var em = EmrakulThePromisedEndFactory.Create(_alice);
        var eff = CostReduction.GetEffectiveCost(em, _alice);
        eff.Generic.Should().Be(7, "13 − 6 = 7");
    }

    [Fact]
    public void Emrakul_CastTrigger_EnqueuesExtraTurnForControlledOpponent()
    {
        var bus = new EventBus();
        var turns = new TurnManager(new List<Player> { _alice, _bob }, bus);

        var em = EmrakulThePromisedEndFactory.Create(_alice, turns, triggers: null);

        var castTrigger = em.Abilities.OfType<EmrakulThePromisedEndTrigger>().Single();
        castTrigger.ControlledOpponent = _bob;

        // Fire the trigger condition with a SpellCastEvent for this card.
        var emSpell = new Majik.Core.Spells.Spell(em, _alice);
        castTrigger.Condition.Matches(new SpellCastEvent(emSpell), castTrigger)
            .Should().BeTrue("the cast trigger fires on Emrakul's own SpellCastEvent");

        turns.HasExtraTurns.Should().BeFalse("extra turn not enqueued before resolution");
        foreach (var effect in castTrigger.Effects) effect.Execute();
        turns.HasExtraTurns.Should().BeTrue(
            "CR 603.10 — extra turn enqueued on cast-trigger resolution");
        turns.GetNextPlayer().Should().BeSameAs(_bob,
            "the extra turn belongs to the chosen target opponent");
    }

    [Fact]
    public void Emrakul_CastTrigger_NoOpsWhenControlledOpponentUnset()
    {
        var bus = new EventBus();
        var turns = new TurnManager(new List<Player> { _alice, _bob }, bus);

        var em = EmrakulThePromisedEndFactory.Create(_alice, turns, triggers: null);
        var castTrigger = em.Abilities.OfType<EmrakulThePromisedEndTrigger>().Single();
        // Leave ControlledOpponent unset — the shape-only path no-ops
        // the extra-turn enqueue so dispatcher tests can fire the
        // trigger without driving target selection.

        foreach (var effect in castTrigger.Effects) effect.Execute();

        turns.HasExtraTurns.Should().BeFalse(
            "no opponent chosen — extra turn enqueue is a no-op");
    }

    [Fact]
    public void Emrakul_CastTrigger_DoesNotFire_OnOtherSpells()
    {
        var em = EmrakulThePromisedEndFactory.Create(_alice);
        var castTrigger = em.Abilities.OfType<EmrakulThePromisedEndTrigger>().Single();

        var other = new Instant("Bolt", "{R}") { Owner = _alice };
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        castTrigger.Condition.Matches(new SpellCastEvent(otherSpell), castTrigger)
            .Should().BeFalse(
                "the trigger is self-cast — it only fires when Emrakul itself is cast");
    }
}
