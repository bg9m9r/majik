using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for Frost Marsh — the U/B "plain" snow tapland (Coldsnap).
/// Oracle text (verified against Scryfall 2026-06-14):
///   "This land enters tapped.
///    {T}: Add {U} or {B}."
///
/// Type line: Snow Land (NO land subtypes — unlike the Ice Age snow duals
/// such as Ice Tunnel, which carry Island/Swamp subtypes). Same fileless-JSON
/// posture as <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
/// dispatch for Sulfurous Mire / Ice Tunnel: no hand-written wrapper factory,
/// the source generator dispatches the embedded <c>frost-marsh.json</c>.
///
/// Covers:
/// - Identity: Land type, Snow supertype (CR 205.4d), nonbasic, no subtypes.
/// - Two mana abilities producing {U} and {B} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - No rider (no extra activated / triggered ability — plain tapland).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by EntersTappedBinder from the printed oracle text, not by this
/// definition (same posture as the Sulfurous Mire / Ice Tunnel snow duals).
/// </summary>
[Trait("Color", "M")]
public class FrostMarshFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FrostMarsh_Identity_IsNonbasicSnowLand_WithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Frost Marsh", _alice);

        land.Name.Should().Be("Frost Marsh");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Frost Marsh is a Snow Land (CR 205.4d)");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Frost Marsh is nonbasic");

        // Frost Marsh's type line is just "Snow Land" — it carries NO land
        // subtypes (CR 205.3i), unlike the Ice Age snow duals.
        land.HasSubtype(CardSubtype.Island).Should().BeFalse();
        land.HasSubtype(CardSubtype.Swamp).Should().BeFalse();
    }

    [Fact]
    public void FrostMarsh_HasTwoManaAbilities_ProducingBlueAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Frost Marsh", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "{T}: Add {U} or {B}");
        mana.Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void FrostMarsh_HasNoRiderAbility()
    {
        var land = (Land)NamedCardFactory.Create("Frost Marsh", _alice);

        // Plain tapland: only the two mana abilities, no extra activated or
        // triggered ability (no cycling, no life gain, no scry).
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }
}
