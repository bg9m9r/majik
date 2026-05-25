using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KappaCannoneerFactory"/>
/// (Commander Legends: Battle for Baldur's Gate, {5}{U}).
///
/// Artifact Creature — Turtle Warrior 4/4. Oracle text:
///   "Improvise (...)
///    Ward {4}
///    Whenever this creature or another artifact you control enters,
///    put a +1/+1 counter on this creature. It can't be blocked this
///    turn."
///
/// Covers:
///   - Identity (Artifact Creature, Turtle + Warrior subtypes, {5}{U},
///     4/4, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Improvise + Ward keyword markers attached (deferred wiring noted).
///   - <see cref="KappaCannoneerFactory.BuildWardEffect"/> exposes a
///     bound <see cref="Majik.Core.Keywords.WardEffect"/> with the
///     printed {4} cost.
///   - ETB trigger fires when an artifact you control enters → +1/+1
///     counter on Kappa + EOT CannotBeBlocked restriction.
///   - ETB trigger fires for Kappa entering itself (Kappa is an Artifact
///     Creature → satisfies the "or" branch of its own trigger).
///   - ETB trigger does not fire for non-artifact creatures entering.
///   - ETB trigger does not fire for an opponent's artifacts entering.
/// </summary>
public class KappaCannoneerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KappaCannoneer_Identity()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);

        kappa.Name.Should().Be("Kappa Cannoneer");
        kappa.ManaCost.Should().Be("{5}{U}");
        kappa.HasType(CardType.Creature).Should().BeTrue();
        kappa.HasType(CardType.Artifact).Should().BeTrue(
            "Kappa Cannoneer is an Artifact Creature (CR 301.1 / 302.1)");
        kappa.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        kappa.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        kappa.BasePower.Should().Be(4);
        kappa.BaseToughness.Should().Be(4);
        kappa.Owner.Should().BeSameAs(_alice);
        kappa.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KappaCannoneer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Kappa Cannoneer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Kappa Cannoneer");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(4);

        // One triggered ability (artifact ETB) + two keyword markers
        // (Improvise + Ward) — no Improvise cost-reduction primitive yet.
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "single artifact-ETB trigger");
    }

    // -----------------------------------------------------------------------
    // Keyword markers (deferred wiring posture)
    // -----------------------------------------------------------------------

    [Fact]
    public void KappaCannoneer_HasImproviseAndWardKeywordMarkers()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var keywords = kappa.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Improvise",
            "CR 702.127 — Improvise marker attached for future cost-reduction discovery");
        keywords.Should().Contain(k => k.Keyword == "Ward",
            "CR 702.21 — Ward marker attached for future trigger plumbing");
    }

    [Fact]
    public void KappaCannoneer_BuildWardEffect_ExposesPrinted4Cost()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var ward = KappaCannoneerFactory.BuildWardEffect(kappa);

        ward.Source.Should().BeSameAs(kappa);
        ward.Cost.Generic.Should().Be(4,
            "Ward {4} — 4 generic");
    }

    // -----------------------------------------------------------------------
    // Artifact-ETB trigger — predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void KappaCannoneer_ArtifactEntersUnderControl_TriggerMatches()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        kappa.SetZone(ZoneType.Battlefield);

        // Another artifact enters under Alice's control.
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        solRing.SetController(_alice);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: solRing,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Kappa's trigger fires when another artifact enters under controller");
    }

    [Fact]
    public void KappaCannoneer_SelfEnters_TriggerMatches()
    {
        // Kappa itself is an Artifact Creature — satisfies the "this
        // creature or another artifact" clause via the artifact predicate
        // alone (no separate self-ETB branch needed).
        var kappa = KappaCannoneerFactory.Create(_alice);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: kappa,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Kappa's own ETB fires its own trigger (Kappa is an Artifact)");
    }

    [Fact]
    public void KappaCannoneer_NonArtifactCreatureEnters_TriggerDoesNotMatch()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        kappa.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: bear,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "non-artifact creature does NOT fire Kappa's trigger");
    }

    [Fact]
    public void KappaCannoneer_OpponentArtifactEnters_TriggerDoesNotMatch()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        kappa.SetZone(ZoneType.Battlefield);

        var oppArtifact = new Artifact("Sol Ring", "{1}");
        oppArtifact.SetOwner(_bob);
        oppArtifact.SetController(_bob);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(
            card: oppArtifact,
            fromZone: ZoneType.Hand,
            toZone: ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "opponent's artifact does NOT fire Kappa's trigger (CR 109.5 — 'you control')");
    }

    // -----------------------------------------------------------------------
    // Artifact-ETB trigger — effect (counter + can't be blocked rider)
    // -----------------------------------------------------------------------

    [Fact]
    public void KappaCannoneer_TriggerEffect_AddsPlusOnePlusOneCounter()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        kappa.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(kappa);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        kappa.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Kappa gains a +1/+1 counter when an artifact ETBs under its controller");
    }

    [Fact]
    public void KappaCannoneer_TriggerEffect_RegistersCannotBeBlockedEffect_WhenServiceSupplied()
    {
        var effects = new ContinuousEffectsService();
        var kappa = KappaCannoneerFactory.Create(
            _alice, eventBus: null, triggers: null, continuousEffects: effects);
        kappa.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(kappa);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        // CR 702.x — "can't be blocked this turn" surfaces as a per-turn
        // CombatRestrictionEffect on the continuous-effects service.
        effects.HasRestriction(kappa, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "EOT CannotBeBlocked restriction is registered against Kappa");
    }

    [Fact]
    public void KappaCannoneer_TriggerEffect_NoServiceSupplied_OnlyCounterApplied()
    {
        // Shape-only path: no continuous-effects service supplied.
        var kappa = KappaCannoneerFactory.Create(_alice);
        kappa.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(kappa);

        var trigger = kappa.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in trigger.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "shape-only path silently skips the can't-be-blocked rider");
        kappa.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "counter is still added even when no continuous-effects service is wired");
    }
}
