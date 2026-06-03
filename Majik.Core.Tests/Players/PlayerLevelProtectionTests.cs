using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using TargetLegality = Majik.Core.Targeting.TargetLegality;
using TargetSpec = Majik.Core.Targeting.TargetSpec;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// Tests for the player-level continuous-property channel (CR 702.16 /
/// 702.18) that pays down the player-level-shroud-and-damage-prevention
/// deferral: player SHROUD, player PROTECTION-FROM-CARD-TYPE, and the
/// persistent player damage-prevention shields. Mirrors
/// <see cref="PlayerHexproofTests"/>.
/// </summary>
public class PlayerLevelProtectionTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PlayerLevelProtectionTests() => PlayerStaticAbilities.Clear();

    public void Dispose() => PlayerStaticAbilities.Clear();

    // ── Registry primitives ──────────────────────────────────────────────

    [Fact]
    public void Player_NoShroud_NoProtection_ByDefault()
    {
        _alice.HasShroud.Should().BeFalse();
        _alice.HasProtectionFromCardType(CardType.Instant).Should().BeFalse();
    }

    [Fact]
    public void AddShroud_LightsUpQuery_RemoveDrops()
    {
        var token = new object();
        PlayerStaticAbilities.AddShroud(token, _alice);

        _alice.HasShroud.Should().BeTrue();
        _bob.HasShroud.Should().BeFalse();

        PlayerStaticAbilities.RemoveShroud(token);
        _alice.HasShroud.Should().BeFalse();
    }

    [Fact]
    public void AddProtectionFromCardType_IsTypeSpecific()
    {
        var token = new object();
        PlayerStaticAbilities.AddProtectionFromCardType(token, _alice, CardType.Creature);

        _alice.HasProtectionFromCardType(CardType.Creature).Should().BeTrue();
        _alice.HasProtectionFromCardType(CardType.Instant).Should().BeFalse();
        _bob.HasProtectionFromCardType(CardType.Creature).Should().BeFalse();

        PlayerStaticAbilities.RemoveProtectionFromCardType(token);
        _alice.HasProtectionFromCardType(CardType.Creature).Should().BeFalse();
    }

    // ── Target legality (CR 608.2b) ──────────────────────────────────────

    [Fact]
    public void Shroud_BlocksTargeting_FromAnyone_IncludingSelf()
    {
        // CR 702.18a — shroud blocks ALL targeting, even the player's own.
        PlayerStaticAbilities.AddShroud(new object(), _alice);
        var spec = new TargetSpec("any").AnyCreatureOrPlayer();

        TargetLegality.IsLegal(spec, _alice, _bob).Should().BeFalse();   // opponent
        TargetLegality.IsLegal(spec, _alice, _alice).Should().BeFalse(); // self too
    }

    [Fact]
    public void ProtectionFromCardType_GateRejectsMatchingSourceSpell()
    {
        // Serra's Emissary player half: Alice has protection from instants.
        PlayerStaticAbilities.AddProtectionFromCardType(new object(), _alice, CardType.Instant);

        var instant = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var action = new CastSpellAction(
            instant, _bob,
            sorcerySpeedAvailable: true,
            fromZone: Majik.Core.Zones.ZoneType.Hand,
            targets: new object[] { _alice });

        var result = new ActionValidator().ValidateAction(action);
        result.IsValid.Should().BeFalse();
        result.Violation!.RuleNumber.Should().Be("702.16");

        // A sorcery (different card type) is unaffected.
        var sorcery = new Sorcery("Lava Spike", "{R}") { Owner = _bob };
        var sorceryAction = new CastSpellAction(
            sorcery, _bob,
            sorcerySpeedAvailable: true,
            fromZone: Majik.Core.Zones.ZoneType.Hand,
            targets: new object[] { _alice });
        new ActionValidator().ValidateAction(sorceryAction).IsValid.Should().BeTrue();
    }

    // ── Damage-prevention shields (CR 615) ───────────────────────────────

    [Fact]
    public void PreventAllDamageToPlayerShield_CancelsDamageToController_WhileSourceOnBattlefield()
    {
        var source = new Enchantment("Solitary Confinement", "{2}{W}") { Owner = _alice };
        source.SetController(_alice);
        source.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToPlayerShield(source));

        var attacker = new Creature("Goblin", "{R}", 2, 2) { Owner = _bob };
        var intent = new DamageIntent(attacker, 3, TargetPlayer: _alice);
        bus.Apply(intent).Should().BeNull("all damage to the controller is prevented");

        // Damage to the OTHER player is untouched.
        var toBob = new DamageIntent(attacker, 3, TargetPlayer: _bob);
        bus.Apply(toBob).Should().NotBeNull();

        // Source off the battlefield → no prevention (CR 614.6).
        source.SetZone(Majik.Core.Zones.ZoneType.Graveyard);
        bus.Apply(new DamageIntent(attacker, 3, TargetPlayer: _alice)).Should().NotBeNull();
    }

    [Fact]
    public void PreventDamageFromCardTypeShield_OnlyCancelsMatchingTypeSources()
    {
        var source = new Creature("Serra's Emissary", "{4}{W}{W}{W}", 7, 7) { Owner = _alice };
        source.SetController(_alice);
        source.SetZone(Majik.Core.Zones.ZoneType.Battlefield);

        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventDamageToPlayerFromCardTypeShield(source, CardType.Creature));

        // A creature source dealing damage to Alice is prevented.
        var creatureSrc = new Creature("Goblin", "{R}", 2, 2) { Owner = _bob };
        bus.Apply(new DamageIntent(creatureSrc, 3, TargetPlayer: _alice)).Should().BeNull();

        // An instant source (no Creature type) is NOT prevented.
        var instantSrc = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        bus.Apply(new DamageIntent(instantSrc, 3, TargetPlayer: _alice)).Should().NotBeNull();
    }

    // ── Solitary Confinement (CR 702.18 + 615 + 117.5 + 603.1) ───────────

    [Fact]
    public void SolitaryConfinement_GrantsShroud_AndPreventsDamage_WhileOnBattlefield()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var replacements = new ReplacementBus();
        SkipDrawRegistry.Clear();

        var sc = SolitaryConfinementFactory.Create(_alice, bus, replacements, triggers: null);
        PlaceOnBattlefield(_alice, sc, zones);

        // Shroud — Alice can't be targeted by anyone (CR 702.18a).
        _alice.HasShroud.Should().BeTrue();
        var spec = new TargetSpec("any").AnyCreatureOrPlayer();
        TargetLegality.IsLegal(spec, _alice, _alice).Should().BeFalse();
        TargetLegality.IsLegal(spec, _alice, _bob).Should().BeFalse();

        // Damage to Alice is prevented.
        var attacker = new Creature("Goblin", "{R}", 2, 2) { Owner = _bob };
        replacements.Apply(new DamageIntent(attacker, 3, TargetPlayer: _alice)).Should().BeNull();

        // Skip-draw registered for Alice.
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeTrue();

        // Leaves play → shroud drops, damage no longer prevented.
        zones.MoveCard(sc, ZoneType.Battlefield, ZoneType.Graveyard);
        _alice.HasShroud.Should().BeFalse();
        replacements.Apply(new DamageIntent(attacker, 3, TargetPlayer: _alice)).Should().NotBeNull();
        SkipDrawRegistry.Clear();
    }

    [Fact]
    public void SolitaryConfinement_Upkeep_SacrificesWhenHandEmpty()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var sc = SolitaryConfinementFactory.Create(_alice, bus, replacements: null, triggers: null);
        PlaceOnBattlefield(_alice, sc, zones);
        // No cards in hand → can't discard → sacrifice.
        SolitaryConfinementFactory.ResolveUpkeep(sc, bus);
        sc.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void SolitaryConfinement_Upkeep_DiscardsToSurvive_NoAgent_DefaultDiscards()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var sc = SolitaryConfinementFactory.Create(_alice, bus, replacements: null, triggers: null);
        PlaceOnBattlefield(_alice, sc, zones);

        var card = new Instant("Opt", "{U}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        SolitaryConfinementFactory.ResolveUpkeep(sc, bus);

        sc.Zone.Should().Be(ZoneType.Battlefield, "the default (no agent) discards to keep it");
        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ── Serra's Emissary (CR 702.16 player + creatures) ───────────────────

    [Fact]
    public void SerrasEmissary_Grant_ProtectsPlayerAndControlledCreatures()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var continuous = new ContinuousEffectsService();
        var replacements = new ReplacementBus();

        var emissary = SerrasEmissaryFactory.Create(_alice, continuous, replacements, triggers: null, eventBus: bus);
        PlaceOnBattlefield(_alice, emissary, zones);

        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        PlaceOnBattlefield(_alice, bear, zones);

        // Choose "instants".
        var token = new object();
        SerrasEmissaryFactory.Grant(emissary, token, CardType.Instant, continuous, replacements, bus);
        continuous.Compute(bear); // reconcile group grant

        // Player half — Alice has protection from instants.
        _alice.HasProtectionFromCardType(CardType.Instant).Should().BeTrue();

        // Creatures half — the bear gained "protection from instants".
        Protection.HasProtectionFromCardType(bear, CardType.Instant).Should().BeTrue();
        Protection.HasProtectionFromCardType(bear, CardType.Creature).Should().BeFalse();

        // Damage to Alice from an instant source is prevented.
        var instantSrc = new Instant("Bolt", "{R}") { Owner = _bob };
        replacements.Apply(new DamageIntent(instantSrc, 3, TargetPlayer: _alice)).Should().BeNull();
    }

    private static void PlaceOnBattlefield(Player controller, ICard card, ZoneService zones)
    {
        if (card is Permanent perm) perm.SetController(controller);
        controller.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield);
    }
}
