using FluentAssertions;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CI lint/audit gate for the <b>"bound-but-inert (resolver-null)"</b> card-factory
/// bug class (#2540 / #2543 / #2549 / #2551).
///
/// <para>
/// The <see cref="ResolverNullInertEffectAudit"/> source analyzer statically scans
/// the <c>[CardName]</c> factory sources under
/// <c>Majik.Core/CardData/Factories/</c> and flags any factory that
/// (a) declares a <c>Func&lt;…Player…&gt;</c> / <c>Func&lt;…Permanent…&gt;</c>
/// resolver param, (b) invokes it inside an <c>Effect</c> / <c>Fx.Inline</c>
/// resolution closure, and (c) never reads <c>rc.Game</c> / <c>ctx.Game</c> /
/// <c>ContextOpponents</c> / <c>ResolutionContext</c> — the triple that means the
/// effect body is inert on the production routed build (the resolver is null
/// there).
/// </para>
///
/// <para>
/// The unit tests below prove the analyzer flags a deliberately-inert sample and
/// does NOT flag a context-reading one. The <see cref="RealFactories_HaveNoResolverNullInertEffects"/>
/// fact is the live gate: it scans every real factory and fails on any violation
/// outside the genuine-infra <see cref="Allowlist"/>.
/// </para>
/// </summary>
public sealed class ResolverNullInertEffectAuditTests
{
    // ===================================================================== //
    //  Analyzer unit tests (TDD) — synthetic factory strings.               //
    // ===================================================================== //

    private const string InertSample = """
        using System;
        using System.Collections.Generic;
        public static class FakeInertFactory
        {
            public static object Create(Player owner) => Create(owner, opponentResolver: null);
            public static object Create(Player owner, Func<IReadOnlyList<Player>>? opponentResolver)
            {
                var drain = new Effect("drain", () =>
                {
                    var opps = opponentResolver?.Invoke();
                    if (opps == null) return;
                    foreach (var o in opps) o.LoseLife(2);
                });
                return drain;
            }
        }
        """;

    private const string ContextReadingSample = """
        using System;
        using System.Collections.Generic;
        public static class FakeContextFactory
        {
            public static object Create(Player owner)
            {
                var drain = new Effect("drain", ctx =>
                {
                    foreach (var o in ContextOpponents.Of(ctx, owner)) o.LoseLife(2);
                });
                return drain;
            }
        }
        """;

    private const string InertViaHelperSample = """
        using System;
        using System.Collections.Generic;
        public static class FakeInertHelperFactory
        {
            public static object Create(Player owner) => Create(owner, playerResolver: null);
            public static object Create(Player owner, Func<IReadOnlyList<Player>>? playerResolver)
            {
                var etb = new Effect("each player sac", () => Resolve(playerResolver));
                return etb;
            }
            private static void Resolve(Func<IReadOnlyList<Player>>? playerResolver)
            {
                var players = playerResolver?.Invoke();
                if (players == null) return;
                foreach (var p in players) p.Sacrifice();
            }
        }
        """;

    private const string CanActivateGateSample = """
        using System;
        using System.Collections.Generic;
        public static class FakeGateFactory
        {
            public static object Create(Player owner) => Create(owner, opponentResolver: null);
            public static object Create(Player owner, Func<IReadOnlyList<Player>>? opponentResolver)
            {
                // Resolver consulted only in a canActivateCheck gate — NOT an Effect body.
                bool CanActivate() => OpponentLostLife(opponentResolver);
                var counter = new Effect("put a counter", () => { /* no resolver here */ });
                var ability = new ActivatedAbility(canActivateCheck: CanActivate, effects: new[] { counter });
                return ability;
            }
            private static bool OpponentLostLife(Func<IReadOnlyList<Player>>? opponentResolver)
            {
                var players = opponentResolver?.Invoke();
                return players != null;
            }
        }
        """;

