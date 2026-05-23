using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SwordOfFeastAndFamineFactory"/> (Mirrodin
/// Besieged, {3}).
///
/// Covers:
/// - Identity (name, type, mana cost, Equipment subtype, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {2} activated ability cost.
/// - +2/+2 boost to equipped creature via AttachedBoostEffect (Layer 7c).
/// - Protection from black + green granted to equipped creature
///   (CR 702.16) — surfaced via Rules.Protection.HasProtectionFromColor.
/// - Combat-damage-to-a-player trigger fires and resolves: damaged
///   player discards 1 + controller's lands untap (CR 510 / CR 603.1 /
///   CR 701.16a / CR 701.20).
/// - Combat-damage trigger does NOT fire on damage to a creature.
/// </summary>
public class SwordOfFeastAndFamineTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFeastAndFamine_Identity()
    {
        var c = SwordOfFeastAndFamineFactory.Create(_alice);

        c.Name.Should().Be("Sword of Feast and Famine");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SwordOfFeastAndFamine_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sword of Feast and Famine", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Sword of Feast and Famine");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Equip {2} is the only activated ability");
    }

    // -----------------------------------------------------------------------
    // Equip cost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFeastAndFamine_EquipAbility_HasGenericTwoCost()
    {
        var c = SwordOfFeastAndFamineFactory.Create(_alice);

        var equip = c.Abilities.OfType<ActivatedAbility>().Single();
        var mana = equip.Costs.OfType<ManaCostCost>().Single();

        mana.Cost.Generic.Should().Be(2,
            "Equip {2} is the printed activation cost");
    }

    // -----------------------------------------------------------------------
    // +2/+2 boost
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFeastAndFamine_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(
            _alice, svc, eventBus: null, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+2 boost from Sword of Feast and Famine");
        bear.GetToughness().Should().Be(4);
    }

    [Fact]
    public void SwordOfFeastAndFamine_Detach_RestoresPT()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(
            _alice, svc, eventBus: null, triggers: null);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);

        bear.GetPower().Should().Be(4);

        sword.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach (Layer 7c, AttachedBoostEffect.IsActive)");
        bear.GetToughness().Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Protection from black + green
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFeastAndFamine_Equipped_GainsProtectionFromBlackAndGreen()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1U", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(
            _alice, svc, eventBus: null, triggers: null);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);

        // Without a bus, manually re-sync the grant lifecycles (the
        // AttachTo() above is a direct call — not a CardMovedEvent — so
        // the lifecycle handler is never fired; same workaround that the
        // SplinterTwin test path uses for its non-bus harness).
        SwordOfFeastAndFamineFactory.ProtectionGrants.Sync(sword);

        // CR 702.16 surfaces via Rules.Protection.HasProtectionFromColor
        // by scanning the bearer's Abilities for ProtectionAbility(quality).
        Protection.HasProtectionFromColor(bear, ManaColor.Black).Should().BeTrue(
            "Sword grants protection from black to the equipped creature");
        Protection.HasProtectionFromColor(bear, ManaColor.Green).Should().BeTrue(
            "Sword grants protection from green to the equipped creature");

        // Negative controls — protection only applies to black and green.
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse(
            "no protection from red is granted");
        Protection.HasProtectionFromColor(bear, ManaColor.Blue).Should().BeFalse(
            "no protection from blue is granted");
        Protection.HasProtectionFromColor(bear, ManaColor.White).Should().BeFalse(
            "no protection from white is granted");
    }

    [Fact]
    public void SwordOfFeastAndFamine_Detach_RevokesProtectionGrants()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1U", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(
            _alice, svc, eventBus: null, triggers: null);
        sword.Zone = ZoneType.Battlefield;
        sword.AttachTo(bear);
        SwordOfFeastAndFamineFactory.ProtectionGrants.Sync(sword);

        Protection.HasProtectionFromColor(bear, ManaColor.Black).Should().BeTrue();

        sword.Unattach();
        SwordOfFeastAndFamineFactory.ProtectionGrants.Sync(sword);

        Protection.HasProtectionFromColor(bear, ManaColor.Black).Should().BeFalse(
            "protection grant is revoked on detach (CR 702.16 — ability granted by the equipment lapses)");
        Protection.HasProtectionFromColor(bear, ManaColor.Green).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Combat-damage-to-a-player trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void SwordOfFeastAndFamine_CombatDamageToPlayer_DamagedPlayerDiscards_AndControllerLandsUntap()
    {
        // Equipped Bear belongs to Alice; Bob is the damaged player.
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        // Two tapped lands on Alice's battlefield (the Sword's controller).
        var aliceLand1 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        aliceLand1.Tap();
        var aliceLand2 = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        aliceLand2.Tap();
        _alice.Zones.Battlefield.AddCard(aliceLand1);
        _alice.Zones.Battlefield.AddCard(aliceLand2);

        // A tapped land on Bob's side — must NOT untap.
        var bobLand = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        bobLand.Tap();
        _bob.Zones.Battlefield.AddCard(bobLand);

        // Two cards in Bob's hand — one will be discarded.
        var bobHand1 = new Card("Junk1", "");
        var bobHand2 = new Card("Junk2", "");
        bobHand1.SetOwner(_bob);
        bobHand2.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobHand1);
        _bob.Zones.Hand.AddCard(bobHand2);

        // Pre-conditions.
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        aliceLand1.IsTapped.Should().BeTrue();
        aliceLand2.IsTapped.Should().BeTrue();
        bobLand.IsTapped.Should().BeTrue();

        // Fire the trigger: equipped Bear deals 2 combat damage to Bob.
        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(bear, _bob, 2);
        trigger.IsTriggered(dmgEvent).Should().BeTrue(
            "equipped creature dealing combat damage to a player matches the trigger");

        foreach (var e in trigger.Effects) e.Execute();

        // 1) Damaged player (Bob) discarded the first card in hand.
        _bob.Zones.Hand.GetCards().Should().HaveCount(1,
            "damaged player discards one card (CR 701.16a)");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bobHand1,
                "v1 deterministic first-card-in-hand pick");

        // 2) Both of Alice's (Sword controller's) lands untapped.
        aliceLand1.IsTapped.Should().BeFalse("controller's lands untap (CR 701.20)");
        aliceLand2.IsTapped.Should().BeFalse();

        // 3) Bob's land is unaffected — "you untap all lands you control"
        //    binds to the Sword's controller, not the damaged player.
        bobLand.IsTapped.Should().BeTrue(
            "the damaged player's lands are NOT untapped");
    }

    [Fact]
    public void SwordOfFeastAndFamine_CombatDamage_ToCreature_DoesNotFire()
    {
        // Oracle text says "combat damage to a player" — damage to a
        // creature target must NOT fire (distinguishes Sword from
        // Umezawa's Jitte, which fires on any combat damage).
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var blocker = new Creature("Blocker", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        var dmgEvent = new CombatDamageDealtEvent(bear, blocker, 2);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "the trigger gates on TargetPlayer != null — creature target does not fire");
    }

    [Fact]
    public void SwordOfFeastAndFamine_CombatDamage_FromUnequippedCreature_DoesNotFire()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var other = new Creature("Other", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        var sword = SwordOfFeastAndFamineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);
        sword.AttachTo(bear);

        var trigger = sword.Abilities.OfType<TriggeredAbility>().Single();
        // A different creature dealing combat damage to a player → no fire.
        var dmgEvent = new CombatDamageDealtEvent(other, _bob, 2);

        trigger.IsTriggered(dmgEvent).Should().BeFalse(
            "only the equipped creature's combat damage feeds the trigger");
    }
}
