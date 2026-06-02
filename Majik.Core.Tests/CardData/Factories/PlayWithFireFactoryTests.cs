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
/// Unit tests for <see cref="PlayWithFireFactory"/> (Midnight Hunt, {R}).
///
/// Play with Fire — Instant.
/// Oracle text (verified against Scryfall):
///   "Play with Fire deals 2 damage to any target. If a player is dealt
///    damage this way, scry 1. (Look at the top card of your library. You
///    may put that card on the bottom.)"
///
/// Covers:
/// - Identity ({R} Instant, name, owner/controller) loaded from the embedded
///   JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 2 damage to a player target (CR 120.3).
/// - Resolve deals 2 damage to a creature target.
/// - Scry 1 runs ONLY when a player was dealt damage:
///     * Player target → default (no agent) sends the peeked card to the
///       bottom of the caster's library (CR 701.20).
///     * Player target with an agent registered → agent decision honoured.
///     * Player target on an empty library → no-ops cleanly.
///     * Creature target → NO scry (no player was dealt damage this way).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "R")]
public class PlayWithFireFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void PlayWithFire_Identity_InstantAtR()
    {
        var card = PlayWithFireFactory.Create(_alice);

        card.Name.Should().Be("Play with Fire");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void PlayWithFire_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void PlayWithFire_Resolve_DealsTwoDamageToPlayer()
    {
        // No library cards — scry 1 will peek an empty list and no-op.
        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(18, "Play with Fire deals 2 damage to any target (CR 120.3)");
    }

    [Fact]
    public void PlayWithFire_Resolve_DealsTwoDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(2, "Play with Fire deals 2 damage to target creature");
    }

    // ── Conditional scry 1 ────────────────────────────────────────────────────

    [Fact]
    public void PlayWithFire_PlayerTarget_ScryOne_DefaultSendsCardToBottom()
    {
        // Library: [a, b, c]. Scry sees [a]; default sends it to bottom.
        // Final library: [b, c, a]. Damage to Bob is 2.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");
        var c = SeedLibraryCard(_alice, "C");

        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(18);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a },
            "scry 1 default sends the peeked card to the bottom, remainder stays on top");
    }

    [Fact]
    public void PlayWithFire_PlayerTarget_ScryOne_AgentKeepsCardOnTop()
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

        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
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
    public void PlayWithFire_PlayerTarget_EmptyLibrary_ScryNoOpsCleanly()
    {
        // Caster has an empty library. Damage still resolves; scry peek
        // returns empty and short-circuits without throwing (CR 701.20).
        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
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

    [Fact]
    public void PlayWithFire_CreatureTarget_NoScry()
    {
        // Creature target → no player was dealt damage this way, so the
        // caster does NOT scry (CR 608.2 conditional). Library is untouched.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");

        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = PlayWithFireFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(2);
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b },
            "no player was dealt damage, so Play with Fire does not scry");
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
