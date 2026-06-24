using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TerrorTideFactory"/>.
///
/// Card: Terror Tide — Sorcery {2}{B}{B} (Modern Horizons 3).
///   "Fathomless descent — All creatures get -X/-X until end of turn,
///    where X is the number of permanent cards in your graveyard."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - X = number of permanent cards in the caster's graveyard (CR 110.4a):
///     lands / creatures / artifacts / enchantments / planeswalkers count;
///     instants + sorceries do NOT.
///   - Resolve applies -X/-X symmetrically across all supplied players
///     (CR 109.5) and locks X in at resolution (CR 608.2).
///   - X = 0 (empty / spell-only graveyard) is a legal no-op.
///   - Plus one identity assert (mana cost / type) per the harness rules.
/// </summary>
[Trait("Color", "B")]
public class TerrorTideTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TerrorTide_Identity()
    {
        var c = TerrorTideFactory.Create(_alice);

        c.Name.Should().Be("Terror Tide");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // X = number of permanent cards in caster's graveyard (CR 110.4a)
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeX_CountsOnlyPermanentCards_InCasterGraveyard()
    {
        // 3 permanent cards (creature, land, enchantment) ...
        _alice.Zones.Graveyard.AddCard(new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        _alice.Zones.Graveyard.AddCard(new Land("Swamp"));
        _alice.Zones.Graveyard.AddCard(new Enchantment("Pacifism", "{1}{W}"));
        // ... plus 2 NON-permanent cards (instant + sorcery) that must NOT count.
        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Sorcery("Divination", "{2}{U}"));
        // A permanent card in the OPPONENT's graveyard must NOT count ("your").
        _bob.Zones.Graveyard.AddCard(new Creature("Hill Giant", "{3}{R}", 3, 3));

        TerrorTideFactory.ComputeX(_alice).Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Resolve — symmetric -X/-X sweep with X from graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AppliesMinusXMinusX_SymmetricallyAcrossBothPlayers()
    {
        // X = 2 (two permanent cards in Alice's graveyard).
        _alice.Zones.Graveyard.AddCard(new Creature("Memnite", "{0}", 1, 1));
        _alice.Zones.Graveyard.AddCard(new Land("Island"));

        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBig = NewCreatureOnBattlefield(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        var effects = TerrorTideFactory.BuildResolveEffect(_alice, new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // -2/-2 hits BOTH battlefields (CR 109.5).
        aliceBear.Toughness.Should().Be(0, "2 - 2 = 0");
        aliceBear.IsDead().Should().BeTrue("toughness 0 is lethal (CR 704.5f)");
        bobBig.Toughness.Should().Be(2, "4 - 2 = 2");
        bobBig.Power.Should().Be(2);
        bobBig.IsDead().Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyGraveyard_IsNoOp_CreaturesUnchanged()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = TerrorTideFactory.BuildResolveEffect(_alice, new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // X = 0 → -0/-0 → unchanged.
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        bear.IsDead().Should().BeFalse();
    }

    [Fact]
    public void Resolve_SpellOnlyGraveyard_IsNoOp()
    {
        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Sorcery("Divination", "{2}{U}"));
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = TerrorTideFactory.BuildResolveEffect(_alice, new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Instants + sorceries are not permanent cards → X = 0 → no-op.
        bear.Toughness.Should().Be(2);
        bear.IsDead().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = new ContinuousEffectsService();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
