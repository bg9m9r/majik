using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ValakutAwakeningFactory"/> and
/// <see cref="ValakutStoneforgeFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Valakut Awakening // Valakut
/// Stoneforge.
///
/// Front face (Valakut Awakening, {2}{R}):
///   Instant. "Put any number of cards from your hand on the bottom of your
///   library, then draw that many cards plus one."
///
/// Back face (Valakut Stoneforge):
///   Land. "This land enters tapped." "{T}: Add {R}."
///
/// Covers:
/// - Identity for both faces.
/// - <see cref="NamedCardFactory"/> dispatches both printed names.
/// - MDFC face-tracker attachment (front-name + back-name on each face).
/// - Front face — resolve: bottom N chosen cards, draw N+1; the "any
///   number" choice routes through the controller's agent.
/// - Front face — choosing zero cards still draws one (CR 121).
/// - Back face — enters tapped (CR 614.1c) + {T}: Add {R} mana ability.
/// </summary>
[Trait("Color", "R")]
public class ValakutAwakeningFactoryTests : IDisposable
{
    public ValakutAwakeningFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void ValakutAwakening_Identity()
    {
        var alice = new Player("Alice", 20);
        var card = ValakutAwakeningFactory.Create(alice);

        card.Name.Should().Be("Valakut Awakening");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(alice);
        card.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void ValakutAwakening_IsRed()
    {
        var alice = new Player("Alice", 20);
        var card = ValakutAwakeningFactory.Create(alice);

        // Colour derived from the {R} pip on the printed mana cost.
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Red);
    }
    [Fact]
    public void ValakutAwakening_CarriesMdfcState_FrontNameAndBackName()
    {
        var alice = new Player("Alice", 20);
        var card = ValakutAwakeningFactory.Create(alice);

        card.MdfcState.Should().NotBeNull(
            "Valakut Awakening is the front face of an MDFC and must carry a face tracker");
        card.MdfcState!.FrontFaceName.Should().Be("Valakut Awakening");
        card.MdfcState!.BackFaceName.Should().Be("Valakut Stoneforge");
        card.MdfcState!.IsBackFace.Should().BeFalse("front face starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Valakut Awakening");
    }

    // =========================================================================
    // Front face — resolve: bottom N, draw N+1
    // =========================================================================

    [Fact]
    public void ValakutAwakening_Resolve_BottomsChosenCards_ThenDrawsThatManyPlusOne()
    {
        var alice = new Player("Alice", 20);

        // Hand: three cards. Library: four cards (Lib0..Lib3, top→bottom).
        var hand0 = new Instant("Hand0", "{R}") { Owner = alice, Controller = alice };
        var hand1 = new Instant("Hand1", "{R}") { Owner = alice, Controller = alice };
        var hand2 = new Instant("Hand2", "{R}") { Owner = alice, Controller = alice };
        foreach (var c in new[] { hand0, hand1, hand2 })
        {
            c.SetZone(ZoneType.Hand);
            alice.Zones.Hand.AddCard(c);
        }

        var lib = new List<ICard>();
        for (var i = 0; i < 4; i++)
        {
            var c = new Instant($"Lib{i}", "{R}") { Owner = alice, Controller = alice };
            c.SetZone(ZoneType.Library);
            alice.Zones.Library.AddCard(c);
            lib.Add(c);
        }

        // Agent bottoms exactly two of the hand cards (hand0, hand1).
        var agent = new ScriptedAgent();
        agent.QueueCardsToBottom(_ => new ICard[] { hand0, hand1 });
        AgentRegistry.Set(alice, agent);

        foreach (var effect in ValakutAwakeningFactory.BuildResolveEffect(alice)) effect.Execute();

        // Two cards went to the bottom of the library.
        alice.Zones.Hand.GetCards().Should().NotContain(hand0);
        alice.Zones.Hand.GetCards().Should().NotContain(hand1);

        // Drew that many (2) plus one = 3 cards. Drew Lib0, Lib1, Lib2 off
        // the top; hand2 (kept) + the three drawn = 4 cards in hand.
        var handNow = alice.Zones.Hand.GetCards().ToList();
        handNow.Should().Contain(hand2, "the kept hand card stays in hand");
        handNow.Should().Contain(lib[0]);
        handNow.Should().Contain(lib[1]);
        handNow.Should().Contain(lib[2]);
        handNow.Should().HaveCount(4, "1 kept + 3 drawn (2 bottomed + 1)");

        // The two bottomed cards are now at the bottom of the library,
        // after Lib3 (the only card not drawn).
        var libNow = alice.Zones.Library.GetCards().ToList();
        libNow.Should().ContainInOrder(lib[3], hand0, hand1);
    }

