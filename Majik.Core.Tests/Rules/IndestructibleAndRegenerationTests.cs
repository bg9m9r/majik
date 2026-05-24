using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Coverage for the destroy-path gates added to
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>:
/// Indestructible (CR 702.12) cancels destroy; Regeneration shields
/// (CR 701.15) substitute tap+clear-damage for the destroy; sacrifice
/// and other non-destroy reasons bypass both gates (CR 701.16,
/// CR 702.12b — sacrifice is not a destroy effect).
/// </summary>
public class IndestructibleAndRegenerationTests
{
    private static (Player alice, Creature bear) MakeBear()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return (alice, bear);
    }

    [Fact]
    public void DestroyVsIndestructibleCreature_NoZoneMove()
    {
        var (alice, bear) = MakeBear();
        bear.AddAbility(new KeywordAbility("Indestructible", bear, alice));

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void SacrificeVsIndestructibleCreature_MovesToGraveyard()
    {
        // CR 701.16 — sacrifice bypasses indestructible (CR 702.12b is
        // scoped to "destroy" effects).
        var (alice, bear) = MakeBear();
        bear.AddAbility(new KeywordAbility("Indestructible", bear, alice));

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Sacrifice);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void DestroyVsCreatureWithRegenShield_ConsumesShieldTapClearDamage()
    {
        var (_, bear) = MakeBear();
        bear.TakeDamage(1);
        bear.AddRegenerationShield();
        bear.HasRegenerationShield.Should().BeTrue();

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.IsTapped.Should().BeTrue();
        bear.Damage.Should().Be(0);
        bear.HasRegenerationShield.Should().BeFalse();
    }

    [Fact]
    public void DestroyVsCreatureWithoutShield_MovesToGraveyard()
    {
        var (alice, bear) = MakeBear();

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void DestroyNoRegeneration_VsCreatureWithRegenShield_MovesToGraveyard()
    {
        // Wrath of God / Damnation / Terminate route: regeneration shields
        // do NOT save the permanent (printed "can't be regenerated"), but
        // indestructible is still honoured per CR 702.12b.
        var (alice, bear) = MakeBear();
        bear.AddRegenerationShield();

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.DestroyNoRegeneration);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        // Shield is unconsumed — the destroy ignored it rather than
        // consuming it (CR 701.15c "instead" is denied by the no-regen
        // rider). Implementation note: the shield is dropped at EOT
        // regardless via ClearRegenerationShields().
        bear.RegenerationShieldCount.Should().Be(1);
    }

    [Fact]
    public void DestroyNoRegeneration_VsIndestructibleCreature_StillSaved()
    {
        var (_, bear) = MakeBear();
        bear.AddAbility(new KeywordAbility("Indestructible", bear, bear.Owner));

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.DestroyNoRegeneration);

        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void MultipleRegenerationShields_StackAndConsumeOnePerDestroy()
    {
        // CR 701.15a — regeneration effects stack; each destroy attempt
        // consumes a single shield.
        var (_, bear) = MakeBear();
        bear.AddRegenerationShield();
        bear.AddRegenerationShield();
        bear.AddRegenerationShield();
        bear.RegenerationShieldCount.Should().Be(3);

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);
        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.RegenerationShieldCount.Should().Be(2);

        // Untap so the next regen tap doesn't no-op (CR 701.15c just taps;
        // an already-tapped permanent stays tapped, but for the assertion
        // hygiene reset to untapped). Skip untap — second destroy still
        // consumes a shield, and IsTapped stays true.
        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);
        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.RegenerationShieldCount.Should().Be(1);

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);
        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.RegenerationShieldCount.Should().Be(0);

        // Fourth destroy with no shields left should resolve.
        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Destroy);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void RegenerationShield_ClearsViaClearRegenerationShields()
    {
        // CR 514.2 — shields are "until end of turn" and clear during
        // cleanup. TurnDriver's Cleanup loop calls
        // Permanent.ClearRegenerationShields() on every battlefield
        // permanent; this asserts the primitive itself.
        var (_, bear) = MakeBear();
        bear.AddRegenerationShield();
        bear.AddRegenerationShield();

        bear.ClearRegenerationShields();

        bear.HasRegenerationShield.Should().BeFalse();
        bear.RegenerationShieldCount.Should().Be(0);
    }

    [Fact]
    public void SacrificeVsCreatureWithRegenShield_StillSacrificed_ShieldUnconsumed()
    {
        // CR 701.16 — sacrifice is not a destroy effect; regen shields
        // are only consumed by destroys (CR 701.15c "the next time it
        // would be destroyed").
        var (alice, bear) = MakeBear();
        bear.AddRegenerationShield();

        OracleSpellBinder.MoveToGraveyard(bear, ZoneMoveReason.Sacrifice);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
        bear.RegenerationShieldCount.Should().Be(1);
    }

    [Fact]
    public void DestroyVsIndestructibleArtifact_NonCreaturePath_NoZoneMove()
    {
        // The Creature-only CombatAbilities helper doesn't apply to a
        // bare Permanent (e.g. Darksteel Citadel). The binder falls back
        // to scanning KeywordAbility markers directly.
        var alice = new Player("Alice", 20);
        var citadel = new Permanent(
            "Darksteel Citadel",
            "{0}",
            new[] { Majik.Core.Cards.Types.CardType.Artifact });
        citadel.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(citadel);
        citadel.SetZone(ZoneType.Battlefield);
        citadel.AddAbility(new KeywordAbility("Indestructible", citadel, alice));

        OracleSpellBinder.MoveToGraveyard(citadel, ZoneMoveReason.Destroy);

        citadel.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void BackwardCompat_NoReasonOverload_TreatedAsDestroy()
    {
        // The default overload still routes through the Destroy gate so
        // legacy call sites get indestructible / regeneration handling
        // for free.
        var (_, bear) = MakeBear();
        bear.AddAbility(new KeywordAbility("Indestructible", bear, bear.Owner));

        OracleSpellBinder.MoveToGraveyard(bear);

        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void FxSacrifice_BypassesIndestructible()
    {
        var (alice, bear) = MakeBear();
        bear.AddAbility(new KeywordAbility("Indestructible", bear, alice));

        Fx.Sacrifice(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear);
    }
}
