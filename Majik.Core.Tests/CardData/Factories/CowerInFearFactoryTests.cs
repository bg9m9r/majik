using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cower in Fear (Innistrad, {1}{B}{B}, Instant).
/// Oracle text:
///   "Creatures your opponents control get -1/-1 until end of turn."
///
/// Covers:
///   - Card identity (Instant, {1}{B}{B}, owner/controller).
///   - NamedCardFactory dispatch.
///   - BuildDefinition has no target requests (untargeted mass effect).
///   - On resolve: each opponent-controlled battlefield creature gets a
///     PumpUntilEndOfTurnEffect(-1,-1) via ActiveEffects (CR 514.2).
///   - Caster's OWN creatures are NOT affected.
///   - Creatures without ActiveEffects wired (shape-only) → no-op, no throw.
///   - No creatures on opponents' battlefields → clean no-op.
/// </summary>
public class CowerInFearFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CowerInFear_Identity_InstantAt1BB()
    {
        var card = CowerInFearFactory.Create(_alice);

        card.Name.Should().Be("Cower in Fear");
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CowerInFear()
    {
        var card = NamedCardFactory.Create("Cower in Fear", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Cower in Fear");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildDefinition_NoTargetRequests_NoModes_NoX()
    {
        var def = CowerInFearFactory.BuildDefinition(_alice, new[] { _alice, _bob });

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty("Cower in Fear is untargeted");
    }

    // -----------------------------------------------------------------------
    // Resolve effects
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_OpponentCreature_Gets_MinusOneMinusOne()
    {
        // Bob controls a 2/2; after resolution it should be 1/1.
        var bear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(_alice, new[] { _alice, _bob }, bear);

        bear.Power.Should().Be(1, "Grizzly Bears 2/2 + -1/-1 = 1/1");
        bear.Toughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_CastersOwnCreature_IsNotAffected()
    {
        // Alice controls a creature; Cower in Fear should NOT affect it.
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        // Bob has a creature that should be affected.
        var bobBear = NewCreatureOnBattlefield(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);

        Resolve(_alice, new[] { _alice, _bob }, aliceBear, bobBear);

        aliceBear.Power.Should().Be(2, "caster's own creature is unaffected");
        aliceBear.Toughness.Should().Be(2);
        bobBear.Power.Should().Be(1, "opponent's creature gets -1/-1");
        bobBear.Toughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_MultipleOpponents_AllCreaturesAffected()
    {
        // Alice casts; Bob and Carol are both opponents.
        var bobBear  = NewCreatureOnBattlefield(_bob,   "Bear", "{1}{G}", 2, 2);
        var carolBear = NewCreatureOnBattlefield(_carol, "Bear", "{1}{G}", 2, 2);

        Resolve(_alice, new[] { _alice, _bob, _carol }, bobBear, carolBear);

        bobBear.Power.Should().Be(1,   "Bob's creature gets -1/-1");
        bobBear.Toughness.Should().Be(1);
        carolBear.Power.Should().Be(1, "Carol's creature gets -1/-1");
        carolBear.Toughness.Should().Be(1);
    }

    [Fact]
    public void Resolve_NoCreaturesOnOpponentsBattlefields_IsCleanNoOp()
    {
        // No creatures at all — should not throw.
        var def = CowerInFearFactory.BuildDefinition(_alice, new[] { _alice, _bob });
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var act = () =>
        {
            foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Resolve_CreatureWithoutActiveEffects_DoesNotThrow()
    {
        // Shape-only path: creature on battlefield but no ContinuousEffectsService.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // bear.ActiveEffects is null here.
        var act = () => Resolve(_alice, new[] { _alice, _bob }, bear);
        act.Should().NotThrow();

        // Stats unchanged (no-op).
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a creature with an active ContinuousEffectsService wired and
    /// place it on the given player's battlefield.
    /// </summary>
    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    /// <summary>
    /// Build a SpellDefinition for Cower in Fear with the given caster +
    /// player list, then fire the effects, supplying AllPlayers via
    /// ChosenSpellParams so the mass-effect body can identify opponents.
    /// </summary>
    private static void Resolve(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        params Creature[] _ /* unused; creatures are already on battlefields */)
    {
        var def = CowerInFearFactory.BuildDefinition(caster, allPlayers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: allPlayers);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
