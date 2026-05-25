using FluentAssertions;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Mana;

/// <summary>
/// Unit tests for the spend-restriction mana primitive
/// (<see cref="ManaTag"/> + <see cref="SpendRestriction"/>). The primitive
/// ships in two pieces:
///
///   1. <b>Data type</b> (this file): the value-object surface that
///      factories use to stamp a spend-restriction on the
///      <see cref="Majik.Core.Abilities.ManaAbility"/> that generates the
///      mana.
///   2. <b>Payment-gate wiring</b> (DEFERRED): the
///      <see cref="Majik.Core.ValueObjects.ManaPool"/> internals + the
///      <see cref="Majik.Core.Costs.ManaPaymentResolver"/> filter that
///      will actually reject tagged mana when paying a non-matching
///      spell. Today's pool stores bucketed colour counts, no per-slot
///      provenance — flipping that surface is a separate slice. Until
///      then, restrictions live as observational metadata on the
///      ability. These tests pin the data-type contract so the wiring
///      slice can land later without breaking factory call-sites.
/// </summary>
public class SpendRestrictionTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // SpendRestriction construction + invariants
    // -----------------------------------------------------------------------

    [Fact]
    public void SpendRestriction_Description_StoredVerbatim()
    {
        var r = new SpendRestriction("creature spell", _ => true);

        r.Description.Should().Be("creature spell");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SpendRestriction_RejectsBlankDescription(string? blank)
    {
        var act = () => new SpendRestriction(blank!, _ => true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SpendRestriction_RejectsNullPredicate()
    {
        var act = () => new SpendRestriction("creature spell", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SpendRestriction_NullSpell_ReturnsFalse()
    {
        var r = new SpendRestriction("anything", _ => true);

        r.SatisfiedBy(null).Should().BeFalse("a null spell has no provenance — restriction can't permit it");
    }

    // -----------------------------------------------------------------------
    // SpendRestriction.SatisfiedBy — the two canonical predicates used by
    // Cavern of Souls + Eldrazi Temple.
    // -----------------------------------------------------------------------

    [Fact]
    public void CavernPredicate_PermitsCreatureSpell()
    {
        var creatureSpell = MakeSpell(MakeCreature("Tarmogoyf"));
        var r = new SpendRestriction(
            "creature spell",
            spell => spell.Card.HasType(CardType.Creature));

        r.SatisfiedBy(creatureSpell).Should().BeTrue();
    }

    [Fact]
    public void CavernPredicate_RejectsNonCreatureSpell()
    {
        var instantSpell = MakeSpell(new Instant("Lightning Bolt", "R"));
        var r = new SpendRestriction(
            "creature spell",
            spell => spell.Card.HasType(CardType.Creature));

        r.SatisfiedBy(instantSpell).Should().BeFalse(
            "Cavern of Souls mana cannot pay for Lightning Bolt");
    }

    [Fact]
    public void EldraziPredicate_PermitsEldraziSpell()
    {
        var endlessOne = MakeCreature("Endless One", subtypes: new[] { CardSubtype.Eldrazi });
        var spell = MakeSpell(endlessOne);

        var r = new SpendRestriction(
            "Eldrazi spell or ability",
            s => s.Card.HasSubtype(CardSubtype.Eldrazi));

        r.SatisfiedBy(spell).Should().BeTrue();
    }

    [Fact]
    public void EldraziPredicate_RejectsNonEldraziCard()
    {
        // Karn Liberated — colorless planeswalker, NOT an Eldrazi.
        // Eldrazi Temple's {C}{C} ability cannot pay any pip of Karn's
        // cost; the cast has to come from untagged colorless mana.
        var karn = new Card(
            "Karn Liberated",
            manaCost: "7",
            cardTypes: new[] { CardType.Planeswalker });
        var spell = MakeSpell(karn);

        var r = new SpendRestriction(
            "Eldrazi spell or ability",
            s => s.Card.HasSubtype(CardSubtype.Eldrazi));

        r.SatisfiedBy(spell).Should().BeFalse(
            "Karn isn't an Eldrazi — Eldrazi Temple mana can't pay its cost");
    }

    // -----------------------------------------------------------------------
    // ManaTag — pairs a ManaColor with an optional restriction.
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaTag_UntaggedMana_AlwaysSpendable()
    {
        var tag = new ManaTag(ManaColor.Red);

        tag.Restriction.Should().BeNull();
        tag.CanSpendOn(MakeSpell(new Instant("Lightning Bolt", "R"))).Should().BeTrue();
        tag.CanSpendOn(MakeSpell(MakeCreature("Tarmogoyf"))).Should().BeTrue();
    }

    [Fact]
    public void ManaTag_Tagged_DelegatesToRestriction()
    {
        var tag = new ManaTag(
            ManaColor.Colorless,
            new SpendRestriction("creature spell", s => s.Card.HasType(CardType.Creature)));

        tag.CanSpendOn(MakeSpell(MakeCreature("Tarmogoyf"))).Should().BeTrue();
        tag.CanSpendOn(MakeSpell(new Instant("Lightning Bolt", "R"))).Should().BeFalse();
    }

    [Fact]
    public void ManaTag_Equality_AccountsForColorAndRestriction()
    {
        Func<ISpell, bool> p = s => s.Card.HasType(CardType.Creature);
        var a = new ManaTag(ManaColor.Red, new SpendRestriction("creature", p));
        var b = new ManaTag(ManaColor.Red, new SpendRestriction("creature", p));
        var differentColor = new ManaTag(ManaColor.Blue, new SpendRestriction("creature", p));

        a.Should().Be(b);
        a.Should().NotBe(differentColor);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Majik.Core.Spells.Spell MakeSpell(ICard card) => new(card, _alice);

    private static Creature MakeCreature(string name, IEnumerable<CardSubtype>? subtypes = null)
        => new(name, manaCost: "1G", power: 2, toughness: 2, supertypes: null, subtypes: subtypes);
}
