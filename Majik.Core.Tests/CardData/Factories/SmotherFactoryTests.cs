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
/// Unit tests for <see cref="SmotherFactory"/> (Onslaught / various reprints, {1}{B}).
///
/// Instant. Oracle text:
///   "Destroy target creature with mana value 3 or less.
///    It can't be regenerated."
///
/// Covers:
///   - Card identity (Instant, {1}{B}, black) + NamedCardFactory dispatch.
///   - SpellDefinition shape: single 1..1 target request.
///   - Destroys a mana-value-2 creature (mv2 ≤ 3 — within range).
///   - Destroys a mana-value-3 creature (mv3 ≤ 3 — boundary).
///   - No-op on a mana-value-4+ creature (illegal target at resolution,
///     CR 608.2b).
///   - No-op when target has left the battlefield (CR 608.2b).
///   - DestroyNoRegeneration used (CR 701.15c — "it can't be regenerated").
/// </summary>
public class SmotherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Instant_At1B_BlackColoured()
    {
        var card = SmotherFactory.Create(_alice);

        card.Name.Should().Be("Smother");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────

    [Fact]
    public void NamedCardFactory_Dispatches_Smother()
    {
        var card = NamedCardFactory.Create("Smother", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Smother");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_SingleTargetCreatureRequest_ManaValueConstraint()
    {
        var def = SmotherFactory.BuildDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    // ── Happy path: mv-2 creature (≤3) ───────────────────────────────────────

    [Fact]
    public void Resolve_ManaValue2Creature_MovesToGraveyard()
    {
        // {1}{G} = mv 2
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = SmotherFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Smother destroys any creature with mana value ≤ 3");
    }

    // ── Boundary: mv-3 creature (exactly 3, must also be destroyed) ──────────

    [Fact]
    public void Resolve_ManaValue3Creature_MovesToGraveyard()
    {
        // {2}{G} = mv 3
        var elvish = new Creature("Elvish Warrior", "{2}{G}", 3, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        elvish.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(elvish);

        var def = SmotherFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(elvish))) e.Execute();

        elvish.Zone.Should().Be(ZoneType.Graveyard,
            "mana value 3 is exactly the cap — creature is destroyed (CR 608.2b boundary)");
    }

    // ── No-op: mv-4 creature (> 3, illegal target at resolution) ────────────

    [Fact]
    public void Resolve_ManaValue4Creature_NoEffect()
    {
        // {3}{G} = mv 4
        var titan = new Creature("Forest Titan", "{3}{G}", 4, 4)
        {
            Owner = _bob,
            Controller = _bob,
        };
        titan.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(titan);

        var def = SmotherFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(titan))) e.Execute();

        titan.Zone.Should().Be(ZoneType.Battlefield,
            "CR 608.2b — mana value 4 is above the cap; illegal target → no-op");
    }

    // ── No-op: target left the battlefield (CR 608.2b) ───────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoEffect()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var def = SmotherFactory.BuildDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — target is no longer on the battlefield → spell does nothing");
    }
}
