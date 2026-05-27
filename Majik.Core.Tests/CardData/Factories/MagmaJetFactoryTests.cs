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
/// Unit tests for <see cref="MagmaJetFactory"/> (Fifth Dawn, {1}{R}).
///
/// Magma Jet — Instant.
/// Oracle text: "Magma Jet deals 2 damage to any target. Scry 2."
///
/// Covers:
/// - Identity ({1}{R} Instant, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 2 damage to a player target (CR 120.3).
/// - Resolve deals 2 damage to a creature target.
/// - Scry 2 runs after damage: default (no agent) sends both cards to
///   the bottom of the caster's library (CR 701.20).
/// - Scry 2 with an agent registered — agent decision honoured.
/// - Scry 2 on an empty library after damage — no-ops cleanly.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class MagmaJetFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void MagmaJet_Identity_InstantAt1R()
    {
        var card = MagmaJetFactory.Create(_alice);

        card.Name.Should().Be("Magma Jet");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MagmaJet()
    {
        var card = NamedCardFactory.Create("Magma Jet", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Magma Jet");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void MagmaJet_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void MagmaJet_Resolve_DealsTwoDamageToPlayer()
    {
        // No library cards — scry 2 will peek an empty list and no-op.
        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(18, "Magma Jet deals 2 damage to any target (CR 120.3)");
    }

    [Fact]
    public void MagmaJet_Resolve_DealsTwoDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(2, "Magma Jet deals 2 damage to target creature");
    }

    // ── Scry 2 ────────────────────────────────────────────────────────────────

    [Fact]
    public void MagmaJet_Resolve_ScryTwo_DefaultSendsBothToBottom()
    {
        // Library: [a, b, c]. Scry sees [a, b]; default sends both to bottom.
        // Final library: [c, a, b]. Damage to Bob is 2.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(18);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c, a, b },
            "scry 2 default sends peeked cards to bottom, remainder stays on top");
    }

    [Fact]
    public void MagmaJet_Resolve_ScryTwo_AgentKeepsBothOnTop()
    {
        // Library: [a, b, c]. Agent keeps [a, b] on top in original order.
        // Final library: [a, b, c].
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c });
        _bob.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void MagmaJet_Resolve_EmptyLibrary_ScryNoOpsCleanly()
    {
        // Caster has an empty library. Damage still resolves; scry peek
        // returns an empty list and short-circuits without throwing (CR 701.20).
        var def = MagmaJetFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        Action act = () => { foreach (var effect in def.EffectFactory(chosen)) effect.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(18);
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
