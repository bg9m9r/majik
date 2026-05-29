using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MalakirRebirthFactory"/> and
/// <see cref="MalakirMireFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Malakir Rebirth // Malakir Mire.
///
/// Front face (Malakir Rebirth, {B}):
///   Instant. "Choose target creature. You lose 2 life. Until end of turn,
///   that creature gains 'When this creature dies, return it to the
///   battlefield tapped under its owner's control.'"
///
/// Back face (Malakir Mire):
///   Land. "This land enters tapped." "{T}: Add {B}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: single 1..1 target-creature request.
/// - Front: resolution loses 2 life and grants the dies-trigger.
/// - Front: granted creature dies → returns to battlefield tapped under
///   its owner's control (driven end-to-end via TriggerManager).
/// - Front: returns under the OWNER's control (not the spell's controller).
/// - Front: granted trigger is NOT Persist (no -1/-1 counter on return).
/// - Front: grant expires at end of turn (ExpireEndOfTurn revokes it).
/// - Front: illegal target at resolution → no life loss, no grant.
/// - Back: Land type, non-basic, {T}: Add {B} mana ability.
/// </summary>
public class MalakirRebirthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams MakeChosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    private static Creature MakeBear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void MalakirRebirth_Identity_B_Instant()
    {
        var card = MalakirRebirthFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Malakir Rebirth");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MalakirRebirth()
    {
        var card = NamedCardFactory.Create("Malakir Rebirth", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Malakir Rebirth");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void MalakirRebirth_IsBlack()
    {
        var card = MalakirRebirthFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B} pip makes it black");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
    }

    [Fact]
    public void MalakirRebirth_CarriesMdfcState_FrontFace()
    {
        var card = MalakirRebirthFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Malakir Rebirth is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Malakir Rebirth");
        card.MdfcState!.BackFaceName.Should().Be("Malakir Mire");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Malakir Rebirth");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreature_NoVariableX()
    {
        var def = MalakirRebirthFactory.BuildSpellDefinition(_alice, o => o!);

        def.HasVariableX.Should().BeFalse("Malakir Rebirth is not an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, "Choose target creature — exactly one");
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — resolution: lose 2 life + grant
    // =========================================================================

    [Fact]
    public void Resolve_LosesTwoLife_AndGrantsDeathTrigger()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        var granted = MalakirRebirthFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeSameAs(bear);
        _alice.LifeTotal.Should().Be(18, "you lose 2 life (CR 119.3)");
        bear.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the dies → return-tapped trigger is granted to the target");
    }

    [Fact]
    public void GrantedDeathTrigger_HasBothActiveZones()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        MalakirRebirthFactory.Resolve(_alice, bear, o => o!);

        var trig = bear.Abilities.OfType<TriggeredAbility>().Single();
        trig.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trig.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "Graveyard must be in ActiveZones — the trigger evaluates after the death zone-move");
    }

    // =========================================================================
    // Front face — end-to-end: dies → returns tapped under owner's control
    // =========================================================================

    [Fact]
    public void GrantedCreatureDies_ReturnsToBattlefieldTapped()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        // Resolve Malakir Rebirth, routing the grant's return through the
        // shared ZoneService so ETB triggers would fire (CR 603.6a).
        MalakirRebirthFactory.Resolve(_alice, bear, o => o!, zones);
        triggers.BindCard(bear);

        // The bear dies (Battlefield → Graveyard).
        zones.MoveCardTo(bear, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1,
            "the granted dies-trigger queues on death");
        triggers.PutPendingTriggersOnStack(_bob);
        stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield, "the creature returns to the battlefield");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
        bear.IsTapped.Should().BeTrue("the creature returns tapped (printed 'tapped')");
    }

    [Fact]
    public void GrantedCreatureDies_ReturnsUnderOwnersControl_NotSpellController()
    {
        // Bob owns the bear; Alice (controlling Malakir Rebirth) gifts the
        // grant. The creature must return under its OWNER's (Bob's) control.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        MalakirRebirthFactory.Resolve(_alice, bear, o => o!, zones);
        triggers.BindCard(bear);

        zones.MoveCardTo(bear, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_bob);
        stack.Pop()!.Resolve();

        bear.Controller.Should().BeSameAs(_bob,
            "returns under its owner's control (printed 'under its owner's control')");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void GrantedCreatureDies_NoMinusOneCounter_NotPersist()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        PutOnBattlefield(_bob, bear);

        MalakirRebirthFactory.Resolve(_alice, bear, o => o!, zones);
        triggers.BindCard(bear);

        zones.MoveCardTo(bear, ZoneType.Graveyard);
        triggers.PutPendingTriggersOnStack(_bob);
        stack.Pop()!.Resolve();

        bear.Counters.Count(Majik.Core.Counters.CounterType.MinusOneMinusOne).Should().Be(0,
            "Malakir Rebirth is NOT Persist — the creature returns with no -1/-1 counter");
    }

    // =========================================================================
    // Front face — EOT expiry of the grant (CR 514.2)
    // =========================================================================

    [Fact]
    public void Grant_ExpiresAtEndOfTurn_TriggerRemoved()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        var effects = new ContinuousEffectsService();
        bear.ActiveEffects = effects;
        PutOnBattlefield(_bob, bear);

        MalakirRebirthFactory.Resolve(_alice, bear, o => o!);
        bear.Abilities.OfType<TriggeredAbility>().Should().ContainSingle("grant is live before cleanup");

        // CR 514.2 — cleanup step expires "until end of turn" grants.
        effects.ExpireEndOfTurn();

        bear.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the until-end-of-turn grant is revoked at cleanup (CR 514.2 / CR 613.6e)");
    }

    // =========================================================================
    // Front face — illegal target at resolution (CR 608.2b/608.2c)
    // =========================================================================

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoLifeLoss_NoGrant()
    {
        var bear = MakeBear("Grizzly Bears", _bob);
        bear.ActiveEffects = new ContinuousEffectsService();
        // NOT on the battlefield (still "in hand" zone).
        bear.SetZone(ZoneType.Hand);

        var granted = MalakirRebirthFactory.Resolve(_alice, bear, o => o!);

        granted.Should().BeNull("illegal target at resolution → spell does nothing (CR 608.2c)");
        _alice.LifeTotal.Should().Be(20, "no life lost when the only target is illegal");
        bear.Abilities.OfType<TriggeredAbility>().Should().BeEmpty("no grant on an illegal target");
    }

    [Fact]
    public void Resolve_TargetNotACreature_NoLifeLoss_NoGrant()
    {
        var granted = MalakirRebirthFactory.Resolve(
            _alice,
            rawTarget: "not-a-creature",
            resolver: _ => "not-a-creature");

        granted.Should().BeNull("non-creature target → spell does nothing");
        _alice.LifeTotal.Should().Be(20, "no life lost");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void MalakirMire_Identity_Land()
    {
        var land = MalakirMireFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Malakir Mire");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Malakir Mire is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MalakirMire()
    {
        var card = NamedCardFactory.Create("Malakir Mire", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Malakir Mire");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void MalakirMire_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = MalakirMireFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull("Malakir Mire is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Malakir Rebirth");
        land.MdfcState!.BackFaceName.Should().Be("Malakir Mire");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Malakir Mire");
    }

    [Fact]
    public void MalakirMire_HasSingleManaAbility_AddingBlack()
    {
        var land = MalakirMireFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }
}
