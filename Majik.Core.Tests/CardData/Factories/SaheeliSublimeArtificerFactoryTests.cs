using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Saheeli, Sublime Artificer (War of the Spark, {1}{U/R}{U/R}).
///
/// Legendary Planeswalker — Saheeli, starting loyalty 5. Oracle text
/// (Scryfall, verified 2026-06-23):
///   "Whenever you cast a noncreature spell, create a 1/1 colorless Servo
///    artifact creature token.
///    −2: Target artifact you control becomes a copy of another target
///    artifact or creature you control until end of turn, except it's an
///    artifact in addition to its other types."
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Saheeli, loyalty 5,
///     {1}{U/R}{U/R}), materialised from the embedded JSON definition.
///   - Static cast trigger: casting a noncreature spell creates a 1/1
///     colourless Servo artifact creature token; a creature spell does NOT
///     (CR 603.1 / 302.1).
///   - −2: target artifact becomes a copy of another artifact/creature you
///     control until end of turn, except it stays an artifact (CR 707.2 /
///     707.9b / 613), expiring at cleanup (CR 514.2).
/// </summary>
[Trait("Color", "M")]
public class SaheeliSublimeArtificerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity (the one non-vanilla *_Identity assert).
    // -----------------------------------------------------------------------

    [Fact]
    public void Saheeli_IsLegendaryPlaneswalker_Saheeli_5Loyalty_AtCost1URUR()
    {
        var saheeli = SaheeliSublimeArtificerFactory.Create(_alice);

        saheeli.Name.Should().Be("Saheeli, Sublime Artificer");
        saheeli.ManaCost.Should().Be("{1}{U/R}{U/R}");
        saheeli.HasType(CardType.Planeswalker).Should().BeTrue();
        saheeli.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        saheeli.HasSubtype(CardSubtype.Saheeli).Should().BeTrue();
        saheeli.Loyalty.Should().Be(5);
        saheeli.StartingLoyalty.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Static cast trigger — Servo on a noncreature spell.
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureSpell_CreatesOneColorlessServoArtifactCreatureToken()
    {
        var saheeli = SaheeliSublimeArtificerFactory.Create(_alice);
        saheeli.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(saheeli);

        var trigger = saheeli.Abilities.OfType<TriggeredAbility>().Single();

        // CR 603.1 — an instant (noncreature) spell cast by the controller fires.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bolt, _alice);
        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, trigger).Should().BeTrue(
            "CR 603.1 — controller cast a noncreature spell");

        trigger.Effects.Single().Execute();

        var servos = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Servo))
            .ToList();

        servos.Should().HaveCount(1);
        var servo = servos.Single();
        servo.Power.Should().Be(1);
        servo.Toughness.Should().Be(1);
        servo.HasType(CardType.Artifact).Should().BeTrue("1/1 colorless Servo artifact creature token");
        servo.HasType(CardType.Creature).Should().BeTrue();
        servo.GetEffectiveColors().Should().BeEmpty("colorless");
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotCreateServo()
    {
        var saheeli = SaheeliSublimeArtificerFactory.Create(_alice);
        var trigger = saheeli.Abilities.OfType<TriggeredAbility>().Single();

        // CR 302.1 — a creature spell is NOT a noncreature spell.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(bear, _alice);
        var castEvent = new SpellCastEvent(spell);

        trigger.Condition.Matches(castEvent, trigger).Should().BeFalse(
            "CR 603.1 — a creature spell does not satisfy 'noncreature spell'");
    }

    // -----------------------------------------------------------------------
    // −2: become a copy, except still an artifact, until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_TargetArtifactBecomesCopyOfCreature_StaysArtifact_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        // Target: an artifact you control (a 0/0-ish artifact — use a plain
        // Artifact). The copy source: a creature you control.
        var target = new Artifact("Ornithopter", "{0}")
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(target);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var saheeli = SaheeliSublimeArtificerFactory.Create(
            _alice,
            copyTargetResolver: () => target,
            copySourceResolver: () => bear,
            effects: effects,
            zones: null);

        saheeli.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        saheeli.Loyalty.Should().Be(3); // 5 − 2

        // CR 707.2 — the target now copies the Bear's characteristics.
        var chars = effects.Compute(target);
        chars.Types.Should().Contain(CardType.Creature, "now a copy of Grizzly Bears");
        chars.Subtypes.Should().Contain(CardSubtype.Bear);
        // CR 707.9b — "except it's an artifact in addition to its other types".
        chars.Types.Should().Contain(CardType.Artifact, "the artifact rider survives the copy's type overwrite");

        // CR 514.2 — both the copy and the Artifact rider drop at cleanup.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Types.Should().NotContain(CardType.Creature, "copy expired");
        after.Types.Should().Contain(CardType.Artifact, "back to its printed artifact self");
    }

    [Fact]
    public void Minus2_WithNoTargets_NoOps_ButLoyaltyStillApplies()
    {
        var effects = new ContinuousEffectsService();
        var saheeli = SaheeliSublimeArtificerFactory.Create(
            _alice,
            copyTargetResolver: () => null,
            copySourceResolver: () => null,
            effects: effects,
            zones: null);

        var act = () => saheeli.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2).Activate();
        act.Should().NotThrow();
        saheeli.Loyalty.Should().Be(3); // 5 − 2
    }
}
