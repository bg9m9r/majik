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
/// Unit tests for <see cref="NoviceInspectorFactory"/>.
///
/// Novice Inspector (Murders at Karlov Manor, {W}):
///   Creature — Human Detective 1/2.
///   "When this creature enters, investigate. (Create a Clue token. It's
///    an artifact with '{2}, Sacrifice this token: Draw a card.')"
///
/// Mechanically a one-for-one reprint of Thraben Inspector's body + ETB —
/// only the second creature type differs (Detective rather than Soldier).
///
/// Covers:
/// - Identity (Creature — Human Detective 1/2 at {W}, owner / controller
///   wired) materialised from the embedded JSON definition.
/// - NamedCardFactory dispatch.
/// - Single ETB triggered ability attached, active only on the
///   battlefield (CR 603.6a).
/// - The ETB effect creates a Clue token (CR 701.39 — Investigate) under
///   the controller, and the Clue carries its sac-to-draw ability.
/// </summary>
[Trait("Color", "W")]
public class NoviceInspectorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NoviceInspector_Identity()
    {
        var c = NoviceInspectorFactory.Create(_alice);

        c.Name.Should().Be("Novice Inspector");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NoviceInspector_AttachesSingleEtbTrigger()
    {
        var c = NoviceInspectorFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Investigate ETB trigger only");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger only active while on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void NoviceInspector_Etb_Investigates_CreatesClueUnderController()
    {
        var c = NoviceInspectorFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Resolve the ETB effect directly (no live ZoneService — the Clue
        // is created but bypasses battlefield placement).
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.Name == "Clue")
            .ToList();
        clues.Should().HaveCount(1, "Investigate creates exactly one Clue token");
        clues[0].HasSubtype(CardSubtype.Clue).Should().BeTrue();
        clues[0].Abilities.OfType<ActivatedAbility>().Should().NotBeEmpty(
            "the Clue carries its '{2}, Sacrifice this token: Draw a card.' ability");
    }
}
