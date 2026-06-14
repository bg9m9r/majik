using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HighlandWealdFactory"/> — the R/G snow dual land.
/// Type line: <c>Snow Land</c> (a plain Snow land with NO land subtypes,
/// unlike Highland Forest's <c>Snow Land — Mountain Forest</c>). Oracle text
/// (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {R} or {G}."
///
/// Covers the card's unique shape:
/// - Identity: Land + Snow supertype (CR 205.4d), nonbasic, and — distinctly —
///   no land subtypes.
/// - Two mana abilities producing {R} and {G} (CR 605.1 — mana abilities don't
///   use the stack), and no non-mana activated/triggered rider.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as <see cref="CinderBarrensFactory"/>).
/// </summary>
[Trait("Color", "C")]
public class HighlandWealdFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HighlandWeald_Identity_IsNonbasicSnowLand_WithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Highland Weald", _alice);

        land.Name.Should().Be("Highland Weald");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is 'Snow Land' (CR 205.4d)");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Highland Weald is nonbasic");
        // Distinct from Highland Forest: Highland Weald has NO land subtypes.
        land.HasSubtype(CardSubtype.Mountain).Should().BeFalse();
        land.HasSubtype(CardSubtype.Forest).Should().BeFalse();
    }

    [Fact]
    public void HighlandWeald_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Highland Weald", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Highland Weald taps for {R} or {G}");
        mana.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void HighlandWeald_HasNoNonManaActivatedOrTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Highland Weald", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities beyond mana");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("no ETB rider on Highland Weald");
    }
}
