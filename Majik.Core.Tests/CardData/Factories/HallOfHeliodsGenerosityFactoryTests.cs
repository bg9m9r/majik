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
/// Tests for Hall of Heliod's Generosity (Theros Beyond Death; reprinted in
/// Duskmourn: House of Horror Commander).
///
/// Legendary Land. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}{W}, {T}: Put target enchantment card from your graveyard on top of
///    your library."
///
/// Covers:
///   - Identity (Legendary Land, "Hall of Heliod's Generosity", owner/controller).
///   - NamedCardFactory dispatch.
///   - {T}: Add {C} mana ability is present (one colourless).
///   - Exactly one non-mana activated ability (the recur).
///   - The recur ability declares a 1..1 "enchantment card in your graveyard"
///     target request.
///   - On resolve with a chosen enchantment in the graveyard: it moves to the
///     top of the library (CR 608, IZone.InsertCardAt(0)).
///   - CR 608.2b illegal-on-resolution rechecks: non-enchantment targets and
///     targets no longer in the graveyard are left untouched.
///   - No target chosen → effect no-ops cleanly.
/// </summary>
[Trait("Color", "W")]
public class HallOfHeliodsGenerosityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Enchantment GraveyardEnchantment(string name = "Some Enchantment")
    {
        var e = new Enchantment(name, "1") { Owner = _alice };
        e.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(e);
        e.SetZone(ZoneType.Graveyard);
        return e;
    }

    private static ActivatedAbility Recur(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single();

    // ------------------------------------------------------------------ Identity

    [Fact]
    public void HallOfHeliodsGenerosity_Identity()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);

        land.Name.Should().Be("Hall of Heliod's Generosity");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Hall of Heliod's Generosity is a Legendary Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HallOfHeliodsGenerosity()
    {
        var card = NamedCardFactory.Create("Hall of Heliod's Generosity", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hall of Heliod's Generosity");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------- Mana ability

    [Fact]
    public void HallOfHeliodsGenerosity_HasColorlessManaAbility()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("land produces exactly {C}")
            .Which.ManaGenerated.Generic.Should().Be(1);
    }

    // --------------------------------------------------------- Recur ability shape

    [Fact]
    public void HallOfHeliodsGenerosity_HasExactlyOneActivatedAbility()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the graveyard-recur ability is the only non-mana activated ability");
    }

    [Fact]
    public void HallOfHeliodsGenerosity_RecurAbility_HasCorrectTargetRequest()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);

        var req = Recur(land).TargetRequests.Should().ContainSingle().Subject;
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("enchantment");
        req.Description.Should().Contain("graveyard");
    }

    // ------------------------------------------------------------------ Resolve

    [Fact]
    public void RecurAbility_Resolve_MovesEnchantmentFromGraveyardToTopOfLibrary()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);
        var ench = GraveyardEnchantment();

        // Pre-seed library with a filler so we can verify the enchantment lands
        // at index 0 (top of library).
        var filler = new Creature("Filler", "1", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(filler);
        filler.SetZone(ZoneType.Library);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        recur.Resolve();

        ench.Zone.Should().Be(ZoneType.Library,
            "the chosen enchantment is moved from graveyard to library on resolve");
        _alice.Zones.Graveyard.ContainsCard(ench).Should().BeFalse();
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(ench,
            "the enchantment sits at index 0 — top of the library — ahead of the filler");
    }

    [Fact]
    public void RecurAbility_Resolve_NonEnchantmentTarget_LeftUntouched()
    {
        // CR 608.2b — a non-enchantment card in the graveyard is not a legal
        // target and is left in place if somehow supplied.
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);
        var creature = new Creature("Some Creature", "1", 1, 1) { Owner = _alice };
        creature.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { creature } });
        recur.Resolve();

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "a non-enchantment card is not a legal target (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RecurAbility_Resolve_TargetNoLongerInGraveyard_NoOps()
    {
        // CR 608.2b — if the chosen card has left the graveyard by resolution
        // it is no longer a legal target; the effect does nothing.
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);
        var ench = GraveyardEnchantment();

        // The enchantment leaves the graveyard before resolution.
        _alice.Zones.Graveyard.RemoveCard(ench);
        ench.SetZone(ZoneType.Exile);

        var recur = Recur(land);
        recur.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { ench } });
        recur.Resolve();

        ench.Zone.Should().Be(ZoneType.Exile,
            "the target left the graveyard before resolution — illegal target, no move (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void RecurAbility_Resolve_NoTargetChosen_NoOps()
    {
        var land = HallOfHeliodsGenerosityFactory.Create(_alice);
        var ench = GraveyardEnchantment();

        // No SetChosenTargets call → ChosenTargets is empty.
        var act = () => Recur(land).Resolve();

        act.Should().NotThrow("an ability with no chosen target should no-op without exception");
        ench.Zone.Should().Be(ZoneType.Graveyard, "nothing should have moved");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
