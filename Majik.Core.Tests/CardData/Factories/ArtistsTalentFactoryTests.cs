using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Classes;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArtistsTalentFactory"/> (Bloomburrow, {1}{R}).
///
/// Enchantment — Class {1}{R}. Oracle text:
///   "(Gain the next level as a sorcery to add its ability.)
///    Whenever you cast a noncreature spell, you may discard a card. If you
///      do, draw a card.
///    {2}{R}: Level 2
///    Noncreature spells you cast cost {1} less to cast.
///    {2}{R}: Level 3
///    If a source you control would deal noncombat damage to an opponent or
///      a permanent an opponent controls, it deals that much damage plus 2
///      instead."
///
/// Covers:
/// - Card identity (name, Enchantment — Class, mana cost, owner/controller).
/// - Class state binder: Level 1, MaxLevel 3, per-level costs {2}{R} / {2}{R}.
/// - Ability set: one Level-1 cast trigger (rummage) + two sorcery-speed
///   level-up activated abilities; the Level-2 cost-reduction static and
///   the Level-3 damage-increase replacement are registered against the
///   supplied services (gated on <see cref="ClassState.CurrentLevel"/>).
/// - Level-1 rummage trigger: at level 1, casting a noncreature spell
///   discards then draws (v1 deterministic "may" → always discards when
///   hand non-empty).
/// - Level-2 cost reduction: only active at level &gt;= 2; reduces
///   noncreature spells by {1} generic, never creature spells.
/// - Level-3 damage increase: only active at level 3; +2 noncombat damage
///   to opponents / their permanents, never to the controller and never
///   to combat damage.
/// - <see cref="NamedCardFactory"/> dispatch returns a wired Class instance.
/// </summary>
public class ArtistsTalentFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtistsTalent_NameIsCorrect()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        c.Name.Should().Be("Artist's Talent");
    }

    [Fact]
    public void ArtistsTalent_IsEnchantmentClass()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        c.HasType(CardType.Enchantment).Should().BeTrue("printed oracle is Enchantment — Class");
        c.HasSubtype(CardSubtype.Class).Should().BeTrue(
            "CR 205.3h — Class is an enchantment subtype (CR 716)");
    }

    [Fact]
    public void ArtistsTalent_HasPrintedManaCost()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        var parsed = ManaCost.Parse(ArtistsTalentFactory.PrintedManaCost);
        parsed.Generic.Should().Be(1, "the printed cost is {1}{R}");
        parsed.Red.Should().Be(1);
        parsed.TotalValue.Should().Be(2);
        c.ManaCost.Should().Be(ArtistsTalentFactory.PrintedManaCost);
    }

    [Fact]
    public void ArtistsTalent_OwnerAndControllerAreSet()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtistsTalent_HasOneCastTrigger_TheLevelOneRummage()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "only the Level-1 'whenever you cast a noncreature spell, rummage' trigger " +
            "is a triggered ability; Levels 2 and 3 are a static + a replacement");
    }

    [Fact]
    public void ArtistsTalent_HasTwoLevelUpActivatedAbilities_BothSorcerySpeed()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        var levelUps = c.Abilities.OfType<ActivatedAbility>().ToList();
        levelUps.Should().HaveCount(2,
            "CR 716 — one level-up activated ability per level above 1 ({2}{R}: Level 2 / Level 3)");
        levelUps.Should().OnlyContain(a => a.IsSorcerySpeed,
            "CR 716.3 — Class level-up activations are sorcery-speed only");
    }

    [Fact]
    public void ArtistsTalent_ClassStateAttached_LevelOne_MaxThree()
    {
        var c = ArtistsTalentFactory.Create(_alice);
        var state = ((Permanent)c).ClassState;
        state.Should().NotBeNull("CR 716 — Class enchantments carry a leveling tracker");
        state!.CurrentLevel.Should().Be(1);
        state.MaxLevel.Should().Be(3);
        state.CostFor(2).Should().Be(ManaCost.Parse("{2}{R}"));
        state.CostFor(3).Should().Be(ManaCost.Parse("{2}{R}"));
    }

    // -----------------------------------------------------------------------
    // Level-1 rummage trigger
    // -----------------------------------------------------------------------

    /// <summary>
    /// "Whenever you cast a noncreature spell, you may discard a card. If you
    /// do, draw a card." v1 deterministic "may" → always discards when the
    /// hand is non-empty, then draws. Net hand size unchanged; one card moved
    /// to graveyard, top of library moved to hand.
    /// </summary>
    [Fact]
    public void ArtistsTalent_LevelOne_Rummage_DiscardsThenDraws_OnNoncreatureCast()
    {
        var (card, _, stack, triggers) = Wire();

        // Hand: one card to pitch. Library: one card to draw.
        var inHand = new Instant("Shock", "R") { Owner = _alice };
        inHand.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(inHand);

        var inLibrary = new Instant("Opt", "U") { Owner = _alice };
        inLibrary.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(inLibrary);

        // Cast a noncreature spell.
        var spell = new Majik.Core.Spells.Spell(new Instant("Bolt", "R") { Owner = _alice }, _alice);
        _bus.Publish(new Majik.Core.Domain.DomainEvents.SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(1, "the Level-1 rummage trigger queues on a noncreature cast");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Graveyard.GetCards().Should().Contain(inHand, "the pitched card lands in the graveyard");
        _alice.Zones.Hand.GetCards().Should().Contain(inLibrary, "the drawn card lands in hand");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1, "discard one, draw one → net hand unchanged");
    }

    [Fact]
    public void ArtistsTalent_LevelOne_Rummage_DoesNotTriggerOnCreatureSpell()
    {
        var (_, _, _, triggers) = Wire();
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(creature, _alice);
        _bus.Publish(new Majik.Core.Domain.DomainEvents.SpellCastEvent(spell));

        triggers.PendingCount.Should().Be(0, "the rummage trigger only fires on noncreature spells");
    }

    // -----------------------------------------------------------------------
    // Level-2 cost reduction
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtistsTalent_LevelTwo_ReducesNoncreatureSpellByOne_NotBeforeLevelTwo()
    {
        var (_, state, _, _) = Wire();

        var noncreature = new Instant("Big Spell", "4R") { Owner = _alice };

        // Level 1 — no reduction yet.
        CostReduction.GetEffectiveCost(noncreature, _alice).Generic.Should().Be(4,
            "the cost reducer is gated on ClassState level >= 2");

        state.LevelUpTo(2);
        state.LevelUpTo(3); // intentionally over-level; reduction still applies at >= 2

        CostReduction.GetEffectiveCost(noncreature, _alice).Generic.Should().Be(3,
            "at level >= 2 noncreature spells you cast cost {1} less (generic only)");
    }

    [Fact]
    public void ArtistsTalent_LevelTwo_DoesNotReduceCreatureSpells()
    {
        var (_, state, _, _) = Wire();
        state.LevelUpTo(2);

        var creature = new Creature("Dragon", "4R", 4, 4) { Owner = _alice };
        CostReduction.GetEffectiveCost(creature, _alice).Generic.Should().Be(4,
            "the reduction is restricted to noncreature spells");
    }

    // -----------------------------------------------------------------------
    // Level-3 damage increase
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtistsTalent_LevelThree_AddsTwoToNoncombatDamageToOpponent()
    {
        var (card, state, _, _) = Wire();
        var replacements = new ReplacementBus();
        ArtistsTalentFactory.RegisterLevelThreeDamage(card, state, replacements);

        var source = new Instant("Bolt", "R") { Owner = _alice };
        source.SetController(_alice);
        var intent = new DamageIntent(source, 3, TargetPlayer: _bob);

        // Level 1 — no increase.
        replacements.Apply(intent)!.Amount.Should().Be(3,
            "the damage increase is gated on level 3");

        state.LevelUpTo(2);
        state.LevelUpTo(3);

        replacements.Apply(intent)!.Amount.Should().Be(5,
            "at level 3, noncombat damage from a source you control to an opponent deals +2");
    }

    [Fact]
    public void ArtistsTalent_LevelThree_DoesNotIncreaseCombatDamage()
    {
        var (card, state, _, _) = Wire();
        var replacements = new ReplacementBus();
        ArtistsTalentFactory.RegisterLevelThreeDamage(card, state, replacements);
        state.LevelUpTo(2);
        state.LevelUpTo(3);

        var source = new Creature("Attacker", "R", 3, 3) { Owner = _alice };
        source.SetController(_alice);
        var combat = new DamageIntent(source, 3, TargetPlayer: _bob) { IsCombatDamage = true };

        replacements.Apply(combat)!.Amount.Should().Be(3,
            "the clause only increases NONCOMBAT damage");
    }

    [Fact]
    public void ArtistsTalent_LevelThree_DoesNotIncreaseDamageToController()
    {
        var (card, state, _, _) = Wire();
        var replacements = new ReplacementBus();
        ArtistsTalentFactory.RegisterLevelThreeDamage(card, state, replacements);
        state.LevelUpTo(2);
        state.LevelUpTo(3);

        var source = new Instant("Bolt", "R") { Owner = _alice };
        source.SetController(_alice);
        var selfHit = new DamageIntent(source, 3, TargetPlayer: _alice);

        replacements.Apply(selfHit)!.Amount.Should().Be(3,
            "the clause only increases damage to an opponent or a permanent they control");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchesArtistsTalent()
    {
        var card = NamedCardFactory.Create("Artist's Talent", _alice);
        card.Should().BeOfType<Enchantment>("Artist's Talent is an Enchantment — Class");
        card.Name.Should().Be("Artist's Talent");
        card.HasSubtype(CardSubtype.Class).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the dispatcher attaches both level-up activated abilities");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the dispatcher attaches the Level-1 rummage trigger");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (Enchantment Card, ClassState State, Majik.Core.Stack.Stack Stack, TriggerManager Triggers) Wire()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var card = ArtistsTalentFactory.Create(_alice, triggers, _bus);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        triggers.BindCard(card);

        var state = ((Permanent)card).ClassState!;
        return (card, state, stack, triggers);
    }
}
