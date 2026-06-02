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
/// Unit tests for <see cref="SipOfHemlockFactory"/> (Onslaught, {4}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target creature. Its controller loses 2 life."
///
/// Covers:
/// - Identity ({4}{B}{B} black Sorcery, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - SpellDefinition shape (1 target-creature request).
/// - Happy path: creature destroyed → graveyard AND its controller loses 2 life.
/// - Illegal target (CR 608.2b): creature not on battlefield → no destroy, no life loss.
/// </summary>
[Trait("Color", "B")]
public class SipOfHemlockFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_SorceryAt4BB_BlackColoured()
    {
        var card = SipOfHemlockFactory.Create(_alice);

        card.Name.Should().Be("Sip of Hemlock");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{4}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────
    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_SingleTargetCreatureRequest()
    {
        var def = SipOfHemlockFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TargetCreature_MovesToGraveyard_AndControllerLoses2Life()
    {
        // Bob controls a 2/2 creature; Alice casts Sip of Hemlock targeting it.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner      = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = SipOfHemlockFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Sip of Hemlock destroys the target — it goes to the graveyard");
        _bob.LifeTotal.Should().Be(18,
            "the creature's controller (Bob) loses 2 life");
        _alice.LifeTotal.Should().Be(20,
            "Alice (caster) does NOT lose life — only the creature's controller does");
    }

    // ── Caster is also the controller of the target ───────────────────────────

    [Fact]
    public void Resolve_CasterTargetsOwnCreature_CasterLoses2Life()
    {
        // Alice targets her own creature (unusual but legal).
        var snake = new Creature("Typhoid Rats", "{B}", 1, 1)
        {
            Owner      = _alice,
            Controller = _alice,
        };
        snake.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(snake);

        var def = SipOfHemlockFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(snake))) e.Execute();

        snake.Zone.Should().Be(ZoneType.Graveyard,
            "creature is destroyed");
        _alice.LifeTotal.Should().Be(18,
            "Alice is both caster and the creature's controller — she loses 2 life");
    }

    // ── Illegal target (CR 608.2b) ────────────────────────────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoDestroyAndNoLifeLoss()
    {
        // Bear is already in the graveyard when the spell resolves.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner      = _bob,
            Controller = _bob,
        };
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);

        var def = SipOfHemlockFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "already in graveyard — zone unchanged");
        _bob.LifeTotal.Should().Be(20,
            "CR 608.2b — illegal target → spell does nothing, no life loss");
        _alice.LifeTotal.Should().Be(20,
            "caster also unaffected");
    }
}
