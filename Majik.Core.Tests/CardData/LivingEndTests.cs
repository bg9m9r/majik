using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Living End (Time Spiral, {2}{B}{B}{B}, Sorcery — "Cascade.
/// Each player exiles all creature cards from their graveyard, then
/// sacrifices all creatures they control, then puts all cards they
/// exiled this way onto the battlefield.").
///
/// Covers:
///   - Card identity (Sorcery, {2}{B}{B}{B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Both players reanimate their own graveyard's creatures while
///     their existing battlefield creatures are simultaneously sac'd.
///   - ETB-draw trigger on a reanimated creature fires (PR #165 / #174
///     plumbing — ZoneService publishes CardMovedEvent for each move).
///   - Empty graveyard on one side: only sac happens for that player.
///   - No creatures anywhere: no-op (no exceptions, no state change).
///
/// Cascade (CR 702.85) is intentionally NOT exercised here — that
/// keyword/trigger is not in this PR.
/// </summary>
public class LivingEndTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public LivingEndTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(eventBus: _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LivingEnd_IsSorcery_AtCost2BBB()
    {
        var le = LivingEndFactory.Create(_alice);

        le.Name.Should().Be("Living End");
        le.ManaCost.Should().Be("{2}{B}{B}{B}");
        le.HasType(CardType.Sorcery).Should().BeTrue();
        le.Owner.Should().BeSameAs(_alice);
        le.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LivingEnd()
    {
        var card = NamedCardFactory.Create("Living End", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Living End");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve behaviour
    // -----------------------------------------------------------------------

    private static Creature GraveyardCreature(string name, Player owner)
    {
        var c = new Creature(name, "2", 2, 2) { Owner = owner, Zone = ZoneType.Graveyard };
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static Creature BattlefieldCreature(string name, Player controller)
    {
        var c = new Creature(name, "2", 2, 2) { Owner = controller, Zone = ZoneType.Battlefield };
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static ChosenSpellParams Chosen(params Player[] all) =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty,
            AllPlayers: all);

    [Fact]
    public void LivingEnd_BothPlayers_ReanimateGraveyardAndSacBattlefield()
    {
        // Alice: 2 creature cards in graveyard, 1 creature on battlefield.
        var aliceYardA = GraveyardCreature("Alice-Yard-A", _alice);
        var aliceYardB = GraveyardCreature("Alice-Yard-B", _alice);
        var aliceBoardOld = BattlefieldCreature("Alice-Board-Old", _alice);

        // Bob: 1 creature card in graveyard, 2 on battlefield.
        var bobYard = GraveyardCreature("Bob-Yard", _bob);
        var bobBoardOldA = BattlefieldCreature("Bob-Board-Old-A", _bob);
        var bobBoardOldB = BattlefieldCreature("Bob-Board-Old-B", _bob);

        var def = LivingEndFactory.BuildSpellDefinition(_zones);
        foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();

        // Step 1 + 3: graveyard creatures now on each owner's battlefield,
        // controlled by their original owner.
        aliceYardA.Zone.Should().Be(ZoneType.Battlefield);
        aliceYardB.Zone.Should().Be(ZoneType.Battlefield);
        aliceYardA.Controller.Should().BeSameAs(_alice);
        aliceYardB.Controller.Should().BeSameAs(_alice);
        bobYard.Zone.Should().Be(ZoneType.Battlefield);
        bobYard.Controller.Should().BeSameAs(_bob);

        // Step 2: previously-on-battlefield creatures sac'd → graveyard.
        aliceBoardOld.Zone.Should().Be(ZoneType.Graveyard);
        bobBoardOldA.Zone.Should().Be(ZoneType.Graveyard);
        bobBoardOldB.Zone.Should().Be(ZoneType.Graveyard);

        // Sanity: zone collections match.
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { aliceYardA, aliceYardB });
        _alice.Zones.Battlefield.GetCards().Should().NotContain(aliceBoardOld);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceBoardOld);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobYard);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(new ICard[] { bobBoardOldA, bobBoardOldB });
        _bob.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bobBoardOldA, bobBoardOldB });
    }

    [Fact]
    public void LivingEnd_ReanimatedCreatureETBTrigger_Fires()
    {
        // Pre-stock Alice's library so the ETB-draw trigger has something
        // to pull. Library contents are irrelevant — only count matters.
        for (var i = 0; i < 3; i++)
        {
            var stock = new Creature($"Stock-{i}", "1", 1, 1) { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(stock);
        }

        // Alice has an ETB-draw bear in the graveyard. When Living End
        // reanimates it, the trigger must fire (CR 603.6a).
        var etbBear = new Creature("ETB Drawer", "2B", 2, 2)
        {
            Owner = _alice,
            Zone = ZoneType.Graveyard,
        };
        var ability = new TriggeredAbility(
            source: etbBear,
            controller: _alice,
            condition: Triggers.OnEnterBattlefieldSelf(etbBear),
            effects: new IEffect[]
            {
                new Effect("etb-draw", () =>
                {
                    var top = _alice.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) return;
                    _alice.Zones.Library.RemoveCard(top);
                    _alice.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }),
            });
        etbBear.AddAbility(ability);
        _alice.Zones.Graveyard.AddCard(etbBear);
        _triggers.BindCard(etbBear);

        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var handBefore = _alice.Zones.Hand.Count;

        var def = LivingEndFactory.BuildSpellDefinition(_zones);
        foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();

        // The bear is on the battlefield, controlled by Alice.
        etbBear.Zone.Should().Be(ZoneType.Battlefield);
        etbBear.Controller.Should().BeSameAs(_alice);

        // Two moves were published for the bear (graveyard→exile, exile→battlefield).
        movedEvents
            .Where(e => ReferenceEquals(e.Card, etbBear))
            .Select(e => (e.FromZone, e.ToZone))
            .Should().Equal(
                (ZoneType.Graveyard, ZoneType.Exile),
                (ZoneType.Exile, ZoneType.Battlefield));

        // Exactly one ETB trigger queued.
        _triggers.PendingCount.Should().Be(1);

        // Resolve the trigger → Alice draws exactly one card.
        _triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        _stack.Count.Should().Be(1);
        var resolved = (TriggeredAbility)_stack.Pop()!;
        resolved.Resolve();

        _alice.Zones.Hand.Count.Should().Be(handBefore + 1);
    }

    [Fact]
    public void LivingEnd_OneSideEmptyGraveyard_OnlySacOccursOnThatSide()
    {
        // Alice has a graveyard + creature pair. Bob has only a battlefield
        // creature — empty graveyard. Expect: Alice reanimates and sacs,
        // Bob just sacs.
        var aliceYard = GraveyardCreature("Alice-Yard", _alice);
        var aliceBoardOld = BattlefieldCreature("Alice-Board-Old", _alice);
        var bobBoardOld = BattlefieldCreature("Bob-Board-Old", _bob);

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();

        var def = LivingEndFactory.BuildSpellDefinition(_zones);
        foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();

        aliceYard.Zone.Should().Be(ZoneType.Battlefield);
        aliceYard.Controller.Should().BeSameAs(_alice);
        aliceBoardOld.Zone.Should().Be(ZoneType.Graveyard);
        bobBoardOld.Zone.Should().Be(ZoneType.Graveyard);

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        // Bob's graveyard now contains his sac'd creature (and nothing else).
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bobBoardOld);
    }

    [Fact]
    public void LivingEnd_NoCreaturesAnywhere_IsNoOp()
    {
        // Neither player has anything in graveyard or battlefield.
        var def = LivingEndFactory.BuildSpellDefinition(_zones);

        var resolve = () =>
        {
            foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();
        };

        resolve.Should().NotThrow();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
        _triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void LivingEnd_NoncreatureCardsInGraveyard_AreNotExiledOrReanimated()
    {
        // Bystander check: only creature cards are exiled by step 1. A
        // noncreature card in the graveyard must stay put.
        var aliceYardCreature = GraveyardCreature("Alice-Yard-Creature", _alice);
        var aliceYardSpell = new Sorcery("Random Sorcery", "{B}")
        {
            Owner = _alice,
            Zone = ZoneType.Graveyard,
        };
        _alice.Zones.Graveyard.AddCard(aliceYardSpell);

        var def = LivingEndFactory.BuildSpellDefinition(_zones);
        foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();

        aliceYardCreature.Zone.Should().Be(ZoneType.Battlefield);
        aliceYardCreature.Controller.Should().BeSameAs(_alice);

        // Noncreature stays in the graveyard.
        aliceYardSpell.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceYardSpell);
        _alice.Zones.Exile.GetCards().Should().NotContain(aliceYardSpell);
    }

    [Fact]
    public void LivingEnd_WithoutZoneService_StillMovesCards_ButPublishesNoEvents()
    {
        // Fallback path: when no ZoneService is supplied, moves still happen
        // (direct zone mutation) but no CardMovedEvent fires. This mirrors
        // the contract of LibrarySpellFactory.ReturnAllFromGraveyardSpell.
        var aliceYard = GraveyardCreature("Alice-Yard", _alice);
        var aliceBoardOld = BattlefieldCreature("Alice-Board-Old", _alice);

        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var def = LivingEndFactory.BuildSpellDefinition(zones: null);
        foreach (var e in def.EffectFactory(Chosen(_alice, _bob))) e.Execute();

        aliceYard.Zone.Should().Be(ZoneType.Battlefield);
        aliceYard.Controller.Should().BeSameAs(_alice);
        aliceBoardOld.Zone.Should().Be(ZoneType.Graveyard);
        movedEvents.Should().BeEmpty();
    }
}
