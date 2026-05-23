using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AngerOfTheGodsFactory"/>.
///
/// Card: Anger of the Gods — Sorcery {2}{R} (Theros).
///   "Anger of the Gods deals 3 damage to each creature. If a creature
///    dealt damage this way would die this turn, exile it instead."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve dishes 3 damage to every creature on both battlefields.
///   - Replacement registered on the ReplacementBus rewrites tagged
///     creatures' battlefield→graveyard moves to exile.
///   - Replacement is scoped to "dealt damage this way" — untouched
///     creatures dying for unrelated reasons still go to the graveyard.
///   - Replacement only intercepts battlefield→graveyard; other zone
///     moves are unaffected.
///   - Replacement is EOT-expirable — the cleanup sweep drops it.
///   - Single-arg BuildResolveEffect (replacements null) still applies
///     the sweep cleanly.
/// </summary>
public class AngerOfTheGodsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AngerOfTheGods_Identity()
    {
        var c = AngerOfTheGodsFactory.Create(_alice);

        c.Name.Should().Be("Anger of the Gods");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AngerOfTheGods()
    {
        var card = NamedCardFactory.Create("Anger of the Gods", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Anger of the Gods");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsThreeDamage_ToEveryCreature_AcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var aliceGiant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var bobBig = NewCreatureOnBattlefield(_bob, "Wall of Doubt", "{2}{U}", 0, 5);

        var effects = AngerOfTheGodsFactory.BuildResolveEffect(
            new[] { _alice, _bob }, replacements: null);
        foreach (var e in effects) e.Execute();

        aliceBear.Damage.Should().Be(3);
        aliceGiant.Damage.Should().Be(3);
        bobBig.Damage.Should().Be(3, "opponent creatures are also damaged");

        aliceBear.IsDead().Should().BeTrue("3 damage on a 2/2 is lethal");
        aliceGiant.IsDead().Should().BeTrue("3 damage on a 3/3 is lethal");
        bobBig.IsDead().Should().BeFalse("3 damage on a 0/5 is survivable");
    }

    [Fact]
    public void Resolve_WithoutReplacements_SkipsRiderQuietly()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var act = () =>
        {
            var effects = AngerOfTheGodsFactory.BuildResolveEffect(
                new[] { _alice, _bob }, replacements: null);
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
        bear.Damage.Should().Be(3, "sweep still applies without a bus");
    }

    // -----------------------------------------------------------------------
    // Exile rider
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RegistersReplacement_RewritingTaggedCreatureDeathsToExile()
    {
        var bus = new ReplacementBus();

        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = AngerOfTheGodsFactory.BuildResolveEffect(
            new[] { _alice, _bob }, replacements: bus);
        foreach (var e in effects) e.Execute();

        // Bear was caught by the sweep. Its subsequent battlefield→graveyard
        // move (e.g. SBA-driven death from the sweep's damage) is rewritten
        // to battlefield→exile by the registered replacement.
        var dyingIntent = new ZoneMoveIntent(
            Card: bear,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var result = bus.Apply(dyingIntent);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "tagged creatures dying go to exile instead of graveyard");
    }

    [Fact]
    public void Replacement_DoesNotApplyToUntaggedCreatures()
    {
        var bus = new ReplacementBus();

        // Anger fires when only Alice's bear is on the battlefield — Bob's
        // bear enters AFTER resolution, so it isn't in the "damaged this
        // way" set.
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = AngerOfTheGodsFactory.BuildResolveEffect(
            new[] { _alice, _bob }, replacements: bus);
        foreach (var e in effects) e.Execute();

        var laterBear = NewCreatureOnBattlefield(_bob, "Late Bear", "{1}{G}", 2, 2);

        var dyingIntent = new ZoneMoveIntent(
            Card: laterBear,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _bob);

        var result = bus.Apply(dyingIntent);
        result!.ToZone.Should().Be(ZoneType.Graveyard,
            "the rider is scoped to creatures dealt damage by this Anger — " +
            "creatures that weren't on the battlefield during the sweep go to graveyard normally");

        // Sanity — Alice's bear (tagged) still routes to exile.
        var aliceDying = new ZoneMoveIntent(
            Card: aliceBear,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);
        bus.Apply(aliceDying)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Replacement_OnlyRewrites_BattlefieldToGraveyardMoves()
    {
        var bus = new ReplacementBus();

        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = AngerOfTheGodsFactory.BuildResolveEffect(
            new[] { _alice, _bob }, replacements: bus);
        foreach (var e in effects) e.Execute();

        // Bounce — battlefield→hand on the tagged creature: rider should
        // NOT trigger (destination is not graveyard).
        var bounceIntent = new ZoneMoveIntent(
            Card: bear,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Hand,
            Controller: _alice);
        bus.Apply(bounceIntent)!.ToZone.Should().Be(ZoneType.Hand,
            "the rider only intercepts moves to the graveyard");

        // Library→graveyard (mill) of the same card: rider should NOT
        // trigger because the source is not the battlefield. The card was
        // damaged ON the battlefield; mills from elsewhere are unrelated.
        var millIntent = new ZoneMoveIntent(
            Card: bear,
            FromZone: ZoneType.Library,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);
        bus.Apply(millIntent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "the rider only catches battlefield→graveyard moves (creature death)");
    }

    // -----------------------------------------------------------------------
    // EOT cleanup
    // -----------------------------------------------------------------------

    [Fact]
    public void EndOfTurn_Cleanup_RemovesReplacement()
    {
        var bus = new ReplacementBus();

        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = AngerOfTheGodsFactory.BuildResolveEffect(
            new[] { _alice, _bob }, replacements: bus);
        foreach (var e in effects) e.Execute();

        // Before cleanup — tagged bear's death is exiled.
        bus.Apply(new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Exile);

        // Cleanup sweep — CR 514.2.
        bus.ExpireEndOfTurn();

        // After cleanup — same intent now goes to graveyard normally.
        bus.Apply(new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Graveyard,
                "the EOT sweep dropped the IEndOfTurnExpirable replacement");
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
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
