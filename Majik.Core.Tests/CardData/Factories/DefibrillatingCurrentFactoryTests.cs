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
/// Tests for <see cref="DefibrillatingCurrentFactory"/> (Murders at Karlov
/// Manor, {2/R}{2/W}{2/B}, Sorcery).
///
/// Oracle text (verified against the embedded Scryfall seed):
///   "Defibrillating Current deals 4 damage to target creature or planeswalker
///    and you gain 2 life."
///
/// Unique behaviour covered here (the contract test in CardFactoryContractTests
/// already asserts dispatch + well-formedness):
///   - Identity: {2/R}{2/W}{2/B} three-color twobrid cost, mana value 6 (CR
///     202.3f), R/W/B color identity.
///   - SpellDefinition shape: single 1..1 "target creature or planeswalker"
///     request, no X.
///   - Resolve deals 4 damage to a target creature, caster gains 2 life.
///   - Resolve removes 4 loyalty from a target planeswalker (CR 306.7), caster
///     gains 2 life.
///   - Lifegain still resolves when the damage clause's target is illegal
///     (CR 608.2c — only the targeted clause is skipped).
/// </summary>
[Trait("Color", "M")]
public class DefibrillatingCurrentFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void DefibrillatingCurrent_Identity_SorceryThreeColorTwobrid()
    {
        var card = DefibrillatingCurrentFactory.Create(_alice);

        card.Name.Should().Be("Defibrillating Current");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2/R}{2/W}{2/B}");
        card.ManaCostValue.TotalValue.Should().Be(6,
            because: "each {2/X} twobrid pip counts its generic alternative of 2 (CR 202.3f)");
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(new[] { ManaColor.Red, ManaColor.White, ManaColor.Black });
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Spell definition shape ───────────────────────────────────────────────

    [Fact]
    public void DefibrillatingCurrent_SpellDefinition_SingleCreatureOrPlaneswalkerTarget_NoX()
    {
        var def = DefibrillatingCurrentFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature or planeswalker");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage + lifegain ────────────────────────────────────────────────────

    [Fact]
    public void DefibrillatingCurrent_Resolve_DealsFourDamageToCreature_AndGainsTwoLife()
    {
        var creature = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveAt(creature);

        creature.Damage.Should().Be(4,
            because: "Defibrillating Current deals 4 damage to the target creature (CR 120.1a)");
        _alice.LifeTotal.Should().Be(22,
            because: "and you gain 2 life (CR 119.3)");
    }

    [Fact]
    public void DefibrillatingCurrent_Resolve_RemovesFourLoyaltyFromPlaneswalker_AndGainsTwoLife()
    {
        var pw = NewControlledPlaneswalker(_bob, "Liliana of the Veil", "{1}{B}{B}", startingLoyalty: 6);

        ResolveAt(pw);

        pw.Loyalty.Should().Be(2,
            because: "4 damage to a planeswalker removes 4 loyalty (CR 306.7)");
        _alice.LifeTotal.Should().Be(22,
            because: "and you gain 2 life (CR 119.3)");
    }

    [Fact]
    public void DefibrillatingCurrent_Resolve_IllegalTarget_StillGainsTwoLife()
    {
        // A target that is no longer a creature/planeswalker on the battlefield
        // (here: a card in hand) is illegal for the damage clause (CR 608.2b),
        // but the untargeted "you gain 2 life" clause still resolves (CR 608.2c).
        var stale = new Card("Bygone Bishop", "{2}{W}");
        stale.SetOwner(_bob);
        stale.SetZone(ZoneType.Hand);

        ResolveAt(stale);

        _alice.LifeTotal.Should().Be(22,
            because: "the lifegain is not tied to the (now-illegal) damage target (CR 608.2c)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ResolveAt(object target)
    {
        var def = DefibrillatingCurrentFactory.BuildSpellDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();
    }

    private static Creature NewControlledCreature(
        Player owner, string name, string cost, int power, int toughness)
    {
        var creature = new Creature(name, cost, power, toughness);
        creature.SetOwner(owner);
        creature.SetController(owner);
        creature.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(creature);
        return creature;
    }

    private static Planeswalker NewControlledPlaneswalker(
        Player owner, string name, string cost, int startingLoyalty)
    {
        var pw = new Planeswalker(name, cost, startingLoyalty);
        pw.SetOwner(owner);
        pw.SetController(owner);
        pw.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(pw);
        return pw;
    }
}
