using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StormbreathDragonFactory"/> (Theros, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text:
///   "Flying, haste, protection from white
///    {5}{R}{R}: Monstrosity 3.
///    When this creature becomes monstrous, it deals damage to each
///    opponent equal to the number of cards in that player's hand."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - Flying + Haste keyword markers.
///   - Protection from white via Rules.Protection.HasProtectionFromColor.
///   - Monstrosity activation places three +1/+1 counters + flips the
///     monstrous flag; second activation no-ops.
///   - Becomes-monstrous trigger deals each opponent damage equal to
///     THAT opponent's own hand size (per-opponent variable; no
///     hand-size threshold).
/// </summary>
[Trait("Color", "R")]
public class StormbreathDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_ShipsCreatureShape_RedHybrid()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        dragon.Should().BeOfType<Creature>();
        dragon.Name.Should().Be("Stormbreath Dragon");
        dragon.Power.Should().Be(4);
        dragon.Toughness.Should().Be(4);
        dragon.ManaCost.Should().Be("{3}{R}{R}");
        dragon.ManaCostValue.TotalValue.Should().Be(5);
        dragon.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        dragon.Owner.Should().BeSameAs(_alice);
        dragon.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------------
    // Keyword markers + protection
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_AttachesFlying_Haste_ProtectionFromWhite()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        var keywords = dragon.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Haste");

        // Protection from white as a ProtectionAbility (not a keyword
        // marker — Protection rides its own ability shape).
        dragon.Abilities.OfType<ProtectionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ProtectionFromWhite_HasProtectionFromColor_ReadsWhite()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        Protection.HasProtectionFromColor(dragon, ManaColor.White).Should().BeTrue();
        Protection.HasProtectionFromColor(dragon, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(dragon, ManaColor.Red).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Monstrosity activation
    // -------------------------------------------------------------------------

    [Fact]
    public void Monstrosity_AddsThreePlusOnePlusOneCounters_AndMarksMonstrous()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        monstrosity.IsMonstrous.Should().BeFalse("starts not-monstrous");
        dragon.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        ResolveMonstrosity(dragon);

        monstrosity.IsMonstrous.Should().BeTrue();
        dragon.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(StormbreathDragonFactory.MonstrosityCounters);
    }

    [Fact]
    public void Monstrosity_SecondActivation_NoOps()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        ResolveMonstrosity(dragon);
        ResolveMonstrosity(dragon); // second pop

        dragon.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(StormbreathDragonFactory.MonstrosityCounters,
                "CR 702.95b — the activation self-gates on the monstrous flag");
    }

    [Fact]
    public void Monstrosity_Cost_IsFiveRR()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);
        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();

        var manaCost = monstrosity.Costs.OfType<ManaCostCost>().SingleOrDefault();
        manaCost.Should().NotBeNull();
        manaCost!.Cost.TotalValue.Should().Be(7, "5 generic + 2 red = 7");
        manaCost!.Cost.Red.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Becomes-monstrous trigger
    // -------------------------------------------------------------------------

    private static void FillHand(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler{i}", "");
            c.SetOwner(p);
            p.Zones.Hand.AddCard(c);
        }
    }

    /// <summary>
    /// Drive the Monstrosity 3 activated ability's resolution through the
    /// real async path with a live <see cref="GameContext"/> built from the
    /// supplied players, so the becomes-monstrous trigger reads opponents +
    /// hand sizes off <c>ctx.Game.AllPlayers</c> exactly as it does in a live
    /// match. Mirrors the engine's <c>ActivatedAbility.ResolveAsync</c> wiring.
    /// </summary>
    private static void ResolveMonstrosity(Creature dragon, params Player[] players)
    {
        var controller = dragon.Controller!;
        GameContext? game = players.Length == 0
            ? null
            : new GameContext(
                self: controller,
                allPlayers: players,
                activePlayer: controller,
                turnNumber: 1,
                currentPhase: null,
                stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var ctx = ResolutionContext.For(controller, agent: null, game, chosenTargets: null);

        var monstrosity = dragon.Abilities.OfType<StormbreathDragonAbility>().Single();
        foreach (var e in monstrosity.Effects)
        {
            e.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void BecomesMonstrous_IsARealTriggeredAbility_InCardAbilities()
    {
        // The pool-wide audit reads card.Abilities.OfType<ITriggeredAbility>();
        // the becomes-monstrous trigger must be a real bound ability.
        var dragon = StormbreathDragonFactory.Create(_alice);
        dragon.Abilities.OfType<ITriggeredAbility>().Should().ContainSingle(
            "the becomes-monstrous trigger is a real TriggeredAbility in card.Abilities");
    }

    [Fact]
    public void BecomesMonstrous_DealsEachOpponentDamageEqualToTheirOwnHandSize()
    {
        var carol = new Player("Carol", 20);
        var dragon = StormbreathDragonFactory.Create(_alice);

        // Bob holds 4 cards, Carol holds 2 — each opponent takes damage
        // equal to THAT player's hand size (per-opponent variable).
        FillHand(_bob, 4);
        FillHand(carol, 2);

        var bobLifeBefore = _bob.LifeTotal;
        var carolLifeBefore = carol.LifeTotal;

        ResolveMonstrosity(dragon, _alice, _bob, carol);

        _bob.LifeTotal.Should().Be(bobLifeBefore - 4, "Bob held 4 cards");
        carol.LifeTotal.Should().Be(carolLifeBefore - 2, "Carol held 2 cards");
    }

    [Fact]
    public void BecomesMonstrous_OpponentWithEmptyHand_TakesNoDamage()
    {
        var dragon = StormbreathDragonFactory.Create(_alice);

        // Bob's hand is empty — zero cards means zero damage (CR 701.31
        // damage = the number of cards in that player's hand). No
        // hand-size threshold gate exists in the current oracle.
        var bobLifeBefore = _bob.LifeTotal;

        ResolveMonstrosity(dragon, _alice, _bob);

        _bob.LifeTotal.Should().Be(bobLifeBefore, "Bob had no cards in hand");
    }

    [Fact]
    public void BecomesMonstrous_ControllerHandSize_DoesNotMatter()
    {
        // The damage keys off each OPPONENT's hand, never the controller's.
        // Stuff Alice's (controller) hand; Bob's empty — Bob still takes 0.
        var dragon = StormbreathDragonFactory.Create(_alice);

        FillHand(_alice, 7);

        var bobLifeBefore = _bob.LifeTotal;

        ResolveMonstrosity(dragon, _alice, _bob);

        _bob.LifeTotal.Should().Be(bobLifeBefore,
            "controller hand size is irrelevant under the current oracle");
    }

    [Fact]
    public void BecomesMonstrous_OnlyFiresOnce_SecondActivationNoOp()
    {
        // CR 701.31 — monstrosity does nothing if the creature is already
        // monstrous, so the becomes-monstrous trigger does not re-fire.
        var dragon = StormbreathDragonFactory.Create(_alice);

        FillHand(_bob, 3);

        ResolveMonstrosity(dragon, _alice, _bob); // becomes monstrous → 3 damage

        var bobLifeAfterFirst = _bob.LifeTotal;
        bobLifeAfterFirst.Should().Be(20 - 3);

        ResolveMonstrosity(dragon, _alice, _bob); // already monstrous → no-op

        _bob.LifeTotal.Should().Be(bobLifeAfterFirst,
            "already monstrous — monstrosity (and its trigger) does nothing");
    }

    [Fact]
    public void BecomesMonstrous_NoGameContext_NoOp()
    {
        // Shape-only resolution with no live game context — the damage
        // effect finds no opponents to read and is a safe no-op.
        var dragon = StormbreathDragonFactory.Create(_alice);

        FillHand(_bob, 5);

        var bobLifeBefore = _bob.LifeTotal;

        ResolveMonstrosity(dragon); // no players ⇒ ctx.Game is null

        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }

    // -------------------------------------------------------------------------
    // PROD-PATH: GameFacade routed build wires the trigger
    // -------------------------------------------------------------------------

    /// <summary>
    /// PROD-PATH regression guard (same class as the Festival Crasher / Kiln
    /// Fiend fix). The production <c>GameFacade</c> routed build dispatches
    /// <see cref="NamedCardFactory.Create(string, Player, Majik.Core.Effects.ContinuousEffectsService?)"/>
    /// (the effects-aware overload), NOT the single-arg factory overload. If
    /// the generator does not see a <c>Create(Player, ContinuousEffectsService)</c>
    /// overload the routed build falls through to shape-only dispatch and the
    /// becomes-monstrous trigger is absent in live play (the bug the pool-wide
    /// audit flags as MissingTrigger). This builds the card exactly as prod
    /// does and asserts the trigger is bound + deals opponent damage.
    /// </summary>
    [Fact]
    public void EffectsAwareDispatch_WiresBecomesMonstrousTrigger_OnProdPath()
    {
        var effects = new Majik.Core.Effects.ContinuousEffectsService();

        // Prod dispatch: GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner, effects).
        var built = NamedCardFactory.Create("Stormbreath Dragon", _alice, effects);
        built.Should().BeOfType<Creature>();
        var dragon = (Creature)built;

        dragon.Abilities.OfType<ITriggeredAbility>().Should().ContainSingle(
            "the prod effects-aware dispatch must route through the "
            + "Create(Player, ContinuousEffectsService) overload — not shape-only");

        FillHand(_bob, 4);
        var bobLifeBefore = _bob.LifeTotal;

        ResolveMonstrosity(dragon, _alice, _bob);

        dragon.Abilities.OfType<StormbreathDragonAbility>().Single().IsMonstrous
            .Should().BeTrue("monstrosity resolved on the prod-built card");
        _bob.LifeTotal.Should().Be(bobLifeBefore - 4,
            "the prod-built trigger deals each opponent damage = their hand size");
    }
}
