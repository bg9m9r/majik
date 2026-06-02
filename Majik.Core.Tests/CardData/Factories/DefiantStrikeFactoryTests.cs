using FluentAssertions;
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
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DefiantStrikeFactory"/> (Fate Reforged, {W}).
///
/// Defiant Strike — Instant.
/// Oracle text (verified against Scryfall):
///   "Target creature gets +1/+0 until end of turn.
///    Draw a card."
///
/// Covers:
/// - Identity ({W} Instant, name, owner/controller) loaded from the embedded
///   JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "target creature" request, no X.
/// - Resolve pumps the target creature +1/+0 (CR 613.1g / CR 514.2).
/// - The +1/+0 expires at end of turn (CR 514.2).
/// - Resolve draws a card for the caster (CR 121.1) — ordered after the pump
///   (CR 608.2e left-to-right), and the draw happens regardless of which
///   creature was targeted.
/// - Empty-library draw flags the caster for the draw-from-empty penalty
///   (CR 704.5b) without throwing.
/// - Illegal target (creature no longer on the battlefield) → the pump
///   no-ops, but the spell still resolves the draw clause (the creature
///   target is still a legal object reference here; CR 608.2b only suppresses
///   the pump body, not the independent draw clause).
/// </summary>
[Trait("Color", "W")]
public class DefiantStrikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefiantStrike_Identity_InstantAtW()
    {
        var card = DefiantStrikeFactory.Create(_alice);

        card.Name.Should().Be("Defiant Strike");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{W}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
    }
    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void DefiantStrike_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    // ── Pump ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DefiantStrike_Resolve_TargetGetsPlusOnePlusZero()
    {
        var bear = BuildBear(_bob);

        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = Chosen(bear);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.GetPower().Should().Be(3, "Defiant Strike grants +1 power (CR 613.1g)");
        bear.GetToughness().Should().Be(2, "Defiant Strike grants +0 toughness");
    }

    [Fact]
    public void DefiantStrike_PumpEffect_ExpiresAtEndOfTurn()
    {
        var bear = BuildBear(_bob);
        var svc = bear.ActiveEffects!;

        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute();

        bear.GetPower().Should().Be(3);

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        bear.GetPower().Should().Be(2);
        bear.GetToughness().Should().Be(2);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DefiantStrike_Resolve_DrawsACardForTheCaster()
    {
        var bear = BuildBear(_bob);
        var top = SeedLibraryCard(_alice, "Top");

        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute();

        // CR 121.1 — "Draw a card." The top library card moves to the caster's hand.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void DefiantStrike_Resolve_EmptyLibrary_FlagsDrawFromEmpty_NoThrow()
    {
        var bear = BuildBear(_bob);
        // Alice's library is empty.

        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Action act = () => { foreach (var effect in def.EffectFactory(Chosen(bear))) effect.Execute(); };

        act.Should().NotThrow();
        // CR 704.5b — a player who tried to draw from an empty library loses.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        bear.GetPower().Should().Be(3, "the pump still applied before the draw");
    }

    // ── Illegal pump target ───────────────────────────────────────────────────

    [Fact]
    public void DefiantStrike_TargetNotOnBattlefield_PumpNoOp_StillDraws()
    {
        // Creature is in the graveyard at resolution (CR 608.2b — the pump
        // body no-ops). The draw clause is independent and still happens.
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(dead);

        var top = SeedLibraryCard(_alice, "Top");

        var def = DefiantStrikeFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var effect in def.EffectFactory(Chosen(dead))) effect.Execute();

        dead.GetPower().Should().Be(2, "the pump no-ops off the battlefield (CR 608.2b)");
        _alice.Zones.Hand.GetCards().Should().Contain(top, "the draw clause is independent");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ChosenSpellParams Chosen(object target) => new(
        ModeIndex: null,
        X:         null,
        Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
        Mana:      ManaPayment.Empty);

    private static Creature BuildBear(Player owner)
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = owner, Controller = owner, ActiveEffects = new ContinuousEffectsService() };
        bear.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(bear);
        return bear;
    }

    private static Card SeedLibraryCard(Player player, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(player);
        player.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
