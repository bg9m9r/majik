using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Cling to Dust (Theros Beyond Death, {B}, Instant).
///
/// "Choose one —
///   • Exile target card from a graveyard. You gain life equal to its mana value.
///   • Exile target card from a graveyard. Draw a card and you lose 1 life."
///
/// Covers:
///   - Card identity + NamedCardFactory dispatch.
///   - SpellDefinition shape (2 modes, 2 per-mode TargetRequests).
///   - Mode 0 (exile + gain mv life) exiles the chosen card and gains
///     life equal to its mana value.
///   - Mode 1 (exile + draw 1 + lose 1) exiles, draws a card, and
///     subtracts 1 from the caster's life total.
///   - Empty library on mode 1 → draw skipped, life loss still applies.
///   - Illegal target at resolution (card no longer in graveyard) → no-op.
/// </summary>
public class ClingToDustTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ClingToDustTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void ClingToDust_Identity_Instant_B()
    {
        var ctd = ClingToDustFactory.Create(_alice);

        ctd.Name.Should().Be("Cling to Dust");
        ctd.ManaCost.Should().Be("{B}");
        ctd.HasType(CardType.Instant).Should().BeTrue();
        ctd.Owner.Should().BeSameAs(_alice);
        ctd.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ClingToDust()
    {
        var card = NamedCardFactory.Create("Cling to Dust", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Cling to Dust");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_ExposesTwoModes_WithPerModeTargets()
    {
        var def = ClingToDustFactory.BuildSpellDefinition(_alice, t => t);

        def.Modes.Should().HaveCount(2);
        def.Modes[ClingToDustFactory.ModeExileGainLife].Should().Contain("gain life");
        def.Modes[ClingToDustFactory.ModeExileDrawLose].Should().Contain("Draw a card");

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[1].MinTargets.Should().Be(0);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Mode0_ExileGainLife_ExilesCard_AndGainsManaValueLife()
    {
        // Bob's graveyard has a Grizzly Bears ({1}{G}, mv 2). Alice exiles
        // it and gains 2 life via mode 0.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        var def = ClingToDustFactory.BuildSpellDefinition(_alice, t => t);
        var aliceLifeBefore = _alice.LifeTotal;

        var chosen = new ChosenSpellParams(
            ModeIndex: ClingToDustFactory.ModeExileGainLife,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { bears },     // mode 0 target
                Array.Empty<object>(),      // mode 1 unused
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        // Exiled.
        bears.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bears);
        _bob.Zones.Exile.GetCards().Should().Contain(bears);

        // Gained life = bears' mana value (2).
        _alice.LifeTotal.Should().Be(aliceLifeBefore + 2);
    }

    [Fact]
    public void Mode1_ExileDrawLose_ExilesCard_DrawsOne_LosesOneLife()
    {
        // Bob's graveyard has a Lightning Bolt ({R}, mv 1). Alice exiles
        // it via mode 1, draws a card from her library, and loses 1 life.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);

        // Seed Alice's library with one card so the draw resolves cleanly.
        var topCard = new Card("AliceTop", "");
        topCard.SetOwner(_alice);
        topCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(topCard);

        var def = ClingToDustFactory.BuildSpellDefinition(_alice, t => t);
        var aliceLifeBefore = _alice.LifeTotal;
        var aliceHandBefore = _alice.Zones.Hand.GetCards().Count();

        var chosen = new ChosenSpellParams(
            ModeIndex: ClingToDustFactory.ModeExileDrawLose,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),      // mode 0 unused
                new object[] { bolt },      // mode 1 target
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        // Bolt exiled.
        bolt.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bolt);
        _bob.Zones.Exile.GetCards().Should().Contain(bolt);

        // Draw 1 — top card moved to Alice's hand.
        _alice.Zones.Hand.GetCards().Count().Should().Be(aliceHandBefore + 1);
        topCard.Zone.Should().Be(ZoneType.Hand);

        // Lose 1 life.
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1);
    }

    [Fact]
    public void Mode1_EmptyLibrary_DrawSkipped_LifeLossStillApplies()
    {
        // Alice's library is empty; mode 1 should still exile + lose 1 life.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);

        var def = ClingToDustFactory.BuildSpellDefinition(_alice, t => t);
        var aliceLifeBefore = _alice.LifeTotal;

        var chosen = new ChosenSpellParams(
            ModeIndex: ClingToDustFactory.ModeExileDrawLose,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                new object[] { bolt },
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bolt.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.LifeTotal.Should().Be(aliceLifeBefore - 1);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void Mode0_IllegalTargetAtResolution_NoExile_NoLifeGain()
    {
        // Target card is no longer in a graveyard at resolution
        // (CR 608.2b — illegal target → effect does nothing).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetZone(ZoneType.Battlefield); // not in a graveyard
        _bob.Zones.Battlefield.AddCard(bears);

        var def = ClingToDustFactory.BuildSpellDefinition(_alice, t => t);
        var aliceLifeBefore = _alice.LifeTotal;

        var chosen = new ChosenSpellParams(
            ModeIndex: ClingToDustFactory.ModeExileGainLife,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { bears },
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        // Still on the battlefield, life total unchanged.
        bears.Zone.Should().Be(ZoneType.Battlefield);
        _alice.LifeTotal.Should().Be(aliceLifeBefore);
    }
}
