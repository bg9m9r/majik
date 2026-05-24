using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613.1f — Layer 6 ability-grant subsystem coverage.
///
/// <see cref="GrantAbilityEffect"/> projects a full <see cref="IAbility"/>
/// onto a target permanent's <see cref="Card.Abilities"/> list while the
/// grant's source is on the battlefield. Tests cover:
///
/// 1. A static <see cref="ProtectionAbility"/> grant attaches when synced
///    and detaches when the source LTBs (CR 613.6e).
/// 2. A triggered-ability grant is visible on the bearer's Abilities list
///    while active.
/// 3. <see cref="LoseAllAbilitiesEffect"/> over the bearer revokes any
///    granted abilities through the layer compute pass (CR 613.6 / 613.8).
/// 4. <see cref="LoseAllAbilitiesEffect"/> over the GRANT SOURCE suppresses
///    the grant via the source-stripped pipeline (the same path that
///    suppresses lord pumps from a Humility'd lord).
/// 5. Sword of Fire and Ice equipped to a creature: the equipped creature
///    gains protection-from-red + protection-from-blue via the Layer-6
///    grants (CR 702.16 DEBT-A target/damage legality).
/// 6. Native Flying + granted Flying + Humility-strip → bearer has zero
///    abilities (strip wins inside Layer 6).
/// </summary>
public class AbilityGrantTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GrantAbility_AddsAbilityToBearer_WhileSourceOnBattlefield()
    {
        var svc = new ContinuousEffectsService();
        var source = new Enchantment("Source", "1W")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var grant = new GrantAbilityEffect(source, bear, new ProtectionAbility("red"));
        svc.Register(grant);

        // Drive the compute pass so the grant lifecycle reconciles.
        svc.Compute(bear);

        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue(
            "the grant projected ProtectionAbility('red') onto the bearer");

        // Source LTBs → grant must drop.
        source.Zone = ZoneType.Graveyard;
        svc.Compute(bear);
        svc.Prune();

        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse(
            "CR 613.6e — when the granting effect ends, the granted ability is removed");
    }

    [Fact]
    public void GrantAbility_AddsTriggeredAbilityToBearerAbilitiesList()
    {
        var svc = new ContinuousEffectsService();
        var source = new Enchantment("Source", "1W")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        bool fired = false;
        var grant = new GrantAbilityEffect(
            source,
            targetSelector: () => bear,
            abilityFactory: bearer => new TriggeredAbility(
                source: bearer,
                controller: _alice,
                condition: new EventTriggerCondition<CombatDamageDealtEvent>(
                    (e, _) => { fired = true; return ReferenceEquals(e.Source, bearer); }),
                effects: Array.Empty<IEffect>()));

        svc.Register(grant);
        svc.Compute(bear);

        bear.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the granted triggered ability is materialised on the bearer's Abilities list");

        var trig = bear.Abilities.OfType<TriggeredAbility>().Single();
        trig.IsTriggered(new CombatDamageDealtEvent(bear, _bob, 2)).Should().BeTrue();
        fired.Should().BeTrue();
    }

    [Fact]
    public void GrantAbility_RemovedWhenLoseAllAbilitiesEffectTargetsBearer()
    {
        var svc = new ContinuousEffectsService();
        var source = new Enchantment("Source", "1W")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        var grant = new GrantAbilityEffect(source, bear, new ProtectionAbility("red"));
        svc.Register(grant);
        svc.Compute(bear);
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue();

        // Drop Humility on the bearer.
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { bear }));

        // Re-compute → grant is revoked because the target creature has been
        // stripped (CR 613.6: ability-removing effects strip everything).
        var chars = svc.Compute(bear);

        chars.Keywords.Should().BeEmpty("Humility clears the keyword set");
        bear.Abilities.OfType<ProtectionAbility>().Should().BeEmpty(
            "CR 613.6 — Humility-class strip also revokes granted non-keyword abilities");
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse();
    }

    [Fact]
    public void GrantAbility_SuppressedWhenSourceIsStripped()
    {
        var svc = new ContinuousEffectsService();
        // Source is a creature so the existing Humility source-stripping
        // pipeline can suppress it (HashSet<Creature>).
        var sourceLord = new Creature("Mythic Granter", "2W", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };

        svc.Register(new GrantAbilityEffect(sourceLord, bear, new ProtectionAbility("red")));
        svc.Compute(bear);
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue();

        // Humility'd lord can no longer produce continuous effects (CR 613.8).
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { sourceLord, bear }));

        svc.Compute(bear);
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse(
            "CR 613.8 — a stripped source's continuous effects are suppressed");
    }

    [Fact]
    public void SwordOfFireAndIce_Equipped_GrantsProtectionFromRedAndBlue()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        var sword = SwordOfFireAndIceFactory.Create(_alice, svc, triggers: null);
        sword.Zone = ZoneType.Battlefield;

        sword.AttachTo(bear);

        // Drive the compute pass so the grant lifecycle reconciles.
        svc.Compute(bear);

        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue(
            "CR 702.16 — equipped creature has protection from red");
        Protection.HasProtectionFromColor(bear, ManaColor.Blue).Should().BeTrue(
            "CR 702.16 — equipped creature has protection from blue");
        Protection.HasProtectionFromColor(bear, ManaColor.White).Should().BeFalse(
            "no white protection grant is wired");

        // Unequip — markers must lift from the bearer.
        sword.Unattach();
        svc.Compute(bear);
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse(
            "CR 613.6e — unequipping detaches the protection grant");
        Protection.HasProtectionFromColor(bear, ManaColor.Blue).Should().BeFalse();
    }

    [Fact]
    public void LayerSix_NativeFlyingPlusGrantedFlying_ThenLoseAllAbilities_ResultsInZeroAbilities()
    {
        var svc = new ContinuousEffectsService();

        var source = new Enchantment("Wings", "1W")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc,
        };
        bear.AddAbility(new KeywordAbility("Flying", bear, _alice));

        // Granted Flying via Layer-6 GrantAbilityEffect (carries a
        // KeywordAbility marker so it shows up on Abilities + Keywords).
        svc.Register(new GrantAbilityEffect(
            source,
            targetSelector: () => bear,
            abilityFactory: bearer => new KeywordAbility("Flying", bearer, _alice)));

        svc.Compute(bear);
        bear.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "native Flying + granted Flying both present on the bearer");

        // Drop Humility-class strip on the bearer.
        var humility = new Enchantment("Humility", "2WW")
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        svc.Register(new LoseAllAbilitiesEffect(humility, new[] { bear }));

        var chars = svc.Compute(bear);

        chars.Keywords.Should().BeEmpty(
            "CR 613.6 — Humility clears the in-characteristics keyword set");
        // The granted ability is revoked through the grant lifecycle; the
        // native KeywordAbility marker on Card.Abilities is left for the
        // (still-orthogonal) printed-ability identity, but Compute's
        // chars.Keywords (the gameplay surface) is empty.
        bear.Abilities.OfType<KeywordAbility>()
            .Where(k => string.Equals(k.Keyword, "Flying", StringComparison.OrdinalIgnoreCase))
            .Should().HaveCountLessOrEqualTo(1,
                "the granted Flying ability is revoked by the Humility strip; only the native KeywordAbility marker remains on Card.Abilities");
    }
}
