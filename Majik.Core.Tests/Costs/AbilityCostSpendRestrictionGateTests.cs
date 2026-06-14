using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 106.4 — spend-restriction enforcement on the ABILITY-ACTIVATION cost path
/// (the deferral half this slice pays down). The spell-cast path already gates
/// restricted mana via <see cref="ManaPaymentResolver"/>
/// (<see cref="ManaSpendRestrictionGateTests"/> /
/// <see cref="SpendRestrictionProvenanceGateTests"/>), but an ability's mana
/// cost was paid through <see cref="ManaCostCost"/> →
/// <see cref="Player.PayMana"/> straight from the bucketed pool, NEVER consulting
/// the per-slot provenance ledger. So:
/// <list type="bullet">
/// <item>floating restricted mana (Eldrazi Temple's {C}{C}, Cavern's chosen
/// colour) could ILLEGALLY pay an arbitrary ability cost; and</item>
/// <item>Sunken Citadel's "spend only to activate abilities of land sources" /
/// Eldrazi Temple's "or activate abilities of Eldrazi" half could never be
/// POSITIVELY satisfied (no ability-spend context reached the restriction).</item>
/// </list>
/// This fixture pins both directions through the new ability-spend context.
/// </summary>
public class AbilityCostSpendRestrictionGateTests
{
    private readonly Player _alice = new("Alice", 20);

