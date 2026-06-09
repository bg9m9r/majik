using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Production binder for the "manland" (creature-land) cycle — Worldwake /
/// Battle for Zendikar / Oath of the Gatewatch / Kaldheim / Streets of New
/// Capenna / March of the Machine "Restless" cards. Binds the
/// <b>animate activated ability</b> ("{cost}: … this land becomes a N/N …
/// creature … It's still a land.") and, for the Restless cycle, the
/// <b>"Whenever this land attacks, …" trigger</b> directly from oracle text.
///
/// <para><b>Why this binder exists.</b> Manlands ship per-card
/// <c>[CardName]</c> factories (CreepingTarPitFactory, RestlessVentsFactory,
/// CelestialColonnadeFactory, …) that wire these abilities — but
/// <see cref="Majik.Core.Api.GameFacade"/>'s deck-build path
/// (<c>BuildDeckCard</c>) NEVER routes a Land through its named factory (the
/// factory instance-swap is explicitly gated on
/// <c>!shell.HasType(CardType.Land)</c>). Lands are built only through the
/// binder chain. So before this binder, every manland's animate ability +
/// attack trigger were <b>dead in production</b>: the land never animated and
/// its attack triggers never fired in a real match. The factories remain for
/// their (test-only) dispatch + richer per-card riders; this binder makes the
/// high-value uniform behaviour work on the live table.</para>
///
/// <para>Re-uses the same continuous-effect primitives the factories use
/// (<see cref="AnimateLandEffect"/> — Layer 4 type/subtype grant + Layer 7b
/// set-base P/T + optional Layer 6 keyword grants) so combat math surfaces
/// through <see cref="ContinuousEffectsService.Compute(Permanent)"/>'s
/// creature-row upgrade.</para>
///
/// <para><b>Deferred (per-card riders this generic binder does NOT bind).</b>
/// See v1-deferrals + the per-pattern comments below:
/// <list type="bullet">
///   <item>Colour identity of the animated body (Layer 5) — no colour-setting
///     primitive exists; the "blue and black" text is recorded in the
///     effect-name string only (same gap as the factories).</item>
///   <item>Animate riders carrying a <b>granted quoted ability</b> ("with
///     \"{X}: …\"" / "with \"Whenever this creature attacks, …\"") — Den of
///     the Bugbear, Raging Ravine, Lavaclaw Reaches, Wandering Fumarole,
///     Restless Spire's conditional first strike, Hall of Storm Giants' ward.
///     The N/N body + simple keywords still bind; the granted ability is
///     dropped (no granted-activated/triggered-on-animate primitive).</item>
///   <item>"becomes a … with all creature types" (Mutavault) and X/X bodies
///     (Lair of the Hydra) — non-fixed subtype / non-fixed P/T.</item>
///   <item>A "Put counters … Then you may have it become …" preamble
///     (Crawling Barrens) — the animate is conditional on a prior counter
///     step.</item>
///   <item>Attack triggers that need a target prompt or the defending player
///     (Restless Bivouac / Cottage / Reef / Ridgeline / Vinestalk / Bones /
///     Fortress drain) — only the non-targeted, self-contained Restless
///     triggers (rummage, scry, anthem-pump) bind here; the rest are no-ops
///     until a target-prompt / defender-capture-aware binding lands.</item>
/// </list>
/// </para>
/// </summary>
public static class ManlandBinder
{
    // --- Animate line -----------------------------------------------------
    // Two oracle orderings, both meaning "until end of turn":
    //   A) "{cost}: Until end of turn, this land becomes a[n] N/N <colors>
    //       <Subtype> creature[ with <keywords>]. It's still a land."
    //   B) "{cost}: This land becomes a[n] N/N <colors> <Subtype> creature[
    //       with <keywords>] until end of turn. It's still a land."
    //
    // The body group captures everything between "becomes a[n] " and the
    // "creature" token. We match only the FRONT of the becomes-clause:
    //   "<cost>: [Until end of turn, ] this land becomes a[n] N/N <body>
    //    creature<rest…>"
    // where <body> = "<colors> [artifact] <Subtype>". The <rest> group is the
    // remainder of the oracle text (keyword tail + "until end of turn" + "It's
    // still a land." + any later lines); it is NOT pattern-anchored, so a
    // quoted granted ability with its own periods does not break the match.
    // The "still a land" gate + keyword / artifact / quoted-rider parsing are
    // done on the captured fragments in code (see Bind). X/X bodies (Lair of
    // the Hydra) are deferred — the P/T group requires digits.
    private static readonly Regex AnimateLine = new(
        @"(?<cost>(?:\{[^}]+\})+)\s*:\s*" +                       // {1}{U}{B}:
        @"(?:Until end of turn,\s*)?" +                            // optional leading EOT
        @"this land becomes an?\s+" +                             // becomes a / an
        @"(?<power>\d+)/(?<toughness>\d+)\s+" +                   // N/N (digits only)
        @"(?<body>(?:(?!creature\b)[^.\s]+\s+)*?)creature\b" +    // <colors> [artifact] <Subtype>
        @"(?<rest>.*)$",                                           // remainder (parsed in code)
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    // The colour words that precede the creature subtype in the body group.
    // Stripped so the trailing token is the subtype.
    private static readonly HashSet<string> ColorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "white", "blue", "black", "red", "green", "colorless", "and",
    };

