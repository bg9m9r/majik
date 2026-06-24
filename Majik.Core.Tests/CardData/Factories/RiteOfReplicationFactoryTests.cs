using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RiteOfReplicationFactory"/> (Zendikar, {2}{U}{U}).
///
/// Scryfall oracle (verbatim, verified 2026-06-24):
///   "Kicker {5} (You may pay an additional {5} as you cast this spell.)
///    Create a token that's a copy of target creature. If this spell was
///    kicked, create five of those tokens instead."
///
/// Combines the copy-token mechanism of
/// <see cref="CacklingCounterpartFactory"/> (CR 706.2 / CR 707.2) with the
/// kicker-conditional branch of <see cref="RoilEruptionFactory"/>
/// (CR 702.33b — "if this spell was kicked"). Differences from Cackling
/// Counterpart: this is a Sorcery, its target is ANY creature (not "you
/// control"), and the kicked branch mints FIVE token copies instead of one.
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity ({2}{U}{U} Sorcery).
/// - Spell definition shape: 1..1 "target creature".
/// - Un-kicked resolve: one token copy of the chosen creature under the
///   caster's control (name + P/T + keywords copied; CR 706.2 / CR 707.2).
/// - Kicked resolve: FIVE token copies (CR 702.33b — was-kicked branch).
/// - Kicker cost is the printed {5} (CR 702.33).
/// </summary>
[Trait("Color", "U")]
public class RiteOfReplicationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RiteOfReplication_Identity_SorceryAt2UU()
    {
        var card = RiteOfReplicationFactory.Create(_alice);

        card.Name.Should().Be("Rite of Replication");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{U}{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RiteOfReplication_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var card = RiteOfReplicationFactory.Create(_alice);
        var def = RiteOfReplicationFactory.BuildSpellDefinition(card, _alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void RiteOfReplication_Resolve_Unkicked_SpawnsSingleTokenCopyUnderCasterControl()
    {
        // A creature BOB controls — Rite targets ANY creature, not just the
        // caster's. The copy still enters under the CASTER's control (CR 707.2).
        var source = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        source.SetOwner(_bob);
        source.SetController(_bob);
        source.SetZone(ZoneType.Battlefield);
        source.AddAbility(new KeywordAbility("flying", source, _bob));
        _bob.Zones.Battlefield.AddCard(source);

        var card = RiteOfReplicationFactory.Create(_alice); // not kicked
        var def = RiteOfReplicationFactory.BuildSpellDefinition(card, _alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { source } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().ContainSingle("an un-kicked Rite of Replication creates exactly one token copy");
        var copy = tokens.Single();
        copy.Name.Should().Be("Grizzly Bears");
        copy.BasePower.Should().Be(2);
        copy.BaseToughness.Should().Be(2);
        copy.Controller.Should().BeSameAs(_alice, "CR 707.2 — copy token's controller is the effect's controller (the caster)");
        copy.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword.ToLowerInvariant())
            .Should().Contain("flying");
    }

    [Fact]
    public void RiteOfReplication_Resolve_Kicked_SpawnsFiveTokenCopies()
    {
        var source = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(source);

        // CR 702.33b — stamp the cast-time kicker sentinel so the resolve body
        // takes the "create five of those tokens instead" branch.
        var card = RiteOfReplicationFactory.Create(_alice);
        card.SetWasKicked(true);

        var def = RiteOfReplicationFactory.BuildSpellDefinition(card, _alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { source } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(5, "a kicked Rite of Replication creates five token copies (CR 702.33b)");
        tokens.Should().OnlyContain(c => c.Name == "Grizzly Bears");
        tokens.Should().OnlyContain(c => c.Controller == _alice);
    }

    [Fact]
    public void RiteOfReplication_Resolve_NonCreatureTarget_NoOp()
    {
        // CR 608.2b — if the target is illegal (not a creature) on resolution,
        // the token creation is a clean no-op rather than a crash.
        var card = RiteOfReplicationFactory.Create(_alice);
        var def = RiteOfReplicationFactory.BuildSpellDefinition(card, _alice, resolver: _ => "not-a-creature");
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { new object() } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RiteOfReplication_KickerCost_IsFiveGeneric()
    {
        var card = RiteOfReplicationFactory.Create(_alice);
        var cost = RiteOfReplicationFactory.BuildAdditionalCost(card);

        var kicker = cost.Should().BeOfType<KickerAdditionalCost>().Subject;
        kicker.KickerCost.Should().Be(ManaCost.Parse("{5}"),
            "printed kicker cost is {5} (CR 702.33)");
    }
}
