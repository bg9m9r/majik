using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Glarb, Calamity's Augur (Modern Horizons 3, {B}{G}{U}) —
/// Legendary Creature — Frog Wizard Noble 2/4 with Deathtouch, {T}: Surveil 2,
/// and "you may play lands and cast spells with mana value 4 or greater from the
/// top of your library" (CR 601.3e / CR 305.6 / CR 715.4).
///
/// Covers only Glarb's UNIQUE behaviour + a single identity assert. The generic
/// dispatch / well-formedness check is owned by CardFactoryContractTests.
/// </summary>
[Trait("Color", "M")]
public class GlarbCalamitysAugurFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, ContinuousEffectsService effects) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService(bus);
        return (zones, effects);
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    [Fact]
    public void Identity_LegendaryFrogWizardNoble_2_4_AtBGU()
    {
        var glarb = GlarbCalamitysAugurFactory.Create(_alice);

        glarb.Name.Should().Be("Glarb, Calamity's Augur");
        glarb.ManaCost.Should().Be("{B}{G}{U}");
        glarb.HasType(CardType.Creature).Should().BeTrue();
        glarb.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        glarb.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        glarb.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        glarb.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        glarb.BasePower.Should().Be(2);
        glarb.BaseToughness.Should().Be(4);
    }

    [Fact]
    public void HasDeathtouch()
    {
        var glarb = GlarbCalamitysAugurFactory.Create(_alice);

        glarb.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Deathtouch",
                "CR 702.2 — Glarb has Deathtouch");

        CombatAbilities.HasDeathtouch(glarb).Should().BeTrue(
            "CR 702.2 — the Deathtouch marker is consumed by CombatAbilities");
    }

    [Fact]
    public void HasSingleTapSurveil2ActivatedAbility()
    {
        var glarb = GlarbCalamitysAugurFactory.Create(_alice);

        var activated = glarb.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not IManaAbility)
            .ToList();
        activated.Should().ContainSingle("the only activated ability is {T}: Surveil 2");
        activated[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the activation cost is {T}");
        activated[0].TargetRequests.Should().BeEmpty(
            "surveil targets nothing — the controller's own library");
    }

    [Fact]
    public void OnBattlefield_TopExpensiveSpell_IsCastable_AndRevealed()
    {
        // CR 601.3e — a nonland spell with mana value 4+ on top is castable.
        var (zones, effects) = BuildEngine();
        var glarb = GlarbCalamitysAugurFactory.Create(_alice, effects);
        var bigSpell = new Sorcery("Big Spell", "{2}{B}{B}"); // MV 4
        bigSpell.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bigSpell);
        bigSpell.SetZone(ZoneType.Library);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bigSpell).Should().BeFalse(
            "the grant only exists while Glarb is on the battlefield");

        EnterBattlefield(zones, _alice, glarb);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bigSpell).Should().BeTrue(
            "CR 601.3e — a MV4 spell on top is castable under Glarb's grant");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue(
            "CR 715.4 — Glarb may look at the top card any time (revealed)");
    }

    [Fact]
    public void OnBattlefield_TopCheapSpell_NotCastable()
    {
        // CR 202.3 — a spell with mana value < 4 stays uncastable from the top.
        var (zones, effects) = BuildEngine();
        var glarb = GlarbCalamitysAugurFactory.Create(_alice, effects);
        var cheap = new Instant("Lightning Bolt", "{R}"); // MV 1
        cheap.SetOwner(_alice);
        _alice.Zones.Library.AddCard(cheap);
        cheap.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, glarb);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, cheap).Should().BeFalse(
            "the mana-value-4 floor excludes a MV1 spell");
    }

    [Fact]
    public void OnBattlefield_TopOrdinaryLand_NotPlayable()
    {
        // CR 202.3 — an ordinary land is mana value 0, below the MV4 floor, so it
        // is NOT playable from the top under Glarb's grant (unlike Oracle of Mul
        // Daya, whose Lands grant carries no mana-value restriction).
        var (zones, effects) = BuildEngine();
        var glarb = GlarbCalamitysAugurFactory.Create(_alice, effects);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest }); // MV 0
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, glarb);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeFalse(
            "CR 202.3 — an ordinary land is MV0, below Glarb's mana-value-4 floor");
    }

    [Fact]
    public void HasManaValue4OrGreater_Predicate_GatesAtFour()
    {
        var mv4 = new Sorcery("MV4", "{2}{B}{B}");
        var mv3 = new Sorcery("MV3", "{1}{B}{B}");

        GlarbCalamitysAugurFactory.HasManaValue4OrGreater(mv4).Should().BeTrue();
        GlarbCalamitysAugurFactory.HasManaValue4OrGreater(mv3).Should().BeFalse();
    }

    [Fact]
    public void LookAtTopOfLibrary_ReturnsTopCard_OrNullWhenEmpty()
    {
        GlarbCalamitysAugurFactory.LookAtTopOfLibrary(_alice).Should().BeNull(
            "an empty library has no top card");

        var top = new Card("Top", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        GlarbCalamitysAugurFactory.LookAtTopOfLibrary(_alice).Should().BeSameAs(top);
    }
}
