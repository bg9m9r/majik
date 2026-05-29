using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ArcTrailFactory"/>.
///
/// Card: Arc Trail — Sorcery {1}{R} (Scars of Mirrodin).
///   "Arc Trail deals 2 damage to any target and 1 damage to any other
///    target."
///
/// Shape mirrors <see cref="SearingBlazeFactory"/> (two simultaneous
/// 1..1 target requests, fixed damage per request) and <see cref="ShockFactory"/>
/// (single "any target" → flat damage via <see cref="Fx.DealDamageAny"/>).
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve: 2 damage to the first target, 1 damage to the second
///     ("any other") target — both players.
///   - Resolve: damage to a creature (marked-damage path, CR 119.3).
///   - Resolve against a planeswalker (loyalty-removal path, CR 306.7).
///   - The two targets must be distinct ("any other target", CR 601.2c) —
///     a single recipient takes the sum only when it is named twice
///     (engine does not enforce distinctness at the factory level; the
///     "any other" constraint is asserted at the agent/caller level, same
///     V1 posture as Searing Blaze's "controlled by" relationship).
/// </summary>
public class ArcTrailFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ArcTrail_Identity()
    {
        var at = ArcTrailFactory.Create(_alice);

        at.Name.Should().Be("Arc Trail");
        at.ManaCost.Should().Be("{1}{R}");
        at.HasType(CardType.Sorcery).Should().BeTrue();
        at.Owner.Should().BeSameAs(_alice);
        at.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ArcTrail()
    {
        var card = NamedCardFactory.Create("Arc Trail", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Arc Trail");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TwoPlayers_2ToFirst_1ToSecond()
    {
        var def = ArcTrailFactory.BuildSpellDefinition(o => o!);
        var bobL = _bob.LifeTotal;
        var carolL = _carol.LifeTotal;

        var effects = def.EffectFactory(MakeChosen(new object[] { _bob }, new object[] { _carol }));
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(bobL - 2, "first target takes 2 damage");
        _carol.LifeTotal.Should().Be(carolL - 1, "the 'any other' target takes 1 damage");
    }

    [Fact]
    public void Resolve_FirstTargetCreature_TakesTwoMarkedDamage()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var carolL = _carol.LifeTotal;

        var def = ArcTrailFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(new object[] { bear }, new object[] { _carol }));
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(2, "first target creature takes 2 damage");
        _carol.LifeTotal.Should().Be(carolL - 1, "second target takes 1 damage");
    }

    [Fact]
    public void Resolve_SecondTargetPlaneswalker_RemovesOneLoyalty()
    {
        var pw = new Planeswalker(
            "Chandra, Torch of Defiance",
            "{2}{R}{R}",
            startingLoyalty: 5,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);

        var aliceL = _alice.LifeTotal;

        var def = ArcTrailFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(new object[] { _alice }, new object[] { pw }));
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(aliceL - 2, "first target takes 2 damage");
        pw.Loyalty.Should().Be(5 - 1, "second target planeswalker loses 1 loyalty (CR 306.7)");
    }

    [Fact]
    public void Resolve_DeclaresTwoTargetRequests()
    {
        var def = ArcTrailFactory.BuildSpellDefinition(o => o!);

        def.TargetRequests.Should().HaveCount(2, "2 damage target + 'any other' 1 damage target");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[1].MinTargets.Should().Be(1);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ChosenSpellParams MakeChosen(object[] first, object[] second) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { first, second },
            Mana: ManaPayment.Empty);
}
