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
/// Tests for <see cref="AgadeemsAwakeningFactory"/> and
/// <see cref="AgadeemTheUndercryptFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card
/// Agadeem's Awakening // Agadeem, the Undercrypt.
///
/// Front face (Agadeem's Awakening, {X}{B}{B}{B}):
///   Sorcery. "Return from your graveyard to the battlefield any number of
///   target creature cards that each have a different mana value X or less."
///
/// Back face (Agadeem, the Undercrypt):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {B}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: HasVariableX=true, 0..N target request.
/// - Front: returns a single creature card whose mana value ≤ X.
/// - Front: returns multiple creature cards with distinct mana values ≤ X.
/// - Front: drops a card whose mana value exceeds X (CR 601.2c).
/// - Front: keeps at most one card per distinct mana value (CR 601.2c).
/// - Front: drops illegal-at-resolution targets (CR 608.2b).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {B} mana ability.
/// - Back: pay 3 life → enters untapped.
/// - Back: decline → enters tapped.
/// - Back: life &lt; 3 → enters tapped (CR 119.4).
/// - Back: no agent → enters tapped.
/// </summary>
public class AgadeemsAwakeningFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AgadeemsAwakeningFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static void PutInGraveyard(Player owner, Card card)
    {
        card.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private static ChosenSpellParams MakeChosen(int x, params object[] targets) =>
        new(
            ModeIndex: null,
            X: x,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void AgadeemsAwakening_Identity_XBBB_Sorcery()
    {
        var card = AgadeemsAwakeningFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Agadeem's Awakening");
        card.ManaCost.Should().Be("{X}{B}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AgadeemsAwakening()
    {
        var card = NamedCardFactory.Create("Agadeem's Awakening", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Agadeem's Awakening");
        card.ManaCost.Should().Be("{X}{B}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void AgadeemsAwakening_IsBlack()
    {
        var card = AgadeemsAwakeningFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "three {B} pips make it black");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void AgadeemsAwakening_CarriesMdfcState_FrontFace()
    {
        var card = AgadeemsAwakeningFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Agadeem's Awakening is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Agadeem's Awakening");
        card.MdfcState!.BackFaceName.Should().Be("Agadeem, the Undercrypt");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Agadeem's Awakening");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndAnyNumberOfTargets()
    {
        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);

        def.HasVariableX.Should().BeTrue("Agadeem's Awakening is an X-spell");
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0,
            "any number of targets — zero is valid");
        def.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue,
            "any number of targets");
    }

    // =========================================================================
    // Front face — resolve: single creature card, mana value <= X
    // =========================================================================

    [Fact]
    public void Resolve_ReturnsSingleCreatureCard_WithManaValueAtMostX()
    {
        // Bear: mana value 2. X = 2 → returned.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutInGraveyard(_alice, bear);

        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(2, bear));
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "mana value 2 ≤ X=2 → returned to battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(_alice, "returned under the caster's control");
    }

    // =========================================================================
    // Front face — resolve: multiple distinct mana values
    // =========================================================================

    [Fact]
    public void Resolve_ReturnsMultipleCreatures_WithDistinctManaValues()
    {
        var oneDrop = new Creature("One Drop", "{B}", 1, 1);        // mv 1
        var twoDrop = new Creature("Two Drop", "{1}{B}", 2, 2);      // mv 2
        var threeDrop = new Creature("Three Drop", "{1}{B}{B}", 3, 3); // mv 3
        PutInGraveyard(_alice, oneDrop);
        PutInGraveyard(_alice, twoDrop);
        PutInGraveyard(_alice, threeDrop);

        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(3, oneDrop, twoDrop, threeDrop));
        foreach (var e in effects) e.Execute();

        oneDrop.Zone.Should().Be(ZoneType.Battlefield);
        twoDrop.Zone.Should().Be(ZoneType.Battlefield);
        threeDrop.Zone.Should().Be(ZoneType.Battlefield);
    }

    // =========================================================================
    // Front face — resolve: mana value > X is dropped (CR 601.2c)
    // =========================================================================

    [Fact]
    public void Resolve_DropsCreature_WithManaValueAboveX()
    {
        var smallGuy = new Creature("Small", "{B}", 1, 1);          // mv 1
        var bigGuy = new Creature("Big", "{4}{B}", 5, 5);           // mv 5
        PutInGraveyard(_alice, smallGuy);
        PutInGraveyard(_alice, bigGuy);

        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(2, smallGuy, bigGuy));
        foreach (var e in effects) e.Execute();

        smallGuy.Zone.Should().Be(ZoneType.Battlefield, "mana value 1 ≤ X=2");
        bigGuy.Zone.Should().Be(ZoneType.Graveyard,
            "mana value 5 > X=2 → not returned (CR 601.2c)");
    }

    // =========================================================================
    // Front face — resolve: at most one per distinct mana value (CR 601.2c)
    // =========================================================================

    [Fact]
    public void Resolve_KeepsAtMostOneCard_PerDistinctManaValue()
    {
        // Two creatures share mana value 2 — only the first survives.
        var firstTwo = new Creature("First Two", "{1}{B}", 2, 2);
        var secondTwo = new Creature("Second Two", "{1}{G}", 2, 2);
        PutInGraveyard(_alice, firstTwo);
        PutInGraveyard(_alice, secondTwo);

        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(3, firstTwo, secondTwo));
        foreach (var e in effects) e.Execute();

        firstTwo.Zone.Should().Be(ZoneType.Battlefield,
            "first card offered for mana value 2 wins");
        secondTwo.Zone.Should().Be(ZoneType.Graveyard,
            "each must have a DIFFERENT mana value — duplicate dropped (CR 601.2c)");
    }

    // =========================================================================
    // Front face — resolve: illegal-at-resolution targets dropped (CR 608.2b)
    // =========================================================================

    [Fact]
    public void Resolve_DropsIllegalTargets()
    {
        // A non-creature object resolved → dropped, nothing returned.
        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(
            _alice, resolver: _ => "not-a-creature");

        var effects = def.EffectFactory(MakeChosen(5, _bob));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "illegal targets at resolution → nothing returned (CR 608.2b)");
    }

    [Fact]
    public void Resolve_DropsCreatureNotInCasterGraveyard()
    {
        // Creature owned by the opponent (in opponent's graveyard) — "your
        // graveyard" only, so it is not returned.
        var opponentCreature = new Creature("Opp Bear", "{1}{G}", 2, 2);
        PutInGraveyard(_bob, opponentCreature);

        var def = AgadeemsAwakeningFactory.BuildSpellDefinition(_alice, o => o!);
        var effects = def.EffectFactory(MakeChosen(3, opponentCreature));
        foreach (var e in effects) e.Execute();

        opponentCreature.Zone.Should().Be(ZoneType.Graveyard,
            "only the caster's own graveyard creatures are returned (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void AgadeemUndercrypt_Identity_Land()
    {
        var land = AgadeemTheUndercryptFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Agadeem, the Undercrypt");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Agadeem, the Undercrypt is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AgadeemTheUndercrypt()
    {
        var card = NamedCardFactory.Create("Agadeem, the Undercrypt", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Agadeem, the Undercrypt");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // =========================================================================
    // Back face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void AgadeemUndercrypt_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = AgadeemTheUndercryptFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Agadeem, the Undercrypt is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Agadeem's Awakening");
        land.MdfcState!.BackFaceName.Should().Be("Agadeem, the Undercrypt");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Agadeem, the Undercrypt");
    }

    // =========================================================================
    // Back face — {T}: Add {B}
    // =========================================================================

    [Fact]
    public void AgadeemUndercrypt_HasSingleManaAbility_AddingBlack()
    {
        var land = AgadeemTheUndercryptFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void AgadeemUndercrypt_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = AgadeemTheUndercryptFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void AgadeemUndercrypt_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = AgadeemTheUndercryptFactory.Create(_alice, replacements: bus);

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
    public void AgadeemUndercrypt_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = AgadeemTheUndercryptFactory.Create(_alice, replacements: bus);

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
    public void AgadeemUndercrypt_EntersTapped_WhenLifeBelowThree()
    {
        // CR 119.4 — can't pay life you don't have.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if prompted, ScriptedAgent would throw.
        AgentRegistry.Set(_alice, agent);

        var land = AgadeemTheUndercryptFactory.Create(_alice, replacements: bus);

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
    public void AgadeemUndercrypt_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        // No AgentRegistry.Set — no agent at all.

        var land = AgadeemTheUndercryptFactory.Create(_alice, replacements: bus);

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
