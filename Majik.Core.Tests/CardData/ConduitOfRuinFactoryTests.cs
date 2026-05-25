using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ConduitOfRuinFactory"/> (Battle for Zendikar, {6}).
///
/// Oracle text:
///   "When you cast this spell, you may search your library for a
///    colorless creature card with mana value 7 or greater, then shuffle
///    and put that card on top of your library.
///    The first colorless creature spell you cast each turn costs {2}
///    less to cast."
///
/// Covers:
///   - Identity (5/5 Creature — Eldrazi, {6}, owner / controller, colourless).
///   - NamedCardFactory dispatch.
///   - Cast trigger condition matches self-cast SpellCastEvent.
///   - Cast trigger ActiveZones == Stack (CR 603.6a fires from the stack).
///   - IsTutorCandidate pure helper:
///     - Colourless creature with mv 7 → true.
///     - Coloured creature with mv 7 → false.
///     - Colourless creature with mv 6 → false.
///     - Colourless non-creature (artifact) with mv 7 → false.
///   - Cast trigger resolution puts a qualifying creature on top of the
///     library and removes it from elsewhere in the library.
///   - SpellCostReductionAbility predicate accepts colourless creature spells
///     and rejects coloured creature spells / colourless non-creature spells.
///   - End-to-end cost-calc: a colourless creature spell with Conduit in
///     play discounts {2} off generic.
/// </summary>
public class ConduitOfRuinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ConduitOfRuin_Identity()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);

        conduit.Name.Should().Be("Conduit of Ruin");
        conduit.ManaCost.Should().Be("{6}");
        conduit.Power.Should().Be(5);
        conduit.Toughness.Should().Be(5);
        conduit.HasType(CardType.Creature).Should().BeTrue();
        conduit.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        conduit.Owner.Should().BeSameAs(_alice);
        conduit.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(conduit).Should().BeEmpty("Conduit of Ruin is colourless");
    }

    [Fact]
    public void ConduitOfRuin_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Conduit of Ruin", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Conduit of Ruin");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
    }

    [Fact]
    public void ConduitOfRuin_CastTrigger_ActiveZonesIncludesStack()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        var trigger = conduit.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Stack,
            "CR 603.6a — cast triggers fire while the spell is on the stack");
    }

    [Fact]
    public void ConduitOfRuin_CastTrigger_MatchesSelfSpellCastEvent()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        // The trigger's source must be in an ActiveZone — Conduit is on
        // the Stack at cast time (CR 603.6a — cast triggers fire from the
        // stack).
        _alice.Zones.Stack.AddCard(conduit);
        conduit.SetZone(ZoneType.Stack);

        var trigger = conduit.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(conduit, _alice);
        var castEvent = new SpellCastEvent(spell);

        trigger.IsTriggered(castEvent).Should().BeTrue(
            "self-cast detection: SpellCastEvent.Spell.Card == conduit");
    }

    [Fact]
    public void ConduitOfRuin_CastTrigger_DoesNotMatchOtherSpellCast()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        _alice.Zones.Stack.AddCard(conduit);
        conduit.SetZone(ZoneType.Stack);

        var trigger = conduit.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Instant("Lightning Bolt", "{R}");
        other.SetOwner(_alice);
        var spell = new Majik.Core.Spells.Spell(other, _alice);
        var castEvent = new SpellCastEvent(spell);

        trigger.IsTriggered(castEvent).Should().BeFalse();
    }

    [Fact]
    public void IsTutorCandidate_ColorlessCreatureMv7_IsTrue()
    {
        var ulamog = new Creature("Ulamog, the Ceaseless Hunger", "{10}", 10, 10,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Eldrazi });

        ConduitOfRuinFactory.IsTutorCandidate(ulamog).Should().BeTrue();
    }

    [Fact]
    public void IsTutorCandidate_ColorlessCreatureMv6_IsFalse()
    {
        var conduit = new Creature("Conduit of Ruin", "{6}", 5, 5);
        // mv 6 → below the >= 7 threshold.
        ConduitOfRuinFactory.IsTutorCandidate(conduit).Should().BeFalse();
    }

    [Fact]
    public void IsTutorCandidate_ColouredCreatureMv7_IsFalse()
    {
        var dragon = new Creature("Bogardan Hellkite", "{4}{R}{R}{R}", 5, 5);
        // mv 7, but red — coloured pip disqualifies.
        ConduitOfRuinFactory.IsTutorCandidate(dragon).Should().BeFalse();
    }

    [Fact]
    public void IsTutorCandidate_ColorlessNonCreatureMv7_IsFalse()
    {
        var artifact = new Artifact("Heavy Arbalest", "{7}");
        ConduitOfRuinFactory.IsTutorCandidate(artifact).Should().BeFalse();
    }

    [Fact]
    public void ConduitOfRuin_CastTrigger_PlacesQualifyingCardOnTop()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        // Conduit is on the stack at cast time.
        _alice.Zones.Stack.AddCard(conduit);
        conduit.SetZone(ZoneType.Stack);

        // Seed Alice's library with a non-qualifying card on top and a
        // qualifying Ulamog buried below.
        var topDecoy = new Instant("Lightning Bolt", "{R}");
        topDecoy.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topDecoy);
        topDecoy.SetZone(ZoneType.Library);

        var ulamog = new Creature("Ulamog, the Ceaseless Hunger", "{10}", 10, 10,
            subtypes: new[] { CardSubtype.Eldrazi });
        ulamog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(ulamog);
        ulamog.SetZone(ZoneType.Library);

        // Pad with a few more decoys.
        for (var i = 0; i < 3; i++)
        {
            var c = new Instant($"Decoy {i}", "{1}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var trigger = conduit.Abilities.OfType<TriggeredAbility>().Single();
        var spell = new Majik.Core.Spells.Spell(conduit, _alice);
        var castEvent = new SpellCastEvent(spell);
        // Fire the trigger condition so any captured-caster state lives.
        trigger.IsTriggered(castEvent).Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Library.GetCards().First().Should().BeSameAs(ulamog,
            "the tutored card is placed at index 0 of the library AFTER the shuffle (CR 701.20a)");
        _alice.Zones.Library.GetCards().Count(c => ReferenceEquals(c, ulamog))
            .Should().Be(1, "no duplicate Ulamog in the library");
    }

    [Fact]
    public void ConduitOfRuin_CastTrigger_NoQualifyingCard_IsNoop()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        _alice.Zones.Stack.AddCard(conduit);
        conduit.SetZone(ZoneType.Stack);

        // Only non-qualifying cards in the library.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var trigger = conduit.Abilities.OfType<TriggeredAbility>().Single();
        var spell = new Majik.Core.Spells.Spell(conduit, _alice);
        var castEvent = new SpellCastEvent(spell);
        trigger.IsTriggered(castEvent).Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        // No change to library composition (still just the bolt).
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bolt);
    }

    [Fact]
    public void ConduitOfRuin_CostReduction_AppliesToColorlessCreatureSpells()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(conduit);
        conduit.SetZone(ZoneType.Battlefield);

        // Reality Smasher — colourless creature spell ({4}{C} → mv 5).
        var realitySmasher = new Creature("Reality Smasher", "{4}{C}", 5, 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        realitySmasher.SetOwner(_alice);

        var effective = CostReduction.GetEffectiveCost(realitySmasher, _alice);
        // Engine parses {4}{C} into 5 generic (the {C} pip is bucketed
        // as generic per the colourless-payment gap noted in Eldrazi
        // Temple); Conduit discounts {2} → 3 generic remaining.
        effective.Generic.Should().Be(3,
            "Reality Smasher {4}{C} → 5 generic; Conduit subtracts {2}");
    }

    [Fact]
    public void ConduitOfRuin_CostReduction_DoesNotApplyToColouredCreatureSpells()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(conduit);
        conduit.SetZone(ZoneType.Battlefield);

        // Coloured creature — should NOT be reduced.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);

        var effective = CostReduction.GetEffectiveCost(bear, _alice);
        effective.Generic.Should().Be(1,
            "Conduit's discount targets colourless creature spells only");
    }

    [Fact]
    public void ConduitOfRuin_CostReduction_DoesNotApplyToColorlessNonCreatureSpells()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(conduit);
        conduit.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Hedron Archive", "{4}");
        artifact.SetOwner(_alice);

        var effective = CostReduction.GetEffectiveCost(artifact, _alice);
        effective.Generic.Should().Be(4,
            "Conduit's discount targets CREATURE spells only — artifacts pay full");
    }

    [Fact]
    public void ConduitOfRuin_CostReduction_OnlyFromBattlefield()
    {
        var conduit = ConduitOfRuinFactory.Create(_alice);
        // Conduit is in hand — not on the battlefield, so its rider should
        // not be in scope per CR 117.7 (reducers must be in play).
        _alice.Zones.Hand.AddCard(conduit);
        conduit.SetZone(ZoneType.Hand);

        var realitySmasher = new Creature("Reality Smasher", "{4}{C}", 5, 5);
        realitySmasher.SetOwner(_alice);

        var effective = CostReduction.GetEffectiveCost(realitySmasher, _alice);
        effective.Generic.Should().Be(5,
            "Conduit not on the battlefield → no discount (full {4}{C} = 5 generic)");
    }
}
