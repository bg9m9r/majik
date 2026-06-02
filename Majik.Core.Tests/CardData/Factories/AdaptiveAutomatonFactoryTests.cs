using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Adaptive Automaton (Magic 2012 — Artifact Creature — Construct
/// {3} 2/2).
///
/// Oracle (verified against Scryfall):
///   "As this creature enters, choose a creature type.
///    This creature is the chosen type in addition to its other types.
///    Other creatures you control of the chosen type get +1/+1."
///
/// Coverage:
///   * Identity: Artifact Creature — Construct, {3}, 2/2.
///   * NamedCardFactory dispatch.
///   * Unwired single-arg path: no chosen type, no effects.
///   * As-enters type choice stored + exposed via GetChosenType.
///   * "This creature is the chosen type in addition to its other types" —
///     the chosen subtype is granted (additive; Construct preserved).
///   * Another creature of the chosen type you control gets +1/+1.
///   * A creature NOT of the chosen type is unaffected.
///   * An opponent's creature of the chosen type is unaffected
///     ("creatures YOU control", CR 109.5).
///   * Adaptive Automaton itself does not get the buff ("Other creatures").
///   * Adaptive Automaton leaving the battlefield lifts the buff.
/// </summary>
[Trait("Color", "C")]
public class AdaptiveAutomatonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();

    private static Func<Player, CardSubtype> Choose(CardSubtype t) => _ => t;

    private Creature OwnCreature(string name, CardSubtype subtype, int p = 2, int t = 2)
    {
        var c = new Creature(name, "{1}{G}", p, t, subtypes: new[] { subtype })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };
        return c;
    }

    private Creature AutomatonOnBattlefield(CardSubtype chosen)
    {
        var auto = AdaptiveAutomatonFactory.Create(_alice, _effects, Choose(chosen));
        auto.ActiveEffects = _effects;
        auto.SetZone(ZoneType.Battlefield);
        return auto;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AdaptiveAutomaton_IsArtifactCreatureConstruct_2_2_AtCost3()
    {
        var c = AdaptiveAutomatonFactory.Create(_alice);

        c.Name.Should().Be("Adaptive Automaton");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void AdaptiveAutomaton_SingleArgPath_NoChoice_NoEffects()
    {
        var c = AdaptiveAutomatonFactory.Create(_alice);

        AdaptiveAutomatonFactory.GetChosenType(c).Should().BeNull(
            "the single-arg path resolves no creature-type choice");
    }

    // -----------------------------------------------------------------------
    // As-enters choice + "is the chosen type" (CR 614.12 / CR 613.1d)
    // -----------------------------------------------------------------------

    [Fact]
    public void AdaptiveAutomaton_StoresChosenType()
    {
        var c = AdaptiveAutomatonFactory.Create(_alice, _effects, Choose(CardSubtype.Goblin));

        AdaptiveAutomatonFactory.GetChosenType(c).Should().Be(CardSubtype.Goblin);
    }

    [Fact]
    public void AdaptiveAutomaton_IsChosenType_InAdditionToConstruct()
    {
        var c = AutomatonOnBattlefield(CardSubtype.Goblin);

        var chars = _effects.Compute((Permanent)c);
        chars.Subtypes.Should().Contain(CardSubtype.Goblin,
            "CR 613.1d — Adaptive Automaton becomes the chosen type");
        chars.Subtypes.Should().Contain(CardSubtype.Construct,
            "the chosen type is gained IN ADDITION to its other types (CR 205.3)");
    }

    // -----------------------------------------------------------------------
    // "Other creatures you control of the chosen type get +1/+1" (CR 613.7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void OtherCreatureOfChosenType_YouControl_GetsPlusOnePlusOne()
    {
        AutomatonOnBattlefield(CardSubtype.Goblin);

        var goblin = OwnCreature("Goblin Piker", CardSubtype.Goblin);

        goblin.GetPower().Should().Be(3, "CR 613.7c — another Goblin you control gets +1/+1");
        goblin.GetToughness().Should().Be(3);
    }

    [Fact]
    public void CreatureOfDifferentType_IsUnaffected()
    {
        AutomatonOnBattlefield(CardSubtype.Goblin);

        var bear = OwnCreature("Grizzly Bears", CardSubtype.Bear);

        bear.GetPower().Should().Be(2, "a Bear is not the chosen creature type (Goblin)");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void OpponentCreatureOfChosenType_IsUnaffected()
    {
        AutomatonOnBattlefield(CardSubtype.Goblin);

        var oppGoblin = new Creature("Goblin Piker", "{1}{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = _effects,
        };

        oppGoblin.GetPower().Should().Be(2,
            "'creatures YOU control' is controller-scoped (CR 109.5)");
        oppGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void AdaptiveAutomaton_DoesNotBuffItself()
    {
        // Choose Construct — the Automaton itself IS a Construct, but "Other
        // creatures" excludes the source (CR 109.5).
        var auto = AutomatonOnBattlefield(CardSubtype.Construct);

        auto.GetPower().Should().Be(2,
            "Adaptive Automaton does not buff ITSELF ('Other creatures', CR 109.5)");
        auto.GetToughness().Should().Be(2);
    }

    [Fact]
    public void AutomatonLeavesBattlefield_BuffLifts()
    {
        var auto = AutomatonOnBattlefield(CardSubtype.Goblin);
        var goblin = OwnCreature("Goblin Piker", CardSubtype.Goblin);

        // Baseline: buff active.
        goblin.GetPower().Should().Be(3);
        goblin.GetToughness().Should().Be(3);

        // Adaptive Automaton dies — LordStaticEffect.IsActive() short-circuits
        // when the source isn't on the battlefield (CR 613 — continuous
        // effects from a permanent stop applying when it leaves play).
        auto.SetZone(ZoneType.Graveyard);

        goblin.GetPower().Should().Be(2, "buff lifts on LTB");
        goblin.GetToughness().Should().Be(2);
    }
}
