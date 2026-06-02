using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2e — modal "Choose two —" spell. Atarka's Command, Dragons of
/// Tarkir, {R}{G}, four targetless modes:
///   0 — Your opponents can't gain life this turn.
///   1 — Atarka's Command deals 3 damage to each opponent.
///   2 — You may put a land card from your hand onto the battlefield.
///   3 — Creatures you control get +1/+1 and gain reach until end of turn.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/> — same pattern as
/// <see cref="KolaghansCommandTests"/> — plus identity/dispatch checks
/// mirroring <see cref="ZealousPersecutionFactoryTests"/>.
/// </summary>
[Trait("Color", "M")]
public class AtarkasCommandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_RedGreen()
    {
        var c = AtarkasCommandFactory.Create(_alice);

        c.Name.Should().Be("Atarka's Command");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.ManaCost.Should().Be("{R}{G}");
        CardColors.GetColors(c).Should().Contain(ManaColor.Red);
        CardColors.GetColors(c).Should().Contain(ManaColor.Green);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAtarkasCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Atarka's Command", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Atarka's Command");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesFourModes_NoTargetRequests()
    {
        var def = AtarkasCommandFactory.BuildDefinition(_alice, allPlayers: null);

        def.Modes.Should().HaveCount(4);
        // CR 114.1 — every Atarka mode is targetless.
        def.TargetRequests.Should().BeEmpty();
        def.ModeIntentsOrEmpty.Should().HaveCount(4);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Your opponents can't gain life this turn
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_OpponentsCantGainLife_ButCasterStillCan()
    {
        // Each player owns its own ReplacementBus (CR 614).
        var aliceBus = new ReplacementBus();
        var bobBus   = new ReplacementBus();
        _alice.AttachReplacementBus(aliceBus);
        _bob.AttachReplacementBus(bobBus);

        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { AtarkasCommandFactory.ModeNoLifeGain, AtarkasCommandFactory.ModeDamageEach });

        var chosen = Choose(
            AtarkasCommandFactory.ModeNoLifeGain,
            AtarkasCommandFactory.ModeDamageEach);

        var effects = def.EffectFactory(chosen);
        // Execute only the no-lifegain effect (first chosen mode).
        effects[0].Execute();

        var bobBefore   = _bob.LifeTotal;
        var aliceBefore = _alice.LifeTotal;

        _bob.GainLife(5);
        _alice.GainLife(5);

        _bob.LifeTotal.Should().Be(bobBefore,
            because: "mode 0 zeroes life gain for the caster's opponents this turn");
        _alice.LifeTotal.Should().Be(aliceBefore + 5,
            because: "'your opponents' excludes the caster — Alice still gains life");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Deal 3 damage to each opponent
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DealsThreeDamageToEachOpponent_NotCaster()
    {
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { AtarkasCommandFactory.ModeNoLifeGain, AtarkasCommandFactory.ModeDamageEach });

        var chosen = Choose(
            AtarkasCommandFactory.ModeNoLifeGain,
            AtarkasCommandFactory.ModeDamageEach);

        var effects = def.EffectFactory(chosen);
        // Mode 1 (3 damage) is the second chosen mode.
        effects[1].Execute();

        _bob.LifeTotal.Should().Be(17,
            because: "mode 1 deals 3 damage to each opponent (CR 800.4 / CR 119.3)");
        _alice.LifeTotal.Should().Be(20,
            because: "the caster is not an opponent and takes no damage");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — You may put a land card from your hand onto the battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_PutsLandFromHandOntoBattlefield()
    {
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { AtarkasCommandFactory.ModePutLand, AtarkasCommandFactory.ModeDamageEach });

        var chosen = Choose(
            AtarkasCommandFactory.ModePutLand,
            AtarkasCommandFactory.ModeDamageEach);

        var effects = def.EffectFactory(chosen);
        // Mode 2 is the first chosen mode.
        effects[0].Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 2 puts a land from hand onto the battlefield (CR 113.6c)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void Mode2_NoLandInHand_IsCleanNoOp()
    {
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { AtarkasCommandFactory.ModePutLand, AtarkasCommandFactory.ModeDamageEach });

        var chosen = Choose(
            AtarkasCommandFactory.ModePutLand,
            AtarkasCommandFactory.ModeDamageEach);

        var effects = def.EffectFactory(chosen);
        var act = () => effects[0].Execute();

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 3 — Creatures you control get +1/+1 and gain reach until EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode3_PumpsAndGrantsReach_ToYourCreaturesOnly_ExpiresEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var myBear  = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2, effects);
        var foeBear = NewCreature(_bob,   "Runeclaw Bear", "{1}{G}", 2, 2, effects);

        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { AtarkasCommandFactory.ModeDamageEach, AtarkasCommandFactory.ModePumpAndReach });

        var chosen = Choose(
            AtarkasCommandFactory.ModeDamageEach,
            AtarkasCommandFactory.ModePumpAndReach);

        var built = def.EffectFactory(chosen);
        // Mode 3 is the second chosen mode.
        built[1].Execute();

        // Caster's creature: +1/+1 and reach.
        myBear.GetPower().Should().Be(3);
        myBear.GetToughness().Should().Be(3);
        effects.Compute(myBear).Keywords.Contains(AtarkasCommandFactory.ReachKeyword)
            .Should().BeTrue("mode 3 grants reach to creatures you control");

        // Opponent's creature: untouched ("creatures you control" only).
        foeBear.GetPower().Should().Be(2);
        foeBear.GetToughness().Should().Be(2);
        effects.Compute(foeBear).Keywords.Contains(AtarkasCommandFactory.ReachKeyword)
            .Should().BeFalse("the rider is scoped to the caster's creatures");

        // CR 514.2 — riders expire at the cleanup step.
        effects.ExpireEndOfTurn();
        myBear.GetPower().Should().Be(2, "pump expires at end of turn");
        myBear.GetToughness().Should().Be(2);
        effects.Compute(myBear).Keywords.Contains(AtarkasCommandFactory.ReachKeyword)
            .Should().BeFalse("granted reach expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Modal pick-count discipline
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectFactory_PicksExactlyTwoDistinctModes()
    {
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice, allPlayers: new[] { _alice, _bob });

        // Duplicates + an extra mode should collapse to two distinct effects.
        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModeDamageEach,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModeDamageEach,
                AtarkasCommandFactory.ModeDamageEach,   // dup — ignored (CR 700.2e)
                AtarkasCommandFactory.ModePutLand,
                AtarkasCommandFactory.ModeNoLifeGain,   // beyond PickCount — dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ChosenSpellParams Choose(params int[] modes) =>
        new(
            ModeIndex: modes.Length > 0 ? modes[0] : null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: modes);

    private static Creature NewCreature(
        Player owner, string name, string manaCost, int power, int toughness,
        ContinuousEffectsService effects)
    {
        var c = new Creature(name, manaCost, power, toughness)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
            ActiveEffects = effects,
        };
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
