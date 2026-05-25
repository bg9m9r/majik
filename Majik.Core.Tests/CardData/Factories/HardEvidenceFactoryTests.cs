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
/// Tests for <see cref="HardEvidenceFactory"/> — Sorcery {U} (Innistrad:
/// Midnight Hunt). Oracle:
///   "Return target creature to its owner's hand. Investigate."
///
/// Covers:
///   - Card identity (Sorcery, {U}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Spell definition shape: 1..1 "target creature" request.
///   - Resolve: opponent creature → returned to its owner's hand;
///     Clue token created under the caster.
///   - Resolve: self creature → returned to its owner's hand
///     (no opponent-only restriction printed).
///   - Resolve: target left the battlefield → bounce no-op, Clue still
///     created (CR 608.2b — illegal target only fizzles that part of
///     the effect, but Hard Evidence's two clauses are separate effects;
///     the Clue is created regardless because Investigate has no target).
///   - Resolve: noncreature target (e.g. land) → bounce no-op, Clue
///     still created.
/// </summary>
public class HardEvidenceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void HardEvidence_Identity_SorceryAtU()
    {
        var card = HardEvidenceFactory.Create(_alice);

        card.Name.Should().Be("Hard Evidence");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HardEvidence_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hard Evidence", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Hard Evidence");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void SpellDefinition_HasSingleCreatureTarget()
    {
        var def = HardEvidenceFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Resolve_BouncesOpponentCreature_AndCreatesClue()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var def = HardEvidenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Bear returns to Bob's hand.
        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);

        // Clue token created on Alice's battlefield (CR 701.30).
        var clues = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(1);
        clues[0].HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Resolve_BouncesOwnCreature_AndCreatesClue()
    {
        // Hard Evidence has no "an opponent controls" gate — self-bounce
        // is legal (and occasionally useful for ETB re-triggering).
        var myBear = new Creature("Saproling", "{G}", 1, 1);
        myBear.SetOwner(_alice);
        myBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(myBear);
        myBear.SetZone(ZoneType.Battlefield);

        var def = HardEvidenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { myBear },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Saproling returns to Alice's hand.
        myBear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(myBear);

        // Clue token created.
        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Clue))
            .Should().Be(1);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_BounceNoOp_ButClueStillCreated()
    {
        // Investigate has no target — it always resolves. The bounce
        // fizzles cleanly (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var def = HardEvidenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        // Bear leaves the battlefield (e.g. dies) before Hard Evidence
        // resolves.
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Bear stays in graveyard.
        bear.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Hand.GetCards().Should().NotContain(bear);

        // Clue still created — the investigate clause has no target.
        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Clue))
            .Should().Be(1);
    }

    [Fact]
    public void Resolve_NoncreatureTarget_BounceNoOp_ButClueStillCreated()
    {
        // Land target — fails the resolution-time creature gate
        // (CR 608.2b).
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_bob);
        island.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);

        var def = HardEvidenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { island },
            },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Island stays put.
        island.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(island);

        // Clue still created.
        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Clue))
            .Should().Be(1);
    }
}
