using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StormchasersTalentFactory"/>.
///
/// Covers (full Class leveling — CR 716 — see <c>ClassLevelingTests</c> for
/// the per-level activation + cast-trigger behavioural sweep):
/// - Card identity (name, Enchantment type, Class subtype, mana cost, owner/
///   controller).
/// - Ability set: the ETB <see cref="TriggeredAbility"/> + two level-up
///   <see cref="ActivatedAbility"/>s + two per-level cast triggers.
/// - ETB resolution: spawns a 1/1 Mercenary creature token with a
///   <c>"Prowess"</c> <see cref="KeywordAbility"/> marker under the
///   controller's battlefield.
/// - <see cref="NamedCardFactory"/> dispatch returns a fully-wired
///   Stormchaser's Talent instance.
/// </summary>
public class StormchasersTalentFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StormchasersTalent_NameIsCorrect()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        c.Name.Should().Be("Stormchaser's Talent");
    }

    [Fact]
    public void StormchasersTalent_IsEnchantmentClass()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        c.HasType(CardType.Enchantment).Should().BeTrue(
            "printed oracle is Enchantment — Class");
        c.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "CR 205.3h — Class is an enchantment subtype (CR 716)");
    }

    [Fact]
    public void StormchasersTalent_HasPrintedManaCost()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        // Printed cost is {U}{R}. ManaCost.Parse round-trips via one blue
        // pip + one red pip (total mana value 2).
        var parsed = ManaCost.Parse(StormchasersTalentFactory.PrintedManaCost);
        parsed.Blue.Should().Be(1, "the printed cost is one blue pip");
        parsed.Red.Should().Be(1, "the printed cost is one red pip");
        parsed.TotalValue.Should().Be(2);
        c.ManaCost.Should().Be(StormchasersTalentFactory.PrintedManaCost);
    }

    [Fact]
    public void StormchasersTalent_OwnerAndControllerAreSet()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set — v1 ETB-only scope
    // -----------------------------------------------------------------------

    [Fact]
    public void StormchasersTalent_HasThreeTriggeredAbilities_EtbPlusTwoCastTriggers()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "the ETB Mercenary-token trigger + the Level-2 and Level-3 " +
            "noncreature-spell-cast triggers (gated by ClassState.CurrentLevel)");
    }

    [Fact]
    public void StormchasersTalent_HasTwoLevelUpActivatedAbilities_BothSorcerySpeed()
    {
        var c = StormchasersTalentFactory.Create(_alice);

        var levelUps = c.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activated ability per printed level above 1 " +
            "({1}{U}{R}: Level 2 and {3}{U}{R}: Level 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 / CR 307.5 — Class level-up activations are sorcery-speed only");
    }

    [Fact]
    public void StormchasersTalent_ClassStateAttached_LevelOne_MaxThree()
    {
        var c = StormchasersTalentFactory.Create(_alice);
        ((Majik.Core.Cards.Permanent)c).ClassState.Should().NotBeNull(
            "CR 716 — Class enchantments carry a leveling tracker (mirrors SagaState)");
        ((Majik.Core.Cards.Permanent)c).ClassState!.CurrentLevel.Should().Be(1);
        ((Majik.Core.Cards.Permanent)c).ClassState!.MaxLevel.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — spawns the Mercenary token
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 603.6a — "When this Class enters, create a 1/1 blue and red
    /// Mercenary creature token with prowess." Token shape: 1/1 Creature
    /// with the Mercenary subtype and a <c>"Prowess"</c>
    /// <see cref="KeywordAbility"/> marker. Token colour identity (blue +
    /// red) deferred — see factory xmldoc.
    /// </summary>
    [Fact]
    public void StormchasersTalent_Etb_CreatesOneOneMercenaryTokenWithProwess()
    {
        var zones = new ZoneService(_bus);
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var talent = StormchasersTalentFactory.Create(_alice, zones, triggers);
        talent.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(talent);
        triggers.BindCard(talent);

        // ETB via ZoneService — fires CardMovedEvent so the trigger queues.
        zones.MoveCardTo(talent, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(1,
            "ETB trigger must queue when Stormchaser's Talent enters the battlefield");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // One new creature on Alice's battlefield — the Mercenary token.
        var newCreatures = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, talent))
            .ToList();

        newCreatures.Should().HaveCount(1, "ETB spawns exactly one Mercenary token");

        var token = newCreatures[0];
        token.Name.Should().Be("Mercenary");
        token.IsToken.Should().BeTrue("CR 111 — Mercenary is a token");
        token.HasSubtype(CardSubtype.Mercenary).Should().BeTrue(
            "the spawned token carries the Mercenary creature subtype");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.Controller.Should().BeSameAs(_alice);
        token.Owner.Should().BeSameAs(_alice);

        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Prowess",
                "CR 702.108 — printed token has Prowess; KeywordAbility marker " +
                "attached via TokenFactory's Keywords list (live pump deferred — " +
                "TokenFactory doesn't thread ContinuousEffectsService for token-" +
                "resident keywords yet)");
    }

    /// <summary>
    /// Single-arg dispatcher path still attaches the ETB trigger to the
    /// card shape (no bus-driven firing). Test invokes the trigger's
    /// effect directly to confirm the token-spawn body runs.
    /// </summary>
    [Fact]
    public void StormchasersTalent_SingleArgPath_AttachesEtbTriggerShape()
    {
        var talent = StormchasersTalentFactory.Create(_alice);
        talent.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(talent);

        // The ETB trigger is the one whose condition is OnEnterBattlefieldSelf —
        // the two cast-triggers also live on the card now (gated by Level >= N).
        // The ETB sits first; we identify it positionally + by zone-active filter.
        var etb = talent.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects)
        {
            effect.Execute();
        }

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => !ReferenceEquals(c, talent));

        token.Name.Should().Be("Mercenary");
        token.HasSubtype(CardSubtype.Mercenary).Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchesStormchasersTalent()
    {
        var card = NamedCardFactory.Create("Stormchaser's Talent", _alice);

        card.Should().BeOfType<Enchantment>(
            "Stormchaser's Talent is an Enchantment — Class");
        card.Name.Should().Be("Stormchaser's Talent");
        card.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "the dispatcher returns a fully-wired card with the Class subtype");

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "the dispatcher attaches the ETB trigger + Level-2 + Level-3 cast triggers");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the dispatcher attaches both level-up activated abilities (Level 2 + Level 3)");
    }
}
