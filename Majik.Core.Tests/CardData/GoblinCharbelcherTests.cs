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
/// single activated ability (real Mirrodin oracle text):
///   "{3}, {T}: Reveal cards from the top of your library until you reveal
///    a land card. Goblin Charbelcher deals damage equal to the number of
///    nonland cards revealed this way to any target. If the revealed land
///    card was a Mountain, Goblin Charbelcher deals double that damage
///    instead. Put the revealed cards on the bottom of your library in any
///    order."
///
/// Covers:
/// - Identity (Artifact, {4}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability shape: {3} mana + {T} + 1..1 "any target" request.
/// - Reveal terminates on the first LAND; nonlands stack damage.
/// - Mountain-terminator doubling clause.
/// - A non-Mountain terminating land does not double.
/// - Landless library → reveal walks to exhaustion, damage = whole library.
/// - Empty library → reveal exits cleanly, zero damage.
///
/// 2026-06-13 (Belcher Phase B): the prior assertions encoded an INVERTED
/// implementation (reveal-until-nonland, damage = land count) — the opposite
/// of the printed card, which silently killed the Belcher combo (a landless
/// library dealt 0). Rewritten to the real oracle (reveal-until-land, damage
/// = nonland count).
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
    // Resolution — reveal-until-LAND + damage = nonland count to target
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveBelch_RevealsUntilLand_DealsDamageEqualToNonlandCount()
    {
        // Library top → bottom: Nonland, Nonland, Nonland, Forest, Nonland
        // Reveal stops at the first LAND (the Forest); 3 nonlands revealed
        // before it → 3 damage. The terminating land is a Forest (not a
        // Mountain), so no doubling.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Forest,   land: true),
            (kind: LandKind.Nonland,  land: false),
        });

        // Seed the RNG so the random-bottom step is deterministic.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1234));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.NonlandCount.Should().Be(3);
        result.RevealedLandIsMountain.Should().BeFalse();
        result.Damage.Should().Be(3);
        _bob.LifeTotal.Should().Be(17);

        // 4 cards revealed (3 nonlands + 1 land terminator); the 5th card
        // is still on top of the library (untouched).
        result.Revealed.Should().HaveCount(4);
        _alice.Zones.Library.Count.Should().Be(5,
            "all four revealed cards were bottomed; one untouched card remains plus four bottomed = five total");
    }

    [Fact]
    public void ResolveBelch_MountainTerminator_DoublesDamage()
    {
        // 3 nonlands, then a Mountain terminator. 3 nonlands revealed and the
        // revealed land was a Mountain → 3 × 2 = 6 damage.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Mountain, land: true),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.NonlandCount.Should().Be(3);
        result.RevealedLandIsMountain.Should().BeTrue("the terminating land is a Mountain");
        result.Damage.Should().Be(6);
        _bob.LifeTotal.Should().Be(14);
    }

    [Fact]
    public void ResolveBelch_LandlessLibrary_RevealWalksToExhaustion_DamageEqualsLibrary()
    {
        // No land in the deck → reveal exhausts the library; every card is a
        // nonland → damage = whole library (no Mountain terminator → no
        // doubling). 5 nonlands → 5 damage. THIS is the Belcher kill shape.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Nonland, land: false),
            (kind: LandKind.Nonland, land: false),
            (kind: LandKind.Nonland, land: false),
            (kind: LandKind.Nonland, land: false),
            (kind: LandKind.Nonland, land: false),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 7));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.NonlandCount.Should().Be(5);
        result.RevealedLandIsMountain.Should().BeFalse("no land was revealed at all");
        result.Damage.Should().Be(5);
        _bob.LifeTotal.Should().Be(15);

        // All five reveals bottomed; library count is still 5.
        _alice.Zones.Library.Count.Should().Be(5);
    }

    [Fact]
    public void ResolveBelch_FirstCardIsLand_ZeroDamage()
    {
        // First card is a land — zero nonlands revealed before it → 0 damage.
        // Even a Mountain terminator doubles 0 = 0.
        SeedLibrary(_alice, new[]
        {
            (kind: LandKind.Mountain, land: true),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
        });

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 2));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.NonlandCount.Should().Be(0);
        result.RevealedLandIsMountain.Should().BeTrue();
        result.Damage.Should().Be(0, "0 nonlands revealed, 0 × 2 = 0");
        _bob.LifeTotal.Should().Be(20, "no damage was dealt");
    }

    [Fact]
    public void ResolveBelch_EmptyLibrary_NoDamage_NoThrow()
    {
        // Empty library → reveal exits immediately; no damage; no throw.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 99));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, _bob);

        result.NonlandCount.Should().Be(0);
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
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Nonland,  land: false),
            (kind: LandKind.Mountain, land: true),
        });
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 3));

        var result = GoblinCharbelcherFactory.ResolveBelch(_alice, target: null);

        result.NonlandCount.Should().Be(2);
        result.Damage.Should().Be(4, "2 nonlands × Mountain-double = 4 (computed but not dealt)");
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
