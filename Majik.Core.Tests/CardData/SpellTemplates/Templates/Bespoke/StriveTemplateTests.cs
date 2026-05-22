using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// StriveTemplate tests — confirms the bespoke template binds the Strive
/// family (CR 702.124) and that the wrapped EffectFactory applies the
/// underlying effect once per chosen target.
/// </summary>
public class StriveTemplateTests
{
    // Builds a SpellBindContext against the SAME registry as production.
    // Strive needs SetRegistry called on it (the registry does that at
    // construction), so we use OracleSpellBinder.Registry's already-wired
    // composer. The actual template under test is held by the registry.
    private static SpellBindContext Ctx(string oracle)
    {
        return new SpellBindContext(
            new CardEntity { Name = "TestStrive", OracleText = oracle },
            new Player("A", 20),
            _ => _,
            Effects: null,
            Stack: null);
    }

    // Find the registered StriveTemplate instance via the production
    // registry so the SetRegistry hook is already in place.
    private static StriveTemplate FindStrive()
    {
        foreach (var t in OracleSpellBinder.Registry.OrderedTemplates)
        {
            if (t is StriveTemplate s) return s;
        }
        throw new Xunit.Sdk.XunitException("StriveTemplate not registered");
    }

    [Theory]
    // Aerial Formation
    [InlineData("Strive — This spell costs {2}{U} more to cast for each target beyond the first.\nAny number of target creatures each get +1/+1 and gain flying until end of turn.")]
    // Ajani's Presence
    [InlineData("Strive — This spell costs {2}{W} more to cast for each target beyond the first.\nAny number of target creatures each get +1/+1 and gain indestructible until end of turn.")]
    // Kiora's Dismissal
    [InlineData("Strive — This spell costs {U} more to cast for each target beyond the first.\nReturn any number of target enchantments to their owners' hands.")]
    public void Strive_MatchesFamily(string oracle)
    {
        FindStrive().TryBind(Ctx(oracle))
            .Should().NotBeNull("the Strive family must bind on cards with the prefix");
    }

    [Theory]
    [InlineData("Destroy target creature.")]
    [InlineData("Counter target spell.")]
    [InlineData("Any number of target creatures each get +1/+1 until end of turn.")]
    public void Strive_DoesNotMatchOutOfFamily(string oracle)
    {
        FindStrive().TryBind(Ctx(oracle))
            .Should().BeNull("cards without the Strive prefix must not bind");
    }

    [Fact]
    public void Strive_ExpandsTargetSlot_ToAnyNumber()
    {
        // Oracle whose post-strip text binds to DamageAnyTarget (single
        // target). Strive should expand max to MaxTargets.
        var oracle =
            "Strive — This spell costs {1}{R} more to cast for each target beyond the first.\n" +
            "This spell deals 2 damage to any target.";

        var def = FindStrive().TryBind(Ctx(oracle));
        def.Should().NotBeNull();

        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(StriveTemplate.MaxTargets);
    }

    [Fact]
    public void Strive_EffectFactory_AppliesInnerEffectOncePerTarget_SingleTarget()
    {
        var oracle =
            "Strive — This spell costs {1}{R} more to cast for each target beyond the first.\n" +
            "This spell deals 2 damage to any target.";

        var def = FindStrive().TryBind(Ctx(oracle));
        def.Should().NotBeNull();

        var caster = new Player("A", 20);
        var enemy = new Player("B", 20);

        // 1 target — inner effect runs once.
        var p1 = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { enemy } },
            Mana: ManaPayment.Empty);

        var effects1 = def!.EffectFactory(p1);
        effects1.Count.Should().Be(1, "single-target Strive cast yields one inner effect");

        foreach (var e in effects1) e.Execute();
        enemy.LifeTotal.Should().Be(18, "2 damage applied once");
    }

    [Fact]
    public void Strive_EffectFactory_AppliesInnerEffectOncePerTarget_ThreeTargets()
    {
        var oracle =
            "Strive — This spell costs {1}{R} more to cast for each target beyond the first.\n" +
            "This spell deals 2 damage to any target.";

        var def = FindStrive().TryBind(Ctx(oracle));
        def.Should().NotBeNull();

        var t1 = new Player("T1", 20);
        var t2 = new Player("T2", 20);
        var t3 = new Player("T3", 20);

        // 3 targets — inner effect runs three times. The inner factory
        // captures Targets[0][0] in closure, so each invocation reads the
        // singleton slot we synthesize per target. (Whether the captured
        // target reflects the per-iteration slot vs. the original slot
        // depends on whether the inner factory reads at factory-call time
        // or at effect-execute time. DamageAnyTarget reads at
        // factory-call time, which means we expect three distinct
        // effects each bound to the per-iteration target.)
        var p3 = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { t1, t2, t3 } },
            Mana: ManaPayment.Empty);

        var effects3 = def!.EffectFactory(p3);
        effects3.Count.Should().Be(3, "three-target Strive cast yields three inner effects");

        foreach (var e in effects3) e.Execute();

        // Each target took 2 damage.
        t1.LifeTotal.Should().Be(18);
        t2.LifeTotal.Should().Be(18);
        t3.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void Strive_FallsBackToEmptyShell_WhenInnerDoesNotBind()
    {
        // Strive prefix detected, but the post-strip text isn't a pattern
        // any inner template covers. Must still bind (so the card loads)
        // with a single any-number target slot.
        var oracle =
            "Strive — This spell costs {2}{G} more to cast for each target beyond the first.\n" +
            "Some completely unrecognized effect happens here, no template will match.";

        var def = FindStrive().TryBind(Ctx(oracle));
        def.Should().NotBeNull("Strive must always bind once the prefix is detected");
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MaxTargets.Should().Be(StriveTemplate.MaxTargets);
    }

    [Fact]
    public void Strive_CardsBindThroughOracleSpellBinder()
    {
        // End-to-end: the same path ScryfallCardFactory uses must bind a
        // Strive card. This guards against the Strive template falling out
        // of the registry list.
        var entity = new CardEntity
        {
            Name = "Aerial Formation",
            OracleText =
                "Strive — This spell costs {2}{U} more to cast for each target beyond the first.\n" +
                "Any number of target creatures each get +1/+1 and gain flying until end of turn.",
        };
        var def = OracleSpellBinder.Bind(
            entity, new Player("A", 20), _ => _, stack: null);
        def.Should().NotBeNull(
            "OracleSpellBinder must route Strive cards through StriveTemplate");
    }
}
