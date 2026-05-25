using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ArcboundRavagerFactory"/>.
///
/// Card: Arcbound Ravager — Artifact Creature — Beast {2} (Darksteel /
/// Modern Horizons 2).
///   "Sacrifice an artifact: Put a +1/+1 counter on this creature.
///    Modular 1 (This creature enters with a +1/+1 counter on it. When it
///    dies, you may put its +1/+1 counters on target artifact creature.)"
///
/// Covers:
///   - Identity (name, types — Artifact + Creature, subtype Beast, mana
///     cost, 0/0, owner/controller).
///   - NamedCardFactory dispatch returns a Creature shell carrying the
///     Artifact type and both abilities (Modular death trigger +
///     sacrifice-an-artifact activated ability).
///   - Ability list: 1 TriggeredAbility (Modular death) + 1
///     ActivatedAbility (sac an artifact).
///   - Modular 1 ETB via ReplacementBus pipeline (intent
///     PlusOneCountersOnEnter is stamped).
///   - MarkEntersWithCounter helper stamps the bag directly.
///   - Activated ability: sac-an-artifact cost pays from the
///     battlefield + counter is added on resolve.
///   - Modular death trigger: counters on the graveyard object move to
///     a target artifact creature on the controller's battlefield.
///   - Modular death trigger no-op when no counters / no target.
/// </summary>
public class ArcboundRavagerTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void ArcboundRavager_Identity()
    {
        var c = ArcboundRavagerFactory.Create(_alice);

        c.Name.Should().Be("Arcbound Ravager");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue("printed type — Creature");
        c.HasType(CardType.Artifact).Should().BeTrue("printed type — Artifact (multi-type)");
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue("Beast is the printed subtype");
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(0);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArcboundRavager_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Arcbound Ravager", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Arcbound Ravager");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Modular death trigger is attached at construction");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the sacrifice-an-artifact activated ability is attached");
    }

    [Fact]
    public void ArcboundRavager_AbilityList_OneTrigger_OneActivated()
    {
        var c = ArcboundRavagerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Modular 1 — ETB +1/+1 counter (CR 702.43a / CR 614.1d)
    // -------------------------------------------------------------------------

    [Fact]
    public void Modular_ReplacementBus_StampsEtbCounterIntent()
    {
        // When a ReplacementBus is supplied, the EntersWithCountersReplacement
        // is registered and rewrites the ETB ZoneMoveIntent so the
        // ZoneService lands the counter post-ETB. Here we exercise the
        // replacement directly (no ZoneService dependency).
        var bus = new ReplacementBus();
        var ravager = ArcboundRavagerFactory.Create(_alice, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: ravager,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var rewritten = bus.Apply(intent);

        rewritten.Should().NotBeNull("the ETB replacement rewrites — it does not cancel");
        rewritten!.PlusOneCountersOnEnter.Should().Be(ArcboundRavagerFactory.ModularValue,
            "Modular 1 — replacement bus rewrites the ETB intent to carry 1 +1/+1 counter");
    }

    [Fact]
    public void Modular_NoBus_MarkEntersWithCounter_StampsBagDirectly()
    {
        // Shape-only path — no ReplacementBus. MarkEntersWithCounter is the
        // documented fallback for tests that put Arcbound Ravager on the
        // battlefield without funnelling through ZoneService.
        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);

        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counter yet — ETB hasn't been simulated");

        ArcboundRavagerFactory.MarkEntersWithCounter(ravager);

        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Modular 1 — the helper stamps a single +1/+1 counter");
    }

    // -------------------------------------------------------------------------
    // Activated ability — sacrifice an artifact: +1/+1 counter
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasSacrificeAnArtifactCost()
    {
        var ravager = ArcboundRavagerFactory.Create(_alice);

        var activated = ravager.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().ContainSingle(c => c is SacrificeAnArtifactCost,
            "the only printed cost on the activated ability is 'sacrifice an artifact'");
    }

    [Fact]
    public void ActivatedAbility_CanPay_WhenAnotherArtifactOnBattlefield()
    {
        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);

        // A spare artifact — Mishra's Bauble shape, mana cost {0}.
        var spare = new Artifact("Spare Artifact", "{0}");
        spare.SetOwner(_alice);
        PutOnBattlefield(_alice, spare);

        var activated = ravager.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = activated.Costs.OfType<SacrificeAnArtifactCost>().Single();

        sacCost.CanPay(_alice).Should().BeTrue(
            "Alice controls two artifacts — the cost can be paid");
    }

    [Fact]
    public void ActivatedAbility_Resolve_AddsPlusOneCounter()
    {
        // Put the spare artifact on FIRST so the deterministic-first-artifact
        // picker grabs it (the v1 picker doesn't exclude the ability's
        // source — see SacrificeAnArtifactCost xmldoc). With the spare
        // listed first, Ravager survives the sacrifice and we can observe
        // the +1/+1 counter landing on it.
        var spare = new Artifact("Spare Artifact", "{0}");
        spare.SetOwner(_alice);
        PutOnBattlefield(_alice, spare);

        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);
        ArcboundRavagerFactory.MarkEntersWithCounter(ravager);

        var activated = ravager.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = activated.Costs.OfType<SacrificeAnArtifactCost>().Single();

        // Pay (sacrifice the spare) then resolve the effect.
        sacCost.Pay(_alice);
        spare.Zone.Should().Be(ZoneType.Graveyard,
            "the sacrifice cost moved Spare Artifact to Alice's graveyard");

        activated.Resolve();

        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "ETB counter (1) + activated-ability counter (1)");
    }

    // -------------------------------------------------------------------------
    // Modular 1 — death trigger (CR 702.43b)
    // -------------------------------------------------------------------------

    [Fact]
    public void ModularDeathTrigger_MovesCountersToArtifactCreature()
    {
        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);
        ArcboundRavagerFactory.MarkEntersWithCounter(ravager);
        // Simulate two sacrifice activations → 3 counters total (1 ETB + 2).
        ravager.Counters.Add(CounterType.PlusOnePlusOne, 2);
        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);

        // Bestowal recipient — an artifact creature on Alice's battlefield.
        // Walking-Ballista-shape: Construct 0/0.
        var bestowee = new Creature("Test Artifact Creature", "{2}", 0, 0);
        bestowee.SetOwner(_alice);
        bestowee.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, bestowee);

        // Move Arcbound Ravager to graveyard (simulate death).
        _alice.Zones.Battlefield.RemoveCard(ravager);
        _alice.Zones.Graveyard.AddCard(ravager);
        ravager.SetZone(ZoneType.Graveyard);

        // Resolve the Modular death trigger.
        var modular = ravager.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "all 3 counters from Arcbound Ravager move to the chosen artifact creature");
        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "counters are removed from the graveyard object after bestowal");
    }

    [Fact]
    public void ModularDeathTrigger_NoTarget_LeavesCountersOnGraveObject()
    {
        // No artifact-creature target → the may-effect resolves as a no-op
        // (counters stay on the graveyard object, mirroring the deferred
        // "you may decline" posture).
        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);
        ArcboundRavagerFactory.MarkEntersWithCounter(ravager);

        // Move to graveyard — no artifact creature on the battlefield.
        _alice.Zones.Battlefield.RemoveCard(ravager);
        _alice.Zones.Graveyard.AddCard(ravager);
        ravager.SetZone(ZoneType.Graveyard);

        var modular = ravager.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        ravager.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "no target — counters remain on the graveyard object");
    }

    [Fact]
    public void ModularDeathTrigger_NoCounters_NoOp()
    {
        // Edge case — Arcbound Ravager dies with no +1/+1 counters (its
        // ETB counter was somehow removed pre-death). The trigger fires
        // (counter-presence is not in the trigger condition per oracle —
        // it's only in the resolution effect) but resolves to nothing.
        var ravager = ArcboundRavagerFactory.Create(_alice);
        PutOnBattlefield(_alice, ravager);

        var bestowee = new Creature("Spare AC", "{2}", 0, 0);
        bestowee.SetOwner(_alice);
        bestowee.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, bestowee);

        _alice.Zones.Battlefield.RemoveCard(ravager);
        _alice.Zones.Graveyard.AddCard(ravager);
        ravager.SetZone(ZoneType.Graveyard);

        var modular = ravager.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counters to move — bestowee is unchanged");
    }

    [Fact]
    public void ModularDeathTrigger_ActiveZones_IncludeBattlefieldAndGraveyard()
    {
        // The trigger source is Arcbound Ravager itself, and its zone at
        // resolution time is Graveyard (it just died). The active-zones
        // set must include Graveyard so the trigger's zone-guard passes
        // when evaluated post-move (mirrors Undying's posture).
        var ravager = ArcboundRavagerFactory.Create(_alice);
        var modular = ravager.Abilities.OfType<TriggeredAbility>().Single();

        modular.ActiveZones.Should().Contain(ZoneType.Battlefield);
        modular.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }
}
