using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="RoastFactory"/> (Dragons of Tarkir).
///
/// Oracle text: "Roast deals 5 damage to target creature without flying."
/// ({1}{R} Sorcery.)
///
/// Covers:
/// - Identity ({1}{R} Sorcery, red).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target creature without flying" request.
/// - Resolve body deals 5 damage to a non-flying creature (CR 119.2).
/// - Resolve body is a no-op when the target is not a creature (CR 608.2b).
/// - Resolve body is a no-op when the target has flying — the spell can
///   only legally target creatures without flying (CR 115.4 / CR 608.2b).
/// </summary>
public class RoastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature CreatureOnBattlefield(Player owner, int power, int tough, bool flying = false)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        if (flying)
        {
            c.AddAbility(new KeywordAbility("Flying", source: c, controller: owner));
        }
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Roast_Identity_SorceryAt1R()
    {
        var card = RoastFactory.Create(_alice);

        card.Name.Should().Be("Roast");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Roast_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Roast", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Roast");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Roast_SpellDefinition_HasSingleTargetCreatureWithoutFlyingRequest()
    {
        var def = RoastFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
        req.Description.Should().Contain("without flying");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Roast_Resolve_DealsFiveDamageToNonFlyingCreature()
    {
        var target = CreatureOnBattlefield(_bob, 6, 6);

        var def = RoastFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        target.Damage.Should().Be(5, "Roast deals 5 damage to target creature without flying");
    }

    [Fact]
    public void Roast_Resolve_NoOp_OnFlyingCreatureTarget()
    {
        // CR 115.4 / CR 608.2b — a creature with flying is not a legal target.
        // If a flier slips through the resolver (e.g. it gained flying after
        // targeting), the effect deals no damage.
        var flier = CreatureOnBattlefield(_bob, 6, 6, flying: true);

        var def = RoastFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { flier },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        flier.Damage.Should().Be(0, "Roast only damages creatures without flying");
    }

    [Fact]
    public void Roast_Resolve_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — if a spell's only target becomes illegal, the spell does
        // nothing on resolution.
        var def = RoastFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(20, "Roast only damages creatures, not players");
    }
}
