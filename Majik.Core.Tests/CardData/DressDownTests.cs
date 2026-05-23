using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Dress Down — Enchantment {1}{U}, Flash.
///   "Creatures lose all abilities and have base power and toughness 1/1.
///    At the beginning of the end step, sacrifice Dress Down."
///
/// Validates:
///   * Card identity + dispatch + Flash keyword.
///   * CR 613.6 — Layer 6 strip of abilities on every creature in the
///     ETB-time pool.
///   * CR 613.7b — Layer 7b set-base 1/1 overriding printed P/T (and
///     CDA-driven P/T like Tarmogoyf's).
///   * LTB restores printed P/T and keywords.
///   * CR 500.4 / CR 603.1 — end-step sacrifice trigger fires on the
///     controller's End step and resolves by moving Dress Down to the
///     graveyard.
/// </summary>
public class DressDownTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public DressDownTests()
    {
        _zones = new ZoneService(_bus);
    }

    private Func<IEnumerable<Creature>> AllBattlefieldCreatures => () =>
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Concat(_bob.Zones.Battlefield.GetCards().OfType<Creature>());

    private Func<IEnumerable<ICard>> AllGraveyards => () =>
        _alice.Zones.Graveyard.GetCards()
            .Concat(_bob.Zones.Graveyard.GetCards());

    /// <summary>
    /// Seed a card into <paramref name="owner"/>'s library so a subsequent
    /// <see cref="ZoneService.MoveCard"/> from Library → Battlefield
    /// actually populates the player-side battlefield zone (the
    /// <see cref="ZoneManager.MoveCard"/> path only adds to target if
    /// source removal succeeded).
    /// </summary>
    private void StartInLibrary(ICard card, Player owner)
    {
        card.SetOwner(owner);
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
    }

    // ------------------------------------------------------------------
    // Card identity + dispatch + Flash
    // ------------------------------------------------------------------

    [Fact]
    public void DressDown_IsEnchantmentNamedDressDown_AtCost1U()
    {
        var dd = DressDownFactory.Create(_alice);

        dd.Name.Should().Be("Dress Down");
        dd.HasType(CardType.Enchantment).Should().BeTrue();
        dd.ManaCost.Should().Be("{1}{U}");
        dd.Owner.Should().BeSameAs(_alice);
        dd.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DressDown_WithFlash()
    {
        var dd = NamedCardFactory.Create("Dress Down", _alice);

        dd.Should().BeOfType<Enchantment>();
        dd.Name.Should().Be("Dress Down");
        dd.ManaCost.Should().Be("{1}{U}");

        // CR 702.8 — Flash keyword wired on the shape-only dispatch.
        dd.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Dress Down has Flash (CR 702.8)");
    }

    // ------------------------------------------------------------------
    // CR 613.6 — Layer 6 strip
    // ------------------------------------------------------------------

    /// <summary>
    /// Goblin Guide has Haste (a printed keyword). With Dress Down on the
    /// battlefield, Layer 6 strips Haste from the working
    /// CreatureCharacteristics.
    /// </summary>
    [Fact]
    public void DressDown_OnBattlefield_StripsKeywordFromCreature()
    {
        // Build Goblin Guide (vanilla 2/2 with Haste) and put it on the
        // battlefield BEFORE Dress Down enters — must be in the ETB pool.
        var goblinGuide = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            ActiveEffects = _effects,
        };
        goblinGuide.SetController(_alice);
        goblinGuide.AddAbility(new KeywordAbility("Haste", goblinGuide, _alice));
        StartInLibrary(goblinGuide, _alice);
        _zones.MoveCard(goblinGuide, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Baseline — Haste is on the effective keyword set.
        _effects.Compute(goblinGuide).Keywords.Should().Contain("Haste");

        // Dress Down enters and snapshots the battlefield (Goblin Guide is
        // in the pool).
        var dd = DressDownFactory.Create(
            _alice, _effects, _bus, triggers: null, AllBattlefieldCreatures);
        StartInLibrary(dd, _alice);
        _zones.MoveCard(dd, ZoneType.Library, ZoneType.Battlefield, _alice);

        var underDressDown = _effects.Compute(goblinGuide);
        underDressDown.Keywords.Should().NotContain("Haste",
            "CR 613.6 — Layer 6 ability-removing effect strips all keywords");
    }

    // ------------------------------------------------------------------
    // CR 613.7b — Layer 7b base 1/1 override (including over a CDA)
    // ------------------------------------------------------------------

    /// <summary>
    /// Tarmogoyf's CDA (CR 613.2 Layer 7a) computes from graveyards. Under
    /// Dress Down (Layer 6 strip + Layer 7b 1/1), the CDA is suppressed
    /// because its Source has been stripped (CR 613.8 dependency from
    /// LoseAllAbilitiesEffect), AND the 7b set-base overrides whatever
    /// Layer 7a produced anyway. Net effect: Tarmogoyf is 1/1.
    /// </summary>
    [Fact]
    public void DressDown_OverridesTarmogoyfCdaTo_1_1()
    {
        var goyf = TarmogoyfFactory.Create(_alice, _effects, _bus, AllGraveyards);
        goyf.ActiveEffects = _effects;
        StartInLibrary(goyf, _alice);
        _zones.MoveCard(goyf, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Seed two card types into a graveyard so the CDA would normally
        // produce 2/3.
        var instant = new Card("Spell", "", cardTypes: new[] { CardType.Instant });
        var creatureCard = new Card("Some Creature", "", cardTypes: new[] { CardType.Creature });
        _alice.Zones.Graveyard.AddCard(instant);
        _alice.Zones.Graveyard.AddCard(creatureCard);

        // Baseline — Tarmogoyf is 2/3 from two distinct card types.
        var baseline = _effects.Compute(goyf);
        baseline.Power.Should().Be(2);
        baseline.Toughness.Should().Be(3);

        // Dress Down resolves and snapshots Tarmogoyf into the pool.
        var dd = DressDownFactory.Create(
            _alice, _effects, _bus, triggers: null, AllBattlefieldCreatures);
        StartInLibrary(dd, _alice);
        _zones.MoveCard(dd, ZoneType.Library, ZoneType.Battlefield, _alice);

        var underDressDown = _effects.Compute(goyf);
        underDressDown.Power.Should().Be(1,
            "CR 613.7b — Layer 7b set-base P/T overrides Tarmogoyf's CDA");
        underDressDown.Toughness.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // LTB restoration
    // ------------------------------------------------------------------

    /// <summary>
    /// When Dress Down leaves the battlefield (e.g. end-step sac, removal),
    /// the static-effect lifecycle unregisters both the Layer 6 strip and
    /// every Layer 7b override. Affected creatures revert to printed P/T
    /// and printed keywords.
    /// </summary>
    [Fact]
    public void DressDown_LeavesBattlefield_RestoresAbilitiesAndPT()
    {
        var goblinGuide = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            ActiveEffects = _effects,
        };
        goblinGuide.SetController(_alice);
        goblinGuide.AddAbility(new KeywordAbility("Haste", goblinGuide, _alice));
        StartInLibrary(goblinGuide, _alice);
        _zones.MoveCard(goblinGuide, ZoneType.Library, ZoneType.Battlefield, _alice);

        var dd = DressDownFactory.Create(
            _alice, _effects, _bus, triggers: null, AllBattlefieldCreatures);
        StartInLibrary(dd, _alice);
        _zones.MoveCard(dd, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Mid-state sanity — Haste stripped, P/T 1/1.
        var under = _effects.Compute(goblinGuide);
        under.Keywords.Should().NotContain("Haste");
        under.Power.Should().Be(1);
        under.Toughness.Should().Be(1);

        // Send Dress Down to the graveyard — the static-effect lifecycle
        // should observe the CardMovedEvent and unregister everything.
        _zones.MoveCard(dd, ZoneType.Battlefield, ZoneType.Graveyard);

        var restored = _effects.Compute(goblinGuide);
        restored.Keywords.Should().Contain("Haste",
            "Layer 6 effect unregistered → printed keyword restored");
        restored.Power.Should().Be(2,
            "Layer 7b effect unregistered → printed P/T restored");
        restored.Toughness.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // CR 500.4 / CR 603.1 — end-step sacrifice trigger
    // ------------------------------------------------------------------

    /// <summary>
    /// On the controller's End step, the registered end-step trigger
    /// fires. PutPendingTriggersOnStack + resolve moves Dress Down to its
    /// owner's graveyard.
    /// </summary>
    [Fact]
    public void DressDown_AtControllersEndStep_SacrificesItself()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var dd = DressDownFactory.Create(
            _alice, _effects, _bus, triggers, AllBattlefieldCreatures);
        StartInLibrary(dd, _alice);
        _zones.MoveCard(dd, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Fire End step on the controller's turn — the trigger should
        // queue, resolve to a sacrifice (Battlefield → Graveyard).
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "Dress Down's end-step trigger fires at the start of the controller's End step");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        dd.Zone.Should().Be(ZoneType.Graveyard,
            "the end-step trigger resolves by sacrificing Dress Down");
        _alice.Zones.Graveyard.GetCards().Should().Contain(dd);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dd);
    }

    /// <summary>
    /// End step on the OPPONENT's turn must not fire the trigger
    /// (Triggers.OnStepBegin filters on controller). Other steps on the
    /// controller's turn must also not fire.
    /// </summary>
    [Fact]
    public void DressDown_EndStepOnOpponentsTurn_DoesNotSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var dd = DressDownFactory.Create(
            _alice, _effects, _bus, triggers, AllBattlefieldCreatures);
        StartInLibrary(dd, _alice);
        _zones.MoveCard(dd, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Opponent's end step + controller's non-end steps — none of these
        // should fire the trigger.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Main, _alice));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires on the controller's End step");
        dd.Zone.Should().Be(ZoneType.Battlefield,
            "Dress Down remains on the battlefield until its end-step trigger fires");
    }
}