    // Simple printed keywords a manland can animate with (CR 702). Multi-word
    // keywords are matched before single-word ones. Anything not in this set
    // (e.g. a quoted granted ability) is deferred.
    private static readonly (string Pattern, string Keyword)[] SimpleKeywords =
    {
        ("double strike", "Double Strike"),
        ("first strike", "First Strike"),
        ("flying", "Flying"),
        ("vigilance", "Vigilance"),
        ("reach", "Reach"),
        ("hexproof", "Hexproof"),
        ("lifelink", "Lifelink"),
        ("deathtouch", "Deathtouch"),
        ("trample", "Trample"),
        ("menace", "Menace"),
        ("haste", "Haste"),
        ("infect", "Infect"),
    };

    // --- Attack trigger ---------------------------------------------------
    // "Whenever this land attacks, <effect>." The non-targeted Restless
    // riders we bind (each branch handled in BindAttackTrigger):
    private static readonly Regex AttackTrigger = new(
        @"Whenever this land attacks,\s*(?<effect>[^.]*(?:\.[^.]*)*?)\.?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Rummage = new(
        @"you may discard a card\.\s*If you do, draw a card",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Scry = new(
        @"scry (?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Anthem = new(
        @"other creatures you control get \+(?<p>\d+)/\+(?<t>\d+) until end of turn",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Inspect <paramref name="entity"/>'s oracle text and bind the manland
    /// animate ability (+ a bound subset of Restless attack triggers) to
    /// <paramref name="card"/>. No-op unless the card is a <see cref="Land"/>
    /// with a recognisable animate line.
    /// </summary>
    /// <returns><c>true</c> if an animate ability was bound.</returns>
    public static bool Bind(
        ICard card,
        CardEntity entity,
        Player controller,
        ContinuousEffectsService effects,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(effects);

        if (card is not Land land) return false;

        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = AnimateLine.Match(text);
        if (!m.Success) return false;

        // Parse P/T (digits only — X/X bodies don't match the regex).
        if (!int.TryParse(m.Groups["power"].Value, out var power) ||
            !int.TryParse(m.Groups["toughness"].Value, out var toughness))
        {
            return false;
        }

        // Gate on "It's still a land." appearing in the remainder. This is the
        // manland signature (CR 613.1c). Without it the matched line is some
        // other "this land becomes a … creature" wording we don't model.
        var rest = m.Groups["rest"].Value;
        if (!rest.Contains("still a land", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Body = "<colors> [artifact] <Subtype>". The subtype is the last
        // remaining token after stripping colour words; a trailing "artifact"
        // before it makes the animated body an artifact creature (Blinkmoth
        // Nexus, Mishra's Factory, Frostwalk Bastion — extra Artifact type).
        // "becomes a … creature with all creature types" (Mutavault) and X/X
        // (Lair of the Hydra) never reach here (no fixed subtype / no digit
        // P/T) — they defer.
        var (subtype, isArtifact) = ParseBody(m.Groups["body"].Value);
        if (subtype is null) return false; // unknown / "all creature types" → defer

        // Keyword tail — the "with <kw…>" segment of the remainder, up to the
        // first sentence terminator OUTSIDE a quote. A quoted granted ability
        // ("with \"…\"") is deferred: bind the body + any simple keywords that
        // precede the quote, drop the quoted rider.
        var keywords = ParseKeywords(rest);

        var costText = m.Groups["cost"].Value;

        // The animate effect re-uses the cycle's shared continuous-effect
        // primitives (same as the per-card factories):
        //   - ManlandCycleAnimateEffect (Layer 4): Creature [+ Artifact] type,
        //     the parsed subtype, and the granted simple keywords. Printed Land
        //     stays ("It's still a land", CR 613.1c).
        //   - ManlandCycleBecomesPTEffect (Layer 7b): set-base P/T.
        // Both ExpiresAtEndOfTurn (CR 514.2 cleanup step lifts the animation).
        var capturedSubtype = subtype.Value;
        var extraTypes = isArtifact ? new[] { CardType.Artifact } : null;
        var animateEffect = new Effect(
            $"{land.Name}: becomes {power}/{toughness} {capturedSubtype} " +
            $"{(isArtifact ? "artifact " : "")}creature until EOT (still a land)",
            () =>
            {
                effects.Register(new ManlandCycleAnimateEffect(
                    land,
                    keywords: keywords,
                    subtypes: new[] { capturedSubtype },
                    extraTypes: extraTypes));
                effects.Register(new ManlandCycleBecomesPTEffect(
                    land, power, toughness));
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: controller,
            costs: new ICost[] { new ManaCostCost(costText) },
            effects: new IEffect[] { animateEffect }));

        // Restless cycle attack trigger (non-targeted subset only).
        BindAttackTrigger(land, text, controller, effects, triggers);

        return true;
    }

    /// <summary>
    /// Strip leading colour words from the body group, detect a trailing
    /// "artifact" supertype word, and Enum.TryParse the trailing token as a
    /// <see cref="CardSubtype"/>. Returns (null, _) for an unknown / multi-word
    /// subtype (defer). The bool is true when the body is an artifact creature.
    /// </summary>
    private static (CardSubtype? Subtype, bool IsArtifact) ParseBody(string body)
    {
        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !ColorWords.Contains(t))
            .ToList();
        if (tokens.Count == 0) return (null, false);

        var isArtifact = false;
        // "<Subtype> artifact" never happens; the printed order is
        // "<colors> [artifact] <Subtype>", so a lone trailing "artifact" with
        // no following subtype (shouldn't occur) and an "artifact" token before
        // the subtype are both handled by removing every "artifact" token.
        if (tokens.RemoveAll(t => t.Equals("artifact", StringComparison.OrdinalIgnoreCase)) > 0)
        {
            isArtifact = true;
        }
        if (tokens.Count == 0) return (null, isArtifact);

        // Normalise hyphenated subtypes to their PascalCase enum name
        // ("Assembly-Worker" → "AssemblyWorker" — Mishra's Factory / Foundry).
        var token = tokens[^1].Replace("-", "");
        return Enum.TryParse<CardSubtype>(token, ignoreCase: true, out var st)
            ? (st, isArtifact)
            : (null, isArtifact);
    }

    /// <summary>
    /// Parse the "with &lt;keywords&gt;" segment out of the becomes-clause
    /// remainder into canonical keyword strings. The segment runs from the
    /// first " with " after "creature" up to the first period OUTSIDE a quote
    /// (so the "It's still a land." rider and any later lines are excluded);
    /// the contents of a quoted granted ability are skipped (deferred). Returns
    /// only the simple keywords (CR 702) found in that segment.
    /// </summary>
    private static List<string> ParseKeywords(string rest)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(rest)) return result;

        // Trim leading "until end of turn" that can precede the keyword segment
        // in the "becomes a … creature with … until end of turn" ordering — it
        // carries no keyword, just normalise it out.
        var withIdx = rest.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (withIdx < 0) return result; // no "with" → no granted keywords

        // Walk from after " with " to the first unquoted period; skip the
        // interior of any quoted granted ability.
        var i = withIdx + " with ".Length;
        var seg = new System.Text.StringBuilder();
        var inQuote = false;
        for (; i < rest.Length; i++)
        {
            var ch = rest[i];
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == '.' && !inQuote) break;
            if (!inQuote) seg.Append(ch);
        }

        var lower = seg.ToString().ToLowerInvariant();
        foreach (var (pattern, keyword) in SimpleKeywords)
        {
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(pattern)}\b"))
            {
                result.Add(keyword);
            }
        }
        return result;
    }

    /// <summary>
    /// Bind the non-targeted Restless "Whenever this land attacks, …" trigger
    /// re-using the same effect primitives the Restless factories use. Targeted
    /// triggers (put a counter on target creature, mill target player, exile
    /// target card, pump another target creature) are deferred — they need an
    /// agent target prompt wired through the binder.
    /// </summary>
    private static void BindAttackTrigger(
        Land land,
        string text,
        Player controller,
        ContinuousEffectsService effects,
        TriggerManager? triggers)
    {
        var am = AttackTrigger.Match(text);
        if (!am.Success) return;

        var effectText = am.Groups["effect"].Value;

        IEffect? effect = null;

        if (Rummage.IsMatch(effectText))
        {
            // Restless Vents — "you may discard a card. If you do, draw a card."
            // Same rummage shape as RestlessVentsFactory (v1: discard taken
            // unconditionally, "if you do" honoured via discard-count gate).
            effect = new Effect(
                $"{land.Name}: discard a card, if you do draw a card (attack trigger, CR 508.1f)",
                () =>
                {
                    var ctrl = land.Controller ?? controller;
                    var discarded = Majik.Core.Primitives.Fx.Discard(ctrl, 1);
                    if (discarded.Count == 0) return;
                    Majik.Core.Primitives.Fx.DrawCards(ctrl, 1);
                });
        }
        else if (Scry.Match(effectText) is { Success: true } sm)
        {
            // Restless Spire — "scry N." Non-targeted (RestlessSpireFactory).
            var n = int.Parse(sm.Groups["n"].Value);
            effect = new Effect(
                $"{land.Name}: scry {n} (attack trigger)",
                async ctx =>
                {
                    var ctrl = land.Controller ?? controller;
                    var peeked = Majik.Core.Keywords.ScryAction.Peek(ctrl, n);
                    if (peeked.Count == 0) return;

                    var agent = ctx.Agent ?? Majik.Core.Players.Agents.AgentRegistry.Get(ctrl);
                    Majik.Core.Keywords.ScryAction.ScryDecision decision;
                    if (agent != null)
                    {
                        decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                            ToBottom: peeked.ToList(),
                            TopOrder: System.Array.Empty<ICard>());
                    }
                    Majik.Core.Keywords.ScryAction.Apply(ctrl, peeked.Count, decision);
                });
        }
        else if (Anthem.Match(effectText) is { Success: true } anm)
        {
            // Restless Prairie — "other creatures you control get +1/+1 until
            // end of turn." Non-targeted snapshot pump (RestlessPrairieFactory).
            var p = int.Parse(anm.Groups["p"].Value);
            var t = int.Parse(anm.Groups["t"].Value);
            effect = new Effect(
                $"{land.Name}: other creatures you control get +{p}/+{t} until end of turn (attack trigger)",
                () =>
                {
                    var ctrl = land.Controller ?? controller;
                    var others = ctrl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => !ReferenceEquals(c, land))
                        .ToList();
                    foreach (var creature in others)
                    {
                        effects.Register(new PumpUntilEndOfTurnEffect(creature, p, t));
                    }
                });
        }

        if (effect is null)
        {
            // Deferred Restless trigger (targeted / mill / Food+exile / +1/+1
            // counter on target / pump another target). Bind nothing — the
            // animate ability still works; the trigger is a no-op until a
            // target-prompt-aware binding lands. Recorded in v1-deferrals.
            return;
        }

        var trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }
}
