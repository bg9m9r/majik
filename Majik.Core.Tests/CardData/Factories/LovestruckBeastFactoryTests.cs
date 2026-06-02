using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LovestruckBeastFactory"/> (Throne of Eldraine,
/// {2}{G}).
///
/// - Lovestruck Beast — Creature — Beast Noble {2}{G}, 5/5.
///     "This creature can't attack unless you control a 1/1 creature."
/// - Heart's Desire (Adventure) — Sorcery — Adventure {G}.
///     "Create a 1/1 white Human creature token."
///
/// Covers:
///   - Identity / shape (Beast Noble / {2}{G} / 5/5) from the embedded JSON.
///   - NamedCardFactory dispatch.
///   - "Can't attack unless you control a 1/1 creature" predicate-mode
///     CombatRestrictionEffect (CR 508.1c) — scoped to Lovestruck Beast,
///     evaluated against the controller's live battlefield, lifting the
///     instant a 1/1 is under control, gated to the battlefield.
///   - Heart's Desire helper structural shape (no targets, no X, no modes).
///   - Heart's Desire resolve: mints a 1/1 white Human creature token for the
///     caster (CR 111 / 111.4).
///
/// Adventure cast-from-hand-to-exile (CR 715) routing is exercised by the
/// cast-pipeline suite.
/// </summary>
[Trait("Color", "G")]
public class LovestruckBeastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ShipsBeastNoble_5_5_AtCost2G()
    {
        var beast = LovestruckBeastFactory.Create(_alice);

        beast.Should().BeOfType<Creature>();
        beast.Name.Should().Be("Lovestruck Beast");
        beast.ManaCost.Should().Be("{2}{G}");
        beast.HasType(CardType.Creature).Should().BeTrue();
        beast.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        beast.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        beast.BasePower.Should().Be(5);
        beast.BaseToughness.Should().Be(5);
        beast.Owner.Should().BeSameAs(_alice);
        beast.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LovestruckBeast()
    {
        var card = NamedCardFactory.Create("Lovestruck Beast", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lovestruck Beast");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Create_AttachesHeartsDesireAdventure()
    {
        var beast = LovestruckBeastFactory.Create(_alice);

        beast.AdventureSpec.Should().NotBeNull();
        beast.AdventureSpec!.Name.Should().Be("Heart's Desire");
        beast.AdventureSpec.IsSorcery.Should().BeTrue("Heart's Desire is a Sorcery");
        beast.AdventureSpec.ManaCost.TotalValue.Should().Be(1, "{G} = 1");
        beast.AdventureSpec.ManaCost.Green.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Can't attack unless you control a 1/1 creature (CR 508.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void NoOneOneControlled_BeastCannotAttack()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        // Alice controls no 1/1 creature — Lovestruck Beast can't attack.
        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeTrue("you control no 1/1 creature");
    }

    [Fact]
    public void ControlsAOneOne_BeastCanAttack()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        var token = new Creature("Human", "", 1, 1);
        token.SetOwner(_alice);
        token.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeFalse("you control a 1/1 creature — the restriction lifts");
    }

    [Fact]
    public void Restriction_LiftsImmediately_WhenAOneOneEnters()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack).Should().BeTrue();

        // A 1/1 enters the controller's battlefield — predicate re-reads the
        // live board every pass, so the lock lifts immediately.
        var token = new Creature("Human", "", 1, 1);
        token.SetOwner(_alice);
        token.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeFalse("predicate re-reads the live battlefield every pass");
    }

    [Fact]
    public void Restriction_OpponentsOneOne_DoesNotCount()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        // Bob (the opponent) controls a 1/1 — "you control a 1/1" must scope
        // to the Beast's controller, so the lock stays on (CR 109.5).
        var bobToken = new Creature("Human", "", 1, 1);
        bobToken.SetOwner(_bob);
        bobToken.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobToken);
        bobToken.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeTrue("only a 1/1 YOU control lifts the restriction");
    }

    [Fact]
    public void Restriction_NonOneOneCreature_DoesNotCount()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        // A 2/2 Alice controls is not a 1/1 — the restriction stays on.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeTrue("a 2/2 is not a 1/1");
    }

    [Fact]
    public void Restriction_ScopedToBeastOnly_NotOtherCreatures()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        // An unrelated creature must not pick up Lovestruck Beast's
        // self-scoped restriction even though Alice controls no 1/1.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        effects.HasRestriction(beast, CombatRestriction.CannotAttack).Should().BeTrue();
        effects.HasRestriction(bear, CombatRestriction.CannotAttack)
            .Should().BeFalse("the restriction is scoped to Lovestruck Beast only");
    }

    [Fact]
    public void Restriction_SuppressedOffBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var beast = LovestruckBeastFactory.Create(_alice, effects);
        // Not on the battlefield — static restriction is suppressed
        // (CR 603.6e), even though Alice controls no 1/1.

        effects.HasRestriction(beast, CombatRestriction.CannotAttack)
            .Should().BeFalse("static restriction functions only on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Heart's Desire helper — structural shape + resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void HeartsDesire_Helper_HasNoTargets_NoX_NoModes()
    {
        var def = LovestruckBeastFactory.BuildAdventureSpell(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void HeartsDesire_Resolve_CreatesOneOneWhiteHumanToken()
    {
        var def = LovestruckBeastFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 111 / 111.4 — one 1/1 white Human creature token on Alice's
        // battlefield.
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1);
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Human).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.White);
        token.Controller.Should().BeSameAs(_alice);
    }
}
