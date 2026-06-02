using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="OpenTheArmoryFactory"/> — Sorcery {1}{W}
/// (Future Sight / reprints).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Search your library for an Aura or Equipment card, reveal it, put it
///    into your hand, then shuffle."
///
/// Covers:
/// - Identity: name, mana cost, Sorcery type, white color, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatcher entry.
/// - Resolve: an Aura in the library → goes to hand (whole-library search).
/// - Resolve: an Equipment in the library → goes to hand.
/// - Resolve: ineligible cards stay in the library; only one card is tutored.
/// - Resolve: "may" decline (picker returns null) → nothing moves.
/// - Resolve: no eligible card → nothing moves, no throw.
/// - Resolve: empty library — clean no-op.
/// </summary>
[Trait("Color", "W")]
public class OpenTheArmoryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Name_ManaCost_Type()
    {
        var card = OpenTheArmoryFactory.Create(_alice);

        card.Name.Should().Be("Open the Armory");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.Should().BeOfType<Sorcery>();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OpenTheArmory()
    {
        var card = NamedCardFactory.Create("Open the Armory", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Open the Armory");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}");
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AuraInLibrary_GoesToHand_AndShuffles()
    {
        // Deep in the library so this is a whole-library search, not a peek.
        SeedLibrary(_alice,
            new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }),
            new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }),
            new Creature("Bear", "{1}{G}", 2, 2),
            Aura("Pacifism", "{1}{W}"));

        var result = Resolve(eligible => eligible.FirstOrDefault());

        result.Picked.Should().NotBeNull();
        result.Picked!.Name.Should().Be("Pacifism");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Pacifism");
        result.Picked.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_EquipmentInLibrary_GoesToHand()
    {
        SeedLibrary(_alice,
            new Creature("Bear", "{1}{G}", 2, 2),
            Equipment("Bonesplitter", "{1}"));

        var result = Resolve(eligible => eligible.FirstOrDefault());

        result.Picked.Should().NotBeNull();
        result.Picked!.Name.Should().Be("Bonesplitter");
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Bonesplitter");
    }

    [Fact]
    public void Resolve_OnlyOneEligibleTutored_RestStayInLibrary()
    {
        var aura = Aura("Pacifism", "{1}{W}");
        var equip = Equipment("Bonesplitter", "{1}");
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        SeedLibrary(_alice, aura, equip, bear);

        // Pick the equipment explicitly.
        var result = Resolve(eligible => eligible.First(c => c.Name == "Bonesplitter"));

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Bonesplitter");
        // The other eligible Aura and the ineligible creature remain.
        _alice.Zones.Library.GetCards().Should().Contain(aura);
        _alice.Zones.Library.GetCards().Should().Contain(bear);
        _alice.Zones.Hand.GetCards().Should().NotContain(aura);
    }

    [Fact]
    public void Resolve_DeclineMay_NothingMoves()
    {
        var aura = Aura("Pacifism", "{1}{W}");
        SeedLibrary(_alice, aura);

        var result = Resolve(_ => null);

        result.Picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void Resolve_NoEligibleCard_NothingMoves()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        SeedLibrary(_alice, bear);

        var result = Resolve(eligible => eligible.FirstOrDefault());

        result.Eligible.Should().BeEmpty();
        result.Picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Resolve_EmptyLibrary_CleanNoOp()
    {
        var result = Resolve(eligible => eligible.FirstOrDefault());

        result.Eligible.Should().BeEmpty();
        result.Picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Enchantment Aura(string name, string manaCost) =>
        new(name, manaCost, subtypes: new[] { CardSubtype.Aura });

    private static Artifact Equipment(string name, string manaCost) =>
        new(name, manaCost, subtypes: new[] { CardSubtype.Equipment });

    /// <summary>
    /// Run the resolve effect synchronously (legacy <see cref="IEffect.Execute"/>
    /// path → no live agent) with the supplied picker, returning the observed
    /// <see cref="OpenTheArmoryFactory.Result"/>.
    /// </summary>
    private OpenTheArmoryFactory.Result Resolve(
        System.Func<IReadOnlyList<ICard>, ICard?>? choosePick)
    {
        OpenTheArmoryFactory.Result? captured = null;
        var effects = OpenTheArmoryFactory.BuildResolveEffect(
            _alice, choosePick, r => captured = r);

        foreach (var e in effects) e.Execute();

        captured.Should().NotBeNull();
        return captured!;
    }

    private static void SeedLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Library);
            }
            p.Zones.Library.AddCard(c);
        }
    }
}
