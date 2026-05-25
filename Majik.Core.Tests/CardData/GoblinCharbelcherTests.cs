using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GoblinCharbelcherFactory"/> — Artifact {4} with a
/// single activated ability:
///   "{3}, {T}: Reveal cards from the top of your library until you reveal
///    a nonland card. Goblin Charbelcher deals damage equal to the number
///    of land cards revealed this way to any target. If all revealed
///    cards are Mountains, double that damage. Then put the revealed
///    cards on the bottom of your library in a random order."
///
/// Covers:
/// - Identity (Artifact, {4}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability shape: {3} mana + {T} + 1..1 "any target" request.
/// - Reveal terminates on the first nonland; lands stack damage.
/// - All-Mountains doubling clause.
/// - Nonland mixed in disables the doubling.
/// - Revealed cards bottom in deterministic order under a seeded RNG.
/// - Empty library + no nonland (mono-Mountain pile) — reveal exits
///   cleanly and the doubling applies.
/// </summary>
public class GoblinCharbelcherTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinCharbelcher_IsArtifact_WithFourManaCost()
    {
        var belcher = GoblinCharbelcherFactory.Create(_alice);

        belcher.HasType(CardType.Artifact).Should().BeTrue();
        belcher.Name.Should().Be("Goblin Charbelcher");
        belcher.ManaCost.Should().Be("{4}");
        belcher.Owner.Should().BeSameAs(_alice);
        belcher.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinCharbelcher()
    {
        var card = NamedCardFactory.Create("Goblin Charbelcher", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Goblin Charbelcher");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinCharbelcher_HasOneActivatedAbility_With_3Mana_Tap_AndOneAnyTarget()
    {
        var belcher = GoblinCharbelcherFactory.Create(_alice);
        var activated = belcher.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1);

        var belch = activated[0];
        belch.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("3"),
                "the belch costs {3}");
        belch.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the belch costs {T}");

        belch.TargetRequests.Should().HaveCount(1);
        belch.TargetRequests[0].MinTargets.Should().Be(1);
        belch.TargetRequests[0].MaxTargets.Should().Be(1);
        belch.TargetRequests[0].Description.Should().Contain("any target");
    }

    // -----------------------------------------------------------------------
    // Resolution — reveal-until-nonland + damage to target
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveBelch_RevealsUntilNonland_DealsDamageEqualToLandCount()
    {
        // Library top → bottom: Land, Land, Land, Nonland, Land
        // Reveal stops at the first nonland; 3 lands revealed → 3 damage.
        // Lands are not all Mountains (we use Forests), so no doubling.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Forest,   land: true),
            (kind: LandKind.Forest,   land: true),
            (kind: LandKind.Forest,   land: true),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Forest,   land: true),
        });

        // Seed the RNG so the random-bottom step is deterministic.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1234));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.LandCount.Should().Be(3);
        result.AllMountains.Should().BeFalse();
        result.Damage.Should().Be(3);
        _bob.LifeTotal.Should().Be(17);

        // 4 cards revealed (3 lands + 1 nonland terminator); the 5th land
        // is still on top of the library (untouched).
        result.Revealed.Should().HaveCount(4);
        _alice.Zones.Library.Count.Should().Be(5,
            "all four revealed cards were bottomed; one untouched land remains plus four bottomed = five total");
    }

    [Fact]
    public void ResolveBelch_AllMountains_DoublesDamage()
    {
        // 5 Mountains, then a nonland terminator. 5 lands revealed.
        // BUT a nonland appears (not a Mountain) — so allMountains
        // flips false. To exercise the doubling, the reveal must exit by
        // exhausting the library with no nonland (a mono-Mountain library).
        // First test the no-doubling path, then a separate test the pure
        // doubling case.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Nonland,  land: false),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.LandCount.Should().Be(3);
        result.AllMountains.Should().BeFalse("the nonland terminator is not a Mountain");
        result.Damage.Should().Be(3);
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void ResolveBelch_MonoMountainLibrary_AllMountainsDoubles_RevealEndsByExhaustion()
    {
        // No nonland in the deck → reveal exhausts the library, every
        // revealed card was a Mountain → doubling applies.
        // 5 Mountains → 5 lands × 2 = 10 damage.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 7));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.LandCount.Should().Be(5);
        result.AllMountains.Should().BeTrue("library is all Mountains; reveal exited on library-empty");
        result.Damage.Should().Be(10);
        _bob.LifeTotal.Should().Be(10);

        // All five reveals bottomed; library count is still 5.
        _alice.Zones.Library.Count.Should().Be(5);
    }

    [Fact]
    public void ResolveBelch_FirstCardIsNonland_ZeroDamage_DoublingDoesNotApply()
    {
        // First card is a nonland — zero lands revealed → 0 damage; the
        // nonland terminator flips allMountains false (a nonland is not a
        // Mountain). 0 × 1 = 0.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 2));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.LandCount.Should().Be(0);
        result.AllMountains.Should().BeFalse();
        result.Damage.Should().Be(0);
        _bob.LifeTotal.Should().Be(20, "no damage was dealt");
    }

    [Fact]
    public void ResolveBelch_EmptyLibrary_NoDamage_NoThrow()
    {
        // Empty library → reveal exits immediately; no damage; no throw.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 99));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.LandCount.Should().Be(0);
        result.Damage.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void ResolveBelch_NoTarget_StillRevealsAndBottoms_NoDamageDealt()
    {
        // No target supplied (illegal at resolution path — CR 608.2b):
        // damage step is skipped but the reveal + bottom still runs since
        // the cost was paid.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Nonland,  land: false),
        });
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 3));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, target: null);

        result.LandCount.Should().Be(2);
        result.Damage.Should().Be(2);
        _bob.LifeTotal.Should().Be(20);
        _alice.LifeTotal.Should().Be(20);
        _alice.Zones.Library.Count.Should().Be(3, "all three reveals were bottomed");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private enum LandKind { Mountain, Forest, Nonland }

    private void SeedLibrary(Player player, (LandKind kind, bool land)[] cards)
    {
        // Seed in the order listed — index 0 is the TOP of the library.
        // `Zone.AddCard` appends, which is the bottom; we use
        // `InsertCardAt(0)` semantics via prepend.
        for (int i = 0; i < cards.Length; i++)
        {
            var c = MakeCard(cards[i].kind, cards[i].land);
            c.SetOwner(player);
            c.SetController(player);
            // Append in order: index 0 ends up on top via the AddCard /
            // GetCards.FirstOrDefault contract used in the factory.
            // Zone.AddCard appends to the end; GetCards.FirstOrDefault
            // returns index 0 of the internal list, which is the FIRST
            // added. So adding in declared order means cards[0] is on
            // top, exactly what we want.
            player.Zones.Library.AddCard(c);
        }
    }

    private ICard MakeCard(LandKind kind, bool land)
    {
        if (land)
        {
            var subtype = kind == LandKind.Mountain
                ? CardSubtype.Mountain
                : CardSubtype.Forest;
            return new Land(
                name: kind.ToString(),
                supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { subtype });
        }
        // Plain nonland — a vanilla Instant works as "nonland" terminator.
        return new Instant("Stop", "{1}");
    }
}
