using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NeedleDropFactory"/> (Born of the Gods, {R}).
///
/// Needle Drop — Instant.
/// Oracle text (verified against Scryfall):
///   "Needle Drop deals 1 damage to any target that was dealt damage this
///    turn.
///    Draw a card."
///
/// Covers:
/// - Identity ({R} Instant, name, owner/controller) from the CardDef DSL.
/// - Spell definition shape: single 1..1 "was dealt damage this turn" request.
/// - The targeting RESTRICTION (CR 120.3): only already-damaged objects are
///   offered as candidates; an undamaged creature / player is NOT a candidate.
/// - Resolve deals 1 damage to a damaged creature + the caster draws a card.
/// - Resolve deals 1 damage to a damaged player + the caster draws a card.
/// - CR 608.2c — an illegal (undamaged) target on resolution: no damage, no
///   draw.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "R")]
public class NeedleDropFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose() => AgentRegistry.Clear();

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void NeedleDrop_Identity_InstantAtR()
    {
        var card = NeedleDropFactory.Create(_alice);

        card.Name.Should().Be("Needle Drop");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void NeedleDrop_SpellDefinition_HasSingleDamagedTargetRequest_NoX()
    {
        var def = NeedleDropFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("was dealt damage this turn");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Targeting restriction (CR 120.3) ─────────────────────────────────────

    [Fact]
    public void NeedleDrop_OnlyDamagedTargetsAreCandidates()
    {
        // A damaged creature qualifies; an undamaged creature does NOT.
        var damaged = MakeBear(_bob);
        damaged.TakeDamage(1);
        var healthy = MakeBear(_bob);

        // A player who was dealt damage qualifies; an undamaged player does not.
        _bob.RecordDamageDealt(1);

        var def = NeedleDropFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());
        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(damaged, "it was dealt damage this turn");
        candidates.Should().Contain(_bob, "Bob was dealt damage this turn");
        candidates.Should().NotContain(healthy, "an undamaged creature is not a legal target");
        candidates.Should().NotContain(_alice, "Alice was not dealt damage this turn");
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void NeedleDrop_Resolve_DealsOneDamageToDamagedCreature_AndDraws()
    {
        var bear = MakeBear(_bob, toughness: 4);
        bear.TakeDamage(1); // pre-damaged this turn
        SeedLibraryCard(_alice, "Top");

        var def = NeedleDropFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, bear);

        bear.Damage.Should().Be(2, "Needle Drop deals 1 more damage (1 + 1)");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "the caster draws a card");
    }

    [Fact]
    public void NeedleDrop_Resolve_DealsOneDamageToDamagedPlayer_AndDraws()
    {
        _bob.RecordDamageDealt(1); // Bob was dealt damage this turn
        SeedLibraryCard(_alice, "Top");

        var def = NeedleDropFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, _bob);

        _bob.LifeTotal.Should().Be(19, "Needle Drop deals 1 damage to the player");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "the caster draws a card");
    }

    [Fact]
    public void NeedleDrop_Resolve_IllegalUndamagedTarget_NoDamageNoDraw()
    {
        // CR 608.2c — single illegal target on resolution: the spell doesn't
        // resolve, so no damage AND no draw (the draw is part of the same
        // resolution).
        var healthy = MakeBear(_bob, toughness: 4);
        SeedLibraryCard(_alice, "Top");

        var def = NeedleDropFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, healthy);

        healthy.Damage.Should().Be(0, "an undamaged target is illegal — no damage");
        _alice.Zones.Hand.GetCards().Should().BeEmpty("no resolution → no draw");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new[] { target } },
            Mana:      ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();
    }

    private Creature MakeBear(Player owner, int power = 2, int toughness = 2)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
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
