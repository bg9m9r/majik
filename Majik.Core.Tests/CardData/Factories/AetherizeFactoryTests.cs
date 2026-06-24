using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Aetherize (Gatecrash, {3}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Return all attacking creatures to their owner's hand."
///
/// Covers:
///   - Card identity (Instant, {3}{U}, Blue).
///   - SpellDefinition shape: NO target requests (no "target" in the oracle
///     text — every attacking creature is affected).
///   - Resolve returns EVERY attacking creature (across both players) to its
///     OWNER's hand (CR 506.2 / CR 701.20).
///   - Non-attacking creatures and non-creature permanents are untouched.
///   - A creature that left the battlefield before resolution is a no-op
///     (CR 608.2b).
///
/// The attacking-creature set is injected via the factory's
/// <c>attackerLookup</c> parameter (same combat-state injection posture as
/// Condemn / Settle the Wreckage); production wires the default lookup that
/// reads the live combat-membership registry.
/// </summary>
[Trait("Color", "U")]
public class AetherizeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Aetherize_HasInstantShape_Blue_AtCost3U()
    {
        var card = AetherizeFactory.Create(_alice);

        card.Name.Should().Be("Aetherize");
        card.ManaCost.Should().Be("{3}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(4);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasNoTargetRequests()
    {
        // "Return all attacking creatures …" has no "target" word — the spell
        // affects every attacker, so it carries no TargetRequests.
        var def = AetherizeFactory.BuildSpellDefinition(attackerLookup: () => Array.Empty<Creature>());

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void ReturnsEachAttackingCreature_ToItsOwnersHand()
    {
        // Both players have an attacking creature; both should be bounced to
        // their respective owners' hands (CR 701.20). Aetherize is symmetric.
        var aliceAttacker = NewCreatureOnBattlefield(_alice, "Goblin Guide", "{R}", 2, 2);
        var bobAttacker = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(aliceAttacker, bobAttacker);

        aliceAttacker.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(aliceAttacker,
            because: "it returns to ITS OWNER's hand (CR 701.20)");

        bobAttacker.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bobAttacker,
            because: "it returns to ITS OWNER's hand (CR 701.20)");
    }

    [Fact]
    public void DoesNotBounce_NonAttackingCreature()
    {
        var attacker = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var blocker = NewCreatureOnBattlefield(_alice, "Wall of Omens", "{1}{W}", 0, 4);

        // Only the attacker is in the attacking set.
        Resolve(attacker);

        attacker.Zone.Should().Be(ZoneType.Hand,
            because: "attacking creatures are returned (CR 506.2)");
        blocker.Zone.Should().Be(ZoneType.Battlefield,
            because: "Aetherize only returns ATTACKING creatures — a non-attacker stays put");
    }

    [Fact]
    public void CreatureNotOnBattlefield_AtResolution_IsNoOp()
    {
        var attacker = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        // It was declared as an attacker but left the battlefield (e.g. died)
        // before resolution.
        _bob.Zones.Battlefield.RemoveCard(attacker);
        attacker.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(attacker);

        Resolve(attacker);

        attacker.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — a creature not on the battlefield at resolution is not moved");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Resolve Aetherize with the supplied creatures treated as the
    /// current combat's attackers (injected lookup — no live combat loop).</summary>
    private static void Resolve(params Creature[] attackers)
    {
        var def = AetherizeFactory.BuildSpellDefinition(
            attackerLookup: () => attackers,
            zoneService: null);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string cost, int power, int toughness)
    {
        var creature = new Creature(name, cost, power, toughness);
        creature.SetOwner(owner);
        creature.SetController(owner);
        creature.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(creature);
        return creature;
    }
}
