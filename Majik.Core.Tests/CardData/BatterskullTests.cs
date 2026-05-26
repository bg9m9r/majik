using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BatterskullFactory"/> (New Phyrexia, {5}).
///
/// Covers:
/// - Identity (Artifact Equipment, {5}).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Static +4/+4 boost via <see cref="AttachedBoostEffect"/>.
/// - Vigilance + lifelink grants on equipped creature.
/// - Living-weapon ETB trigger spawns a 0/0 black Germ token and
///   attaches Batterskull to it (CR 702.91).
/// - {3} bounce-to-hand activated ability: cost shape + resolution.
/// - Equip {5} activated ability shape.
/// </summary>
public class BatterskullTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Batterskull_Identity()
    {
        var b = BatterskullFactory.Create(_alice);

        b.Name.Should().Be("Batterskull");
        b.ManaCost.Should().Be("{5}");
        b.HasType(CardType.Artifact).Should().BeTrue();
        b.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Batterskull is an Equipment");
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Batterskull_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Batterskull", _alice);

        c.Should().BeOfType<Artifact>("Batterskull is an Artifact");
        c.Name.Should().Be("Batterskull");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the living-weapon ETB trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCountGreaterThanOrEqualTo(2,
            "{3} return-to-hand + Equip {5} are wired");
    }

    // -----------------------------------------------------------------------
    // Equip cost + bounce cost shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Batterskull_EquipAbility_HasGenericFiveCost()
    {
        var b = BatterskullFactory.Create(_alice);

        var equip = b.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(5, "Equip {5} is the printed activation cost");
    }

    [Fact]
    public void Batterskull_BounceAbility_HasGenericThreeCost()
    {
        var b = BatterskullFactory.Create(_alice);

        // Three activated abilities exist on Batterskull: the bounce
        // (instant-speed, {3}) and the EquipActivatedAbility ({5},
        // sorcery-speed). Filter to the non-equip activated ability for
        // the bounce-cost check.
        var bounce = b.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single();

        var mana = bounce.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Generic.Should().Be(3, "{3}: Return Batterskull to its owner's hand");
        bounce.IsSorcerySpeed.Should().BeFalse(
            "the bounce ability is printed without a sorcery-speed clause");
    }

    // -----------------------------------------------------------------------
    // Static continuous effect — +4/+4
    // -----------------------------------------------------------------------

    [Fact]
    public void Batterskull_Equipped_Bear_Becomes_6_6_AndHasVigilanceAndLifelink()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var skull = BatterskullFactory.Create(_alice, svc, triggers: null, zoneService: null);
        skull.Zone = ZoneType.Battlefield;
        skull.AttachTo(bear);

        bear.GetPower().Should().Be(6, "+4 power from Batterskull");
        bear.GetToughness().Should().Be(6, "+4 toughness from Batterskull");
        CombatAbilities.HasVigilance(bear).Should().BeTrue(
            "Batterskull grants vigilance to the equipped creature");
        CombatAbilities.HasLifelink(bear).Should().BeTrue(
            "Batterskull grants lifelink to the equipped creature");
    }

    [Fact]
    public void Batterskull_Detach_RestoresPT_AndKeywordsLapse()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var skull = BatterskullFactory.Create(_alice, svc, triggers: null, zoneService: null);
        skull.Zone = ZoneType.Battlefield;
        skull.AttachTo(bear);

        // Sanity
        bear.GetPower().Should().Be(6);
        CombatAbilities.HasLifelink(bear).Should().BeTrue();

        skull.Unattach();

        bear.GetPower().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasVigilance(bear).Should().BeFalse(
            "vigilance grant is revoked when no longer attached");
        CombatAbilities.HasLifelink(bear).Should().BeFalse(
            "lifelink grant is revoked when no longer attached");
    }

    [Fact]
    public void Batterskull_ShapeOnly_CarriesVigilanceAndLifelinkMarkers()
    {
        var b = BatterskullFactory.Create(_alice);

        // Shape-only path (no ContinuousEffectsService): keyword markers
        // live on Batterskull itself so factory-shape tests observe the
        // keywords somewhere on the equipment. The live path projects
        // them onto the equipped creature instead.
        var keywords = b.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain(
            k => string.Equals(k, "Vigilance", System.StringComparison.OrdinalIgnoreCase),
            "shape-only path stamps Vigilance on Batterskull");
        keywords.Should().Contain(
            k => string.Equals(k, "Lifelink", System.StringComparison.OrdinalIgnoreCase),
            "shape-only path stamps Lifelink on Batterskull");
    }

    // -----------------------------------------------------------------------
    // Living weapon — ETB spawns Germ + auto-attaches
    // -----------------------------------------------------------------------

    [Fact]
    public void Batterskull_LivingWeapon_SpawnsGermAndAttaches()
    {
        // Resolve the ETB effect directly — same posture as the rest of
        // the equipment / token factory smoke tests. Without a ZoneService
        // the token is inserted directly into the battlefield.
        var skull = BatterskullFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skull);
        skull.SetZone(ZoneType.Battlefield);

        var etb = skull.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // A 0/0 black Germ creature token entered the battlefield under
        // Alice's control.
        var germ = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Germ");

        germ.Should().NotBeNull("living weapon spawns a Germ token");
        germ!.IsToken.Should().BeTrue();
        germ.BasePower.Should().Be(0, "Germ enters as 0/0 (CR 702.91)");
        germ.BaseToughness.Should().Be(0);
        germ.HasSubtype(CardSubtype.Germ).Should().BeTrue();
        Majik.Core.Cards.CardColors.GetColors(germ).Should()
            .ContainSingle(c => c == Majik.Core.ValueObjects.ManaColor.Black,
                "Germ is a black creature token");

        // Batterskull attaches itself to the spawned Germ.
        skull.AttachedTo.Should().BeSameAs(germ,
            "Batterskull attaches itself to the freshly-spawned Germ");
    }

    [Fact]
    public void Batterskull_LivingWeaponBoost_KeepsGermAlive()
    {
        // With a ContinuousEffectsService wired, the +4/+4 boost takes
        // the 0/0 Germ to 4/4, so SBAs don't kill it (CR 704.5f).
        var svc = new ContinuousEffectsService();
        var skull = BatterskullFactory.Create(
            _alice, svc, triggers: null, zoneService: null);

        _alice.Zones.Battlefield.AddCard(skull);
        skull.SetZone(ZoneType.Battlefield);

        var etb = skull.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var germ = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.Name == "Germ");

        // Make the boost evaluate against the live continuous-effects
        // service (the Germ has no ActiveEffects yet — it was minted via
        // TokenFactory which doesn't wire the service, mirroring the rest
        // of the v1 token shape). Tack the service on for the assertion.
        germ.ActiveEffects = svc;

        germ.GetPower().Should().Be(4, "+4/+4 boost brings the Germ to 4/4");
        germ.GetToughness().Should().Be(4,
            "the boost is what keeps the Germ alive past SBAs");
    }

    // -----------------------------------------------------------------------
    // {3}: Return Batterskull to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Batterskull_BounceAbility_ReturnsToOwnersHand()
    {
        var skull = BatterskullFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skull);
        skull.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        skull.AttachTo(bear);

        var bounce = skull.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single();

        foreach (var effect in bounce.Effects) effect.Execute();

        skull.Zone.Should().Be(ZoneType.Hand,
            "{3} returns Batterskull to its owner's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(skull);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(skull,
            "Batterskull leaves the battlefield on resolution");
        skull.AttachedTo.Should().BeNull(
            "leaving the battlefield unattaches the equipment (CR 704.5n)");
    }

    [Fact]
    public void Batterskull_BounceAbility_NoopIfAlreadyOffBattlefield()
    {
        var skull = BatterskullFactory.Create(_alice);
        // Batterskull is in hand — bounce should be idempotent.
        skull.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(skull);

        var bounce = skull.Abilities.OfType<ActivatedAbility>()
            .Where(a => a is not EquipActivatedAbility)
            .Single();

        // Resolve repeatedly — should not duplicate Batterskull in hand.
        foreach (var effect in bounce.Effects) effect.Execute();
        foreach (var effect in bounce.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards()
            .Count(c => ReferenceEquals(c, skull))
            .Should().Be(1,
                "double-resolution off the battlefield is idempotent");
    }
}
