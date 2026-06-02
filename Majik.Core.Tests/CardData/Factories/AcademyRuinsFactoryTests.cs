using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Academy Ruins (Time Spiral; reprinted in Modern Masters 2017,
/// Double Masters, etc.).
///
/// Legendary Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}{U}, {T}: Put target artifact card from your graveyard on top of
///    your library."
///
/// Structurally identical to <see cref="HallOfHeliodsGenerosityFactory"/> —
/// a utility Legendary Land whose recursion is an ACTIVATED ability that puts
/// a typed card from the graveyard on top of the library — with the type swapped
/// from enchantment to artifact and the activation pip from {W} to {U}.
///
/// Covers:
///   - Identity (Legendary Land, "Academy Ruins", owner/controller).
///   - NamedCardFactory dispatch.
///   - {T}: Add {C} mana ability is present (one colourless).
///   - Exactly one non-mana activated ability (the recur).
///   - The recur ability declares a 1..1 "artifact card in your graveyard"
///     target request.
///   - On resolve with a chosen artifact in the graveyard: it moves to the
///     top of the library (CR 608, IZone.InsertCardAt(0)).
///   - CR 608.2b illegal-on-resolution rechecks: non-artifact targets and
///     targets no longer in the graveyard are left untouched.
///   - No target chosen → effect no-ops cleanly.
/// </summary>
[Trait("Color", "U")]
public class AcademyRuinsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Artifact GraveyardArtifact(string name = "Some Artifact")
    {
        var a = new Artifact(name, "1") { Owner = _alice };
        a.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(a);
        a.SetZone(ZoneType.Graveyard);
        return a;
    }

    private static ActivatedAbility Recur(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single();

    // ------------------------------------------------------------------ Identity

    [Fact]
    public void AcademyRuins_Identity()
    {
        var land = AcademyRuinsFactory.Create(_alice);

        land.Name.Should().Be("Academy Ruins");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Academy Ruins is a Legendary Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AcademyRuins()
    {
        var card = NamedCardFactory.Create("Academy Ruins", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Academy Ruins");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------- Mana ability

    [Fact]
    public void AcademyRuins_HasColorlessManaAbility()
    {
        var land = AcademyRuinsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("land produces exactly {C}")
            .Which.ManaGenerated.Generic.Should().Be(1);
    }

    // --------------------------------------------------------- Recur ability shape

    [Fact]
    public void AcademyRuins_HasExactlyOneActivatedAbility()
    {
        var land = AcademyRuinsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the graveyard-recur ability is the only non-mana activated ability");
    }

    [Fact]
    public void AcademyRuins_RecurAbility_HasCorrectTargetRequest()
    {
        var land = AcademyRuinsFactory.Create(_alice);

        var req = Recur(land).TargetRequests.Should().ContainSingle().Subject;
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact");
        req.Description.Should().Contain("graveyard");
    }

    // ------------------------------------------------------------------ Resolve

    [Fact]
    public void RecurAbility_Resolve_MovesArtifactFromGraveyardToTopOfLibrary()
    {
        var land = AcademyRuinsFactory.Create(_alice);
        var artifact = GraveyardArtifact();

        // Pre-seed library with a filler so we can verify the artifact lands
        // at index 0 (top of library).
        var filler = new Creature("Filler", "1", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(filler);
        filler.SetZone(ZoneType.Library);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });
        recur.Resolve();

        artifact.Zone.Should().Be(ZoneType.Library,
            "the chosen artifact is moved from graveyard to library on resolve");
        _alice.Zones.Graveyard.ContainsCard(artifact).Should().BeFalse();
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(artifact,
            "the artifact sits at index 0 — top of the library — ahead of the filler");
    }

    [Fact]
    public void RecurAbility_Resolve_NonArtifactTarget_LeftUntouched()
    {
        // CR 608.2b — a non-artifact card in the graveyard is not a legal
        // target and is left in place if somehow supplied.
        var land = AcademyRuinsFactory.Create(_alice);
        var creature = new Creature("Some Creature", "1", 1, 1) { Owner = _alice };
        creature.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { creature } });
        recur.Resolve();

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "a non-artifact card is not a legal target (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RecurAbility_Resolve_TargetNoLongerInGraveyard_NoOps()
    {
        // CR 608.2b — if the chosen card has left the graveyard by resolution
        // it is no longer a legal target; the effect does nothing.
        var land = AcademyRuinsFactory.Create(_alice);
        var artifact = GraveyardArtifact();

        // The artifact leaves the graveyard before resolution.
        _alice.Zones.Graveyard.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Exile);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { artifact } });
        recur.Resolve();

        artifact.Zone.Should().Be(ZoneType.Exile,
            "the target left the graveyard before resolution — illegal target, no move (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RecurAbility_Resolve_NoTargetChosen_NoOps()
    {
        var land = AcademyRuinsFactory.Create(_alice);
        var artifact = GraveyardArtifact();

        // No SetChosenTargets call → ChosenTargets is empty.
        var act = () => Recur(land).Resolve();

        act.Should().NotThrow("an ability with no chosen target should no-op without exception");
        artifact.Zone.Should().Be(ZoneType.Graveyard, "nothing should have moved");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
