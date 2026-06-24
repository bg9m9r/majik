using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StoicSphinxFactory"/>.
///
/// Stoic Sphinx ({2}{U}{U}, Creature — Sphinx 5/3):
///   "Flash / Flying / This creature has hexproof as long as you haven't cast a
///    spell this turn."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, {2}{U}{U}, Sphinx subtype, 5/3) — single Identity assert.
/// - Flash (CR 702.8) + Flying (CR 702.9) keyword markers from the JSON.
/// - Conditional hexproof (CR 702.11 / 613.3): untargetable by opponents while
///   the controller has cast no spell this turn; targetable once they have;
///   the controller can always target it; restored at the next turn boundary.
///   Exercised both via the public flag drivers (shape path) and via the live
///   event bus (SpellCastEvent / TurnStartedEvent).
/// </summary>
[Trait("Color", "U")]
public class StoicSphinxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void StoicSphinx_Identity()
    {
        var c = StoicSphinxFactory.Create(_alice);

        c.Name.Should().Be("Stoic Sphinx");
        c.ManaCost.Should().Be("{2}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Sphinx).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Keywords (Flash + Flying) ───────────────────────────────────────────

    [Fact]
    public void StoicSphinx_HasFlashAndFlying()
    {
        var c = StoicSphinxFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain("Flash", "Stoic Sphinx has Flash (CR 702.8).");
        keywords.Should().Contain("Flying", "Stoic Sphinx has Flying (CR 702.9).");
    }

    // ── Conditional hexproof (CR 702.11) — flag-driven (shape path) ─────────

    private static TargetSpec CreatureTargetSpec() =>
        new TargetSpec("target creature").Creatures();

    private static HexproofWhileYouHaventCastSpellEffect HexproofEffectOn(Creature sphinx) =>
        GetRegisteredEffects(sphinx.ActiveEffects!)
            .OfType<HexproofWhileYouHaventCastSpellEffect>()
            .Single();

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }

    [Fact]
    public void StoicSphinx_NoSpellCast_IsHexproofFromOpponents()
    {
        var svc = new ContinuousEffectsService();
        var sphinx = StoicSphinxFactory.Create(_alice, svc);
        sphinx.SetZone(ZoneType.Battlefield);

        // CR 702.11 — Bob can't target the Sphinx while Alice hasn't cast a spell.
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob)
            .Should().BeFalse("a Sphinx whose controller hasn't cast a spell has hexproof.");
    }

    [Fact]
    public void StoicSphinx_NoSpellCast_ControllerCanStillTarget()
    {
        var svc = new ContinuousEffectsService();
        var sphinx = StoicSphinxFactory.Create(_alice, svc);
        sphinx.SetZone(ZoneType.Battlefield);

        // CR 702.11 — hexproof only blocks OPPONENTS' spells/abilities.
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _alice)
            .Should().BeTrue("hexproof doesn't stop the controller from targeting.");
    }

    [Fact]
    public void StoicSphinx_AfterControllerCastsSpell_IsNotHexproof()
    {
        var svc = new ContinuousEffectsService();
        var sphinx = StoicSphinxFactory.Create(_alice, svc);
        sphinx.SetZone(ZoneType.Battlefield);

        // "...as long as you haven't cast a spell this turn." Once Alice casts a
        // spell, the Sphinx loses hexproof and is a legal target for Bob.
        HexproofEffectOn(sphinx).MarkSpellCastThisTurn();

        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob)
            .Should().BeTrue("after the controller casts a spell the Sphinx loses hexproof.");
    }

    [Fact]
    public void StoicSphinx_HexproofRestored_AtNextTurn()
    {
        var svc = new ContinuousEffectsService();
        var sphinx = StoicSphinxFactory.Create(_alice, svc);
        sphinx.SetZone(ZoneType.Battlefield);

        var effect = HexproofEffectOn(sphinx);

        // Cast a spell → no hexproof.
        effect.MarkSpellCastThisTurn();
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob).Should().BeTrue();

        // New turn resets the per-turn tally (CR 500.1 / 514) → hexproof restored.
        effect.ResetForNewTurn();
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob).Should().BeFalse();
    }

    // ── Conditional hexproof — driven through the live event bus ────────────

    [Fact]
    public void StoicSphinx_BusDriven_ControllerSpellDropsHexproof_OpponentSpellDoesNot()
    {
        var bus = new EventBus();
        var svc = new ContinuousEffectsService(bus);
        var sphinx = StoicSphinxFactory.Create(_alice, svc);
        sphinx.SetZone(ZoneType.Battlefield);

        // Untargetable to start.
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob).Should().BeFalse();

        // Bob's (opponent's) spell does NOT lapse Alice's "haven't cast" clause.
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "BobBolt")));
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob)
            .Should().BeFalse("an opponent's spell doesn't satisfy \"you haven't cast a spell\".");

        // Alice's own spell drops hexproof for the rest of the turn.
        bus.Publish(new SpellCastEvent(NewSpell(_alice, "AliceCantrip")));
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob)
            .Should().BeTrue("once Alice casts a spell, the Sphinx loses hexproof.");

        // Next turn restores it.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));
        TargetLegality.IsLegal(CreatureTargetSpec(), sphinx, _bob)
            .Should().BeFalse("a new turn resets the per-turn cast tally, restoring hexproof.");
    }

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name)
    {
        var instant = new Instant(name, "{U}") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }
}
