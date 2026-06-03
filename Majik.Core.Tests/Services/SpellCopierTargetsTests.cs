using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Xunit;

namespace Majik.Core.Tests.Services;

/// <summary>
/// Regression for the raw-execute-empty-resolution-legacy-context deferral.
///
/// <para>
/// Every ability/spell resolves its effects via the async path
/// (<c>Majik.Core.Spells.Spell.ResolveAsync</c> / <c>ActivatedAbility.ResolveAsync</c>), which
/// builds a <see cref="ResolutionContext"/> threading the stack object's
/// <c>ChosenTargets</c> so a targeted declarative JSON verb reads its pick
/// off <see cref="ResolutionContext.ChosenTargets"/>.
/// </para>
///
/// <para>
/// The one production seam that re-ran a spell's effects via the raw
/// synchronous <c>IEffect.Execute()</c> — <see cref="SpellCopier"/>
/// (CR 707.10 spell-copy) — fed every effect <see cref="ResolutionContext.Legacy"/>,
/// whose <c>ChosenTargets</c> is empty. So a copy of a TARGETED spell
/// resolved with NO targets: a targeted JSON verb would see nothing and
/// fizzle, even though CR 707.10a says the copy reuses the original's
/// targets (the engine's lossy v1 stub: "Targets are reused verbatim").
/// </para>
///
/// <para>
/// Fix: <see cref="SpellCopier"/> now builds a resolution context from the
/// original spell's <see cref="Majik.Core.Spells.Spell.ChosenTargets"/> and resolves each
/// effect against it, so a copy resolves the same whether driven by
/// <c>ResolveAsync</c> or the copier's raw path.
/// </para>
/// </summary>
public class SpellCopierTargetsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>
    /// Build a targeted spell whose single effect is a declarative-verb
    /// analogue: it reads its target off <see cref="ResolutionContext.ChosenTargets"/>
    /// index 0 (exactly how every JSON targeted verb resolves) and records
    /// whom it hit. This mirrors a "deal damage to target / return target to
    /// hand / destroy target" body, minus the actual mutation.
    /// </summary>
    private static Majik.Core.Spells.Spell BuildTargetedSpell(Player controller, object target, List<object> hits)
    {
        var instant = new Instant("Targeted Bolt", "R") { Owner = controller };
        var effect = new Effect("targeted-verb", ctx =>
        {
            // The chosen target read EXACTLY as the JSON targeted verbs do.
            if (ctx.ChosenTargets.Count > 0 && ctx.ChosenTargets[0].Count > 0)
            {
                hits.Add(ctx.ChosenTargets[0][0]);
            }
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });

        var spell = new Majik.Core.Spells.Spell(instant, controller, effects: new IEffect[] { effect });
        spell.ChosenTargets.Add(target);
        return spell;
    }

    [Fact]
    public void CopyOfTargetedSpell_ResolvesAgainstOriginalTargets()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var hits = new List<object>();
        var spell = BuildTargetedSpell(_alice, target: _bob, hits);

        SpellCopier.PushCopyOfTopSpell(stack, spell);

        // The copy must resolve against the ORIGINAL spell's chosen target
        // (CR 707.10a — targets reused verbatim in the v1 stub), NOT an empty
        // Legacy context.
        hits.Should().ContainSingle("the copy re-runs the targeted effect once")
            .Which.Should().BeSameAs(_bob,
                "a copy of a targeted spell resolves against the original's chosen target");
    }

    [Fact]
    public void CopyOfUntargetedSpell_StillResolves()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var ran = 0;
        var instant = new Instant("Untargeted", "R") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(
            instant,
            _alice,
            effects: new IEffect[] { new Effect("count", () => ran++) });

        SpellCopier.PushCopyOfTopSpell(stack, spell);

        ran.Should().Be(1, "untargeted copy still re-runs its effects once");
    }
}
