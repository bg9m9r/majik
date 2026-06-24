using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LiveOrDieFactory"/> (Mystery Booster, {3}{B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Choose one —
///     • Return target creature card from your graveyard to the battlefield.
///     • Destroy target creature."
///
/// CR 700.2d — modal "Choose one —" with per-mode targeting. Modal shape
/// mirrors <see cref="RipApartFactory"/>; the reanimate mode mirrors
/// <see cref="HelpingHandFactory"/> (minus the MV cap / enters-tapped rider),
/// the destroy mode mirrors <see cref="RipApartFactory"/>'s destroy clause
/// narrowed to creatures.
/// </summary>
[Trait("Color", "B")]
public class LiveOrDieFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static IReadOnlyList<object>[] Slots(int modeIndex, params object[] targets)
    {
        var slots = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        slots[modeIndex] = targets;
        return slots;
    }

    private ChosenSpellParams Chosen(int modeIndex, params object[] targets) =>
        new(
            ModeIndex: modeIndex,
            X: null,
            Targets: Slots(modeIndex, targets),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private Creature MakeCreature(string name, Player owner, ZoneType zone)
    {
        var c = new Creature(name, "{1}{B}", 2, 2) { Owner = owner, Controller = owner };
        c.SetZone(zone);
        if (zone == ZoneType.Battlefield) owner.Zones.Battlefield.AddCard(c);
        else if (zone == ZoneType.Graveyard) owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + definition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LiveOrDie_Create_HasInstantShape_Black_ManaValueFive()
    {
        var card = LiveOrDieFactory.Create(_alice);

        card.Name.Should().Be("Live or Die");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(5,
            because: "{3}{B}{B} = generic 3 + 2 black = mana value 5 (CR 202.3)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LiveOrDie_BuildDefinition_ExposesModes_AndPerModeTargets()
    {
        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[LiveOrDieFactory.ModeReturn].Should().Contain("graveyard");
        def.Modes[LiveOrDieFactory.ModeDestroy].Should().Contain("Destroy");

        def.TargetRequests.Should().HaveCount(2);
        // Each mode carries its own target, MinTargets=0 so the unchosen mode
        // doesn't gate the cast (CR 700.2d).
        def.TargetRequests[LiveOrDieFactory.ModeReturn].MinTargets.Should().Be(0);
        def.TargetRequests[LiveOrDieFactory.ModeReturn].MaxTargets.Should().Be(1);
        def.TargetRequests[LiveOrDieFactory.ModeReturn].Description.Should().Contain("graveyard");
        def.TargetRequests[LiveOrDieFactory.ModeDestroy].MinTargets.Should().Be(0);
        def.TargetRequests[LiveOrDieFactory.ModeDestroy].MaxTargets.Should().Be(1);
        def.TargetRequests[LiveOrDieFactory.ModeDestroy].Description.Should().Contain("creature");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — return target creature card from your graveyard.
    // -----------------------------------------------------------------------

    [Fact]
    public void LiveOrDie_Mode0_ReturnsCreatureFromYourGraveyardToBattlefield()
    {
        var target = MakeCreature("Grizzly Bears", _alice, ZoneType.Graveyard);

        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(LiveOrDieFactory.ModeReturn, target))) e.Execute();

        target.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 returns the targeted creature card to the battlefield (CR 701.20)");
        target.Controller.Should().BeSameAs(_alice,
            because: "the returned permanent enters under the caster's control (CR 110.2)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(target);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void LiveOrDie_Mode0_ReturnsUntapped()
    {
        // Live or Die — unlike Helping Hand — has no "enters tapped" rider.
        var target = MakeCreature("Grizzly Bears", _alice, ZoneType.Graveyard);

        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(LiveOrDieFactory.ModeReturn, target))) e.Execute();

        target.IsTapped.Should().BeFalse(
            because: "Live or Die returns the creature without the enters-tapped rider");
    }

    [Fact]
    public void LiveOrDie_Mode0_NoOp_OnOpponentGraveyardCreature()
    {
        // "your graveyard" — a creature in the opponent's graveyard is not a
        // legal source (CR 608.2b re-checked at resolution).
        var theirs = MakeCreature("Grizzly Bears", _bob, ZoneType.Graveyard);

        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(LiveOrDieFactory.ModeReturn, theirs))) e.Execute();

        theirs.Zone.Should().Be(ZoneType.Graveyard,
            because: "only the caster's own graveyard is a legal source for mode 0");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(theirs);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void LiveOrDie_Mode1_DestroysTargetCreature()
    {
        var creature = MakeCreature("Grizzly Bears", _bob, ZoneType.Battlefield);

        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(LiveOrDieFactory.ModeDestroy, creature))) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the targeted creature (CR 701.7)");
    }

    [Fact]
    public void LiveOrDie_Mode1_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — a non-creature permanent is not a legal target for
        // mode 1; it is not destroyed.
        var artifact = new Artifact("Mind Stone", "{2}") { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = LiveOrDieFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(LiveOrDieFactory.ModeDestroy, artifact))) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 1 destroys only creatures, not artifacts (CR 608.2b)");
    }
}