    [Fact]
    public void Analyzer_Flags_InertResolverInEffectBody()
    {
        var violations = ResolverNullInertEffectAudit.Analyze(InertSample, "FakeInertFactory.cs");
        violations.Should().ContainSingle()
            .Which.ResolverParam.Should().Be("opponentResolver");
    }

    [Fact]
    public void Analyzer_DoesNotFlag_ContextReadingFactory()
    {
        var violations = ResolverNullInertEffectAudit.Analyze(ContextReadingSample, "FakeContextFactory.cs");
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Analyzer_Flags_InertResolverInvokedViaHelperCalledFromEffect()
    {
        // The resolver is invoked one hop away (a private helper the Effect body
        // calls) — still inert. The analyzer follows the call into the helper.
        var violations = ResolverNullInertEffectAudit.Analyze(InertViaHelperSample, "FakeInertHelperFactory.cs");
        violations.Should().ContainSingle()
            .Which.ResolverParam.Should().Be("playerResolver");
    }

    [Fact]
    public void Analyzer_DoesNotFlag_ResolverUsedOnlyInCanActivateGate()
    {
        // Genuine-infra shape (cf. Hired Claw): the resolver feeds a
        // canActivateCheck gate, not an Effect resolution closure. The Effect
        // body itself never touches the resolver, so it is NOT the inert-on-prod
        // each-player effect bug — must not flag.
        var violations = ResolverNullInertEffectAudit.Analyze(CanActivateGateSample, "FakeGateFactory.cs");
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Analyzer_DoesNotFlag_FactoryWithNoResolverParam()
    {
        const string plain = """
            public static class PlainFactory
            {
                public static object Create(Player owner)
                {
                    var fx = new Effect("gain", () => owner.GainLife(1));
                    return fx;
                }
            }
            """;
        ResolverNullInertEffectAudit.Analyze(plain, "PlainFactory.cs").Should().BeEmpty();
    }

    // ===================================================================== //
    //  The live gate — scan every real factory.                             //
    // ===================================================================== //

    /// <summary>
    /// Allowlist of factory file names the gate exempts, each with a WHY. Keep it
    /// MINIMAL: an entry should only be a documented straggler/backlog item or a
    /// true engine-infra gap, never a convenient way to silence a real, fixable
    /// inert effect.
    ///
    /// <para>
    /// NOTE on the other two genuine-infra deferrals (v1-deferrals #3b) named in
    /// the #2551 commit — <b>Grove of the Burnwillows</b> and <b>Kaito −2</b> —
    /// they are deliberately NOT in this dictionary because the analyzer is precise
    /// enough that neither trips the gate:
    /// <list type="bullet">
    ///   <item>Grove invokes its resolver inside a <c>ManaAbility</c>
    ///     <c>additionalCostPayer</c> lambda (CR 605.3), not an <c>Effect</c> body,
    ///     so criterion (b) is not met.</item>
    ///   <item>Kaito reads <c>ContextOpponents</c> for its 0-ability, so the file
    ///     satisfies criterion (c) (the −2 target resolver is then irrelevant to
    ///     this gate — it is target-system infra anyway).</item>
    /// </list>
    /// Adding them here would fail <see cref="Allowlist_EntriesStillTripTheGate"/>
    /// (they don't trip), which is the correct signal that they're already clear.
    /// (Hired Claw DOES trip — its damage Effect body falls back to the resolver
    /// to pick "target opponent" when no ChosenTargets — and is listed below as a
    /// genuine targeting-infra deferral.)
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Allowlist =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // === Tier 1: genuine engine-infra exemption (v1-deferrals #3b) =====
            // Hired Claw — the attack-trigger Effect deals 1 damage to "target
            // opponent" (CR 115). On prod the target arrives via the trigger's
            // ChosenTargets (targeting system); the resolver is only a v1 fallback
            // when no target was chosen. Reaching opponents at resolve time without
            // a chosen target needs the ITarget / TargetResolver system, not a
            // bare ContextOpponents read. v1-deferrals #3b.
            ["HiredClawFactory.cs"] =
                "attack-trigger Effect deals damage to 'target opponent'; prod supplies the target via ChosenTargets, resolver is a v1 fallback — needs the targeting system, not a context-read. v1-deferrals #3b.",

            // NOTE: Teferi, Hero of Dominaria was REMOVED from this allowlist once
            // its −3 / +1-untap / emblem clauses were wired to the agent-target
            // infra: the −3 / +1 read the activating player's CHOSEN objects off
            // the live ResolutionContext (ChosenTargets) via real TargetRequests
            // on the loyalty abilities, and the emblem's exile reads ChosenTargets
            // with a ContextOpponents.Of live-context fallback (never a build-time
            // resolver). The factory no longer declares a Func<…Permanent…>
            // resolver param, so it no longer matches the inert signature.
            // Allowlist is now Hired Claw only.
        };

    /// <summary>
    /// Card factories whose card type is <c>Land</c>. Lands are NOT routed through
    /// <c>NamedCardFactory.Create</c> on the production build —
    /// <c>GameFacade.BuildDeckCard</c> gates the named-factory instance-swap on
    /// <c>!shell.HasType(CardType.Land)</c> (lands go through the data/binder
    /// path). A resolver-null <c>Func</c> on a land factory is therefore not
    /// inert-on-prod via that mechanism (the single-arg <c>Create</c> is
    /// test-only), so the gate excludes <c>Land</c>-returning factories. The same
    /// fragile pattern should still be cleaned up, but it is not part of this
    /// inert-on-prod bug class. Detected by a literal "public static Land Create"
    /// signature in the source.
    /// </summary>
    private static bool IsLandFactory(string source)
        => source.Contains("public static Land Create", StringComparison.Ordinal);

    [Fact]
    public void RealFactories_HaveNoResolverNullInertEffects()
    {
        var factoriesDir = ResolverNullInertEffectAuditPaths.FactoriesDir;
        Directory.Exists(factoriesDir).Should().BeTrue(
            $"the factory source dir must be resolvable from the test bin dir (looked at '{factoriesDir}')");

        var files = Directory.EnumerateFiles(factoriesDir, "*Factory.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        files.Should().NotBeEmpty("there must be factory sources to scan");

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var source = File.ReadAllText(file);
            var violations = ResolverNullInertEffectAudit.Analyze(source, name);
            if (violations.Count == 0) continue;

            // Lands are not routed through NamedCardFactory.Create on prod, so a
            // resolver-null Func on a land factory isn't inert-on-prod via that
            // mechanism (test-only single-arg Create). Out of this bug class.
            if (IsLandFactory(source)) continue;

            if (Allowlist.ContainsKey(name)) continue; // documented exemption / backlog.

            foreach (var v in violations)
                offenders.Add($"  - {v.FileName}: {v.Detail}");
        }

        offenders.Should().BeEmpty(
            "no card factory may rely solely on a build-time players/permanents " +
            "resolver inside an Effect/Fx.Inline body (it is NULL on the prod routed " +
            "build → inert in real games). Read the live game off the " +
            "ResolutionContext (ContextOpponents.Of) instead. Offenders:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Allowlist_EntriesStillTripTheGate()
    {
        // Guard against stale allowlist entries: every exempted factory must
        // STILL match the inert signature (otherwise the exemption is dead and
        // should be removed, e.g. once the deferred infra lands and the factory
        // starts reading context).
        var factoriesDir = ResolverNullInertEffectAuditPaths.FactoriesDir;
        foreach (var (name, why) in Allowlist)
        {
            var path = Path.Combine(factoriesDir, name);
            File.Exists(path).Should().BeTrue($"allowlisted factory '{name}' must exist ({why})");

            var violations = ResolverNullInertEffectAudit.Analyze(File.ReadAllText(path), name);
            violations.Should().NotBeEmpty(
                $"allowlisted factory '{name}' no longer matches the inert signature — " +
                "its exemption is stale and should be removed from the Allowlist " +
                $"(reason was: {why}).");
        }
    }
}
