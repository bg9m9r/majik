using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ElspethsSmiteFactory"/> (March of the Machine,
/// {W}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Elspeth's Smite deals 3 damage to target attacking or blocking creature.
///    If that creature would die this turn, exile it instead."
///
/// The "target attacking or blocking creature" combat-gated request mirrors
/// <see cref="RazorgrassAmbushFactoryTests"/>; the "exile-if-would-die" rider
/// mirrors <see cref="ScorchingDragonfireFactoryTests"/>.
///
/// <see cref="CardFactoryContractTests"/> already asserts dispatch +
/// well-formedness for every implemented card, so this file covers only the
/// card's unique behaviour + a single identity assert.
/// </summary>
[Trait("Color", "W")]
public class ElspethsSmiteFactoryTests
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
    // Identity — non-vanilla mana cost / type assert.
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_InstantAtW_White()
    {
        var card = ElspethsSmiteFactory.Create(_alice);

        card.Name.Should().Be("Elspeth's Smite");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{W}");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
    }

    // -----------------------------------------------------------------------
    // Targeting — "attacking or blocking creature" combat-gated candidate pool.
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_SingleAttackingOrBlockingCreatureTargetRequest()
    {
        var def = ElspethsSmiteFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("attacking or blocking creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void CandidateGatherer_ReturnsInjectedCombatCreatures()
    {
        var attacker = CreatureOnBattlefield(_bob, 2, 2);
        var def = ElspethsSmiteFactory.BuildSpellDefinition(
            t => t,
            combatCreatureLookup: () => new[] { attacker });

        var candidates = def.TargetRequests[0].CandidateGatherer!(null!);
        candidates.Should().ContainSingle().Which.Should().BeSameAs(attacker);
    }

    [Fact]
    public void CandidateGatherer_NullLookup_YieldsEmptyPool()
    {
        var def = ElspethsSmiteFactory.BuildSpellDefinition(t => t);
        def.TargetRequests[0].CandidateGatherer!(null!).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — 3 damage to the chosen creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsThreeDamageToTargetCreature()
    {
        var bear = CreatureOnBattlefield(_bob, 2, 4);

        var def = ElspethsSmiteFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(ElspethsSmiteFactory.Damage,
            because: "Elspeth's Smite deals 3 damage to the target creature");
        bear.Damage.Should().Be(3);
    }

    [Fact]
    public void Resolve_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage.
        var def = ElspethsSmiteFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "Elspeth's Smite damages only attacking/blocking creatures, not players");
    }

    // -----------------------------------------------------------------------
    // Exile-instead rider (CR 700.3 / CR 514.2).
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DamagedCreatureDeath_RewrittenToExile()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);

        var def = ElspethsSmiteFactory.BuildSpellDefinition(o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        var dying = new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        var result = bus.Apply(dying);
        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            because: "a creature dealt damage by Elspeth's Smite that would die is exiled instead");
    }

    [Fact]
    public void Resolve_UntargetedCreatureDeath_NotRewritten()
    {
        var bus = new ReplacementBus();
        var bear = CreatureOnBattlefield(_bob, 2, 2);
        var other = CreatureOnBattlefield(_alice, 1, 1);

        var def = ElspethsSmiteFactory.BuildSpellDefinition(o => o, replacements: bus);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        // CR 700.3 — a different creature dying is unaffected: its death stays a
        // graveyard move.
        var dying = new ZoneMoveIntent(other, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(dying)!.ToZone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_NoReplacementBus_DealsDamageOnly()
    {
        var bear = CreatureOnBattlefield(_bob, 2, 2);

        var def = ElspethsSmiteFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Damage.Should().Be(3,
            because: "a null replacement bus still deals the damage; only the exile rider is skipped");
    }
}
