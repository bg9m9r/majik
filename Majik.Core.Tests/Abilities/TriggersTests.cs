using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class TriggersTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Mock<ITriggeredAbility> _ability = new();

    [Fact]
    public void OnEnterBattlefieldSelf_Matches_OnlySourceEnteringBattlefield()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var other = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };

        var cond = Triggers.OnEnterBattlefieldSelf(source);

        cond.Matches(new CardMovedEvent(source, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeFalse();
        cond.Matches(new CardMovedEvent(source, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnAnyCreatureEntersBattlefield_Matches_AnyCreatureEntering()
    {
        var creature = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };
        var land = new Land("Forest") { Owner = _alice };

        var cond = Triggers.OnAnyCreatureEntersBattlefield();

        cond.Matches(new CardMovedEvent(creature, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(land, ZoneType.Hand, ZoneType.Battlefield), _ability.Object).Should().BeFalse();
        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnDies_Matches_CreatureMovingFromBattlefieldToGraveyard()
    {
        var creature = new Creature("Wolf", "2G", 3, 3) { Owner = _alice };

        var cond = Triggers.OnDies(creature);

        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Graveyard), _ability.Object).Should().BeTrue();
        cond.Matches(new CardMovedEvent(creature, ZoneType.Battlefield, ZoneType.Exile), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnCardDrawnByPlayer_OnlyMatchesThatPlayer()
    {
        var card = new Instant("X", "1") { Owner = _alice };

        var cond = Triggers.OnCardDrawnByPlayer(_alice);

        cond.Matches(new CardDrawnEvent(card, _alice), _ability.Object).Should().BeTrue();
        cond.Matches(new CardDrawnEvent(card, _bob), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnSpellCast_Matches_AnySpellCastEvent()
    {
        var spell = new Mock<ISpell>().Object;

        var cond = Triggers.OnSpellCast();

        cond.Matches(new SpellCastEvent(spell), _ability.Object).Should().BeTrue();
        var unrelatedCard = new Instant("X", "1") { Owner = _alice };
        cond.Matches(new CardDrawnEvent(unrelatedCard, _alice), _ability.Object).Should().BeFalse();
    }

    [Fact]
    public void OnEnterBattlefieldSelf_NullSource_Throws()
    {
        var act = () => Triggers.OnEnterBattlefieldSelf(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // OnOpponentSacrifices — CR 603.1 + CR 701.16 + CR 109.5. The
    // controller-gated "whenever an opponent sacrifices …" predicate over
    // the dedicated PermanentSacrificedEvent (the Blood-Artist-on-opponent-
    // sac / Vengeful-Tracker payoff family).
    // ------------------------------------------------------------------

    [Fact]
    public void OnOpponentSacrifices_MatchesOpponentNotController()
    {
        var cond = Triggers.OnOpponentSacrifices(_alice);

        var oppPerm = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        var ownPerm = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };

        // Bob (an opponent of Alice) sacrifices — matches.
        cond.Matches(new PermanentSacrificedEvent(oppPerm, _bob, wasToken: false), _ability.Object)
            .Should().BeTrue("Bob is an opponent of the controller (Alice)");

        // Alice (the controller) sacrifices her own — does NOT match.
        cond.Matches(new PermanentSacrificedEvent(ownPerm, _alice, wasToken: false), _ability.Object)
            .Should().BeFalse("the controller's own sacrifice is not 'an opponent sacrifices'");
    }

    [Fact]
    public void OnOpponentSacrifices_WithTypeFilter_MatchesOnlyThatType()
    {
        // "Whenever an opponent sacrifices an artifact" (Vengeful Tracker).
        var cond = Triggers.OnOpponentSacrifices(_alice, CardType.Artifact);

        var artifact = new Artifact("Bottle Cap", "1") { Owner = _bob };
        var creature = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };

        cond.Matches(new PermanentSacrificedEvent(artifact, _bob, wasToken: false), _ability.Object)
            .Should().BeTrue("an opponent sacrificed an artifact");
        cond.Matches(new PermanentSacrificedEvent(creature, _bob, wasToken: false), _ability.Object)
            .Should().BeFalse("a creature is not an artifact");
        cond.Matches(new PermanentSacrificedEvent(artifact, _alice, wasToken: false), _ability.Object)
            .Should().BeFalse("the controller's own artifact sacrifice does not fire 'an opponent sacrifices'");
    }

    [Fact]
    public void OnOpponentSacrifices_NullController_Throws()
    {
        var act = () => Triggers.OnOpponentSacrifices(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
