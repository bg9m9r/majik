using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// Phase 2 PR-D — runtime fast path. Confirms
/// <see cref="OracleSpellBinder.BindCompiled"/> produces a runnable
/// <see cref="Majik.Core.Game.SpellDefinition"/> for compiled rows and
/// falls back gracefully when the stored template name doesn't resolve
/// in the current build's <c>Registry</c>.
/// </summary>
public class BindCompiledTests
{
    private static SpellBindContext Ctx(string name = "X", string oracle = "")
    {
        var entity = new CardEntity { Name = name, OracleText = oracle };
        var caster = new Player("Alice", 20);
        return new SpellBindContext(entity, caster, _ => _, null, null);
    }

    [Fact]
    public void BindCompiled_KnownTemplate_RehydratesNonNull()
    {
        var ctx = Ctx(oracle: "Counter target spell.");
        var def = OracleSpellBinder.BindCompiled(
            templateName: "CounterTargetSpell",
            paramsJson: "{}",
            ctx.Entity, ctx.Caster, ctx.Resolver, effects: null, stack: null);

        def.Should().NotBeNull();
    }

    [Fact]
    public void BindCompiled_KnownTemplate_WithCapturedParams_Rehydrates()
    {
        // DamageAnyTarget captures `n` — verify the params round-trip.
        var ctx = Ctx(oracle: "Lightning Bolt deals 3 damage to any target.");
        var def = OracleSpellBinder.BindCompiled(
            templateName: "DamageAnyTarget",
            paramsJson: "{\"n\":\"3\"}",
            ctx.Entity, ctx.Caster, ctx.Resolver, effects: null, stack: null);

        def.Should().NotBeNull();
    }

    [Fact]
    public void BindCompiled_UnknownTemplateName_FallsBackToLiveRegistry()
    {
        // Card with text the registry CAN match — the unknown stored
        // template name should not block the fallback.
        var ctx = Ctx(oracle: "Counter target spell.");
        var def = OracleSpellBinder.BindCompiled(
            templateName: "RetiredTemplateFromOldBuild",
            paramsJson: "{}",
            ctx.Entity, ctx.Caster, ctx.Resolver, effects: null, stack: null);

        def.Should().NotBeNull(
            "BindCompiled should fall back to the live registry walk when the stored template doesn't resolve");
    }

    [Fact]
    public void BindCompiled_TemplateRequiresEffects_ButNoneSupplied_ReturnsNull()
    {
        // GainControlTemplate overrides CanBind to require ctx.Effects.
        // BindCompiled honors the CanBind gate and returns null rather than
        // calling Rehydrate (which would NRE on the null effects service).
        var ctx = Ctx(oracle: "Gain control of target creature.");
        var def = OracleSpellBinder.BindCompiled(
            templateName: "GainControl",
            paramsJson: "{}",
            ctx.Entity, ctx.Caster, ctx.Resolver, effects: null, stack: null);

        def.Should().BeNull();
    }

    [Fact]
    public void BindCompiled_EmptyParamsJson_TreatedAsEmptyDictionary()
    {
        // Defensive: a compile-time row that wrote ""/null should not throw.
        var ctx = Ctx(oracle: "Counter target spell.");
        Action call = () => OracleSpellBinder.BindCompiled(
            templateName: "CounterTargetSpell",
            paramsJson: "",
            ctx.Entity, ctx.Caster, ctx.Resolver, effects: null, stack: null);

        call.Should().NotThrow();
    }
}
