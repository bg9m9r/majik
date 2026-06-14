using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WeddingInvitationFactory"/> — Wedding Invitation
/// (Innistrad: Crimson Vow, {2}, Artifact).
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "When this artifact enters, draw a card.
///    {T}, Sacrifice this artifact: Target creature can't be blocked this
///    turn. If it's a Vampire, it also gains lifelink until end of turn."
///
/// Covers the card's UNIQUE behaviour:
/// - <b>ETB cantrip (CR 603.6e / CR 121.1)</b> — "When this enters, draw a card."
/// - <b>{T}, Sacrifice: target creature can't be blocked (CR 509.1c)</b>, the
///   same single-target <see cref="CombatRestrictionEffect"/> grant Slip
///   Through Space / Rogue's Passage install, EOT-scoped (CR 514.2).
/// - <b>Vampire rider (CR 613.1c)</b> — "If it's a Vampire, it also gains
///   lifelink until end of turn." A
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Lifelink") layered on
///   only when the target has the Vampire subtype (mirrors Heliod's lifelink
///   grant).
/// - Identity (mana cost / mana value) for the non-vanilla {2} artifact.
///
/// (NamedCardFactory dispatch + well-formedness are covered for every
/// implemented card by CardFactoryContractTests — not re-asserted here.)
/// </summary>
[Trait("Color", "C")]
public class WeddingInvitationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeCreature(
        Player owner, string name, params CardSubtype[] subtypes)
    {
        var c = new Creature(name, "1G", 2, 2, subtypes: subtypes);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = new ContinuousEffectsService();
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_ColorlessArtifactAtTwo()
    {
        var card = WeddingInvitationFactory.Create(_alice);

        card.Name.Should().Be("Wedding Invitation");
        card.ManaCost.Should().Be("{2}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── ETB cantrip ──────────────────────────────────────────────────────

    [Fact]
    public void HasEtbDrawTrigger()
    {
        // CR 603.6e — "When this artifact enters, draw a card."
        var card = WeddingInvitationFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }

    [Fact]
    public void EtbTrigger_DrawsACard()
    {
        // CR 121.1 — the ETB draws one card.
        var top = new Sorcery("Opt", "{U}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var card = WeddingInvitationFactory.Create(_alice);
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects)
        {
            effect.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "Wedding Invitation draws a card when it enters.");
    }

    // ── {T}, Sacrifice activated ability ─────────────────────────────────

    [Fact]
    public void HasTapSacrificeActivatedAbility()
    {
        var card = WeddingInvitationFactory.Create(_alice);

        var act = card.Abilities.OfType<ActivatedAbility>().Single();
        act.TargetRequests.Should().ContainSingle();
        act.TargetRequests[0].MinTargets.Should().Be(1);
        act.TargetRequests[0].MaxTargets.Should().Be(1);
        act.TargetRequests[0].Description.Should().Be("target creature");
    }

    [Fact]
    public void Activate_NonVampireTarget_GetsUnblockableNoLifelink()
    {
        // CR 509.1c — target creature can't be blocked this turn.
        // CR 613.1c — the lifelink rider only applies to a Vampire.
        var card = WeddingInvitationFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var target = MakeCreature(_alice, "Grizzly Bears"); // not a Vampire
        var act = card.Abilities.OfType<ActivatedAbility>().Single();
        act.SetChosenTargets(new[] { new object[] { target } });

        foreach (var effect in act.Effects) effect.Execute();

        target.ActiveEffects!
            .HasRestriction(target, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("Wedding Invitation makes the target unblockable.");
        target.ActiveEffects!.Compute(target).Keywords
            .Any(k => string.Equals(k, "Lifelink", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse(
                "the lifelink rider only applies when the target is a Vampire.");
    }

    [Fact]
    public void Activate_VampireTarget_AlsoGainsLifelink()
    {
        // CR 613.1c — "If it's a Vampire, it also gains lifelink until end of turn."
        var card = WeddingInvitationFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var vampire = MakeCreature(_alice, "Vampire Nighthawk", CardSubtype.Vampire);
        var act = card.Abilities.OfType<ActivatedAbility>().Single();
        act.SetChosenTargets(new[] { new object[] { vampire } });

        foreach (var effect in act.Effects) effect.Execute();

        vampire.ActiveEffects!
            .HasRestriction(vampire, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue();
        vampire.ActiveEffects!.Compute(vampire).Keywords
            .Any(k => string.Equals(k, "Lifelink", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue(
                "a Vampire target also gains lifelink until end of turn.");
    }

    [Fact]
    public void Activate_SacrificesSelf()
    {
        // CR 602 — sacrifice this artifact is part of the activation cost.
        var card = WeddingInvitationFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var target = MakeCreature(_alice, "Grizzly Bears");
        var act = card.Abilities.OfType<ActivatedAbility>().Single();
        act.SetChosenTargets(new[] { new object[] { target } });

        foreach (var effect in act.Effects) effect.Execute();

        card.Zone.Should().Be(ZoneType.Graveyard,
            "Wedding Invitation sacrifices itself as part of its activation.");
        _alice.Zones.Graveyard.GetCards().Should().Contain((Card)card);
    }
}
