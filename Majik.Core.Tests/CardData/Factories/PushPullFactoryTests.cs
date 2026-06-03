using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Events;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the split card Push // Pull (Strixhaven: School of Mages,
/// {1}{W/B} // {4}{B/R}{B/R}). Both faces are Sorceries.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   Push — Sorcery {1}{W/B}: "Destroy target tapped creature."
///   Pull — Sorcery {4}{B/R}{B/R}: "Put up to two target creature cards from
///     a single graveyard onto the battlefield under your control. They gain
///     haste until end of turn. Sacrifice them at the beginning of the next
///     end step."
///
/// ## Split-card modelling (CR 712 / CR 709)
/// A split card is a single physical card with two halves; the caster picks a
/// half on cast and casts only that half. The engine's v1 posture (same as
/// Wear // Tear — see <see cref="WearTearFactoryTests"/>) gives each printed
/// half its own <c>[CardName]</c>-dispatched factory:
///   * "Push" → <see cref="PushFactory"/> → Sorcery {1}{W/B} destroy-tapped.
///   * "Pull" → <see cref="PullFactory"/> → Sorcery {4}{B/R}{B/R} reanimate.
/// The combined seed row "Push // Pull" flips <c>IsImplemented</c> via the
/// front-face check in <see cref="EmbeddedCardRepository"/> because the front
/// half "Push" is in the <see cref="ImplementedCardNames"/> registry, and
/// <see cref="PushPullFactory"/> dispatches the combined name directly.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "B")]
public class PushPullFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public PushPullFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    public void Dispose() => AgentRegistry.Clear();

    // ── Combined card — identity + dispatch ────────────────────────────────

    [Fact]
    public void PushPull_Combined_Identity_SorceryAtFrontCost()
    {
        var card = PushPullFactory.Create(_alice);

        card.Name.Should().Be("Push // Pull");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        // CR 712 — the combined object carries the front (Push) printed cost.
        card.ManaCost.ToString().Should().Be("{1}{W/B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PushPull_Combined_IsWhiteBlackRed()
    {
        var card = PushPullFactory.Create(_alice);

        // CR 709.4 — the colors of a split card are the combined colors of its
        // halves: Push {1}{W/B} (W,B) + Pull {4}{B/R}{B/R} (B,R) = W,B,R.
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.White);
        colors.Should().Contain(ManaColorEnum.Black);
        colors.Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PushPull()
    {
        var card = NamedCardFactory.Create("Push // Pull", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Push // Pull");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // ── Push half — identity + dispatch ────────────────────────────────────

    [Fact]
    public void Push_Identity_SorceryAt1WB()
    {
        var card = PushFactory.Create(_alice);

        card.Name.Should().Be("Push");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{W/B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Push_IsWhiteAndBlack()
    {
        var card = PushFactory.Create(_alice);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.White);
        colors.Should().Contain(ManaColorEnum.Black);
    }

    [Fact]
    public void Push_CarriesMdfcState_PushFront_PullBack()
    {
        var card = PushFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Push is the front half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Push");
        card.MdfcState!.BackFaceName.Should().Be("Pull");
        card.MdfcState!.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void Push_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = PushFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("tapped");
    }

    // ── Push half — destroy target tapped creature ─────────────────────────

    [Fact]
    public void Push_DestroysTappedCreature_MovesToGraveyard()
    {
        var creature = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        creature.Tap();

        ResolvePush(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Push destroys target tapped creature (CR 701.7)");
    }

    [Fact]
    public void Push_UntappedCreature_DoesNothing()
    {
        var creature = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        // not tapped

        ResolvePush(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Push targets a TAPPED creature — untapped is illegal (CR 608.2b)");
    }

    [Fact]
    public void Push_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        creature.Tap();

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolvePush(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // ── Pull half — identity + dispatch ────────────────────────────────────

    [Fact]
    public void Pull_Identity_SorceryAt4BRBR()
    {
        var card = PullFactory.Create(_alice);

        card.Name.Should().Be("Pull");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{4}{B/R}{B/R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Pull_IsBlackAndRed()
    {
        var card = PullFactory.Create(_alice);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Black);
        colors.Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void Pull_CarriesMdfcState_PushFront_PullBack()
    {
        var card = PullFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Pull is the back half of the split card");
        card.MdfcState!.FrontFaceName.Should().Be("Push");
        card.MdfcState!.BackFaceName.Should().Be("Pull");
        card.MdfcState!.IsBackFace.Should().BeTrue("Pull is built pre-flipped to the back half");
    }

    [Fact]
    public void Pull_SpellDefinition_HasUpToTwoTargetCreatureCards()
    {
        var def = PullFactory.BuildSpellDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        // "up to two" — 0..2 (CR 115.1a).
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(2);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // ── Pull half — reanimate up to two creature cards ─────────────────────

    [Fact]
    public void Pull_ReanimatesTwoCreatures_UnderYourControl_WithHaste()
    {
        var continuousA = new ContinuousEffectsService();
        var continuousB = new ContinuousEffectsService();
        var grizzly = NewCreatureInGraveyard(_bob, "Grizzly Bears", "{1}{G}", 2, 2, continuousA);
        var bear = NewCreatureInGraveyard(_bob, "Runeclaw Bear", "{2}{G}", 2, 2, continuousB);

        ResolvePull(grizzly, bear);

        // CR 110.2 — both enter under the caster's (Alice's) control. The
        // permanents physically live in their owner's (Bob's) battlefield zone
        // manager but their Controller is Alice — control without ownership.
        grizzly.Zone.Should().Be(ZoneType.Battlefield);
        bear.Zone.Should().Be(ZoneType.Battlefield);
        grizzly.Controller.Should().BeSameAs(_alice);
        bear.Controller.Should().BeSameAs(_alice);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(grizzly);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);

        // "They gain haste until end of turn." (CR 702.10)
        CombatAbilities.HasHaste(grizzly).Should().BeTrue();
        CombatAbilities.HasHaste(bear).Should().BeTrue();
        grizzly.HasSummoningSickness.Should().BeFalse();
        bear.HasSummoningSickness.Should().BeFalse();
    }

    [Fact]
    public void Pull_NonCreatureCardInGraveyard_IsNotReanimated()
    {
        // A non-creature card token handed as a target → illegal (CR 608.2b).
        var sorcery = new Sorcery("Lightning Helix", "{R}{W}");
        sorcery.SetOwner(_bob);
        sorcery.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(sorcery);

        ResolvePull(sorcery);

        sorcery.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pull targets creature CARDS only — a sorcery is illegal (CR 608.2b)");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Pull_NoTargets_IsCleanNoOp()
    {
        var def = PullFactory.BuildSpellDefinition(_alice, o => o, _zones, triggers: null);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { Array.Empty<object>() },
            Mana: ManaPayment.Empty);

        var act = () =>
        {
            foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
        };
        act.Should().NotThrow("up to two = zero is legal (CR 115.1a)");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Pull_RegistersDelayedEndStepSacrifice_ForReanimatedCreatures()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var grizzly = NewCreatureInGraveyard(_bob, "Grizzly Bears", "{1}{G}", 2, 2, null);
        var bear = NewCreatureInGraveyard(_bob, "Runeclaw Bear", "{2}{G}", 2, 2, null);

        var def = PullFactory.BuildSpellDefinition(_alice, o => o, _zones, triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { grizzly, bear } },
            Mana: ManaPayment.Empty);
        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        grizzly.Zone.Should().Be(ZoneType.Battlefield);
        bear.Zone.Should().Be(ZoneType.Battlefield);

        // Fire the next End step — the delayed triggers sacrifice both.
        _bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        // CR 701.16 — sacrifice → owner's (Bob's) graveyard.
        grizzly.Zone.Should().Be(ZoneType.Graveyard);
        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(grizzly);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ResolvePush(ICard target)
    {
        var def = PushFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    private void ResolvePull(params ICard[] targets)
    {
        var def = PullFactory.BuildSpellDefinition(_alice, o => o, _zones, triggers: null);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets.Cast<object>().ToArray() },
            Mana: ManaPayment.Empty);
        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Creature NewCreatureInGraveyard(
        Player owner, string name, string cost, int power, int toughness,
        ContinuousEffectsService? continuous)
    {
        var c = new Creature(name, cost, power, toughness)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
            HasSummoningSickness = true,
        };
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }
}
