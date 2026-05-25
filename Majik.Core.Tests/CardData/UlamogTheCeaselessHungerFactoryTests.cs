using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UlamogTheCeaselessHungerFactory"/>
/// (Battle for Zendikar, {10}).
///
/// Legendary Creature — Eldrazi 10/10. Oracle text:
///   "When you cast this spell, exile two target permanents.
///    Indestructible
///    Whenever Ulamog attacks, defending player exiles the top twenty
///    cards of their library."
///
/// Covers:
///   - Identity (Legendary Creature — Eldrazi, {10}, 10/10, owner /
///     controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Indestructible keyword marker.
///   - Cast trigger has Stack as active zone + 2..2 target request.
///   - Cast trigger effect exiles both chosen permanents.
///   - Attack trigger fires only when Ulamog attacks.
///   - Attack trigger effect exiles top 20 from defender's library.
///   - Attack trigger handles short libraries (< 20) gracefully.
/// </summary>
public class UlamogTheCeaselessHungerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Ulamog_Identity()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);

        ulamog.Name.Should().Be("Ulamog, the Ceaseless Hunger");
        ulamog.ManaCost.Should().Be("{10}");
        ulamog.HasType(CardType.Creature).Should().BeTrue();
        ulamog.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ulamog.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ulamog.BasePower.Should().Be(10);
        ulamog.BaseToughness.Should().Be(10);
        ulamog.Owner.Should().BeSameAs(_alice);
        ulamog.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ulamog_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ulamog, the Ceaseless Hunger", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ulamog, the Ceaseless Hunger");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(10);
        ((Creature)card).BaseToughness.Should().Be(10);
    }

    [Fact]
    public void Ulamog_HasIndestructibleMarker()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);

        ulamog.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Indestructible",
                "CR 702.12 — Indestructible marker for SBA / damage gates");
    }

    // -----------------------------------------------------------------------
    // Cast trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void Ulamog_CastTrigger_Matches_OnSelfCast()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var castTrigger = GetCastTrigger(ulamog);

        // Build a stub spell whose Card is Ulamog.
        var spell = new StubSpell(ulamog, _alice);
        var ev = new SpellCastEvent(spell);

        castTrigger.Condition.Matches(ev, castTrigger).Should().BeTrue();
    }

    [Fact]
    public void Ulamog_CastTrigger_DoesNotMatch_OnOtherSpellCast()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var castTrigger = GetCastTrigger(ulamog);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var spell = new StubSpell(other, _alice);
        var ev = new SpellCastEvent(spell);

        castTrigger.Condition.Matches(ev, castTrigger).Should().BeFalse();
    }

    [Fact]
    public void Ulamog_CastTrigger_RequestsTwoTargets()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var castTrigger = GetCastTrigger(ulamog);

        castTrigger.TargetRequests.Should().HaveCount(1);
        var req = castTrigger.TargetRequests[0];
        req.MinTargets.Should().Be(2);
        req.MaxTargets.Should().Be(2);
        req.Intent.HasAny(BotIntent.Removal).Should().BeTrue();
    }

    [Fact]
    public void Ulamog_CastTrigger_ActiveOnStack()
    {
        // Cast trigger fires while Ulamog is still on the stack (same
        // active-zone posture as Cascade), so the ability must list
        // Stack in its active zones.
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var castTrigger = GetCastTrigger(ulamog);

        castTrigger.ActiveZones.Should().Contain(ZoneType.Stack);
    }

    [Fact]
    public void Ulamog_CastTriggerEffect_ExilesTwoChosenPermanents()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var castTrigger = GetCastTrigger(ulamog);

        // Two targets on Bob's battlefield.
        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(_bob);
        bobCreature.SetController(_bob);
        bobCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature);

        var bobArtifact = new Artifact("Sol Ring", "{1}");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        bobArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobArtifact);

        castTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobCreature, bobArtifact },
        });

        foreach (var effect in castTrigger.Effects) effect.Execute();

        bobCreature.Zone.Should().Be(ZoneType.Exile);
        bobArtifact.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(new ICard[] { bobCreature, bobArtifact });
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobArtifact);
    }

    // -----------------------------------------------------------------------
    // Attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void Ulamog_AttackTrigger_Matches_OnSelfAttack()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var attackTrigger = GetAttackTrigger(ulamog);

        var ev = new CreatureAttacksEvent(ulamog, _bob);
        attackTrigger.Condition.Matches(ev, attackTrigger).Should().BeTrue();
    }

    [Fact]
    public void Ulamog_AttackTrigger_DoesNotMatch_OnOtherAttacker()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var attackTrigger = GetAttackTrigger(ulamog);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        var ev = new CreatureAttacksEvent(other, _bob);

        attackTrigger.Condition.Matches(ev, attackTrigger).Should().BeFalse();
    }

    [Fact]
    public void Ulamog_AttackTriggerEffect_ExilesTopTwentyFromDefendingPlayer()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var attackTrigger = GetAttackTrigger(ulamog);

        // Seed Bob's library with 25 cards.
        var seeded = new List<ICard>();
        for (var i = 0; i < 25; i++)
        {
            var c = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            c.SetOwner(_bob);
            c.SetZone(ZoneType.Library);
            _bob.Zones.Library.AddCard(c);
            seeded.Add(c);
        }

        // Drive the predicate evaluation so the captured defender is set.
        attackTrigger.Condition.Matches(new CreatureAttacksEvent(ulamog, _bob), attackTrigger)
            .Should().BeTrue();

        foreach (var effect in attackTrigger.Effects) effect.Execute();

        // First 20 exiled; bottom 5 remain in library.
        var exiled = seeded.Take(UlamogTheCeaselessHungerFactory.AttackTriggerExileCount).ToList();
        var remain = seeded.Skip(UlamogTheCeaselessHungerFactory.AttackTriggerExileCount).ToList();

        foreach (var c in exiled) c.Zone.Should().Be(ZoneType.Exile);
        foreach (var c in remain) c.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Exile.GetCards().Count().Should().Be(UlamogTheCeaselessHungerFactory.AttackTriggerExileCount);
        _bob.Zones.Library.GetCards().Count().Should().Be(5);
    }

    [Fact]
    public void Ulamog_AttackTriggerEffect_ShortLibrary_ExilesAllAndStops()
    {
        var ulamog = UlamogTheCeaselessHungerFactory.Create(_alice);
        var attackTrigger = GetAttackTrigger(ulamog);

        // Bob has only 3 cards in library.
        var seeded = new List<ICard>();
        for (var i = 0; i < 3; i++)
        {
            var c = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            c.SetOwner(_bob);
            c.SetZone(ZoneType.Library);
            _bob.Zones.Library.AddCard(c);
            seeded.Add(c);
        }

        attackTrigger.Condition.Matches(new CreatureAttacksEvent(ulamog, _bob), attackTrigger)
            .Should().BeTrue();
        foreach (var effect in attackTrigger.Effects) effect.Execute();

        foreach (var c in seeded) c.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>The first <see cref="TriggeredAbility"/> wraps the cast
    /// trigger (SpellCastEvent condition; active in Stack).</summary>
    private static TriggeredAbility GetCastTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    /// <summary>The second <see cref="TriggeredAbility"/> wraps the
    /// attack trigger.</summary>
    private static TriggeredAbility GetAttackTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    private sealed class StubSpell : ISpell
    {
        public StubSpell(ICard card, Player controller)
        {
            Card = card;
            Controller = controller;
        }

        public ICard Card { get; }
        public Player Controller { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public IReadOnlyList<Majik.Core.Targeting.ITarget> Targets { get; } =
            Array.Empty<Majik.Core.Targeting.ITarget>();
        public IReadOnlyList<Majik.Core.Costs.ICost> Costs { get; } =
            Array.Empty<Majik.Core.Costs.ICost>();
        public bool CannotBeCountered => false;
        public void Resolve() { }
    }
}
