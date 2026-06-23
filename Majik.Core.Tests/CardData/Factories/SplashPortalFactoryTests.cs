using FluentAssertions;
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
/// Unit tests for <see cref="SplashPortalFactory"/>.
///
/// Splash Portal is the blue flicker-cantrip cousin of Acrobatic Maneuver /
/// Cloudshift, but the draw is CONDITIONAL on the returned creature's
/// creature type. The contract test (CardFactoryContractTests) already
/// asserts NamedCardFactory dispatch + well-formedness, so these cases cover
/// only the UNIQUE behaviour:
/// - Identity (Sorcery, {U}).
/// - SpellDefinition shape — single 1..1 "target creature you control".
/// - Resolve: exiles + returns the target; draws ONLY when it is a Bird /
///   Frog / Otter / Rat (CR 121.1, contingent on "that creature").
/// - Resolve: a non-qualifying creature is still flickered but no draw.
/// - Resolve: opponent-controlled target fizzles the whole spell — no
///   flicker, and (because there is no "that creature") no draw (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class SplashPortalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SplashPortal_IsSorcery_AtCostU()
    {
        var c = SplashPortalFactory.Create(_alice);

        c.Name.Should().Be("Splash Portal");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SplashPortal_Definition_HasSingleControllerCreatureTarget()
    {
        var def = SplashPortalFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    [Theory]
    [InlineData(CardSubtype.Bird)]
    [InlineData(CardSubtype.Frog)]
    [InlineData(CardSubtype.Otter)]
    [InlineData(CardSubtype.Rat)]
    public void SplashPortal_Resolve_QualifyingType_FlickersAndDraws(CardSubtype subtype)
    {
        var critter = NewControlledCreature(_alice, "Splash Friend", "{U}", subtype);
        SeedLibrary(_alice, count: 3);
        var startingHandCount = _alice.Zones.Hand.Count;

        Resolve(SplashPortalFactory.BuildSpellDefinition(_alice), critter);

        critter.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — Splash Portal returns the exiled creature in the same resolution");
        _alice.Zones.Battlefield.GetCards().Should().Contain(critter);
        _alice.Zones.Exile.GetCards().Should().NotContain(critter);
        critter.Controller.Should().BeSameAs(_alice);

        _alice.Zones.Hand.Count.Should().Be(startingHandCount + 1,
            "the returned creature is a Bird/Frog/Otter/Rat → CR 121.1 conditional draw fires");
    }

    [Fact]
    public void SplashPortal_Resolve_NonQualifyingType_FlickersButNoDraw()
    {
        // A Bear (Bear subtype) is not one of the four — flicker, but no draw.
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}", CardSubtype.Bear);
        SeedLibrary(_alice, count: 3);
        var startingHandCount = _alice.Zones.Hand.Count;

        Resolve(SplashPortalFactory.BuildSpellDefinition(_alice), bear);

        bear.Zone.Should().Be(ZoneType.Battlefield, "the flicker half still resolves");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);

        _alice.Zones.Hand.Count.Should().Be(startingHandCount,
            "a non-Bird/Frog/Otter/Rat creature does not satisfy the conditional draw");
    }

    [Fact]
    public void SplashPortal_Resolve_OpponentControlledTarget_Fizzles_NoFlickerNoDraw()
    {
        // Bob's Bird — Alice cannot legally affect it; the whole spell fizzles
        // (CR 608.2b), so there is no "that creature" and no draw.
        var bobBird = NewControlledCreature(_bob, "Sky Tyrant", "{1}{U}", CardSubtype.Bird);
        SeedLibrary(_alice, count: 3);
        var startingHandCount = _alice.Zones.Hand.Count;

        Resolve(SplashPortalFactory.BuildSpellDefinition(_alice), bobBird);

        bobBird.Zone.Should().Be(ZoneType.Battlefield,
            "opponent-controlled target → CR 608.2b illegal-target → flicker fizzles");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBird);

        _alice.Zones.Hand.Count.Should().Be(startingHandCount,
            "no 'that creature' resolved → the conditional draw cannot fire");
    }

    [Fact]
    public void SplashPortal_Resolve_NoTargets_NoOp()
    {
        var def = SplashPortalFactory.BuildSpellDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: System.Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        effects.Should().BeEmpty("no targets → no effects produced (prod cast requires MinTargets = 1)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(SpellDefinition def, Creature target)
    {
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost, CardSubtype subtype)
    {
        var c = new Creature(name, cost, 2, 2, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void SeedLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Creature($"LibCard{i}", "{1}", 1, 1);
            card.SetOwner(owner);
            owner.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
