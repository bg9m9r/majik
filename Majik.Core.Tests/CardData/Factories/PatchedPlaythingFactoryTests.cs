using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PatchedPlaythingFactory"/>.
///
/// Card: Patched Plaything — {2}{W} Artifact Creature — Toy 4/3.
///   "Double strike
///    This creature enters with two -1/-1 counters on it if you cast it from
///    your hand."
///
/// The conditional enters-with-counters behaviour is owned by the prod-path
/// <see cref="EntersWithCountersBinder"/> (the factory only carries the static
/// identity + Double strike keyword), so the binder + ZoneService path is what
/// these behaviour tests exercise — mirroring
/// <see cref="Majik.Core.Tests.Effects.EntersWithCountersTests"/>.
/// </summary>
[Trait("Color", "W")]
public class PatchedPlaythingFactoryTests
{
    private const string Oracle =
        "Double strike\nThis creature enters with two -1/-1 counters on it if you cast it from your hand.";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PatchedPlaything_Identity()
    {
        var c = PatchedPlaythingFactory.Create(_alice);

        c.Name.Should().Be("Patched Plaything");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Toy).Should().BeTrue();
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PatchedPlaything_HasDoubleStrikeKeywordMarker()
    {
        var c = PatchedPlaythingFactory.Create(_alice);

        // CR 702.4 — Double strike. CombatAbilities.HasDoubleStrike consumes
        // this marker to assign both first-strike AND regular combat damage.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Double strike").Should().BeTrue(
                "Patched Plaything has Double strike (CR 702.4)");
    }

    [Fact]
    public void PatchedPlaything_CastFromHand_EntersWithTwoMinusCounters()
    {
        // CR 614.1d — "enters with two -1/-1 counters on it if you cast it from
        // your hand". The prod EntersWithCountersBinder registers the
        // conditional replacement; with the cast-from-hand sentinel set it
        // applies, so the 4/3 enters as a 2/1 (two -1/-1 counters).
        var bus = new ReplacementBus();
        var card = PatchedPlaythingFactory.Create(_alice);
        var entity = new CardEntity { Name = card.Name, OracleText = Oracle };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeTrue(
            "the conditional -1/-1 cast-from-hand clause is recognised");

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        // SpellCastFlow stamps this when the spell was cast from its
        // controller's hand (CR 113.5); ZoneService preserves it across the
        // Stack -> Battlefield move so the ETB replacement reads it at entry.
        card.SetWasCastFromHand(true);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Hand, ZoneType.Battlefield, _alice);

        card.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2,
            "cast from hand ⇒ enters with two -1/-1 counters");
    }

    [Fact]
    public void PatchedPlaything_NotCastFromHand_EntersWithNoCounters()
    {
        // CR 614.1d — the "if you cast it from your hand" gate. A non-hand entry
        // (blink / copy / token, or cast from another zone) leaves
        // WasCastFromHand clear, so the conditional replacement is inert and the
        // creature enters at its full 4/3 with no counters.
        var bus = new ReplacementBus();
        var card = PatchedPlaythingFactory.Create(_alice);
        var entity = new CardEntity { Name = card.Name, OracleText = Oracle };

        EntersWithCountersBinder.Bind(card, entity, bus).Should().BeTrue();

        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        // WasCastFromHand deliberately left false (e.g. entered via a blink /
        // reanimation, not a hand cast).

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        card.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "not cast from hand ⇒ enters with no -1/-1 counters");
    }
}
