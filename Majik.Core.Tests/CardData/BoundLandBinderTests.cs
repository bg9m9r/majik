using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Behavioural verification of the land binders that close the prod-broken
/// lands flagged Stub / MissingTrigger by the pool-wide audit. Each card is
/// driven through the SAME binder the production
/// <c>GameFacade.BindCardAbilities</c> chain runs, against the REAL oracle text
/// from <see cref="EmbeddedCardRepository"/> — lands are never routed through
/// their [CardName] factory in prod, so the binder is the only live path.
/// </summary>
public class BoundLandBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EmbeddedCardRepository _repo = new();

    private Land MakeLandShell(string name)
    {
        var entity = _repo.GetByName(name);
        entity.Should().NotBeNull($"{name} should exist in the embedded pool");
        var parsed = TypeLineParser.Parse(entity!.TypeLine);
        var land = new Land(name, parsed.Supertypes, parsed.Subtypes);
        land.SetOwner(_alice);
        land.SetController(_alice);
        return land;
    }

    private static Land AddBasic(Player p, string name, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(p);
        land.SetController(p);
        land.AddAbility(new ManaAbility(land, p, ManaCost.Parse(ColorFor(subtype))));
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static string ColorFor(CardSubtype s) => s switch
    {
        CardSubtype.Forest => "G",
        CardSubtype.Mountain => "R",
        CardSubtype.Island => "U",
        CardSubtype.Swamp => "B",
        CardSubtype.Plains => "W",
        _ => "C",
    };

    // -----------------------------------------------------------------------
    // Reflecting Pool — six dynamic mana abilities gated on producible types.
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectingPool_BindsSixManaAbilities_GatedOnControllerProducibleTypes()
    {
        var pool = MakeLandShell("Reflecting Pool");
        OracleManaBinder.Bind(pool, _repo.GetByName("Reflecting Pool")!, _alice);
        _alice.Zones.Battlefield.AddCard(pool);
        pool.SetZone(ZoneType.Battlefield);

        pool.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "WUBRG + {C}, one dynamically-gated ManaAbility each");

        // No other land yet → no type is producible → nothing can be activated.
        pool.Abilities.OfType<ManaAbility>().Where(a => a.CanActivate())
            .Should().BeEmpty("no other land seeds any producible type");

        // Add a Forest → only the {G} ability becomes legal.
        AddBasic(_alice, "Forest", CardSubtype.Forest);
        var live = pool.Abilities.OfType<ManaAbility>().Where(a => a.CanActivate()).ToList();
        live.Should().ContainSingle("only {G} is producible via the Forest");
        live[0].ManaGenerated.ToString().Should().Be(ManaCost.Parse("G").ToString());
    }

    [Fact]
    public void ReflectingPool_DoesNotSeedItself_OrAnotherPool()
    {
        var pool = MakeLandShell("Reflecting Pool");
        OracleManaBinder.Bind(pool, _repo.GetByName("Reflecting Pool")!, _alice);
        _alice.Zones.Battlefield.AddCard(pool);
        pool.SetZone(ZoneType.Battlefield);

        var pool2 = MakeLandShell("Reflecting Pool");
        OracleManaBinder.Bind(pool2, _repo.GetByName("Reflecting Pool")!, _alice);
        _alice.Zones.Battlefield.AddCard(pool2);
        pool2.SetZone(ZoneType.Battlefield);

        // Two Pools alone produce nothing (circularity broken).
        pool.Abilities.OfType<ManaAbility>().Where(a => a.CanActivate())
            .Should().BeEmpty("a Pool never seeds itself or another Pool");
    }

    // -----------------------------------------------------------------------
    // Boseiju, Who Shelters All — {T}, Pay 2 life: Add {C}.
    // -----------------------------------------------------------------------

    [Fact]
    public void BoseijuWhoSheltersAll_BindsPayTwoLifeColorlessMana()
    {
        var land = MakeLandShell("Boseiju, Who Shelters All");
        OracleManaBinder.Bind(land, _repo.GetByName("Boseiju, Who Shelters All")!, _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Generic.Should().Be(1, "{C} parses as one generic pip");

        // CR 119.4 — payable while life > 2.
        mana.CanActivate().Should().BeTrue();
        mana.Activate();
        _alice.LifeTotal.Should().Be(18, "paid 2 life as part of the activation cost");

        // At <= 2 life the ability can't be activated (untap + drop to 2 first).
        land.Untap();
        _alice.LoseLife(16); // 18 → 2
        _alice.LifeTotal.Should().Be(2);
        mana.CanActivate().Should().BeFalse("CR 119.4 — can't pay 2 life at 2 life");
    }

    // -----------------------------------------------------------------------
    // Sunken Citadel — chosen-color one + restricted double mana abilities.
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_BindsChosenColorManaPlusRestrictedDouble()
    {
        var land = MakeLandShell("Sunken Citadel");
        OracleManaBinder.Bind(land, _repo.GetByName("Sunken Citadel")!, _alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2,
            "one chosen-colour single-pip + one chosen-colour double-pip " +
            "(CR 614.12 — exactly the printed single chosen colour, not five WUBRG)");

        // One single-pip ability (the chosen colour, dynamic).
        var singles = mana.Where(a => a.ManaGenerated.TotalValue == 1).ToList();
        singles.Should().ContainSingle();
        singles[0].SpendRestriction.Should().BeNull(
            "only the double-mana ability carries the land-source spend rider");

        // One double-pip ability carrying the land-ability-only rider.
        var doubles = mana.Where(a => a.ManaGenerated.TotalValue == 2).ToList();
        doubles.Should().ContainSingle();
        doubles[0].SpendRestriction.Should().NotBeNull(
            "the double-mana ability is land-ability spend-restricted (CR 106.4)");
    }

    [Fact]
    public void SunkenCitadel_ChosenColorAbilities_TrackTheEtbColorChoice()
    {
        // CR 614.12 — the chosen-colour holder drives BOTH abilities. Default
        // (pre-choice) is White; stamping the holder (what the ETB
        // ChooseColorReplacement does) flips both the single and double pip.
        var land = MakeLandShell("Sunken Citadel");
        OracleManaBinder.Bind(land, _repo.GetByName("Sunken Citadel")!, _alice);

        var choice = OracleManaBinder.GetColorChoice(land);
        choice.Should().NotBeNull("a chosen-colour land seeds a ColorChoice holder");

        var single = land.Abilities.OfType<ManaAbility>().Single(a => a.ManaGenerated.TotalValue == 1);
        var dbl = land.Abilities.OfType<ManaAbility>().Single(a => a.ManaGenerated.TotalValue == 2);

        // Default seed: White.
        single.Activate().White.Should().Be(1, "default chosen colour is White");
        land.Untap();

        // Stamp Blue (the ETB choice) → both abilities now produce blue.
        choice!.Choose(Majik.Core.ValueObjects.ManaColor.Blue);
        single.Activate().Blue.Should().Be(1, "single-pip ability tracks the chosen colour");
        land.Untap();
        var two = dbl.Activate();
        two.Blue.Should().Be(2, "double-pip ability tracks the chosen colour");
        two.White.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Temple of the Dragon Queen — chosen-color one mana ability.
    // -----------------------------------------------------------------------

    [Fact]
    public void TempleOfTheDragonQueen_BindsChosenColorMana_NoRestrictedDouble()
    {
        var land = MakeLandShell("Temple of the Dragon Queen");
        OracleManaBinder.Bind(land, _repo.GetByName("Temple of the Dragon Queen")!, _alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().ContainSingle(
            "one chosen-colour single-pip ability (CR 614.12 — the printed " +
            "single chosen colour, not five WUBRG); Temple has no double-mana ability");
        mana[0].ManaGenerated.TotalValue.Should().Be(1);
        mana[0].SpendRestriction.Should().BeNull(
            "Temple has no land-ability spend restriction");
    }
}
