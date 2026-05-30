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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlagstormFactory"/>.
///
/// Slagstorm ({1}{R}{R}) — Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Slagstorm deals 3 damage to each creature.
///     • Slagstorm deals 3 damage to each player."
///
/// CR 700.2d — modal "Choose one —" spell with 2 non-targeted modes.
/// </summary>
public class SlagstormFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ─── Identity + dispatcher ────────────────────────────────────────────────

    [Fact]
    public void Slagstorm_Create_HasSorceryShape_Red()
    {
        var card = SlagstormFactory.Create(_alice);

        card.Name.Should().Be("Slagstorm");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{R}{R} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Slagstorm_PrintedManaCost_IsExact()
    {
        SlagstormFactory.CardName.Should().Be("Slagstorm");
        SlagstormFactory.PrintedManaCost.Should().Be("{1}{R}{R}");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Slagstorm()
    {
        var card = NamedCardFactory.Create("Slagstorm", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Slagstorm");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Slagstorm_BuildDefinition_HasTwoModes_NoTargetRequests()
    {
        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });

        def.Modes.Should().HaveCount(2);
        def.Modes[SlagstormFactory.ModeEachCreature].Should().Contain("creature");
        def.Modes[SlagstormFactory.ModeEachPlayer].Should().Contain("player");

        def.TargetRequests.Should().BeEmpty(because: "neither mode targets");
    }

    // ─── Mode 0: 3 damage to each creature ────────────────────────────────────

    [Fact]
    public void Slagstorm_Mode0_Deals3DamageToEveryCreature_BothBattlefields()
    {
        var aliceCreature = SeedCreature("Goblin", "{R}", _alice, 2, 2);
        var bobCreature   = SeedCreature("Dragon", "{5}{R}{R}", _bob, 5, 5);

        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });
        var effects = def.EffectFactory(ChooseMode(SlagstormFactory.ModeEachCreature));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        aliceCreature.Damage.Should().Be(3, because: "each creature takes 3 damage regardless of controller");
        bobCreature.Damage.Should().Be(3);
    }

    [Fact]
    public void Slagstorm_Mode0_DoesNotDamagePlayers()
    {
        SeedCreature("Bear", "{1}{G}", _bob, 2, 2);

        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });
        foreach (var e in def.EffectFactory(ChooseMode(SlagstormFactory.ModeEachCreature))) e.Execute();

        _alice.LifeTotal.Should().Be(20, because: "mode 0 only hits creatures, not players");
        _bob.LifeTotal.Should().Be(20);
    }

    // ─── Mode 1: 3 damage to each player ──────────────────────────────────────

    [Fact]
    public void Slagstorm_Mode1_Deals3DamageToEveryPlayer()
    {
        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });
        var effects = def.EffectFactory(ChooseMode(SlagstormFactory.ModeEachPlayer));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(17, because: "each player loses 3 life — including the caster (CR 109.5)");
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Slagstorm_Mode1_DoesNotDamageCreatures()
    {
        var bobCreature = SeedCreature("Dragon", "{5}{R}{R}", _bob, 5, 5);

        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });
        foreach (var e in def.EffectFactory(ChooseMode(SlagstormFactory.ModeEachPlayer))) e.Execute();

        bobCreature.Damage.Should().Be(0, because: "mode 1 only hits players, not creatures");
    }

    // ─── Mode dispatch via the multi-pick list ────────────────────────────────

    [Fact]
    public void Slagstorm_Mode1_ViaModeIndexesList_StillResolves()
    {
        var def = SlagstormFactory.BuildDefinition(new[] { _alice, _bob });

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[] { SlagstormFactory.ModeEachPlayer });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.LifeTotal.Should().Be(17);
        _bob.LifeTotal.Should().Be(17);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private ChosenSpellParams ChooseMode(int mode) =>
        new(
            ModeIndex: mode,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    private static Creature SeedCreature(string name, string cost, Player owner, int power, int toughness)
    {
        var creature = new Creature(name, cost, power, toughness);
        creature.SetOwner(owner);
        creature.SetController(owner);
        creature.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(creature);
        return creature;
    }
}