    // A land carrying a {T}: deal 1 damage style activated ability with a
    // {1} mana cost, used as the cost-payment subject.
    private static Land MakeLandWithManaAbilityCost()
    {
        var land = new Land("Test Sink Land");
        // A trivial activated ability costing {1} (mana only). We don't care
        // about its effect — only whether the {1} can be paid.
        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: land.Owner!,
            costs: new ICost[] { new ManaCostCost("1") }));
        return land;
    }

    // -----------------------------------------------------------------------
    // Eldrazi Temple — floating {C}{C} restricted to "Eldrazi spells or
    // abilities of Eldrazi" must NOT pay a non-Eldrazi ability's cost.
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziTempleColorless_CannotPay_NonEldraziAbilityCost()
    {
        // Float Eldrazi Temple's restricted {C}{C} into the pool with its rider.
        var restriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi));
        _alice.AddManaToPool(
            ManaCost.Parse("CC"),
            provenanceSource: "eldrazi-temple",
            restriction: restriction);

        _alice.ManaPool.Colorless.Should().Be(2, "the restricted {C}{C} is floating");

        // A non-Eldrazi source's ability costing {1}: the restricted colorless
        // may not pay it (no Eldrazi context).
        var nonEldraziSource = new Artifact("Mishra's Bauble", manaCost: "0");
        var ctx = ManaSpendContext.ForAbilityCost(nonEldraziSource);

        var cost = new ManaCostCost("1");
        cost.CanPay(_alice, ctx).Should().BeFalse(
            "Eldrazi-restricted {C} can't pay a non-Eldrazi ability cost (CR 106.4)");
    }

    [Fact]
    public void EldraziTempleColorless_CanPay_EldraziAbilityCost()
    {
        var restriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi),
            abilityCtx => abilityCtx.SourceHasSubtype(CardSubtype.Eldrazi));
        _alice.AddManaToPool(
            ManaCost.Parse("CC"),
            provenanceSource: "eldrazi-temple",
            restriction: restriction);

        var eldraziSource = new Creature(
            "Endless One", manaCost: "X", power: 0, toughness: 0,
            supertypes: null, subtypes: new[] { CardSubtype.Eldrazi });
        var ctx = ManaSpendContext.ForAbilityCost(eldraziSource);

        var cost = new ManaCostCost("1");
        cost.CanPay(_alice, ctx).Should().BeTrue(
            "the {C}{C} may pay an Eldrazi source's ability cost (CR 106.4)");

        cost.Pay(_alice, ctx);
        _alice.ManaPool.Colorless.Should().Be(1, "one {C} paid the {1} ability cost");
    }

    // -----------------------------------------------------------------------
    // Sunken Citadel — double-mana restricted to "abilities of land sources".
    // Predicate denies every SPELL but PERMITS a land source's ability.
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_DoubleMana_PaysLandAbilityCost()
    {
        // Float Sunken Citadel's GG (chosen green) restricted to land abilities.
        var citadel = SunkenCitadelFactory.Create(_alice, ManaColor.Green, replacements: null);
        citadel.SetZone(ZoneType.Battlefield);
        var doubleAbility = citadel.Abilities
            .OfType<ManaAbility>()
            .First(a => a.ManaGenerated.Green == 2);

        _alice.AddManaToPool(
            doubleAbility.ManaGenerated,
            provenanceSource: doubleAbility,
            restriction: doubleAbility.SpendRestriction);
        _alice.ManaPool.Green.Should().Be(2);

        // The mana may pay a LAND source's ability cost.
        var landSource = new Land("Some Utility Land");
        var landCtx = ManaSpendContext.ForAbilityCost(landSource);
        var landCost = new ManaCostCost("G");
        landCost.CanPay(_alice, landCtx).Should().BeTrue(
            "Sunken Citadel mana pays a land source's ability (CR 106.4)");

        // But NOT a creature source's ability cost.
        var creatureSource = new Creature("Llanowar Elves", manaCost: "G", power: 1, toughness: 1);
        var creatureCtx = ManaSpendContext.ForAbilityCost(creatureSource);
        var creatureCost = new ManaCostCost("G");
        creatureCost.CanPay(_alice, creatureCtx).Should().BeFalse(
            "Sunken Citadel mana can't pay a non-land source's ability");
    }

    [Fact]
    public void SunkenCitadel_DoubleMana_CannotPay_PlainAbilityCost_NoContext()
    {
        // With NO spend context (the legacy ManaCostCost.Pay(Player) path), the
        // land-only restricted mana is treated as unavailable — it can't pay an
        // arbitrary ability cost.
        var citadel = SunkenCitadelFactory.Create(_alice, ManaColor.Green, replacements: null);
        citadel.SetZone(ZoneType.Battlefield);
        var doubleAbility = citadel.Abilities
            .OfType<ManaAbility>()
            .First(a => a.ManaGenerated.Green == 2);
        _alice.AddManaToPool(
            doubleAbility.ManaGenerated,
            provenanceSource: doubleAbility,
            restriction: doubleAbility.SpendRestriction);

        var cost = new ManaCostCost("G");
        cost.CanPay(_alice, ManaSpendContext.None).Should().BeFalse(
            "no ability context ⇒ land-only mana is unavailable");
    }

    // -----------------------------------------------------------------------
    // Unrestricted mana is unaffected — back-compat with the bucketed path.
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // End-to-end through AbilityActivator (the prod activation path): the gate
    // is enforced where the ability's mana cost is actually paid.
    // -----------------------------------------------------------------------

    [Fact]
    public void AbilityActivator_RejectsActivation_WhenOnlyRestrictedManaFloating_NonMatchingSource()
    {
        // Float Eldrazi Temple's {C}{C} (Eldrazi-only) — the only mana available.
        var restriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi),
            ctx => ctx.SourceHasSubtype(CardSubtype.Eldrazi));
        _alice.AddManaToPool(
            ManaCost.Parse("CC"),
            provenanceSource: "eldrazi-temple",
            restriction: restriction);

        // A NON-Eldrazi artifact with a {1} activated ability.
        var artifact = new Artifact("Mind Stone", manaCost: "2");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        var ability = new ActivatedAbility(
            source: artifact,
            controller: _alice,
            costs: new ICost[] { new ManaCostCost("1") });

        var activator = new Majik.Core.Services.AbilityActivator(
            new Majik.Core.Stack.Stack());

        Action act = () => activator.ActivateAbility(ability, _alice, costs: ability.Costs);
        act.Should().Throw<Majik.Core.Domain.Exceptions.InvalidPlayerActionException>(
            "Eldrazi-restricted {C}{C} can't pay a non-Eldrazi source's ability cost (CR 106.4)");
        _alice.ManaPool.Colorless.Should().Be(2, "atomic — nothing was spent");
    }

    [Fact]
    public void AbilityActivator_AllowsActivation_WhenRestrictedManaMatchesSource()
    {
        var restriction = new SpendRestriction(
            "Eldrazi spell or ability",
            spell => spell.Card.HasSubtype(CardSubtype.Eldrazi),
            ctx => ctx.SourceHasSubtype(CardSubtype.Eldrazi));
        _alice.AddManaToPool(
            ManaCost.Parse("CC"),
            provenanceSource: "eldrazi-temple",
            restriction: restriction);

        // An ELDRAZI creature with a {1} activated ability — its source satisfies
        // the rider, so the {C}{C} may pay it.
        var eldrazi = new Creature(
            "Matter Reshaper", manaCost: "3", power: 3, toughness: 2,
            supertypes: null, subtypes: new[] { CardSubtype.Eldrazi });
        eldrazi.SetOwner(_alice);
        eldrazi.SetController(_alice);
        var ability = new ActivatedAbility(
            source: eldrazi,
            controller: _alice,
            costs: new ICost[] { new ManaCostCost("1") });

        var activator = new Majik.Core.Services.AbilityActivator(
            new Majik.Core.Stack.Stack());

        Action act = () => activator.ActivateAbility(ability, _alice, costs: ability.Costs);
        act.Should().NotThrow("the {C}{C} pays an Eldrazi source's ability cost (CR 106.4)");
        _alice.ManaPool.Colorless.Should().Be(1, "one {C} paid the {1} cost");
    }

    [Fact]
    public void UnrestrictedMana_PaysAbilityCost_AnyContext()
    {
        _alice.AddManaToPool(ManaCost.Parse("G")); // vanilla green
        var cost = new ManaCostCost("G");

        cost.CanPay(_alice).Should().BeTrue("legacy Pay(Player) overload unchanged");
        cost.CanPay(_alice, ManaSpendContext.None).Should().BeTrue();

        cost.Pay(_alice, ManaSpendContext.None);
        _alice.ManaPool.Green.Should().Be(0);
    }
}
