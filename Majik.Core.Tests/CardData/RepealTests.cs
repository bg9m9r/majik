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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Repeal (Ravnica: City of Guilds, {X}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Return target nonland permanent with mana value X to its owner's hand.
///    Draw a card."
///
/// Shape: the bounce analogue of <see cref="EchoingTruthFactory"/> (return a
/// nonland permanent to its owner's hand, CR 701.10) narrowed to a SINGLE
/// target whose mana value equals the cast X, plus a Peek-style cantrip draw
/// (CR 121.1).
///
/// X / target-legality model — the engine has no X-aware CandidateGatherer
/// (GameContext exposes no chosen X), and CR 601.2 announces targets (601.2c)
/// before X is locked in (601.2f), so the gatherer offers EVERY nonland
/// permanent and the "mana value X" restriction (CR 115.4) is enforced as a
/// resolution-time illegal-target gate (CR 608.2b) — same posture as
/// <see cref="DrownInTheLochFactory"/>'s mv-≤-X gate. If the single target is
/// illegal at resolution (gone, now a land, or mv != X) the whole spell does
/// nothing, INCLUDING the cantrip draw (CR 608.2b — a spell with all targets
/// illegal doesn't resolve), mirroring <see cref="PeekFactory"/>'s fizzle
/// guard.
///
/// Covers:
///   - Card identity (Instant, {X}{U}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — HasVariableX true, single 1..1 "target nonland
///     permanent" request, no modes, BotIntent.Bounce.
///   - Resolve: bounces a creature whose mana value == X, then draws a card.
///   - Resolve: bounces a noncreature permanent (artifact) whose mv == X.
///   - Resolve: mana value != X → illegal target → no bounce AND no draw.
///   - Resolve: a land may NOT be the target (nonland restriction) → no-op.
///   - Resolve: off-battlefield target → no-op (CR 608.2b), no draw.
///   - Resolve: only the single target is bounced (no same-name sweep — unlike
///     Echoing Truth).
/// </summary>
public class RepealTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Repeal_IsInstant_AtCostXU()
    {
        var card = RepealFactory.Create(_alice);

        card.Name.Should().Be("Repeal");
        card.ManaCost.Should().Be("{X}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Repeal()
    {
        var card = NamedCardFactory.Create("Repeal", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Repeal");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{X}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Repeal_Definition_HasVariableX_AndSingleNonlandPermanentTarget()
    {
        var def = RepealFactory.BuildDefinition(_alice, o => o);

        def.HasVariableX.Should().BeTrue("X is chosen as Repeal is cast");
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("nonland permanent");
        tr.Intent.Should().Be(BotIntent.Bounce);
    }

    // -----------------------------------------------------------------------
    // Resolve — bounce target whose mana value == X, then draw
    // -----------------------------------------------------------------------

    [Fact]
    public void Repeal_BouncesCreatureWithManaValueX_ThenDraws()
    {
        // Goblin Guide costs {R} → mana value 1. Cast with X = 1.
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var topOfLibrary = SeedLibrary(_alice, "Lightning Bolt", "{R}");

        Resolve(target: goblin, x: 1);

        goblin.Zone.Should().Be(ZoneType.Hand,
            "Repeal returns the nonland permanent with mana value X to its owner's hand (CR 701.10)");
        _bob.Zones.Hand.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        topOfLibrary.Zone.Should().Be(ZoneType.Hand,
            "Repeal then draws a card for its controller (CR 121.1)");
        _alice.Zones.Hand.GetCards().Should().Contain(topOfLibrary);
    }

    [Fact]
    public void Repeal_BouncesArtifactWithManaValueX_ThenDraws()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");
        var topOfLibrary = SeedLibrary(_alice, "Opt", "{U}");

        Resolve(target: artifact, x: 1);

        artifact.Zone.Should().Be(ZoneType.Hand,
            "any nonland permanent type qualifies, including artifacts");
        _bob.Zones.Hand.GetCards().Should().Contain(artifact);
        topOfLibrary.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets fizzle the WHOLE spell (no bounce, no draw)
    // -----------------------------------------------------------------------

    [Fact]
    public void Repeal_TargetManaValueDoesNotEqualX_DoesNothing_AndDoesNotDraw()
    {
        // Tarmogoyf costs {1}{G} → mana value 2, but we cast with X = 1, so the
        // target's mana value != X: an illegal target (CR 115.4) → the spell
        // does nothing, including the cantrip draw (CR 608.2b).
        var goyf = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");
        var topOfLibrary = SeedLibrary(_alice, "Opt", "{U}");

        Resolve(target: goyf, x: 1);

        goyf.Zone.Should().Be(ZoneType.Battlefield,
            "mana value (2) != X (1) → illegal target, no bounce");
        topOfLibrary.Zone.Should().Be(ZoneType.Library,
            "a spell with all targets illegal does not resolve, so no draw (CR 608.2b)");
        _alice.Zones.Hand.GetCards().Should().NotContain(topOfLibrary);
    }

    [Fact]
    public void Repeal_TargetNotOnBattlefield_DoesNothing_AndDoesNotDraw()
    {
        var creature = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var topOfLibrary = SeedLibrary(_alice, "Opt", "{U}");

        // Target leaves the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(target: creature, x: 1);

        creature.Zone.Should().Be(ZoneType.Graveyard, "CR 608.2b illegal target → no-op");
        topOfLibrary.Zone.Should().Be(ZoneType.Library, "no legal target → spell does not resolve → no draw");
    }

    [Fact]
    public void Repeal_LandTarget_DoesNothing_AndDoesNotDraw()
    {
        // The target must be a NONLAND permanent — a land is illegal regardless
        // of mana value (lands have mana value 0).
        var land = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        var topOfLibrary = SeedLibrary(_alice, "Opt", "{U}");

        Resolve(target: land, x: 0);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "Repeal cannot target a land — illegal target, no-op (CR 608.2b)");
        topOfLibrary.Zone.Should().Be(ZoneType.Library, "no legal target → no draw");
    }

    // -----------------------------------------------------------------------
    // Resolve — single-target only (no same-name sweep, unlike Echoing Truth)
    // -----------------------------------------------------------------------

    [Fact]
    public void Repeal_BouncesOnlyTheSingleTarget_NotSameNameCopies()
    {
        var target = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        var sameNameOther = NewControlledCreature(_bob, "Goblin Guide", "{R}");
        SeedLibrary(_alice, "Opt", "{U}");

        Resolve(target: target, x: 1);

        target.Zone.Should().Be(ZoneType.Hand);
        sameNameOther.Zone.Should().Be(ZoneType.Battlefield,
            "Repeal returns a SINGLE target only — there is no same-name sweep");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(Permanent target, int x)
    {
        var def = RepealFactory.BuildDefinition(caster: _alice, targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: x,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Card SeedLibrary(Player owner, string name, string cost)
    {
        var c = new Instant(name, cost);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var a = new Artifact(name, cost)
        {
            Owner = owner,
            Controller = owner,
        };
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
