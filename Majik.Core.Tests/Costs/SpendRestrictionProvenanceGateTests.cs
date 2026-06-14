using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Payment-gate enforcement for the colorless / land-ability / uncounterable
/// spend-restriction family (CR 106.4) — the deferral
/// <c>per-slot-manapool-spend-restriction-provenance-tag</c>. The colored gate
/// (Ancient Ziggurat, Cavern of Souls) already shipped via
/// <see cref="ManaSpendRestrictionGateTests"/>; this fixture pins the
/// <b>colorless</b> half (Eldrazi Temple's {C}{C}, Sunken Citadel's double
/// chosen-color) and Boseiju, Who Shelters All's "that mana → spell can't be
/// countered" cast-time rider.
/// </summary>
public class SpendRestrictionProvenanceGateTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeEldrazi(string name, string cost)
        => new(name, manaCost: cost, power: 4, toughness: 4,
            supertypes: null, subtypes: new[] { CardSubtype.Eldrazi });

    // -----------------------------------------------------------------------
    // Eldrazi Temple — "{T}: Add {C}{C}. Spend this mana only to cast Eldrazi
    // spells …" (colorless restricted mana).
    // -----------------------------------------------------------------------

    /// <summary>
    /// An isolated land carrying ONLY Eldrazi Temple's restricted "{T}: Add
    /// {C}{C}. Spend this mana only to cast Eldrazi spells" ability, so the
    /// resolver's per-source ability pick is unambiguous (the real Temple's
    /// second, unrestricted {T}: Add {C} ability is a separate selection
    /// concern, not part of this restriction gate). Mirrors the production
    /// stamp on EldraziTempleFactory's {C}{C} ManaAbility.
    /// </summary>
    private Land MakeEldraziDoubleColorlessSource()
    {
        var land = new Land("Eldrazi Temple (restricted ability only)");
        land.SetOwner(_alice);
        land.SetController(_alice);
        var eldraziRestriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi));
        land.AddAbility(new Majik.Core.Abilities.ManaAbility(
            land, _alice, ManaCost.Parse("CC"),
            canActivateCheck: null,
            spendRestriction: eldraziRestriction));
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void EldraziTemple_DoubleColorless_OnNonEldraziSpell_IsRejected_NotTapped()
    {
        var temple = MakeEldraziDoubleColorlessSource();

        // A plain (non-Eldrazi) creature costing {2}: the {C}{C} can't pay it.
        var grizzly = new Creature("Grizzly Bears", manaCost: "2", power: 2, toughness: 2);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice, ManaCost.Parse("2"),
            new ManaPayment(new ICard[] { temple }),
            spentOn: grizzly, out _, out _);

        success.Should().BeFalse("Eldrazi Temple's {C}{C} only pays Eldrazi spells (CR 106.4)");
        temple.IsTapped.Should().BeFalse("atomic — rejected payment taps nothing");
        _alice.ManaPool.Total.Should().Be(0, "no mana left floating after rejection");
    }

    [Fact]
    public void EldraziTemple_DoubleColorless_OnEldraziSpell_Succeeds()
    {
        var temple = MakeEldraziDoubleColorlessSource();

        var eldrazi = MakeEldrazi("Emrakul's Hatcher", "2"); // Eldrazi, costs {2} here
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice, ManaCost.Parse("2"),
            new ManaPayment(new ICard[] { temple }),
            spentOn: eldrazi, out _, out _);

        success.Should().BeTrue("Eldrazi Temple's {C}{C} pays an Eldrazi spell");
        temple.IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public void EldraziTemple_DoubleColorless_OnAbilityActivation_NoSpell_IsRejected()
    {
        // CR 106.4 — "spend only to cast Eldrazi spells" can't pay a non-spell
        // (ability activation) cost: spentOn == null ⇒ no permission.
        var temple = MakeEldraziDoubleColorlessSource();
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice, ManaCost.Parse("2"),
            new ManaPayment(new ICard[] { temple }),
            spentOn: null, out _, out _);

        success.Should().BeFalse("Eldrazi-only colorless mana can't pay an ability cost");
        temple.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void EldraziTemple_SingleColorless_IsUnrestricted_PaysAnything()
    {
        // The {T}: Add {C} ability carries NO rider (matches printed oracle) —
        // its single {C} pays a vanilla {1} spell freely. Use a fresh Temple
        // and a {1} cost (the resolver's greedy pick selects the single-{C}
        // ability for a generic-only cost).
        var temple = EldraziTempleFactory.Create(_alice);
        temple.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var spell = new Sorcery("Lava Spike", manaCost: "1");
        var success = resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(new ICard[] { temple }),
            spentOn: spell, out _, out _);

        success.Should().BeTrue("the unrestricted {T}: Add {C} ability pays any {1} spell");
    }

    // -----------------------------------------------------------------------
    // Sunken Citadel — "{T}: Add two mana of the chosen color. Spend this mana
    // only to activate abilities of land sources." Predicate denies every
    // spell, so the double-mana can never pay a spell pip.
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_DoubleMana_OnAnySpell_IsRejected_NotTapped()
    {
        var citadel = SunkenCitadelFactory.Create(_alice, ManaColor.Green, replacements: null);
        citadel.SetZone(ZoneType.Battlefield);
        // Untap — the JSON identity / production path may enter tapped; for the
        // gate test we want the source available.
        if (((Permanent)citadel).IsTapped) ((Permanent)citadel).Untap();

        var spell = new Creature("Llanowar Elves", manaCost: "GG", power: 1, toughness: 1);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice, ManaCost.Parse("GG"),
            new ManaPayment(new ICard[] { citadel }),
            spentOn: spell, out _, out _);

        success.Should().BeFalse("Sunken Citadel's double-mana pays no spell (land abilities only, CR 106.4)");
        ((Permanent)citadel).IsTapped.Should().BeFalse("atomic — nothing tapped on rejection");
    }

    // -----------------------------------------------------------------------
    // Boseiju, Who Shelters All — "{T}, Pay 2 life: Add {C}. If that mana is
    // spent on an instant or sorcery spell, that spell can't be countered."
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_ManaSpentOnInstant_FlagsCardUncounterable()
    {
        var boseiju = BoseijuWhoSheltersAllFactory.Create(_alice);
        boseiju.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var bolt = new Instant("Lightning Bolt", manaCost: "1");
        bolt.PendingCastUncounterable.Should().BeFalse("clean before the spend");

        var success = resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(new ICard[] { boseiju }),
            spentOn: bolt, out _, out _);

        success.Should().BeTrue("Boseiju's {C} pays the {1} instant cost");
        bolt.PendingCastUncounterable.Should().BeTrue(
            "Boseiju mana spent on an instant marks that spell uncounterable (CR 701.5b)");
    }

    [Fact]
    public void Boseiju_ManaSpentOnSorcery_FlagsCardUncounterable()
    {
        var boseiju = BoseijuWhoSheltersAllFactory.Create(_alice);
        boseiju.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var divination = new Sorcery("Divination", manaCost: "1");

        resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(new ICard[] { boseiju }),
            spentOn: divination, out _, out _);

        divination.PendingCastUncounterable.Should().BeTrue(
            "Boseiju mana spent on a sorcery marks that spell uncounterable");
    }

    [Fact]
    public void Boseiju_ManaSpentOnCreatureSpell_DoesNotFlagUncounterable()
    {
        // The rider is instant/sorcery-only; a creature spell paid with Boseiju
        // mana is NOT marked uncounterable.
        var boseiju = BoseijuWhoSheltersAllFactory.Create(_alice);
        boseiju.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var bear = new Creature("Grizzly Bears", manaCost: "1", power: 2, toughness: 2);

        resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(new ICard[] { boseiju }),
            spentOn: bear, out _, out _);

        bear.PendingCastUncounterable.Should().BeFalse(
            "the rider only triggers for instant/sorcery spells");
    }

    // -----------------------------------------------------------------------
    // Cavern of Souls — "{T}: Add one mana of any color. Spend this mana only
    // to cast a creature spell of the chosen type, and that spell can't be
    // countered." The any-color mana both (a) gates on a chosen-type creature
    // spell (the SpendRestriction, already enforced) AND (b) marks that spell
    // uncounterable (CR 701.5b — the same provenance-reaction seam Boseiju uses).
    // -----------------------------------------------------------------------

    [Fact]
    public void Cavern_ManaSpentOnChosenTypeCreature_FlagsCardUncounterable()
    {
        // Choose "Goblin"; pay a Goblin creature's cost from Cavern's any-color
        // mana → it pays (restriction satisfied) AND is marked uncounterable.
        var cavern = CavernOfSoulsFactory.Create(_alice, _ => CardSubtype.Goblin);
        cavern.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var goblin = new Creature("Goblin Guide", manaCost: "R", power: 2, toughness: 2,
            supertypes: null, subtypes: new[] { CardSubtype.Goblin });
        goblin.PendingCastUncounterable.Should().BeFalse("clean before the spend");

        var success = resolver.Pay(
            _alice, ManaCost.Parse("R"),
            new ManaPayment(new ICard[] { cavern }),
            spentOn: goblin, out _, out _);

        success.Should().BeTrue("Cavern's any-color mana pays a chosen-type creature spell");
        goblin.PendingCastUncounterable.Should().BeTrue(
            "Cavern mana spent on a chosen-type creature spell marks it uncounterable (CR 701.5b)");
    }

    [Fact]
    public void Cavern_NoChosenType_ManaSpentOnAnyCreature_FlagsCardUncounterable()
    {
        // With no ETB type resolved the restriction stays "creature spell"; the
        // uncounterable rider still applies to ANY creature spell the mana pays
        // (the rider keys off the chosen-type-creature spend the same predicate
        // gates — here the broader "creature spell" stand-in).
        var cavern = CavernOfSoulsFactory.Create(_alice);
        cavern.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var bear = new Creature("Grizzly Bears", manaCost: "G", power: 2, toughness: 2);

        var success = resolver.Pay(
            _alice, ManaCost.Parse("G"),
            new ManaPayment(new ICard[] { cavern }),
            spentOn: bear, out _, out _);

        success.Should().BeTrue();
        bear.PendingCastUncounterable.Should().BeTrue(
            "Cavern mana spent on a creature spell marks it uncounterable");
    }

    [Fact]
    public void Cavern_ColorlessMana_DoesNotFlagUncounterable()
    {
        // The {T}: Add {C} ability is UNRESTRICTED and carries no uncounterable
        // rider (printed oracle: the rider is on the any-color ability only).
        // The {C} pays a generic pip on a creature spell but never marks it.
        var cavern = CavernOfSoulsFactory.Create(_alice, _ => CardSubtype.Goblin);
        cavern.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var goblin = new Creature("Goblin Piledriver", manaCost: "1", power: 1, toughness: 2,
            supertypes: null, subtypes: new[] { CardSubtype.Goblin });

        var success = resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(new ICard[] { cavern }),
            spentOn: goblin, out _, out _);

        success.Should().BeTrue("the unrestricted {C} pays a {1} generic pip");
        goblin.PendingCastUncounterable.Should().BeFalse(
            "the {T}: Add {C} ability carries no uncounterable rider");
    }
}
