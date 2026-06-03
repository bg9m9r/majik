using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoltOfKeranosFactory"/> (Theros Beyond Death, {1}{R}{R}).
///
/// Bolt of Keranos — Sorcery.
/// Oracle text (Scryfall-confirmed): "Bolt of Keranos deals 3 damage to any
/// target. Scry 1. (Look at the top card of your library. You may put that
/// card on the bottom.)"
///
/// Same two-part resolve shape as <see cref="MagmaJetFactory"/> (deal damage to
/// any target, then scry), only the values differ: sorcery instead of instant,
/// 3 damage instead of 2, scry 1 instead of 2.
///
/// Covers:
/// - Identity ({1}{R}{R} Sorcery, name, owner/controller).
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 3 damage to a player target (CR 120.3).
/// - Resolve deals 3 damage to a creature target.
/// - Scry 1 runs after damage: default (no agent) sends the peeked card to the
///   bottom of the caster's library (CR 701.20).
/// - Scry 1 with an agent registered — agent decision honoured.
/// - Scry 1 on an empty library after damage — no-ops cleanly.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "R")]
public class BoltOfKeranosFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void BoltOfKeranos_Identity_SorceryAt1RR()
    {
        var card = BoltOfKeranosFactory.Create(_alice);

        card.Name.Should().Be("Bolt of Keranos");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BoltOfKeranos_NamedFactoryDispatch_BuildsSorcery()
    {
        var card = NamedCardFactory.Create("Bolt of Keranos", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bolt of Keranos");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void BoltOfKeranos_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void BoltOfKeranos_Resolve_DealsThreeDamageToPlayer()
    {
        // No library cards — scry 1 will peek an empty list and no-op.
        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(17, "Bolt of Keranos deals 3 damage to any target (CR 120.3)");
    }

    [Fact]
    public void BoltOfKeranos_Resolve_DealsThreeDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(3, "Bolt of Keranos deals 3 damage to target creature");
    }

    // ── Scry 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void BoltOfKeranos_Resolve_ScryOne_DefaultSendsCardToBottom()
    {
        // Library: [a, b, c]. Scry sees [a]; default sends it to bottom.
        // Final library: [b, c, a]. Damage to Bob is 3.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(17);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a },
            "scry 1 default sends the peeked card to the bottom, remainder stays on top");
    }

    [Fact]
    public void BoltOfKeranos_Resolve_ScryOne_AgentKeepsCardOnTop()
    {
        // Library: [a, b, c]. Agent keeps [a] on top. Final library: [a, b, c].
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a }));
        AgentRegistry.Set(_alice, agent);

        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c });
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void BoltOfKeranos_Resolve_EmptyLibrary_ScryNoOpsCleanly()
    {
        // Caster has an empty library. Damage still resolves; scry peek
        // returns an empty list and short-circuits without throwing (CR 701.20).
        var def = BoltOfKeranosFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        Action act = () => { foreach (var effect in def.EffectFactory(chosen)) effect.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(17);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Card SeedLibraryCard(Player player, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(player);
        player.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