    [Fact]
    public void ValakutAwakening_Resolve_BottomsZeroCards_StillDrawsOne()
    {
        // "Put ANY NUMBER of cards" includes zero. Bottom 0 → draw 0+1 = 1.
        var alice = new Player("Alice", 20);

        var hand0 = new Instant("Hand0", "{R}") { Owner = alice, Controller = alice };
        hand0.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(hand0);

        var lib0 = new Instant("Lib0", "{R}") { Owner = alice, Controller = alice };
        lib0.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib0);

        var agent = new ScriptedAgent();
        agent.QueueCardsToBottom(_ => Array.Empty<ICard>());
        AgentRegistry.Set(alice, agent);

        foreach (var effect in ValakutAwakeningFactory.BuildResolveEffect(alice)) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(hand0, "no hand cards bottomed");
        alice.Zones.Hand.GetCards().Should().Contain(lib0, "drew the single top card (0 + 1)");
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ValakutAwakening_Resolve_NoAgent_BottomsNothing_DrawsOne()
    {
        // No agent registered → default to bottoming nothing; still draw 1.
        var alice = new Player("Alice", 20);

        var hand0 = new Instant("Hand0", "{R}") { Owner = alice, Controller = alice };
        hand0.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(hand0);

        var lib0 = new Instant("Lib0", "{R}") { Owner = alice, Controller = alice };
        lib0.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(lib0);

        foreach (var effect in ValakutAwakeningFactory.BuildResolveEffect(alice)) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(hand0, "no agent → bottom nothing");
        alice.Zones.Hand.GetCards().Should().Contain(lib0, "still draw one (0 + 1)");
    }

    [Fact]
    public void ValakutAwakening_SpellDefinition_HasNoTargetsNoX()
    {
        var alice = new Player("Alice", 20);
        var def = ValakutAwakeningFactory.BuildSpellDefinition(alice);

        def.TargetRequests.Should().BeEmpty("the effect resolves entirely on the caster");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void ValakutStoneforge_Identity()
    {
        var alice = new Player("Alice", 20);
        var land = ValakutStoneforgeFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Valakut Stoneforge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Valakut Stoneforge is non-Basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }
    [Fact]
    public void ValakutStoneforge_CarriesMdfcState_PreFlippedToBackFace()
    {
        var alice = new Player("Alice", 20);
        var land = ValakutStoneforgeFactory.Create(alice);

        land.MdfcState.Should().NotBeNull(
            "Valakut Stoneforge is the back face of an MDFC and must carry a face tracker");
        land.MdfcState!.FrontFaceName.Should().Be("Valakut Awakening");
        land.MdfcState!.BackFaceName.Should().Be("Valakut Stoneforge");
        land.MdfcState!.IsBackFace.Should().BeTrue("back-face card is constructed pre-flipped");
        land.MdfcState!.ActiveFaceName.Should().Be("Valakut Stoneforge");
    }

    // =========================================================================
    // Back face — {T}: Add {R}
    // =========================================================================

    [Fact]
    public void ValakutStoneforge_HasSingleManaAbility_AddingRed()
    {
        var alice = new Player("Alice", 20);
        var land = ValakutStoneforgeFactory.Create(alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {R} ability");

        var expected = ManaCost.Parse("R");
        manaAbilities[0].ManaGenerated.Red.Should().Be(expected.Red);
        manaAbilities[0].ManaGenerated.Red.Should().BeGreaterThan(0, "produces red mana");
    }

    [Fact]
    public void ValakutStoneforge_HasNoActivatedOrTriggeredAbilities_BeyondMana()
    {
        var alice = new Player("Alice", 20);
        var land = ValakutStoneforgeFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Valakut Stoneforge has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("enters-tapped is a replacement, not a triggered ability (CR 614.1c)");
    }

    // =========================================================================
    // Back face — enters tapped (CR 614.1c)
    // =========================================================================

    [Fact]
    public void ValakutStoneforge_EntersTapped()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);

        var land = ValakutStoneforgeFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("Valakut Stoneforge unconditionally enters tapped");
    }
}
