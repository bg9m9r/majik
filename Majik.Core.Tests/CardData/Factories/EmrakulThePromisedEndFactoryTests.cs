using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EmrakulThePromisedEndFactory"/>
/// (Eldritch Moon, {13}).
///
/// Legendary Creature — Eldrazi 13/13. Oracle text (Scryfall, verified):
///   "This spell costs {1} less to cast for each card type among cards in
///    your graveyard.
///    When you cast this spell, you gain control of target opponent during
///    that player's next turn. After that turn, that player takes an extra
///    turn.
///    Flying, trample, protection from instants"
///
/// Covers the v1-shipped body:
///   - Identity (Legendary Creature — Eldrazi, {13}, 13/13).
///   - Flying + Trample markers attached.
///   - Protection-from-instants predicate rejects instants / accepts
///     non-instants.
///   - Graveyard-card-type cost reduction at 0 / 1 / 5 / all-eight distinct
///     types (one {1} per distinct card type, bounded by the eight types,
///     floor at zero).
///   - Duplicate card types in graveyard only count once.
///
/// The on-cast take-control trigger (CR 720 "Controlling Another Player" /
/// Mindslaver) is deliberately deferred — no ControlPlayer primitive exists
/// in the engine — so there is no cast-trigger test here (mirrors the same
/// gap documented on <see cref="MindslaverFactory"/>).
/// </summary>
public class EmrakulThePromisedEndFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_Identity()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);

        emrakul.Name.Should().Be("Emrakul, the Promised End");
        emrakul.ManaCost.Should().Be("{13}");
        emrakul.HasType(CardType.Creature).Should().BeTrue();
        emrakul.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        emrakul.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        emrakul.BasePower.Should().Be(13);
        emrakul.BaseToughness.Should().Be(13);
        emrakul.Owner.Should().BeSameAs(_alice);
        emrakul.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Emrakul_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Emrakul, the Promised End", _alice);

        card.Should().BeOfType<Creature>();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(13);
        ((Creature)card).BaseToughness.Should().Be(13);
    }

    // -----------------------------------------------------------------------
    // Keyword markers
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_HasFlyingAndTrampleMarkers()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        var keywords = emrakul.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flying", "CR 702.9 — Flying");
        keywords.Should().Contain("Trample", "CR 702.19 — Trample");
    }

    // -----------------------------------------------------------------------
    // Protection from instants (CR 702.16)
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_ProtectionFromInstants_RejectsInstantSpell()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        var prot = emrakul.Abilities.OfType<ProtectionAbility>().Single();
        prot.SpellPredicate.Should().NotBeNull();

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);

        prot.SpellPredicate!(boltSpell).Should().BeTrue(
            "Lightning Bolt is an instant — protection from instants applies");
        Protection.HasProtectionFromSpell(emrakul, boltSpell).Should().BeTrue();
    }

    [Fact]
    public void Emrakul_ProtectionFromInstants_AllowsNonInstantSpell()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        var prot = emrakul.Abilities.OfType<ProtectionAbility>().Single();
        prot.SpellPredicate.Should().NotBeNull();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob };
        var bearSpell = new Majik.Core.Spells.Spell(bear, _bob);

        prot.SpellPredicate!(bearSpell).Should().BeFalse(
            "a creature spell is not an instant — protection does not apply");
        Protection.HasProtectionFromSpell(emrakul, bearSpell).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Cost reduction (CR 117.7) — {1} less per card type in graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Emrakul_EmptyGraveyard_PaysFullThirteen()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(emrakul, _alice);

        effective.Generic.Should().Be(13, "no card types in graveyard → no reduction");
    }

    [Fact]
    public void Emrakul_OneCardTypeInGraveyard_ReducesByOne()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        // Two creatures = a single distinct card type (Creature).
        AddToGraveyard(_alice, new Creature("Bear A", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "{1}{G}", 2, 2));

        var effective = CostReduction.GetEffectiveCost(emrakul, _alice);

        effective.Generic.Should().Be(12,
            "one distinct card type (Creature) → {1} reduction, regardless of count");
    }

    [Fact]
    public void Emrakul_FiveDistinctCardTypesInGraveyard_ReducesByFive()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        AddToGraveyard(_alice, new Creature("Bear", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Instant("Bolt", "{R}"));
        AddToGraveyard(_alice, new Sorcery("Divination", "{2}{U}"));
        AddToGraveyard(_alice, new Artifact("Sol Ring", "{1}"));
        AddToGraveyard(_alice, new Enchantment("Pacifism", "{1}{W}"));

        var effective = CostReduction.GetEffectiveCost(emrakul, _alice);

        effective.Generic.Should().Be(8,
            "five distinct card types → {5} reduction: {13} - {5} = {8}");
    }

    [Fact]
    public void Emrakul_AllEightCardTypesInGraveyard_ReducesByEight()
    {
        var emrakul = EmrakulThePromisedEndFactory.Create(_alice);
        AddToGraveyard(_alice, new Artifact("Sol Ring", "{1}"));
        AddToGraveyard(_alice, new Creature("Bear", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Enchantment("Pacifism", "{1}{W}"));
        AddToGraveyard(_alice, new Instant("Bolt", "{R}"));
        AddToGraveyard(_alice, new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }));
        AddToGraveyard(_alice, new Planeswalker("Jace", "{1}{U}{U}", 3));
        AddToGraveyard(_alice, new Sorcery("Divination", "{2}{U}"));
        AddToGraveyard(_alice, new TribalStub());

        var effective = CostReduction.GetEffectiveCost(emrakul, _alice);

        effective.Generic.Should().Be(5,
            "all eight card types → {8} reduction: {13} - {8} = {5}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    /// <summary>
    /// Minimal card carrying the (legacy) Tribal card type so the
    /// all-eight-types assertion can exercise the Tribal branch without a
    /// real Tribal printing in the pool.
    /// </summary>
    private sealed class TribalStub : Card
    {
        public TribalStub() : base("Tribal Stub", "{B}", new[] { CardType.Tribal })
        {
        }
    }
}
