using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EmrakulTheAeonsTornFactory"/>
/// (Rise of the Eldrazi, {15}).
///
/// Legendary Creature — Eldrazi 15/15. Oracle text:
///   "Emrakul, the Aeons Torn can't be countered.
///    When you cast this spell, take an extra turn after this one.
///    Flying, protection from coloured spells, annihilator 6."
///
/// Covers:
///   - Identity (Legendary Creature — Eldrazi, {15}, 15/15).
///   - Flying + Annihilator 6 + Uncounterable markers attached.
///   - Protection-from-coloured-spells predicate rejects coloured / accepts
///     colourless spells.
///   - Cast trigger calls TurnManager.AddExtraTurn on resolution.
///   - Cast-uncounterable: SpellCastFlow stamps Spell.CannotBeCountered,
///     Counterspell-style RemoveFromStack vetoes the pop.
/// </summary>
[Trait("Color", "C")]
public class EmrakulTheAeonsTornFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Emrakul_Identity()
    {
        var emrakul = EmrakulTheAeonsTornFactory.Create(_alice);

        emrakul.Name.Should().Be("Emrakul, the Aeons Torn");
        emrakul.ManaCost.Should().Be("{15}");
        emrakul.HasType(CardType.Creature).Should().BeTrue();
        emrakul.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        emrakul.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        emrakul.BasePower.Should().Be(15);
        emrakul.BaseToughness.Should().Be(15);
        emrakul.Owner.Should().BeSameAs(_alice);
        emrakul.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Emrakul_HasFlying_Annihilator6_Uncounterable_Markers()
    {
        var emrakul = EmrakulTheAeonsTornFactory.Create(_alice);
        var keywords = emrakul.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Flying",
            "CR 702.9 — Flying");
        keywords.Should().Contain(k => k.Keyword == "Annihilator" && k.Arg == 6,
            "CR 702.86 — printed Annihilator 6");
        keywords.Should().Contain(k => k.Keyword == "Uncounterable",
            "CR 701.5b — \"can't be countered\" marker read at cast time");
    }

    [Fact]
    public void Emrakul_ProtectionFromColouredSpells_RejectsColouredSpell()
    {
        var emrakul = EmrakulTheAeonsTornFactory.Create(_alice);
        var prot = emrakul.Abilities.OfType<ProtectionAbility>().Single();
        prot.SpellPredicate.Should().NotBeNull();

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);

        prot.SpellPredicate!(boltSpell).Should().BeTrue(
            "Lightning Bolt has the red colour identity (CR 105.2)");
        Protection.HasProtectionFromSpell(emrakul, boltSpell).Should().BeTrue();
    }

    [Fact]
    public void Emrakul_ProtectionFromColouredSpells_AllowsColourlessSpell()
    {
        var emrakul = EmrakulTheAeonsTornFactory.Create(_alice);
        var prot = emrakul.Abilities.OfType<ProtectionAbility>().Single();
        prot.SpellPredicate.Should().NotBeNull();

        // Karn Liberated is colourless ({7}).
        var karn = NamedCardFactory.Create("Karn Liberated", _bob);
        var karnSpell = new Majik.Core.Spells.Spell(karn, _bob);

        prot.SpellPredicate!(karnSpell).Should().BeFalse(
            "Karn Liberated is colourless — protection from coloured spells does not apply");
        Protection.HasProtectionFromSpell(emrakul, karnSpell).Should().BeFalse();
    }

    [Fact]
    public void Emrakul_CastTrigger_RegistersExtraTurnOnResolution()
    {
        var bus = new EventBus();
        var turns = new TurnManager(new List<Player> { _alice, _bob }, bus);
        // Bootstrap the rotation so AddExtraTurn doesn't trip the
        // "active player not initialised" path (TurnManager.AddExtraTurn
        // only requires the player be in the rotation list).
        var emrakul = EmrakulTheAeonsTornFactory.Create(_alice, turns, triggers: null, agentSelector: null);

        var castTrigger = emrakul.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("extra turn")));

        // Fire the trigger condition with a SpellCastEvent for this card.
        var emrakulSpell = new Majik.Core.Spells.Spell(emrakul, _alice);
        castTrigger.Condition.Matches(new SpellCastEvent(emrakulSpell), castTrigger)
            .Should().BeTrue("the cast trigger fires on Emrakul's own SpellCastEvent");

        turns.HasExtraTurns.Should().BeFalse("extra turn not enqueued before resolution");
        foreach (var effect in castTrigger.Effects) effect.Execute();
        turns.HasExtraTurns.Should().BeTrue("CR 603.10 — extra turn enqueued on cast-trigger resolution");
        turns.GetNextPlayer().Should().BeSameAs(_alice);
    }
}
