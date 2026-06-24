using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FireshriekerFactory"/>.
///
/// Fireshrieker (Mirrodin, {3}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-24):
///   "Equipped creature has double strike."
///   "Equip {2}"
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, type, Equipment subtype, mana cost) — single assert.
/// - Equip {2} activated-ability shape (the printed activation cost).
/// - The granted Double strike keyword read through the layer system.
/// - Detach: the granted keyword is revoked.
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no *_DispatchesViaNamedCardFactory test here.)
/// </summary>
[Trait("Color", "C")] // colourless artifact
public class FireshriekerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Fireshrieker_Identity()
    {
        var c = FireshriekerFactory.Create(_alice);

        c.Name.Should().Be("Fireshrieker");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Fireshrieker is an Equipment");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void Fireshrieker_EquipAbility_HasGenericTwoCost()
    {
        var c = FireshriekerFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2, "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // Granted Double strike (CR 702.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Fireshrieker_Equipped_Bear_GetsDoubleStrike()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        // The bear has no double strike printed.
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse();

        var spear = FireshriekerFactory.Create(_alice, svc);
        spear.Zone = ZoneType.Battlefield;
        spear.AttachTo(bear);

        // CR 613 — Layer-6 ability grants materialise onto the bearer during a
        // layer pass (SyncAbilityGrants); the returned keyword set settles on
        // the FOLLOWING Compute. Prime one pass so the assertion reads the
        // settled state, as repeated SBA / layer recomputation does in a game.
        svc.Compute(bear);

        CombatAbilities.HasDoubleStrike(bear).Should().BeTrue(
            "Fireshrieker grants double strike (CR 702.4)");
    }

    // -----------------------------------------------------------------------
    // Detach revokes the granted keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void Fireshrieker_Detach_RevokesDoubleStrike()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var spear = FireshriekerFactory.Create(_alice, svc);
        spear.Zone = ZoneType.Battlefield;
        spear.AttachTo(bear);

        // While attached: double strike granted. Prime a layer pass so the
        // Layer-6 grant settles before asserting.
        svc.Compute(bear);
        CombatAbilities.HasDoubleStrike(bear).Should().BeTrue();

        spear.Unattach();

        // The grant gates on AttachedTo — revoked on detach. Prime once more
        // so the revoke settles before asserting.
        svc.Compute(bear);
        CombatAbilities.HasDoubleStrike(bear).Should().BeFalse(
            "granted double strike is revoked on detach");
    }
}
