using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Payment-gate tests for the mana spend-restriction primitive (CR 106.4 —
/// "Some abilities produce mana with a restriction on how that mana can be
/// spent."). Restricted mana (Ancient Ziggurat's "creature spell only",
/// Cavern of Souls' chosen-type rider) must be REJECTED when it would pay a
/// pip on a spell that doesn't satisfy the restriction, and ACCEPTED on a
/// matching spell. This exercises the <see cref="ManaPaymentResolver"/> gate
/// that consumes the per-slot <see cref="ManaProvenanceSlot"/> restriction
/// ledger at spend time, not just the factory-stamp metadata.
/// </summary>
public class ManaSpendRestrictionGateTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeCreature(string name)
        => new(name, manaCost: "G", power: 2, toughness: 2);

    private static Instant MakeInstant(string name)
        => new(name, manaCost: "G");

    [Fact]
    public void Pay_RestrictedMana_OnNonMatchingSpell_IsRejected_SourceNotTapped()
    {
        // Ancient Ziggurat: "{T}: Add one mana of any color. Spend this mana
        // only to cast a creature spell." Paying {G} on an instant must fail.
        var ziggurat = AncientZigguratFactory.Create(_alice);
        ziggurat.SetZone(ZoneType.Battlefield);

        var bolt = MakeInstant("Lightning Helix"); // instant, costs {G} here
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("G"),
            new ManaPayment(new ICard[] { ziggurat }),
            spentOn: bolt,
            out _, out _);

        success.Should().BeFalse("Ziggurat mana can't pay an instant (CR 106.4)");
        ziggurat.IsTapped.Should().BeFalse("rejected payment taps nothing (atomic)");
        _alice.ManaPool.Total.Should().Be(0, "no mana left floating after rejection");
    }

    [Fact]
    public void Pay_RestrictedMana_OnMatchingCreatureSpell_Succeeds()
    {
        var ziggurat = AncientZigguratFactory.Create(_alice);
        ziggurat.SetZone(ZoneType.Battlefield);

        var bear = MakeCreature("Grizzly Bears"); // creature, costs {G} here
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("G"),
            new ManaPayment(new ICard[] { ziggurat }),
            spentOn: bear,
            out _, out _);

        success.Should().BeTrue("Ziggurat mana pays a creature spell");
        ziggurat.IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public void Pay_AbilityActivation_NoSpellContext_RejectsRestrictedMana()
    {
        // CR 106.4 — a restriction limited to "creature spell" can't pay a
        // non-spell context (ability activation cost, spentOn == null).
        var ziggurat = AncientZigguratFactory.Create(_alice);
        ziggurat.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var success = resolver.Pay(
            _alice,
            ManaCost.Parse("G"),
            new ManaPayment(new ICard[] { ziggurat }),
            spentOn: null,
            out _, out _);

        success.Should().BeFalse("creature-spell-only mana can't pay an ability cost");
        ziggurat.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pay_RestrictedMana_DoesNotBlockUnrestrictedManaInSamePool()
    {
        // A Forest's {G} (unrestricted) + Ziggurat's {G} (creature-only) in the
        // pool: paying {G}{G} on an INSTANT must still fail (only one of the two
        // green is spendable), but paying {G} on the instant must succeed using
        // the Forest's unrestricted green — the restricted unit stays unspent.
        var forest = NamedCardFactory.Create("Forest", _alice);
        forest.SetZone(ZoneType.Battlefield);
        var ziggurat = AncientZigguratFactory.Create(_alice);
        ziggurat.SetZone(ZoneType.Battlefield);

        var instant = MakeInstant("Vines of Vastwood");
        var resolver = new ManaPaymentResolver();

        // {G}{G} on an instant: needs both greens, but Ziggurat's is restricted.
        var twoGreen = resolver.Pay(
            _alice,
            ManaCost.Parse("GG"),
            new ManaPayment(new ICard[] { forest, ziggurat }),
            spentOn: instant,
            out _, out _);
        twoGreen.Should().BeFalse("only the Forest's green is spendable on an instant");
        ((Permanent)forest).IsTapped.Should().BeFalse("atomic — nothing tapped");
        ziggurat.IsTapped.Should().BeFalse();

        // {G} on the instant: the Forest's unrestricted green covers it.
        var oneGreen = resolver.Pay(
            _alice,
            ManaCost.Parse("G"),
            new ManaPayment(new ICard[] { forest }),
            spentOn: instant,
            out _, out _);
        oneGreen.Should().BeTrue("unrestricted Forest green pays the instant");
    }

    // -----------------------------------------------------------------------
    // Cavern of Souls — chosen-type refinement gate
    // -----------------------------------------------------------------------

    [Fact]
    public void Pay_CavernOfSouls_ChosenType_RejectsWrongSubtypeCreature()
    {
        // Cavern naming Merfolk: its any-color mana pays a Merfolk creature
        // spell but NOT a Goblin creature spell (CR 106.4 chosen-type rider).
        var cavern = CavernOfSoulsFactory.Create(_alice, _ => CardSubtype.Merfolk);
        cavern.SetZone(ZoneType.Battlefield);
        var resolver = new ManaPaymentResolver();

        var merfolk = new Creature("Lord of Atlantis", manaCost: "U", power: 2, toughness: 2,
            supertypes: null, subtypes: new[] { CardSubtype.Merfolk });
        var goblin = new Creature("Goblin Guide", manaCost: "U", power: 2, toughness: 2,
            supertypes: null, subtypes: new[] { CardSubtype.Goblin });

        // Goblin: wrong chosen type → rejected, land stays untapped.
        var goblinPay = resolver.Pay(
            _alice, ManaCost.Parse("U"),
            new ManaPayment(new ICard[] { cavern }),
            spentOn: goblin, out _, out _);
        goblinPay.Should().BeFalse("Cavern named Merfolk can't pay a Goblin spell");
        ((Permanent)cavern).IsTapped.Should().BeFalse();

        // Merfolk: chosen type matches → succeeds.
        var merfolkPay = resolver.Pay(
            _alice, ManaCost.Parse("U"),
            new ManaPayment(new ICard[] { cavern }),
            spentOn: merfolk, out _, out _);
        merfolkPay.Should().BeTrue("Cavern named Merfolk pays a Merfolk spell");
        ((Permanent)cavern).IsTapped.Should().BeTrue();
    }
}
