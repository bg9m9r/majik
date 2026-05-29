using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HellriderFactory"/> (Dark Ascension, {2}{R}{R}).
///
/// Creature — Devil 3/3. Oracle text:
///   "Haste.
///    Whenever a creature you control attacks, this creature deals 1 damage
///    to the player or planeswalker it's attacking."
///
/// Covers:
///   - Identity (Creature — Devil, {2}{R}{R}, 3/3, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Printed Haste keyword marker (CR 702.10).
///   - Attack trigger matches ANY creature the controller controls
///     (including Hellrider itself), not opponents' attackers (CR 508.1f /
///     CR 109.5 — "you control").
///   - Resolution deals 1 damage to the attacked Player (life loss).
///   - Resolution deals 1 damage to the attacked Planeswalker (loyalty loss).
/// </summary>
public class HellriderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Hellrider_Identity()
    {
        var c = HellriderFactory.Create(_alice);

        c.Name.Should().Be("Hellrider");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Devil).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Hellrider_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Hellrider", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hellrider");
        card.HasSubtype(CardSubtype.Devil).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
    }

    [Fact]
    public void Hellrider_HasPrintedHaste()
    {
        var c = HellriderFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "CR 702.10 — printed Haste.");
        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Attack trigger shape — "a creature you control attacks"
    // -----------------------------------------------------------------------

    [Fact]
    public void Hellrider_AttackTrigger_Matches_OnControllerCreatureAttack()
    {
        var c = HellriderFactory.Create(_alice);
        var trig = GetAttackTrigger(c);

        // Hellrider itself attacking.
        trig.Condition.Matches(new CreatureAttacksEvent(c, _bob), trig)
            .Should().BeTrue("Hellrider attacking is 'a creature you control attacks'.");

        // Another creature Alice controls attacking.
        var other = MakeCreature(_alice);
        trig.Condition.Matches(new CreatureAttacksEvent(other, _bob), trig)
            .Should().BeTrue("any creature you control triggers Hellrider (CR 508.1f).");
    }

    [Fact]
    public void Hellrider_AttackTrigger_DoesNotMatch_OnOpponentAttacker()
    {
        var c = HellriderFactory.Create(_alice);
        var trig = GetAttackTrigger(c);

        // Opponent's creature attacking (e.g. into Alice) — not "you control".
        var oppCreature = MakeCreature(_bob);
        trig.Condition.Matches(new CreatureAttacksEvent(oppCreature, _alice), trig)
            .Should().BeFalse("CR 109.5 — only creatures you control trigger Hellrider.");
    }

    // -----------------------------------------------------------------------
    // Resolution — damage to the attacked player / planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Hellrider_AttackTriggerEffect_DealsOneDamageToAttackedPlayer()
    {
        var c = HellriderFactory.Create(_alice);
        var trig = GetAttackTrigger(c);

        // A creature Alice controls attacks Bob.
        var attacker = MakeCreature(_alice);
        trig.Condition.Matches(new CreatureAttacksEvent(attacker, _bob), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(19,
            "Hellrider deals 1 damage to the player the attacker is attacking (CR 119).");
    }

    [Fact]
    public void Hellrider_AttackTriggerEffect_DealsOneDamageToAttackedPlaneswalker()
    {
        var c = HellriderFactory.Create(_alice);
        var trig = GetAttackTrigger(c);

        var pw = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var attacker = MakeCreature(_alice);
        trig.Condition.Matches(new CreatureAttacksEvent(attacker, pw), trig)
            .Should().BeTrue();

        foreach (var effect in trig.Effects) effect.Execute();

        pw.Loyalty.Should().Be(2,
            "1 damage to a planeswalker removes 1 loyalty (CR 306.7 / CR 120.3).");
        _bob.LifeTotal.Should().Be(20, "damage went to the planeswalker, not the player.");
    }
}
