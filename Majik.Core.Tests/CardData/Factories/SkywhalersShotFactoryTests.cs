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
/// Unit tests for <see cref="SkywhalersShotFactory"/> (Kaladesh, {2}{W}).
///
/// Skywhaler's Shot — Instant.
/// Oracle text: "Destroy target creature with power 3 or greater. Scry 1."
///
/// Covers:
/// - Identity ({2}{W} white Instant, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 target request, power ≥ 3 filter, no X.
/// - Destroys a creature with power exactly 3 → graveyard (CR 701.7).
/// - Destroys a creature with power 5 → graveyard (power ≥ 3).
/// - No-op destroy on a creature with power 2 (power &lt; 3, CR 608.2b illegal-target filter).
/// - Scry 1 fires after the destroy effect: default (no agent) sends top card to bottom.
/// - Scry 1 on an empty library — short-circuits cleanly; no throw.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "W")]
public class SkywhalersShotFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void SkywhalersShot_Identity_InstantAt2W()
    {
        var card = SkywhalersShotFactory.Create(_alice);

        card.Name.Should().Be("Skywhaler's Shot");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{W}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SkywhalersShot_IsWhite()
    {
        var card = SkywhalersShotFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Skywhaler's Shot has {W} in its mana cost");
    }
    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleTargetRequest_PowerThreeOrGreater_NoX()
    {
        var def = SkywhalersShotFactory.BuildDefinition(_alice, targetResolver: o => o);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature with power 3 or greater");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Destroy effect ────────────────────────────────────────────────────────

    [Fact]
    public void SkywhalersShot_Destroys_PowerThreeCreature()
    {
        // Power exactly 3 — legal target (CR 701.7).
        var creature = NewControlledCreature(_bob, "Constructed Constable", "{2}{W}", power: 3, toughness: 3);

        ResolveRaw(creature, _alice);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Skywhaler's Shot destroys target creature with power 3 or greater (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    [Fact]
    public void SkywhalersShot_Destroys_PowerFiveCreature()
    {
        // Power 5 ≥ 3 — legal target.
        var creature = NewControlledCreature(_bob, "Aetherwind Basker", "{4}{G}{G}{G}", power: 5, toughness: 5);

        ResolveRaw(creature, _alice);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "Skywhaler's Shot destroys target creature with power 5 (≥ 3) (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    [Fact]
    public void SkywhalersShot_PowerTwo_NotDestroyed()
    {
        // Power 2 < 3 — illegal target at resolution (CR 608.2b). Effect does nothing.
        var creature = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", power: 2, toughness: 2);

        ResolveRaw(creature, _alice);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Skywhaler's Shot cannot destroy a creature with power less than 3 (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(creature);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(creature);
    }

    // ── Scry 1 ────────────────────────────────────────────────────────────────

    [Fact]
    public void SkywhalersShot_Resolve_ScryOne_DefaultSendsTopToBottom()
    {
        // Alice's library: [top, next]. No agent → default sends top to bottom.
        // After resolve: library = [next, top].
        var top  = SeedLibraryCard(_alice, "Top");
        var next = SeedLibraryCard(_alice, "Next");

        var creature = NewControlledCreature(_bob, "Thriving Rhino", "{3}{G}", power: 3, toughness: 3);
        ResolveRaw(creature, _alice);

        creature.Zone.Should().Be(ZoneType.Graveyard, "target destroyed first");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { next, top },
            "default scry 1 sends the peeked card to the bottom of the library");
    }

    [Fact]
    public void SkywhalersShot_Resolve_EmptyLibrary_ScryNoOp_NoThrow()
    {
        // Alice has no library cards. Destroy still resolves; scry short-circuits.
        var creature = NewControlledCreature(_bob, "Thriving Rhino", "{3}{G}", power: 3, toughness: 3);

        Action act = () => ResolveRaw(creature, _alice);

        act.Should().NotThrow("scry on empty library must not throw");
        creature.Zone.Should().Be(ZoneType.Graveyard, "destroy still resolves (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveRaw(object targetToken, Player caster)
    {
        var def = SkywhalersShotFactory.BuildDefinition(caster, targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana:      ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost,
        int power = 1, int toughness = 1)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
