using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SpringbloomDruidFactory"/> — Creature — Elf Druid
/// {2}{G} 1/1 (Modern Horizons 2). Oracle:
///   "When this creature enters, you may sacrifice a land. If you do, search
///    your library for up to two basic land cards, put them onto the
///    battlefield tapped, then shuffle."
///
/// Covers:
///   - Card identity (Creature + Elf/Druid, {2}{G}, 1/1, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: exactly one ETB <see cref="TriggeredAbility"/>, no
///     activated/mana abilities, no target requests.
///   - ETB resolve (no agent => default accept): sacrifices a land + tutors
///     two basics onto the battlefield tapped.
///   - ETB resolve: only one basic in library → tutors that one (still saccs).
///   - ETB resolve: no land to sacrifice → the "if you do" search is skipped,
///     library untouched.
///   - ETB resolve: nonbasics in library are ignored.
/// </summary>
[Trait("Color", "G")]
public class SpringbloomDruidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land MakeBasicInLibrary(string name, CardSubtype subtype)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(_alice);
        _alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);
        return land;
    }

    private Land MakeLandOnBattlefield(string name)
    {
        var land = new Land(name);
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void SpringbloomDruid_IsElfDruid_AtTwoG_OneOne()
    {
        var c = SpringbloomDruidFactory.Create(_alice);

        c.Name.Should().Be("Springbloom Druid");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpringbloomDruid()
    {
        var card = NamedCardFactory.Create("Springbloom Druid", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Springbloom Druid");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void SpringbloomDruid_HasOneEtbTrigger_NoActivatedOrManaAbilities()
    {
        var c = SpringbloomDruidFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EtbTrigger_HasNoTargetRequests()
    {
        var c = SpringbloomDruidFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Etb_SacrificesALand_AndTutorsTwoBasicsToBattlefieldTapped()
    {
        var forest = MakeBasicInLibrary("Forest", CardSubtype.Forest);
        var island = MakeBasicInLibrary("Island", CardSubtype.Island);
        var sacLand = MakeLandOnBattlefield("Bojuka Bog");

        var druid = SpringbloomDruidFactory.Create(_alice);
        var etb = druid.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        // The sacrificed land went to the graveyard (CR 701.16).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(sacLand);
        _alice.Zones.Graveyard.GetCards().Should().Contain(sacLand,
            "you sacrificed a land to pay the optional cost");
        sacLand.Zone.Should().Be(ZoneType.Graveyard);

        // Both basics entered the battlefield tapped.
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(island);
        forest.IsTapped.Should().BeTrue();
        island.IsTapped.Should().BeTrue();
        forest.Zone.Should().Be(ZoneType.Battlefield);
        island.Zone.Should().Be(ZoneType.Battlefield);

        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(island);
    }

    [Fact]
    public void Etb_OnlyOneBasicInLibrary_TutorsThatOne_StillSacrifices()
    {
        var forest = MakeBasicInLibrary("Forest", CardSubtype.Forest);
        var sacLand = MakeLandOnBattlefield("Bojuka Bog");

        var druid = SpringbloomDruidFactory.Create(_alice);
        var etb = druid.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue();
        _alice.Zones.Graveyard.GetCards().Should().Contain(sacLand);
    }

    [Fact]
    public void Etb_NoLandToSacrifice_SearchSkipped_LibraryUntouched()
    {
        // Two basics in library but NO land on the battlefield to sacrifice —
        // the "if you do" clause never fires, so nothing is fetched.
        var forest = MakeBasicInLibrary("Forest", CardSubtype.Forest);
        var island = MakeBasicInLibrary("Island", CardSubtype.Island);

        var druid = SpringbloomDruidFactory.Create(_alice);
        var etb = druid.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Library.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().Contain(island);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(forest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(island);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Etb_NonbasicsInLibraryAreIgnored()
    {
        // Only a nonbasic in library — must not be fetched even though a land
        // is sacrificed.
        var bog = new Land("Urza's Mine");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);
        var sacLand = MakeLandOnBattlefield("Bojuka Bog");

        var druid = SpringbloomDruidFactory.Create(_alice);
        var etb = druid.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        _alice.Zones.Library.GetCards().Should().Contain(bog);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Graveyard.GetCards().Should().Contain(sacLand);
    }
}
