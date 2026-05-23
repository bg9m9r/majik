using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for Mishra's Workshop (Antiquities).
///
/// Oracle text:
///   "{T}: Add {C}{C}{C}. Spend this mana only to cast artifact spells."
///
/// CR 605 — mana ability (no stack, no targets). CR 106.4 covers the
/// per-mana spend restriction; v1 ships the structural mana amount but
/// defers enforcement until a per-mana provenance ledger exists (see
/// <see cref="MishrasWorkshopFactory"/> xmldoc). The structural test
/// confirms only one printed mana ability is wired and adds three
/// colourless ({C}{C}{C} buckets as +3 generic per CR 107.4c).
/// </summary>
public class MishrasWorkshopTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MishrasWorkshop_Identity()
    {
        var land = MishrasWorkshopFactory.Create(_alice);

        land.Name.Should().Be("Mishra's Workshop");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Supertypes.Should().BeEmpty("Mishra's Workshop is not basic and not legendary");
        land.Subtypes.Should().BeEmpty("Mishra's Workshop has no printed land subtype");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MishrasWorkshop_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mishra's Workshop", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Mishra's Workshop");
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Mishra's Workshop prints exactly one mana ability — the tap-for-{C}{C}{C}");
    }

    [Fact]
    public void MishrasWorkshop_Tap_AddsThreeColorless()
    {
        var workshop = MishrasWorkshopFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(workshop);
        workshop.SetZone(ZoneType.Battlefield);

        var ability = workshop.Abilities.OfType<ManaAbility>().Single();
        var produced = ability.Activate();

        // {C}{C}{C} buckets as +3 generic (CR 107.4c — engine has no
        // dedicated colourless slot today; see ManaCost.Parse).
        produced.Generic.Should().Be(3);
        produced.TotalValue.Should().Be(3);
        workshop.IsTapped.Should().BeTrue("activating the tap mana ability taps the source");
    }

    [Fact]
    public void MishrasWorkshop_ArtifactRestriction_StructuralDeferred()
    {
        // CR 106.4 / printed text: "Spend this mana only to cast
        // artifact spells." Enforcement requires a per-mana provenance
        // ledger that does not yet exist in the engine — see
        // MishrasWorkshopFactory xmldoc + parallel notes in
        // PyromancersGogglesFactory + EngineeredExplosivesFactory.
        //
        // This test pins the v1 shape: exactly one ManaAbility, no
        // additional restriction-enforcement plumbing. When the ledger
        // ships and the gate flips on, this test should be updated to
        // assert the spendability predicate is wired.
        var workshop = MishrasWorkshopFactory.Create(_alice);

        var manaAbilities = workshop.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1);

        // Sanity: no other ability shapes leaked into the wiring.
        workshop.Abilities.Should().HaveCount(1,
            "v1 ships the bare mana ability — no additional restriction wrapper or trigger");
    }
}
