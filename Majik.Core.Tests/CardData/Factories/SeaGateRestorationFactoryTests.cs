using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SeaGateRestorationFactory"/> and
/// <see cref="SeaGateRebornFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Sea Gate Restoration // Sea Gate, Reborn.
///
/// Front face (Sea Gate Restoration, {4}{U}{U}{U}):
///   Sorcery. "Draw cards equal to the number of cards in your hand plus one.
///   You have no maximum hand size for the rest of the game."
///
/// Back face (Sea Gate, Reborn):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {U}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: no targets, not an X-spell.
/// - Front: draws (hand size + 1) cards.
/// - Front: draws exactly 1 when hand is empty (0 + 1).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {U} mana ability.
/// - Back: pay 3 life -> enters untapped.
/// - Back: decline -> enters tapped.
/// - Back: life &lt; 3 -> enters tapped (CR 119.4).
/// - Back: no agent -> enters tapped.
/// </summary>
public class SeaGateRestorationFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public SeaGateRestorationFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static ChosenSpellParams MakeChosen() =>
        new(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    /// <summary>Fill the player's library with vanilla cards to draw from.</summary>
    private static void FillLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Creature($"Library Card {i}", "{G}", 1, 1);
            card.SetOwner(owner);
            owner.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    private static void AddToHand(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Creature($"Hand Card {i}", "{R}", 1, 1);
            card.SetOwner(owner);
            owner.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        }
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SeaGateRestoration_Identity_4UUU_Sorcery()
    {
        var card = SeaGateRestorationFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sea Gate Restoration");
        card.ManaCost.Should().Be("{4}{U}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SeaGateRestoration()
    {
        var card = NamedCardFactory.Create("Sea Gate Restoration", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Sea Gate Restoration");
        card.ManaCost.Should().Be("{4}{U}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void SeaGateRestoration_IsBlue()
    {
        var card = SeaGateRestorationFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "three {U} pips make it blue");
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void SeaGateRestoration_CarriesMdfcState_FrontFace()
    {
        var card = SeaGateRestorationFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Sea Gate Restoration is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Sea Gate Restoration");
        card.MdfcState!.BackFaceName.Should().Be("Sea Gate, Reborn");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Sea Gate Restoration");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_NoTargets_NotXSpell()
    {
        var def = SeaGateRestorationFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse("Sea Gate Restoration is not an X-spell");
        def.TargetRequests.Should().BeEmpty("the draw clause targets nothing");
        def.Modes.Should().BeEmpty();
    }

    // =========================================================================
    // Front face — resolve: draw (hand size + 1)
    // =========================================================================

    [Fact]
    public void Resolve_DrawsCardsEqualToHandSizePlusOne()
    {
        // 3 cards in hand -> draw 3 + 1 = 4. Library has plenty.
        AddToHand(_alice, 3);
        FillLibrary(_alice, 10);

        var def = SeaGateRestorationFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(MakeChosen())) e.Execute();

        // Started with 3 in hand; drew 4 -> 7 in hand, 6 left in library.
        _alice.Zones.Hand.GetCards().Count().Should().Be(7,
            "drew (hand size 3) + 1 = 4 cards on top of the original 3");
        _alice.Zones.Library.GetCards().Count().Should().Be(6,
            "10 - 4 drawn = 6 remaining");
    }

    [Fact]
    public void Resolve_DrawsExactlyOne_WhenHandEmpty()
    {
        // Empty hand -> draw 0 + 1 = 1.
        FillLibrary(_alice, 5);

        var def = SeaGateRestorationFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(MakeChosen())) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(1,
            "empty hand -> draw (0 + 1) = 1 card");
        _alice.Zones.Library.GetCards().Count().Should().Be(4);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void SeaGateReborn_Identity_Land()
    {
        var land = SeaGateRebornFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Sea Gate, Reborn");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Sea Gate, Reborn is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SeaGateReborn()
    {
        var card = NamedCardFactory.Create("Sea Gate, Reborn", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Sea Gate, Reborn");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // =========================================================================
    // Back face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void SeaGateReborn_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = SeaGateRebornFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Sea Gate, Reborn is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Sea Gate Restoration");
        land.MdfcState!.BackFaceName.Should().Be("Sea Gate, Reborn");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Sea Gate, Reborn");
    }

    // =========================================================================
    // Back face — {T}: Add {U}
    // =========================================================================

    [Fact]
    public void SeaGateReborn_HasSingleManaAbility_AddingBlue()
    {
        var land = SeaGateRebornFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {U} ability");
        manaAbilities[0].ManaGenerated.Blue.Should().BeGreaterThan(0, "produces blue mana");
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void SeaGateReborn_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = SeaGateRebornFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void SeaGateReborn_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = SeaGateRebornFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    [Fact]
    public void SeaGateReborn_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = SeaGateRebornFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("enters tapped when agent declines");
        _alice.LifeTotal.Should().Be(20, "no life paid");
    }

    [Fact]
    public void SeaGateReborn_EntersTapped_WhenLifeBelowThree()
    {
        // CR 119.4 — can't pay life you don't have.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if prompted, ScriptedAgent would throw.
        AgentRegistry.Set(_alice, agent);

        var land = SeaGateRebornFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue(
            "can't pay 3 life with only 2 — enters tapped (CR 119.4)");
        _alice.LifeTotal.Should().Be(2, "no payment taken");
    }

    [Fact]
    public void SeaGateReborn_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        // No AgentRegistry.Set — no agent at all.

        var land = SeaGateRebornFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue("no agent → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }
}
