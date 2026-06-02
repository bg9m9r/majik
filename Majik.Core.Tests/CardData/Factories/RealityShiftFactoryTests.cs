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
/// Tests for <see cref="RealityShiftFactory"/> — Instant {1}{U} (Fate
/// Reforged).
///
/// Oracle text (verified against Scryfall):
///   "Exile target creature. Its controller manifests the top card of
///    their library. (That player puts the top card of their library
///    onto the battlefield face down as a 2/2 creature. If it's a
///    creature card, it can be turned face up any time for its mana
///    cost.)"
///
/// Covers:
///   - Card identity: {1}{U} blue Instant, mana value 2.
///   - NamedCardFactory dispatch.
///   - SpellDefinition: one 1..1 "target creature" Removal request.
///   - Resolve exiles the target creature (CR 701.31 exile) and its
///     controller manifests the top of their library (CR 701.31).
///   - The exiled creature's CONTROLLER manifests (not the caster).
///   - Empty-library controller: exile still happens; manifest is a clean
///     no-op.
///   - Illegal target at resolution (creature left battlefield) → no-op
///     (CR 608.2b).
///   - Non-creature resolved target (wrong type) → no-op.
/// </summary>
[Trait("Color", "U")]
public class RealityShiftFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RealityShift_Identity_InstantOneUBlue_ManaValueTwo()
    {
        var card = RealityShiftFactory.Create(_alice);

        card.Name.Should().Be("Reality Shift");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Sorcery).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Reality Shift costs {1}{U} — generic 1 + coloured 1 = MV 2 (CR 202.3)");
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest_Removal()
    {
        var def = RealityShiftFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);

        var req = def.TargetRequests[0];
        req.Description.Should().Contain("creature");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Intent.Should().Be(BotIntent.Removal,
            "Reality Shift removes a creature by exiling it — Removal intent");
    }

    // -----------------------------------------------------------------------
    // Resolve — exile + controller manifests
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectFactory_ExilesTargetCreature_AndControllerManifestsTopOfLibrary()
    {
        // Bob controls a creature on the battlefield.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        // Bob's top-of-library card (gets manifested face-down).
        var topCard = new Creature("Hidden Creature", "{2}{G}", 4, 4);
        topCard.SetOwner(_bob);
        _bob.Zones.Library.AddCard(topCard);

        var def = RealityShiftFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        // Targeted creature exiled (CR 701.31 — exile, not destroy).
        bears.Zone.Should().Be(ZoneType.Exile,
            "Reality Shift exiles the targeted creature");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Exile.GetCards().Should().Contain(bears);

        // The creature's controller (Bob) manifests the top of THEIR
        // library: a face-down 2/2 ManifestedCreature wraps it.
        var wrapper = _bob.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        wrapper.UnderlyingCard.Should().BeSameAs(topCard);
        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.Power.Should().Be(2, "CR 708.2 — face-down creatures are 2/2");
        wrapper.Toughness.Should().Be(2);
        // Underlying is a creature → turn-face-up ability granted (CR 701.31c).
        wrapper.Abilities.OfType<FaceDownActivatedAbility>().Should().ContainSingle();
        _bob.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    [Fact]
    public void EffectFactory_ControllerManifests_NotTheCaster()
    {
        // Alice casts Reality Shift on Bob's creature. Alice has a card on
        // top of HER library too; she must NOT manifest it — only Bob,
        // the exiled creature's controller, manifests.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var aliceTop = new Creature("Alice Top", "{1}{W}", 1, 1);
        aliceTop.SetOwner(_alice);
        _alice.Zones.Library.AddCard(aliceTop);

        var bobTop = new Creature("Bob Top", "{1}{B}", 3, 3);
        bobTop.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobTop);

        var def = RealityShiftFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        // Bob manifested his top card; Alice's library is untouched.
        _bob.Zones.Battlefield.GetCards().OfType<ManifestedCreature>()
            .Single().UnderlyingCard.Should().BeSameAs(bobTop);
        _alice.Zones.Library.GetCards().Should().Contain(aliceTop,
            "only the exiled creature's controller manifests, not the caster");
        _alice.Zones.Battlefield.GetCards().OfType<ManifestedCreature>()
            .Should().BeEmpty();
    }

    [Fact]
    public void EffectFactory_ControllerEmptyLibrary_ExileStillHappens_ManifestNoOp()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);
        // Bob's library is empty.

        var def = RealityShiftFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bears.Zone.Should().Be(ZoneType.Exile, "exile happens regardless of library size");
        _bob.Zones.Battlefield.GetCards().OfType<ManifestedCreature>().Should().BeEmpty(
            "empty library — manifest is a clean no-op");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal/non-creature target → no-op (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectFactory_IllegalTarget_CreatureNotOnBattlefield_NoOp()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        var bobTop = new Creature("Bob Top", "{1}{B}", 3, 3);
        bobTop.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobTop);

        var def = RealityShiftFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target at resolution → Reality Shift does nothing");
        // No manifest happens when the spell does nothing.
        _bob.Zones.Battlefield.GetCards().OfType<ManifestedCreature>().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().Contain(bobTop);
    }

    [Fact]
    public void EffectFactory_NonCreatureTarget_NoOp()
    {
        var forest = new Card("Forest", "");
        forest.SetOwner(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        var def = RealityShiftFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { forest } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var act = () =>
        {
            foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
        };
        act.Should().NotThrow();
        forest.Zone.Should().Be(ZoneType.Battlefield,
            "Reality Shift only exiles Creature targets; a non-creature is a no-op");
    }
}
