using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BeastieBeatdownFactory"/> — Beastie Beatdown
/// (Bloomburrow, {R}{G}).
///
/// Sorcery. "Choose target creature you control and target creature an opponent
/// controls. Delirium — If there are four or more card types among cards in your
/// graveyard, put two +1/+1 counters on the creature you control. The creature
/// you control deals damage equal to its power to the creature an opponent
/// controls."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity — red+green Sorcery at {R}{G} (a single _Identity assert; the
///   contract test covers dispatch + well-formedness).
/// - SpellDefinition shape — two ordered 1..1 creature target requests.
/// - Resolve — ONE-SIDED damage equal to power (NOT a fight: the opponent's
///   creature deals no damage back).
/// - Resolve — Delirium INACTIVE: no +1/+1 counters; damage = base power.
/// - Resolve — Delirium ACTIVE (4+ card types in the controller's graveyard):
///   two +1/+1 counters placed FIRST, so the boosted power deals the damage.
/// - Resolve — illegal targets no-op (CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class BeastieBeatdownFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // =========================================================================
    // Identity
    // =========================================================================

    [Fact]
    public void BeastieBeatdown_Identity_RedGreen_Sorcery_ManaValueTwo()
    {
        var card = BeastieBeatdownFactory.Create(_alice);

        card.Name.Should().Be("Beastie Beatdown");
        card.ManaCost.Should().Be("{R}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
    }

    // =========================================================================
    // SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BeastieBeatdown_BuildDefinition_TwoCreatureTargetRequests()
    {
        var def = BeastieBeatdownFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(2,
            "a creature you control + a creature an opponent controls");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[1].MinTargets.Should().Be(1);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Resolution — one-sided damage, no delirium
    // =========================================================================

    [Fact]
    public void BeastieBeatdown_Resolve_OneSided_DamageEqualToPower_NoBackswing()
    {
        // Delirium inactive (empty graveyard) — no counters; damage = base power.
        var mine = MakeCreature("Mine", power: 4, toughness: 4, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 5, toughness: 6, controller: _bob);

        ExecuteResolve(mine, theirs);

        // The controlled creature deals damage equal to its power...
        theirs.Damage.Should().Be(4, "Mine has power 4 (no delirium boost)");
        // ...and the opponent's creature deals NO damage back (not a fight).
        mine.Damage.Should().Be(0, "Beastie Beatdown is one-sided, not a fight");
        mine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "delirium is inactive — no +1/+1 counters");
    }

    [Fact]
    public void BeastieBeatdown_Resolve_ZeroPowerDealsNoDamage()
    {
        var mine = MakeCreature("Mine", power: 0, toughness: 3, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 4, toughness: 4, controller: _bob);

        ExecuteResolve(mine, theirs);

        theirs.Damage.Should().Be(0, "0 power deals no damage");
        mine.Damage.Should().Be(0);
    }

    // =========================================================================
    // Resolution — delirium boost
    // =========================================================================

    [Fact]
    public void BeastieBeatdown_Resolve_DeliriumActive_TwoCountersFirst_BoostedPowerDealsDamage()
    {
        // CR 702.105 — four or more card types among cards in the controller's
        // graveyard activates delirium.
        FillGraveyardWithFourCardTypes(_alice);

        // Wire a ContinuousEffectsService so +1/+1 counters reflect in Power
        // (the layer system computes P/T from counters).
        var effects = new ContinuousEffectsService();
        var mine = MakeCreature("Mine", power: 2, toughness: 2, controller: _alice);
        mine.ActiveEffects = effects;
        var theirs = MakeCreature("Theirs", power: 5, toughness: 9, controller: _bob);

        ExecuteResolve(mine, theirs);

        // Two +1/+1 counters placed first.
        mine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "delirium puts two +1/+1 counters on the creature you control");
        // ...so the boosted power (2 base + 2 counters = 4) deals the damage.
        mine.Power.Should().Be(4);
        theirs.Damage.Should().Be(4,
            "the boosted power (2 + 2 from delirium counters) is dealt");
        mine.Damage.Should().Be(0, "still one-sided");
    }

    [Fact]
    public void BeastieBeatdown_Resolve_DeliriumInactive_ThreeTypes_NoCounters()
    {
        // Only three distinct card types in the graveyard — delirium NOT active.
        AddToGraveyard(_alice, new Creature("Goblin", "{R}", 1, 1));
        AddToGraveyard(_alice, new Instant("Shock", "{R}"));
        AddToGraveyard(_alice, new Sorcery("Lava Spike", "{R}"));

        var mine = MakeCreature("Mine", power: 3, toughness: 3, controller: _alice);
        var theirs = MakeCreature("Theirs", power: 5, toughness: 5, controller: _bob);

        ExecuteResolve(mine, theirs);

        mine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "only three card types — delirium needs four");
        theirs.Damage.Should().Be(3, "no boost — base power 3");
    }

    // =========================================================================
    // Resolution — illegal targets
    // =========================================================================

    [Fact]
    public void BeastieBeatdown_Resolve_NonCreatureVictim_IsCleanNoOp()
    {
        // CR 608.2b — a non-creature opponent target deals no damage.
        var mine = MakeCreature("Mine", power: 3, toughness: 3, controller: _alice);
        var notACreature = new Land("Mountain");

        Action act = () => ExecuteResolve(mine, notACreature);

        act.Should().NotThrow();
        mine.Damage.Should().Be(0);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private void ExecuteResolve(object mine, object theirs)
    {
        var def = BeastieBeatdownFactory.BuildDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { mine },
                new object[] { theirs },
            },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature MakeCreature(string name, int power, int toughness, Player controller)
    {
        var c = new Creature(name, "{G}", power: power, toughness: toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }

    /// <summary>Put four distinct card types in the player's graveyard
    /// (Creature, Instant, Sorcery, Land) so delirium is active.</summary>
    private void FillGraveyardWithFourCardTypes(Player player)
    {
        AddToGraveyard(player, new Creature("Goblin", "{R}", 1, 1));
        AddToGraveyard(player, new Instant("Shock", "{R}"));
        AddToGraveyard(player, new Sorcery("Lava Spike", "{R}"));
        AddToGraveyard(player, new Land("Mountain"));
    }

    private void AddToGraveyard(Player player, Card card)
    {
        card.SetOwner(player);
        card.SetController(player);
        card.SetZone(ZoneType.Graveyard);
        player.Zones.Graveyard.AddCard(card);
    }
}
