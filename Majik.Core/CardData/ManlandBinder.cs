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
/// <para><b>Colour identity of the animated body (Layer 5, CR 613.1e).</b> The
/// animate line names the body's colour(s) ("blue and black", "white and blue",
/// …). The binder parses those colour words and, on animate, registers a
/// <see cref="SetColorsEffect"/> scoped to the land (expiring at end of turn)
/// so the animated body enters as its printed colour instead of colourless.</para>
///
/// <para><b>Granted quoted abilities / parameterized keywords on animate (now
/// bound, CR 613.1c / CR 508.1f).</b> The animate line can carry a granted
/// quoted ability or a parameterized keyword in its remainder; these now bind:
/// <list type="bullet">
///   <item><b>ward {N}</b> (Hall of Storm Giants) — bound as a "Ward" keyword
///     marker on the animated body (CR 702.21; the {N} cost is recorded in the
///     marker only, same marker-only posture as the simple keywords).</item>
///   <item><b>Quoted "Whenever this creature attacks, &lt;effect&gt;" trigger</b>
///     — registered as a <see cref="TriggeredAbility"/> at animate-resolution
///     (<see cref="ParseQuotedAttackTrigger"/>). Bound effect bodies: create a
///     fixed creature token (Den of the Bugbear), put a +1/+1 counter on it
///     (Raging Ravine), exile target card from a graveyard (Hive of the Eye
///     Tyrant). Unrecognised bodies still defer.</item>
///   <item><b>Quoted "During your turn, this creature has first strike."</b>
///     (Restless Spire) — bound flatly as a First Strike keyword on animate
///     (the body only exists during the controller's turn, so the qualifier is
///     observationally always-true; matches RestlessSpireFactory).</item>
/// </list>
/// </para>
///
/// <para><b>Deferred (per-card riders this generic binder does NOT bind).</b>
/// See v1-deferrals + the per-pattern comments below:
/// <list type="bullet">
///   <item>Animate riders carrying a <b>granted quoted ACTIVATED ability</b>
///     ("with \"{X}: …\"") — Lavaclaw Reaches, Wandering Fumarole (firebreathing
///     pump). The N/N body + simple keywords still bind; the granted activated
///     ability is dropped (no granted-activated-on-animate primitive).</item>
///   <item>"becomes a … with all creature types" (Mutavault) and X/X bodies
///     (Lair of the Hydra) — non-fixed subtype / non-fixed P/T.</item>
///   <item>A "Put counters … Then you may have it become …" preamble
///     (Crawling Barrens) — the animate is conditional on a prior counter
///     step.</item>
///   <item>Restless Bones' "exile up to two target cards from graveyards, then
///     create that many tapped 2/2 Skeletons" attack trigger — the
///     count-linked token rider has no generic primitive yet; deferred.</item>
/// </list>
/// </para>
///
/// <para><b>Targeted Restless attack triggers (now bound, CR 603.3).</b> The
/// six targeted/defender-capturing Restless attack triggers — Bivouac (+1/+1
/// counter on target creature you control), Cottage (Food + exile up to one
/// target graveyard card), Reef (target player mills four), Ridgeline (another
/// target attacking creature gets +2/+0 + untap), Vinestalk (up to one other
/// target creature becomes base 3/3), and Fortress (defending player loses 2 /
/// you gain 2) — bind here as real <see cref="TriggeredAbility"/>s. The five
/// targeting ones declare a <see cref="Players.Agents.TargetRequest"/> with a
/// <c>CandidateGatherer</c> scoped per the oracle; the live
/// <see cref="TriggerManager"/> collects the agent's chosen target via
/// <see cref="Targeting.TargetCollection.CollectAsync"/> and the effect reads
/// <see cref="TriggeredAbility.ChosenTargets"/> on resolution (CR 608.2b
/// resolve-time legality recheck). Fortress is non-targeted; it captures the
/// defending player off the live <see cref="Events.CreatureAttacksEvent"/>.</para>
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

    // CR 105.1 — map each printed colour word to its ManaColor. "colorless" /
    // "and" carry no colour and are filtered (SetColorsEffect drops Colorless).
    private static readonly IReadOnlyDictionary<string, Majik.Core.ValueObjects.ManaColor> ColorWordToColor =
        new Dictionary<string, Majik.Core.ValueObjects.ManaColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = Majik.Core.ValueObjects.ManaColor.White,
            ["blue"] = Majik.Core.ValueObjects.ManaColor.Blue,
            ["black"] = Majik.Core.ValueObjects.ManaColor.Black,
            ["red"] = Majik.Core.ValueObjects.ManaColor.Red,
            ["green"] = Majik.Core.ValueObjects.ManaColor.Green,
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
    // Restless Anchorage — "create a Map token." Non-targeted artifact-token
    // mint (CR 111.10). The Map's "{1},{T}, Sacrifice: target creature you
    // control explores" ability ships on the token from TokenFactory.CreateMap.
    private static readonly Regex CreateMapToken = new(
        @"create a Map token",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // --- Targeted / defender-capturing Restless attack triggers (CR 603.3) ---
    // Restless Bivouac — "put a +1/+1 counter on target creature you control."
    private static readonly Regex CounterOnTargetYouControl = new(
        @"put a \+1/\+1 counter on target creature you control",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Restless Cottage — "create a Food token[,]? then exile up to one target
    // card from a graveyard." (Scryfall uses "and"/"then"; accept both.)
    private static readonly Regex FoodThenExileGraveyardCard = new(
        @"create a Food token[,]?\s+(?:and|then)\s+exile up to one target card from a graveyard",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Restless Reef — "target player mills four cards." (number is fixed text.)
    private static readonly Regex TargetPlayerMills = new(
        @"target player mills (?<n>\d+|a|one|two|three|four|five|six|seven|eight|nine|ten) cards?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Restless Ridgeline — "another target attacking creature gets +N/+0 until
    // end of turn. Untap that creature."
    private static readonly Regex AnotherTargetPumpUntap = new(
        @"another target attacking creature gets \+(?<p>\d+)/\+(?<t>\d+) until end of turn\.?\s*untap that creature",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Restless Vinestalk — "up to one other target creature has base power and
    // toughness N/N until end of turn."
    private static readonly Regex UpToOneOtherTargetBasePT = new(
        @"up to one other target creature has base power and toughness (?<p>\d+)/(?<t>\d+) until end of turn",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Restless Fortress — "defending player loses N life and you gain N life."
    // Non-targeted (defender captured off CreatureAttacksEvent).
    private static readonly Regex DefenderLosesYouGain = new(
        @"defending player loses (?<n>\d+) life and you gain (?<g>\d+) life",
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
        var (subtype, isArtifact, bodyColors) = ParseBody(m.Groups["body"].Value);
        if (subtype is null) return false; // unknown / "all creature types" → defer

        // Keyword tail — the "with <kw…>" segment of the remainder, up to the
        // first sentence terminator OUTSIDE a quote. Simple keywords (CR 702)
        // that precede the quote bind here; ward {N} binds as a marker.
        var keywords = ParseKeywords(rest).ToList();

        // Quoted granted abilities ("with \"…\"") — CR 613.1c / CR 508.1f.
        // Two shapes bound (the rest still defer):
        //   1) A conditional STATIC keyword grant ("During your turn, this
        //      creature has first strike." — Restless Spire). Bound flatly as a
        //      First Strike keyword on animate (v1 posture — the body only
        //      exists during the controller's turn, so "during your turn" is
        //      observationally always-true while it can attack; matches
        //      RestlessSpireFactory).
        //   2) A granted "Whenever this creature attacks, <effect>" TRIGGER
        //      (Den of the Bugbear / Raging Ravine / Hive of the Eye Tyrant) —
        //      registered as a TriggeredAbility at animate-resolution (see
        //      grantedAttackQuote below).
        var grantedKeywordFromQuote = ParseQuotedConditionalKeyword(rest);
        if (grantedKeywordFromQuote is not null &&
            !keywords.Contains(grantedKeywordFromQuote))
        {
            keywords.Add(grantedKeywordFromQuote);
        }
        var grantedAttackQuote = ParseQuotedAttackTrigger(rest, land, controller, triggers);

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
        // CR 613.1e (Layer 5) — the animate line names the body's colour(s)
        // ("blue and black", "white and blue", …). Animated manlands are
        // colourless Lands; SET the parsed colours onto the body for the
        // animation's duration so it enters as its printed colour instead of
        // colourless. No colour word (or only "colorless") → no SET registered.
        var capturedColors = bodyColors;
        var animateEffect = new Effect(
            $"{land.Name}: becomes {power}/{toughness} " +
            $"{string.Join(" and ", capturedColors)} {capturedSubtype} " +
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
                if (capturedColors.Count > 0)
                {
                    effects.Register(new SetColorsEffect(
                        source: land,
                        scope: p => ReferenceEquals(p, land),
                        colors: capturedColors,
                        expiresAtEndOfTurn: true));
                }

                // CR 508.1f — granted quoted "Whenever this creature attacks,
                // <effect>" trigger. Registered at animate-resolution and
                // attached to the land (matches the per-card factories'
                // animate-time grant). A land can only attack while it's the
                // animated creature (CR 508.1a), so leaving it attached after
                // the animation expires is observationally equivalent to an
                // until-EOT grant (the land can't attack once it's no longer a
                // creature). Mirrors RagingRavineFactory / DenOfTheBugbearFactory.
                grantedAttackQuote?.Invoke();
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
    /// Parse the body group "&lt;colors&gt; [artifact] &lt;Subtype&gt;" into the
    /// animated body's colours (CR 105.1), an artifact-supertype flag, and the
    /// <see cref="CardSubtype"/>. The colour words are captured (in printed
    /// order, deduped) for the Layer-5 SET; the remaining tokens after stripping
    /// colour words + "artifact" yield the subtype. Returns
    /// <c>(null, _, _)</c> for an unknown / multi-word subtype (defer).
    /// </summary>
    private static (CardSubtype? Subtype, bool IsArtifact, IReadOnlyList<Majik.Core.ValueObjects.ManaColor> Colors) ParseBody(string body)
    {
        var rawTokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // CR 105.1 — colour words in printed order (deduped), e.g. "blue and
        // black" → [Blue, Black]. "colorless"/"and" carry no colour.
        var colors = new List<Majik.Core.ValueObjects.ManaColor>();
        foreach (var t in rawTokens)
        {
            if (ColorWordToColor.TryGetValue(t, out var c) && !colors.Contains(c))
            {
                colors.Add(c);
            }
        }

        var tokens = rawTokens
            .Where(t => !ColorWords.Contains(t))
            .ToList();
        if (tokens.Count == 0) return (null, false, colors);

        var isArtifact = false;
        // "<Subtype> artifact" never happens; the printed order is
        // "<colors> [artifact] <Subtype>", so a lone trailing "artifact" with
        // no following subtype (shouldn't occur) and an "artifact" token before
        // the subtype are both handled by removing every "artifact" token.
        if (tokens.RemoveAll(t => t.Equals("artifact", StringComparison.OrdinalIgnoreCase)) > 0)
        {
            isArtifact = true;
        }
        if (tokens.Count == 0) return (null, isArtifact, colors);

        // Normalise hyphenated subtypes to their PascalCase enum name
        // ("Assembly-Worker" → "AssemblyWorker" — Mishra's Factory / Foundry).
        var token = tokens[^1].Replace("-", "");
        return Enum.TryParse<CardSubtype>(token, ignoreCase: true, out var st)
            ? (st, isArtifact, colors)
            : (null, isArtifact, colors);
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

        // Ward {N} (CR 702.21) — a parameterized keyword the SimpleKeywords
        // table can't carry. Hall of Storm Giants animates "with ward {3}".
        // Bind it as a "Ward" keyword marker on the animated body (same
        // marker-only posture as HallOfStormGiantsFactory — combat/targeting
        // Ward enforcement reads the effective keyword set; the {N} cost is
        // recorded in the marker only).
        if (Regex.IsMatch(lower, @"\bward\b"))
        {
            result.Add("Ward");
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
        else if (CreateMapToken.IsMatch(effectText))
        {
            // Restless Anchorage — "create a Map token." Non-targeted artifact
            // token mint (CR 111.10). TokenFactory.CreateMap ships the Map's
            // "{1},{T}, Sacrifice: target creature you control explores" ability.
            effect = new Effect(
                $"{land.Name}: create a Map token (attack trigger, CR 111.10)",
                () =>
                {
                    var ctrl = land.Controller ?? controller;
                    Majik.Core.Tokens.TokenFactory.CreateMap(ctrl);
                });
        }

        if (effect is not null)
        {
            // Non-targeted self-contained Restless trigger (rummage / scry /
            // anthem). Simple no-target shape.
            var simpleTrigger = new TriggeredAbility(
                source: land,
                controller: controller,
                condition: Triggers.OnAttackSelf(land),
                effects: new IEffect[] { effect },
                activeZones: new[] { ZoneType.Battlefield });

            land.AddAbility(simpleTrigger);
            triggers?.RegisterTriggeredAbility(simpleTrigger);
            return;
        }

        // ------------------------------------------------------------------
        // Targeted / defender-capturing Restless attack triggers (CR 603.3).
        // Each declares a TargetRequest with a CandidateGatherer (or, for
        // Fortress, captures the defender off CreatureAttacksEvent). The live
        // TriggerManager collects the agent's chosen target and the effect
        // reads ChosenTargets on resolution (CR 608.2b legality recheck).
        // ------------------------------------------------------------------
        if (BindCounterOnTargetTrigger(land, effectText, controller, triggers)) return;
        if (BindFoodExileTrigger(land, effectText, controller, triggers)) return;
        if (BindTargetPlayerMillTrigger(land, effectText, controller, triggers)) return;
        if (BindAnotherTargetPumpUntapTrigger(land, effectText, controller, effects, triggers)) return;
        if (BindUpToOneOtherTargetBasePTTrigger(land, effectText, controller, effects, triggers)) return;
        if (BindDefenderDrainTrigger(land, effectText, controller, triggers)) return;

        // Anything else (Restless Bones' count-linked exile→Skeleton rider)
        // stays deferred — the animate ability still works; the trigger is a
        // no-op until a richer primitive lands. Recorded in v1-deferrals.
    }

    /// <summary>
    /// Restless Bivouac — "put a +1/+1 counter on target creature you control."
    /// 1..1 TargetRequest gated to the controller's creatures; resolution adds
    /// one +1/+1 counter after a CR 608.2b legality recheck.
    /// </summary>
    private static bool BindCounterOnTargetTrigger(
        Land land, string effectText, Player controller, TriggerManager? triggers)
    {
        if (!CounterOnTargetYouControl.IsMatch(effectText)) return false;

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{land.Name}: put a +1/+1 counter on target creature you control (attack trigger)",
            () =>
            {
                var chosen = FirstChosen(trigger);
                if (chosen is not Permanent target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                // CR 608.2b — "you control" recheck against the trigger's
                // controller (land.Controller may be unset pre-game).
                var you = land.Controller ?? controller;
                if (!ReferenceEquals(target.Controller, you)) return;
                target.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: _ => GatherControllerCreatures(land)),
            });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>
    /// Restless Cottage — "create a Food token, then exile up to one target
    /// card from a graveyard." The Food is unconditional; the exile is the
    /// 0..1 target. Resolution always mints the Food and exiles the chosen
    /// graveyard card if one is still in a graveyard (CR 608.2b).
    /// </summary>
    private static bool BindFoodExileTrigger(
        Land land, string effectText, Player controller, TriggerManager? triggers)
    {
        if (!FoodThenExileGraveyardCard.IsMatch(effectText)) return false;

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{land.Name}: create a Food token, then exile up to one target card from a graveyard (attack trigger)",
            () =>
            {
                var ctrl = land.Controller ?? controller;
                Majik.Core.Tokens.TokenFactory.CreateFood(ctrl);

                var chosen = FirstChosen(trigger);
                if (chosen is not ICard card) return;
                if (card.Zone != ZoneType.Graveyard) return;
                Majik.Core.Primitives.Fx.MoveToExile(card);
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "target card in a graveyard",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherGraveyardCards(ctx)),
            });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>
    /// Restless Reef — "target player mills four cards." 1..1 player target;
    /// resolution mills via <see cref="Majik.Core.Keywords.MillAction.Apply"/>.
    /// </summary>
    private static bool BindTargetPlayerMillTrigger(
        Land land, string effectText, Player controller, TriggerManager? triggers)
    {
        var m = TargetPlayerMills.Match(effectText);
        if (!m.Success) return false;
        var n = WordToInt(m.Groups["n"].Value);
        if (n <= 0) return false;

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{land.Name}: target player mills {n} cards (attack trigger)",
            () =>
            {
                var chosen = FirstChosen(trigger);
                if (chosen is not Player target) return;
                Majik.Core.Keywords.MillAction.Apply(target, n);
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Mill,
                    CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),
            });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>
    /// Restless Ridgeline — "another target attacking creature gets +N/+0 until
    /// end of turn. Untap that creature." 1..1 target over OTHER creatures;
    /// resolution registers a +N/+0 pump (CR 613.7c, EOT expiry) and untaps the
    /// creature (CR 701.21, gated on IsTapped).
    /// </summary>
    private static bool BindAnotherTargetPumpUntapTrigger(
        Land land, string effectText, Player controller,
        ContinuousEffectsService effects, TriggerManager? triggers)
    {
        var m = AnotherTargetPumpUntap.Match(effectText);
        if (!m.Success) return false;
        var p = int.Parse(m.Groups["p"].Value);
        var t = int.Parse(m.Groups["t"].Value);

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{land.Name}: another target attacking creature gets +{p}/+{t} until EOT; untap it (attack trigger)",
            () =>
            {
                var chosen = FirstChosen(trigger);
                if (chosen is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (ReferenceEquals(target, land)) return; // "another"
                effects.Register(new PumpUntilEndOfTurnEffect(target, p, t));
                if (target.IsTapped) target.Untap();
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "another target attacking creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => GatherOtherCreatures(land, ctx)),
            });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>
    /// Restless Vinestalk — "up to one other target creature has base power and
    /// toughness N/N until end of turn." 0..1 target over OTHER creatures;
    /// resolution registers a set-base P/T effect (CR 613.7b, EOT expiry).
    /// </summary>
    private static bool BindUpToOneOtherTargetBasePTTrigger(
        Land land, string effectText, Player controller,
        ContinuousEffectsService effects, TriggerManager? triggers)
    {
        var m = UpToOneOtherTargetBasePT.Match(effectText);
        if (!m.Success) return false;
        var p = int.Parse(m.Groups["p"].Value);
        var t = int.Parse(m.Groups["t"].Value);

        TriggeredAbility? trigger = null;
        var effect = new Effect(
            $"{land.Name}: up to one other target creature has base P/T {p}/{t} until EOT (attack trigger)",
            () =>
            {
                var chosen = FirstChosen(trigger);
                if (chosen is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (ReferenceEquals(target, land)) return; // "other"
                effects.Register(new BecomesPTUntilEndOfTurnEffect(target, p, t));
            });

        trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: Triggers.OnAttackSelf(land),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "up to one other target creature",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => GatherOtherCreatures(land, ctx)),
            });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>
    /// Restless Fortress — "defending player loses N life and you gain N life."
    /// Non-targeted: the defending player is captured off the live
    /// <see cref="Events.CreatureAttacksEvent"/> (CR 506.2); the controller's
    /// gain applies regardless (CR 119.3).
    /// </summary>
    private static bool BindDefenderDrainTrigger(
        Land land, string effectText, Player controller, TriggerManager? triggers)
    {
        var m = DefenderLosesYouGain.Match(effectText);
        if (!m.Success) return false;
        var lose = int.Parse(m.Groups["n"].Value);
        var gain = int.Parse(m.Groups["g"].Value);

        Player? capturedDefender = null;
        var effect = new Effect(
            $"{land.Name}: defending player loses {lose} life; you gain {gain} life (attack trigger)",
            () =>
            {
                capturedDefender?.LoseLife(lose);
                var ctrl = land.Controller ?? controller;
                ctrl.GainLife(gain);
            });

        var trigger = new TriggeredAbility(
            source: land,
            controller: controller,
            condition: new Majik.Core.Abilities.EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(
                (e, _) =>
                {
                    capturedDefender = e.DefendingPlayerOrPlaneswalker as Player;
                    return ReferenceEquals(e.Attacker, land);
                }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        land.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
        return true;
    }

    /// <summary>First chosen target across the trigger's first request, or null.</summary>
    private static object? FirstChosen(TriggeredAbility? trigger)
    {
        if (trigger is null) return null;
        var chosen = trigger.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null;
        return chosen[0][0];
    }

    /// <summary>CR 601.2c — the controller's battlefield creatures.</summary>
    private static IReadOnlyList<object> GatherControllerCreatures(Land land)
    {
        var ctrl = land.Controller ?? land.Owner;
        if (ctrl == null) return Array.Empty<object>();
        return ctrl.Zones.Battlefield.GetCards().OfType<Creature>().Cast<object>().ToList();
    }

    /// <summary>CR 601.2c — every creature on the battlefield except the source
    /// land itself ("another" / "other"). Scans every player's battlefield.</summary>
    private static IReadOnlyList<object> GatherOtherCreatures(
        Land land, Majik.Core.Game.GameContext ctx)
    {
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (ReferenceEquals(c, land)) continue;
                if (!result.Any(r => ReferenceEquals(r, c))) result.Add(c);
            }
        }
        return result;
    }

    /// <summary>CR 601.2c — every card in any player's graveyard.</summary>
    private static IReadOnlyList<object> GatherGraveyardCards(Majik.Core.Game.GameContext ctx)
    {
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            foreach (var c in p.Zones.Graveyard.GetCards())
            {
                result.Add(c);
            }
        }
        return result;
    }

    // --- Quoted granted abilities on animate (CR 613.1c / CR 508.1f) -------
    // The body group from AnimateLine stops at "creature"; the quoted granted
    // ability lives in <rest>, between double quotes. These two helpers scan
    // <rest> for the quoted riders we bind.

    // A quoted "Whenever this creature attacks, <effect>." granted trigger.
    private static readonly Regex QuotedAttackTrigger = new(
        "\"\\s*Whenever this creature attacks,\\s*(?<effect>[^\"]*?)\\.?\\s*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A quoted conditional STATIC keyword grant — "During your turn, this
    // creature has first strike." (Restless Spire). Bound flatly (v1 posture).
    private static readonly Regex QuotedConditionalFirstStrike = new(
        "\"[^\"]*\\bfirst strike\\b[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Recognise a quoted conditional first-strike grant in the animate
    /// remainder and return the canonical keyword to add flatly to the
    /// animated body. Returns null when no such quoted rider is present.
    /// CR 702.7 — first strike; the "during your turn" qualifier is recorded
    /// only (the body never exists outside the controller's turn).
    /// </summary>
    private static string? ParseQuotedConditionalKeyword(string rest)
    {
        if (string.IsNullOrWhiteSpace(rest)) return null;
        return QuotedConditionalFirstStrike.IsMatch(rest) ? "First Strike" : null;
    }

    /// <summary>
    /// Recognise a quoted "Whenever this creature attacks, &lt;effect&gt;"
    /// granted trigger in the animate remainder and return an action that, when
    /// invoked at animate-resolution, registers the corresponding
    /// <see cref="TriggeredAbility"/> on <paramref name="land"/> (CR 508.1f).
    /// Returns null for an unrecognised effect body (deferred — the body +
    /// simple keywords still animate). Bound effect bodies:
    /// <list type="bullet">
    ///   <item>"create a [N/N] [color] &lt;Subtype&gt; creature token …" — a
    ///     simple fixed creature token (Den of the Bugbear's Goblin). The
    ///     "tapped and attacking" rider is recorded but the token enters as a
    ///     normal untapped permanent (same posture as DenOfTheBugbearFactory).</item>
    ///   <item>"put a +1/+1 counter on it" — a +1/+1 counter on the land itself
    ///     (Raging Ravine).</item>
    ///   <item>"exile target card from [defending player's] graveyard" — a 1..1
    ///     TargetRequest over graveyard cards; resolution exiles the chosen card
    ///     (Hive of the Eye Tyrant; the defending-player scoping is simplified
    ///     to any graveyard, matching HiveOfTheEyeTyrantFactory).</item>
    /// </list>
    /// </summary>
    private static Action? ParseQuotedAttackTrigger(
        string rest, Land land, Player controller, TriggerManager? triggers)
    {
        if (string.IsNullOrWhiteSpace(rest)) return null;
        var qm = QuotedAttackTrigger.Match(rest);
        if (!qm.Success) return null;
        var effectText = qm.Groups["effect"].Value.Trim();

        // --- "create a 1/1 red Goblin creature token …" (Den of the Bugbear) -
        var tokenM = Regex.Match(
            effectText,
            @"create a (?<p>\d+)/(?<t>\d+)\s+(?<rest>.+?)\s+creature token",
            RegexOptions.IgnoreCase);
        if (tokenM.Success &&
            TryParseTokenBody(tokenM.Groups["rest"].Value,
                out var subtype, out var colors))
        {
            var p = int.Parse(tokenM.Groups["p"].Value);
            var t = int.Parse(tokenM.Groups["t"].Value);
            return () =>
            {
                var tokenEffect = new Effect(
                    $"{land.Name}: create a {p}/{t} {subtype} token (granted attack trigger, CR 508.1f)",
                    () =>
                    {
                        var ctrl = land.Controller ?? controller;
                        var spec = new Majik.Core.Tokens.TokenFactory.TokenSpec(
                            Name: subtype.ToString(),
                            Power: p,
                            Toughness: t,
                            Subtypes: new[] { subtype },
                            Keywords: null,
                            Colors: colors);
                        Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(spec, ctrl);
                    });
                var trigger = new TriggeredAbility(
                    source: land,
                    controller: controller,
                    condition: Triggers.OnAttackSelf(land),
                    effects: new IEffect[] { tokenEffect },
                    activeZones: new[] { ZoneType.Battlefield });
                land.AddAbility(trigger);
                triggers?.RegisterTriggeredAbility(trigger);
            };
        }

        // --- "put a +1/+1 counter on it" (Raging Ravine) --------------------
        if (Regex.IsMatch(effectText,
                @"put a \+1/\+1 counter on it", RegexOptions.IgnoreCase))
        {
            return () =>
            {
                var counterEffect = new Effect(
                    $"{land.Name}: put a +1/+1 counter on itself (granted attack trigger, CR 508.1f)",
                    () =>
                    {
                        if (land.Zone != ZoneType.Battlefield) return;
                        land.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
                    });
                var trigger = new TriggeredAbility(
                    source: land,
                    controller: controller,
                    condition: Triggers.OnAttackSelf(land),
                    effects: new IEffect[] { counterEffect },
                    activeZones: new[] { ZoneType.Battlefield });
                land.AddAbility(trigger);
                triggers?.RegisterTriggeredAbility(trigger);
            };
        }

        // --- "exile target card from [defending player's] graveyard" (Hive) -
        if (Regex.IsMatch(effectText,
                @"exile target card from .*graveyard", RegexOptions.IgnoreCase))
        {
            return () =>
            {
                TriggeredAbility? trigger = null;
                var exileEffect = new Effect(
                    $"{land.Name}: exile target card from a graveyard (granted attack trigger, CR 508.1f)",
                    () =>
                    {
                        var chosen = FirstChosen(trigger);
                        if (chosen is not ICard card) return;
                        if (card.Zone != ZoneType.Graveyard) return;
                        Majik.Core.Primitives.Fx.MoveToExile(card);
                    });
                trigger = new TriggeredAbility(
                    source: land,
                    controller: controller,
                    condition: Triggers.OnAttackSelf(land),
                    effects: new IEffect[] { exileEffect },
                    activeZones: new[] { ZoneType.Battlefield },
                    targetRequests: new[]
                    {
                        new Majik.Core.Players.Agents.TargetRequest(
                            Description: "target card from a graveyard",
                            MinTargets: 1,
                            MaxTargets: 1,
                            LegalCandidates: Array.Empty<object>(),
                            Intent: BotIntent.Removal,
                            CandidateGatherer: ctx => GatherGraveyardCards(ctx)),
                    });
                land.AddAbility(trigger);
                triggers?.RegisterTriggeredAbility(trigger);
            };
        }

        // Unrecognised quoted attack-trigger body — defer (body + simple
        // keywords still animate). Recorded in v1-deferrals.
        return null;
    }

    /// <summary>
    /// Parse a token body "&lt;colors&gt; &lt;Subtype&gt;" (e.g. "red Goblin")
    /// from a quoted "create a N/N &lt;body&gt; creature token" rider into its
    /// <see cref="CardSubtype"/> and printed colours (CR 105.1). Returns false
    /// for an unknown subtype (defer).
    /// </summary>
    private static bool TryParseTokenBody(
        string body, out CardSubtype subtype,
        out IReadOnlyList<Majik.Core.ValueObjects.ManaColor> colors)
    {
        subtype = default;
        var clrs = new List<Majik.Core.ValueObjects.ManaColor>();
        var tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var tk in tokens)
        {
            if (ColorWordToColor.TryGetValue(tk, out var c) && !clrs.Contains(c)) clrs.Add(c);
        }
        colors = clrs;
        var nonColor = tokens.Where(tk => !ColorWords.Contains(tk)).ToList();
        if (nonColor.Count == 0) return false;
        var token = nonColor[^1].Replace("-", "");
        return Enum.TryParse(token, ignoreCase: true, out subtype);
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
