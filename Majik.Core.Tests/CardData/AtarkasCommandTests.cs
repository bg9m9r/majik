using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Atarka's Command, Dragons of Tarkir, {R}{G}, modal "Choose two —" with
/// four non-targeted modes (opponent life-gain lockout / 3 damage to each
/// opponent / play land / +1/+1 + reach mass pump). Tests exercise the
/// EffectFactory directly with crafted <see cref="ChosenSpellParams"/> —
/// same pattern as Kolaghan's Command.
/// </summary>
public class AtarkasCommandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Create_HasInstantShape_RedGreen()
    {
        var c = AtarkasCommandFactory.Create(_alice);

        c.Name.Should().Be("Atarka's Command");
        c.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAtarkasCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Atarka's Command", _alice);
        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Atarka's Command");
    }

    [Fact]
    public void BuildDefinition_ExposesFourModes_WithPerModeIntents()
    {
        var def = AtarkasCommandFactory.BuildDefinition(_alice);

        def.Modes.Should().HaveCount(4);
        def.ModeIntentsOrEmpty.Should().HaveCount(4);
        def.ModeIntentsOrEmpty[AtarkasCommandFactory.ModeDealDamage]
            .Should().Be(BotIntent.Burn);
        def.ModeIntentsOrEmpty[AtarkasCommandFactory.ModePlayLand]
            .Should().Be(BotIntent.Ramp);
        def.ModeIntentsOrEmpty[AtarkasCommandFactory.ModePumpAll]
            .Should().Be(BotIntent.Buff);
        def.TargetRequests.Should().BeEmpty("Atarka's Command has no targeted modes");
    }

    // -----------------------------------------------------------------------
    // Mode 0 + Mode 1 — opponent life-gain lockout + 3 damage to each opponent
    // -----------------------------------------------------------------------

    [Fact]
    public void Modes0And1_LockoutAndDamage_AppliedToEachOpponent()
    {
        var bus = new ReplacementBus();
        _bob.AttachReplacementBus(bus);

        var allPlayers = new[] { _alice, _bob };
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice, allPlayers, replacements: bus,
            chosenModes: new[] { AtarkasCommandFactory.ModeNoLifeGain, AtarkasCommandFactory.ModeDealDamage });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModeNoLifeGain,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: allPlayers,
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModeNoLifeGain,
                AtarkasCommandFactory.ModeDealDamage,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "mode 1 dealt 3 damage to each opponent");
        _alice.LifeTotal.Should().Be(20, "caster takes no damage from mode 1");

        _bob.GainLife(5);
        _bob.LifeTotal.Should().Be(17, "mode 0 locked Bob (an opponent) out of life-gain");
    }

    [Fact]
    public void Mode0_LockoutDoesNotAffectCaster()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);

        var allPlayers = new[] { _alice, _bob };
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice, allPlayers, replacements: bus,
            chosenModes: new[] { AtarkasCommandFactory.ModeNoLifeGain, AtarkasCommandFactory.ModePumpAll });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModeNoLifeGain,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: allPlayers,
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModeNoLifeGain,
                AtarkasCommandFactory.ModePumpAll,
            });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Alice (the caster) is not an opponent — she can still gain life.
        _alice.GainLife(4);
        _alice.LifeTotal.Should().Be(24);
    }

    // -----------------------------------------------------------------------
    // Mode 2 — put a land from hand onto the battlefield (bypasses LandDrop)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_PutLandFromHand_OntoBattlefield()
    {
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(forest);

        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            chosenModes: new[] { AtarkasCommandFactory.ModePlayLand, AtarkasCommandFactory.ModePumpAll });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModePlayLand,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModePlayLand,
                AtarkasCommandFactory.ModePumpAll,
            });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 2 puts a land card from your hand onto the battlefield (CR 305.9 / 113.6c)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void Mode2_NoLandsInHand_NoOps()
    {
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            chosenModes: new[] { AtarkasCommandFactory.ModePlayLand, AtarkasCommandFactory.ModePumpAll });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModePlayLand,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModePlayLand,
                AtarkasCommandFactory.ModePumpAll,
            });

        // Should not throw and battlefield stays empty.
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 3 — Creatures you control get +1/+1 AND gain reach until EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode3_PumpsAndGrantsReach_ToAllControlledCreatures()
    {
        var continuous = new ContinuousEffectsService();

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var bobGiant = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        _bob.Zones.Battlefield.AddCard(bobGiant);

        var def = AtarkasCommandFactory.BuildDefinition(
            _alice,
            chosenModes: new[] { AtarkasCommandFactory.ModeDealDamage, AtarkasCommandFactory.ModePumpAll });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModePumpAll,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModeDealDamage,
                AtarkasCommandFactory.ModePumpAll,
            });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        aliceBear.GetPower().Should().Be(3, "Alice's bear gets +1/+1");
        aliceBear.GetToughness().Should().Be(3);
        CombatAbilities.HasReach(aliceBear).Should().BeTrue("granted Reach EOT");
        bobGiant.GetPower().Should().Be(3, "Bob's giant is unaffected — opponents not pumped");
        bobGiant.GetToughness().Should().Be(3);
        CombatAbilities.HasReach(bobGiant).Should().BeFalse();

        // CR 514.2 — pump + reach expire at EOT.
        continuous.ExpireEndOfTurn();
        aliceBear.GetPower().Should().Be(2);
        aliceBear.GetToughness().Should().Be(2);
        CombatAbilities.HasReach(aliceBear).Should().BeFalse();
    }

    [Fact]
    public void DuplicateModePicked_DroppedPerCR_700_2e()
    {
        var def = AtarkasCommandFactory.BuildDefinition(
            _alice, new[] { _alice, _bob });

        var chosen = new ChosenSpellParams(
            ModeIndex: AtarkasCommandFactory.ModeDealDamage,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                AtarkasCommandFactory.ModeDealDamage,
                AtarkasCommandFactory.ModeDealDamage, // dup — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1,
            "CR 700.2e — duplicate mode picks are dropped");
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "deal-damage runs once, not twice");
    }
}
