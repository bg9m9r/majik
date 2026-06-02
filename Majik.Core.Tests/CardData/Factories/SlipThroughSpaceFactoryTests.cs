using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlipThroughSpaceFactory"/> — Slip Through Space
/// (Oath of the Gatewatch, {U}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Devoid (This card has no color.)
///    Target creature can't be blocked this turn.
///    Draw a card."
///
/// Covers:
/// - Identity (name, type, cost, mana value, owner/controller).
/// - Devoid (CR 702.114) — the {U} card is colorless.
/// - NamedCardFactory dispatch.
/// - "Target creature can't be blocked this turn" (CR 509.1c / CR 702.x) —
///   a single-target <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBeBlocked"/>, EOT-scoped
///   (mirrors <see cref="RoguesPassageFactory"/> / <see cref="DemonicDreadFactory"/>).
/// - Cantrip "Draw a card" (CR 121.1) — mirrors <see cref="SerumVisionsFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class SlipThroughSpaceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = SlipThroughSpaceFactory.Create(_alice);

        card.Name.Should().Be("Slip Through Space");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(1);
    }

    [Fact]
    public void NamedFactory_Dispatch_BuildsSlipThroughSpace()
    {
        var card = NamedCardFactory.Create("Slip Through Space", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Slip Through Space");
    }

    // ── Devoid ───────────────────────────────────────────────────────────

    [Fact]
    public void Devoid_CardIsColorless()
    {
        // CR 702.114 — Devoid: the card has no color despite the {U} pip.
        var card = SlipThroughSpaceFactory.Create(_alice);

        ((Card)card).IsDevoid.Should().BeTrue();
        CardColors.GetColors(card).Should().BeEmpty(
            "Devoid strips the blue pip — Slip Through Space is colorless.");
    }

    [Fact]
    public void Devoid_KeywordAbilityPresent()
    {
        var card = SlipThroughSpaceFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == SlipThroughSpaceFactory.DevoidKeyword)
            .Should().BeTrue();
    }

    // ── Spell definition shape ───────────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = SlipThroughSpaceFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().ContainSingle();
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
    }

    // ── Target creature can't be blocked ─────────────────────────────────

    [Fact]
    public void Resolve_TargetCreature_GetsCannotBeBlockedRestriction()
    {
        // CR 509.1c — chosen creature can't be declared as a blocker's
        // target this turn (it can't be blocked).
        var svc = new ContinuousEffectsService();
        var target = MakeCreature(_alice, "Grizzly Bears");
        target.ActiveEffects = svc;

        SlipThroughSpaceFactory.ApplyCannotBeBlocked(target);

        svc.HasRestriction(target, CombatRestriction.CannotBeBlocked).Should().BeTrue(
            "Slip Through Space makes the target creature unblockable this turn.");
    }

    [Fact]
    public void Resolve_IllegalTarget_OffBattlefield_NoOp()
    {
        // CR 608.2b — target no longer on the battlefield at resolution.
        var svc = new ContinuousEffectsService();
        var target = MakeCreature(_alice, "Grizzly Bears");
        target.ActiveEffects = svc;
        target.SetZone(ZoneType.Graveyard);

        SlipThroughSpaceFactory.ApplyCannotBeBlocked(target);

        svc.HasRestriction(target, CombatRestriction.CannotBeBlocked).Should().BeFalse(
            "an illegal (off-battlefield) target fizzles per CR 608.2b.");
    }

    [Fact]
    public void Resolve_NonCreatureTarget_NoOp()
    {
        var land = NamedCardFactory.Create("Plains", _alice);
        land.SetZone(ZoneType.Battlefield);

        var act = () => SlipThroughSpaceFactory.ApplyCannotBeBlocked(land);
        act.Should().NotThrow();
    }

    // ── Cantrip draw ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DrawsACard()
    {
        // CR 121.1 — "Draw a card." The cantrip happens regardless of the
        // unblockable grant.
        var top = new Sorcery("Opt", "{U}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var target = MakeCreature(_alice, "Grizzly Bears");
        target.ActiveEffects = new ContinuousEffectsService();

        var def = SlipThroughSpaceFactory.BuildDefinition(_alice, o => o);
        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)target } },
            Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "Slip Through Space draws a card on resolution.");
    }
}
