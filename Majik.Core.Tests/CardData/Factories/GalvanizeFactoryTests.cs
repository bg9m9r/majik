using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GalvanizeFactory"/> (Murders at Karlov Manor,
/// {1}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Galvanize deals 3 damage to target creature. If you've drawn two or more
///    cards this turn, Galvanize deals 5 damage to that creature instead."
///
/// The "3 damage to target creature" core mirrors <see cref="MagmaSprayFactory"/>;
/// the "drawn two or more cards this turn" CR 608.2 conditional reads the live
/// per-player draw tally off the resolution context, mirroring
/// <see cref="SlickSequenceFactory"/>'s spells-cast rider.
///
/// These tests cover only Galvanize's UNIQUE behaviour (the variable damage
/// gated on the cards-drawn-this-turn tally). Dispatch + well-formedness are
/// asserted for every implemented card by CardFactoryContractTests.
/// </summary>
[Trait("Color", "R")]
public class GalvanizeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private ChosenSpellParams Chosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity (exact mana cost; the rest of the shape is covered by the
    // contract test).
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_InstantAt1R_Red()
    {
        var card = GalvanizeFactory.Create(_alice);

        card.Name.Should().Be("Galvanize");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_SingleCreatureTargetRequest()
    {
        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Base mode — 3 damage when the caster has drawn fewer than two cards.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsThreeDamage_WhenFewerThanTwoCardsDrawn()
    {
        // No game context => draw tally reads as 0 (< 2) => base 3 damage.
        var bear = CreatureOnBattlefield(_bob, 2, 5);

        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(3,
            because: "Galvanize deals its base 3 damage when the caster has not drawn 2+ cards this turn");
    }

    // -----------------------------------------------------------------------
    // No-op on a non-creature target (CR 608.2b).
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage dealt.
        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "Galvanize damages only its target creature, never a player");
    }

    // -----------------------------------------------------------------------
    // Boosted mode — 5 damage when the caster has drawn two or more cards this
    // turn (CR 608.2 conditional read live off the resolution context).
    // -----------------------------------------------------------------------

    [Fact]
    public void Threshold_Is_TwoCards()
    {
        // Documents the rider threshold: Galvanize never draws cards itself, so
        // "drawn two or more cards this turn" maps directly to a tally >= 2.
        GalvanizeFactory.DrawnThreshold.Should().Be(2);
        GalvanizeFactory.BaseDamage.Should().Be(3);
        GalvanizeFactory.BoostedDamage.Should().Be(5);
    }

    [Fact]
    public void Resolve_DealsFiveDamage_WhenTwoCardsDrawnThisTurn()
    {
        // CR 608.2 conditional — read LIVE off ctx.Game.TurnState. Two draws
        // by the caster this turn => boosted 5 damage.
        var bear = CreatureOnBattlefield(_bob, 2, 7);

        var turnState = new TurnState();
        turnState.RecordCardDrawn(_alice);
        turnState.RecordCardDrawn(_alice);

        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);
        Resolve(def, bear, turnState);

        bear.Damage.Should().Be(5,
            because: "the caster has drawn two or more cards this turn, so Galvanize deals 5 instead of 3");
    }

    [Fact]
    public void Resolve_DealsThreeDamage_WhenOnlyOneCardDrawnThisTurn()
    {
        // One draw (< 2) => base 3 damage.
        var bear = CreatureOnBattlefield(_bob, 2, 7);

        var turnState = new TurnState();
        turnState.RecordCardDrawn(_alice);

        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);
        Resolve(def, bear, turnState);

        bear.Damage.Should().Be(3,
            because: "one card drawn is below the two-card threshold, so Galvanize deals its base 3 damage");
    }

    [Fact]
    public void Resolve_OtherPlayersDraws_DoNotBoost()
    {
        // CR 608.2 — "If YOU'VE drawn" is keyed to the caster only. The
        // opponent's draws never boost the caster's Galvanize.
        var bear = CreatureOnBattlefield(_bob, 2, 7);

        var turnState = new TurnState();
        turnState.RecordCardDrawn(_bob);
        turnState.RecordCardDrawn(_bob);

        var def = GalvanizeFactory.BuildSpellDefinition(_alice, o => o);
        Resolve(def, bear, turnState);

        bear.Damage.Should().Be(3,
            because: "only the caster's draws count toward Galvanize's boost, not the opponent's");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Resolve the spell's effects against a context carrying the
    /// supplied <paramref name="turnState"/> (the CR 608.2 rider reads it live).</summary>
    private void Resolve(SpellDefinition def, object target, TurnState turnState)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        var game = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: true,
            turnState: turnState);
        var ctx = ResolutionContext.For(_alice, agent: null, game: game, chosenTargets: null);

        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }
}
