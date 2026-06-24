using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TheWanderingRescuerFactory"/>.
///
/// Card: The Wandering Rescuer ({3}{W}{W}), Legendary Creature — Human
/// Samurai Noble 3/4. Oracle text (verified against the embedded seed):
///   "Flash
///    Convoke (Your creatures can help cast this spell. ...)
///    Double strike
///    Other tapped creatures you control have hexproof."
///
/// The card's UNIQUE behaviour is the "Other tapped creatures you control
/// have hexproof" static (CR 702.11 hexproof + the tapped-state membership
/// gate). Flash / Double strike are intrinsic keywords (carried by the JSON
/// def). Convoke is a descriptive keyword marker (same shape as Conclave
/// Tribunal). The contract test covers dispatch + well-formedness.
/// </summary>
[Trait("Color", "W")]
public class TheWanderingRescuerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TheWanderingRescuer_Identity()
    {
        var c = TheWanderingRescuerFactory.Create(_alice);

        c.Name.Should().Be("The Wandering Rescuer");
        c.ManaCost.Should().Be("{3}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Samurai).Should().BeTrue();
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);

        // Intrinsic keywords carried by the JSON def.
        c.HasEffectiveKeyword("Flash").Should().BeTrue();
        c.HasEffectiveKeyword("Double strike").Should().BeTrue();

        // Descriptive Convoke marker (CR 702.51) — same shape as Conclave
        // Tribunal.
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Convoke");

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TheWanderingRescuer_GrantsHexproof_ToOtherTappedCreatureYouControl()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Savannah Lions", "{W}", 2, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var rescuer = TheWanderingRescuerFactory.Create(_alice, svc);
        rescuer.Zone = ZoneType.Battlefield;
        rescuer.ActiveEffects = svc;

        // Untapped — no hexproof yet (the static gates on the tapped state).
        svc.Compute(ally).Keywords.Should().NotContain("Hexproof",
            "the static only grants hexproof to TAPPED creatures you control.");

        // Tap the ally — it now has hexproof (CR 702.11). The membership
        // gate is re-evaluated every Compute, so tapping flips it on with no
        // extra wiring.
        ally.Tap();
        svc.Compute(ally).Keywords.Should().Contain("Hexproof",
            "a tapped creature you control gains hexproof from the Rescuer's static.");
    }

    [Fact]
    public void TheWanderingRescuer_DoesNotGrantHexproof_ToItself()
    {
        // "Other tapped creatures" — the Rescuer is excluded even when tapped.
        var svc = new ContinuousEffectsService();

        var rescuer = TheWanderingRescuerFactory.Create(_alice, svc);
        rescuer.Zone = ZoneType.Battlefield;
        rescuer.ActiveEffects = svc;
        rescuer.Tap();

        svc.Compute(rescuer).Keywords.Should().NotContain("Hexproof",
            "the 'Other' rider excludes the Rescuer itself.");
    }

    [Fact]
    public void TheWanderingRescuer_DoesNotGrantHexproof_ToOpponentsTappedCreature()
    {
        var svc = new ContinuousEffectsService();

        var oppCreature = new Creature("Goblin Guide", "{R}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        oppCreature.Tap();

        var rescuer = TheWanderingRescuerFactory.Create(_alice, svc);
        rescuer.Zone = ZoneType.Battlefield;
        rescuer.ActiveEffects = svc;

        svc.Compute(oppCreature).Keywords.Should().NotContain("Hexproof",
            "the static is scoped to creatures YOU control (CR 109.5).");
    }

    [Fact]
    public void TheWanderingRescuer_LTB_LiftsHexproofGrant()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Savannah Lions", "{W}", 2, 1)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        ally.Tap();

        var rescuer = TheWanderingRescuerFactory.Create(_alice, svc);
        rescuer.Zone = ZoneType.Battlefield;
        rescuer.ActiveEffects = svc;

        svc.Compute(ally).Keywords.Should().Contain("Hexproof");

        // Rescuer leaves the battlefield — the static's IsActive gate drops.
        rescuer.SetZone(ZoneType.Graveyard);

        svc.Compute(ally).Keywords.Should().NotContain("Hexproof",
            "granted hexproof lifts when the Rescuer leaves the battlefield.");
    }
}
