using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Synthesizes mana abilities from a card's TypeLine + OracleText:
///  - Basic lands (subtype Mountain/Forest/Plains/Island/Swamp) → fixed
///    tap-for-one-color, regardless of oracle text (Scryfall sometimes
///    omits the rules-text on a basic).
///  - Otherwise, scan oracle text for "{T}: Add {COLOR}." patterns —
///    handles Llanowar Elves, Mox, etc.
///
/// Limitations: ignores cost prefixes that aren't pure {T}, multi-color
/// modal mana ("Add one mana of any color"), and conditional triggers.
/// Those are deliberate gaps left for a richer binder.
/// </summary>
public static class OracleManaBinder
{
    // Shared with Majik.Core.Effects.EffectiveManaAbilities so printed
    // basic-land binding and Layer-4 retyping-induced override stay in
    // sync. See <see cref="BasicLandManaColors"/>.
    private static IReadOnlyDictionary<CardSubtype, string> BasicLandColors
        => BasicLandManaColors.Map;

    // {T}: Add {R}.  or  {T}: Add {R}{R}.  or  {T}: Add one {G}.
    private static readonly Regex TapForManaRegex = new(
        @"\{T\}\s*:\s*Add\s+((?:\{[WUBRGC]\}\s*)+)",
        RegexOptions.IgnoreCase);

    // {T}: Add {R} or {W}.   — dual-color lands (Ravnica shocks, Triomes,
    // checks, painlands, etc.). Binds a separate ManaAbility per option;
    // the bot picks whichever colour it needs at activation time.
    private static readonly Regex TapForModalManaRegex = new(
        @"\{T\}\s*:\s*Add\s+(\{[WUBRGC]\})(?:\s*,\s*(\{[WUBRGC]\}))?(?:\s*,?\s*or\s+(\{[WUBRGC]\}))",
        RegexOptions.IgnoreCase);

    // {T}: Add one mana of any color.  — Mox Opal, City of Brass, etc.
    // Bind as five separate ManaAbility options (one per WUBRG).
    private static readonly Regex TapForAnyColorRegex = new(
        @"\{T\}\s*:\s*Add\s+one\s+mana\s+of\s+any\s+color",
        RegexOptions.IgnoreCase);

    // {T}, Pay 1 life: Add {U} or {R}.  — Modern Horizons "Horizon Canopy"
    // painless-dual cycle (Fiery Islet, Sunbaked Canyon, Silent Clearing,
    // Nurturing Peatland, Waterlogged Grove, Horizon Canopy). The cost prefix
    // is "{T}, Pay 1 life:" rather than a bare "{T}:", so the standard
    // tap-for-mana regexes never match it. Each colour is bound as a separate
    // pay-life ManaAbility (life-floor gate, CR 119.4) via HorizonLandBinder.
    private static readonly Regex PayLifeDualManaRegex = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*:\s*Add\s+(\{[WUBRGC]\})\s*or\s+(\{[WUBRGC]\})",
        RegexOptions.IgnoreCase);

    // {T}, Pay 1 life: Add one mana of any color.  — Mana Confluence
    // (Journey into Nyx). The any-colour sibling of the Horizon Canopy
    // pay-life dual above: the cost prefix is "{T}, Pay 1 life:" (NOT a bare
    // {T}), so neither TapForAnyColorRegex nor the dual pay-life regex match
    // it. Binds five pay-life ManaAbility options (one per WUBRG). The
    // life-floor gate is the precise CR 119.4 reading — payable at exactly
    // 1 life (drops to 0), NOT the stricter "> 1" HorizonLandBinder gate.
    private static readonly Regex PayLifeAnyColorRegex = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*:\s*Add\s+one\s+mana\s+of\s+any\s+color",
        RegexOptions.IgnoreCase);

    // {T}, Remove a mining counter from this land: Add one mana of any color.
    // If there are no mining counters on this land, sacrifice it.
    //   — Gemstone Mine (Weatherlight + reprints). The cost prefix is
    // "{T}, Remove a mining counter from this land:" (NOT a bare {T}), so the
    // bare TapForAnyColorRegex never matches this line — there is no risk of a
    // free any-colour ability slipping in. Binds five counter-cost ManaAbility
    // options (one per WUBRG); each removes a mining counter as part of the
    // activation cost (CR 119.4 — the cost must be payable) and, when the last
    // counter is gone, sacrifices the land (CR 701.16). The "enters with three
    // mining counters" rider is wired as an ETB trigger (see BindGemstoneMine)
    // because EntersWithCountersReplacement only models +1/+1 counters today.
    private static readonly Regex GemstoneMineCounterManaRegex = new(
        @"\{T\}\s*,\s*Remove\s+a\s+mining\s+counter\s+from\s+this\s+land\s*:\s*"
        + @"Add\s+one\s+mana\s+of\s+any\s+color",
        RegexOptions.IgnoreCase);

    // "This land enters with three mining counters on it." — Gemstone Mine's
    // ETB rider. Parameterised on the count word so a future variant reads
    // cleanly; today only "three" ships.
    private static readonly Regex EntersWithMiningCountersRegex = new(
        @"enters\s+with\s+(?<n>a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)"
        + @"\s+mining\s+counters?\s+on\s+it",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Attaches the correct <see cref="ManaAbility"/> to a basic land that
    /// already has its controller set. Idempotent — if the card is not a
    /// Basic land with a known subtype, nothing is added.
    /// </summary>
    public static bool BindBasicLandMana(ICard card, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        return TryBindBasicLand(card, controller);
    }

    public static void Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        if (TryBindBasicLand(card, controller)) return;
        TryBindFromOracle(card, entity, controller);
    }

    /// <summary>
    /// Parse the "{T}: Add …" mana-ability clauses out of <paramref name="oracleText"/>
    /// and return one <see cref="ManaCost"/> per <see cref="ManaAbility"/> that
    /// <see cref="Bind"/> would attach — WITHOUT mutating any card. The same
    /// regexes drive both methods, so a card's printed mana abilities and this
    /// list stay in lock-step.
    ///
    /// <para>This is the card-identity-agnostic core that makes a "{T}: Add {C}"
    /// mana ability RE-HOMABLE to an arbitrary source: a caller can take the
    /// returned costs and build fresh <see cref="ManaAbility"/> instances homed
    /// to any permanent (CR 605.1a). The canonical consumer is Agatha's Soul
    /// Cauldron's ability-grant static — it grants each imprinted creature card's
    /// mana abilities to the bearer by re-running this parse against the imprinted
    /// card's oracle text and constructing mana abilities sourced on the bearer.
    /// Only the {T}-cost mana clauses are returned; non-mana activated abilities
    /// (e.g. "{2}: this gets +1/+1") are not produced by this binder at all.</para>
    /// </summary>
    /// <returns>One <see cref="ManaCost"/> per mana ability the oracle text
    /// implies (an any-colour clause expands to five single-colour costs; a modal
    /// "Add {R} or {W}" to one per option). Empty when the text has no
    /// {T}-cost mana clause.</returns>
    public static IReadOnlyList<ManaCost> ParseTapManaCosts(string? oracleText)
    {
        var result = new List<ManaCost>();
        if (string.IsNullOrWhiteSpace(oracleText)) return result;

        // Any-colour mana sources (Mox Opal, City of Brass, command tower).
        if (TapForAnyColorRegex.IsMatch(oracleText))
        {
            foreach (var color in new[] { "W", "U", "B", "R", "G" })
                result.Add(ManaCost.Parse(color));
            return result;
        }

        // Dual / triple colour modal — one ManaCost per option.
        foreach (Match m in TapForModalManaRegex.Matches(oracleText))
        {
            for (var i = 1; i <= 3; i++)
            {
                if (!m.Groups[i].Success) continue;
                var raw = m.Groups[i].Value.Replace("{", "").Replace("}", "");
                result.Add(ManaCost.Parse(raw));
            }
            return result; // matched a modal — don't double-add via the non-modal regex
        }

        foreach (Match m in TapForManaRegex.Matches(oracleText))
        {
            var symbols = m.Groups[1].Value;
            var stripped = symbols.Replace("{", "").Replace("}", "").Replace(" ", "");
            if (string.IsNullOrEmpty(stripped)) continue;
            result.Add(ManaCost.Parse(stripped));
        }

        return result;
    }

    private static bool TryBindBasicLand(ICard card, Player controller)
    {
        if (!card.HasSupertype(CardSupertype.Basic)) return false;

        foreach (var (subtype, color) in BasicLandColors)
        {
            if (card.HasSubtype(subtype))
            {
                card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(color)));
                return true;
            }
        }
        return false;
    }

    private static void TryBindFromOracle(ICard card, CardEntity entity, Player controller)
    {
        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Horizon Canopy painless-dual cycle: "{T}, Pay 1 life: Add {A} or {B}."
        // Checked before the bare-{T} regexes because the pay-life prefix is a
        // distinct (richer) activation cost — NOT a bare {T} mana ability, so it
        // is intentionally out of scope for ParseTapManaCosts (which only knows
        // the source-agnostic {T}-cost mana clauses). Each colour binds as its
        // own pay-life ManaAbility (CR 119.4 life-floor gate). Only Land cards
        // carry this shape, so it's a no-op on non-lands.
        if (card is Land payLifeLand)
        {
            var payLife = PayLifeDualManaRegex.Match(text);
            if (payLife.Success)
            {
                var colorA = payLife.Groups[1].Value.Replace("{", "").Replace("}", "");
                var colorB = payLife.Groups[2].Value.Replace("{", "").Replace("}", "");
                HorizonLandBinder.AttachPayLifeMana(payLifeLand, controller, colorA);
                HorizonLandBinder.AttachPayLifeMana(payLifeLand, controller, colorB);
                return;
            }

            // Mana Confluence: "{T}, Pay 1 life: Add one mana of any color."
            // Five pay-life ManaAbility options (one per WUBRG). The life-floor
            // gate is the precise CR 119.4 reading (>= 1 — payable at exactly
            // 1 life), distinct from HorizonLandBinder's stricter > 1 gate, so
            // it is built inline here rather than reusing AttachPayLifeMana.
            if (PayLifeAnyColorRegex.IsMatch(text))
            {
                foreach (var color in new[] { "W", "U", "B", "R", "G" })
                {
                    var mana = ManaCost.Parse(color);
                    payLifeLand.AddAbility(new ManaAbility(
                        source: payLifeLand,
                        controller: controller,
                        manaGenerated: mana,
                        canActivateCheck: () =>
                        {
                            if (payLifeLand.IsTapped) return false;
                            var c = payLifeLand.Controller ?? controller;
                            return c.LifeTotal >= 1;
                        },
                        additionalCostPayer: c => c.LoseLife(1)));
                }
                return;
            }

            // Gemstone Mine: "{T}, Remove a mining counter from this land: Add
            // one mana of any color. If there are no mining counters on this
            // land, sacrifice it." Five counter-cost ManaAbility options + the
            // "enters with three mining counters" ETB trigger.
            if (GemstoneMineCounterManaRegex.IsMatch(text))
            {
                BindGemstoneMine(payLifeLand, text, controller);
                return;
            }
        }

        // Single source of truth for the bare-{T} mana clauses: ParseTapManaCosts
        // applies the same regexes and produces one ManaCost per ManaAbility.
        // Bind each homed to this card. (Bot's source-picker scans abilities and
        // picks the first that produces the needed colour.)
        foreach (var cost in ParseTapManaCosts(text))
        {
            card.AddAbility(new ManaAbility(card, controller, cost));
        }
    }

    /// <summary>
    /// Wire Gemstone Mine's counter-cost any-colour mana abilities plus its
    /// "enters with three mining counters" ETB trigger.
    ///
    /// <para>Mana: five <see cref="ManaAbility"/> options (one per WUBRG). Each
    /// is gated on the land being untapped, on the battlefield, and carrying at
    /// least one <see cref="Majik.Core.Counters.CounterType.Mining"/> counter
    /// (the remove-a-mining-counter cost must be payable — CR 119.4). The
    /// additional-cost payer removes one mining counter and, when none remain,
    /// sacrifices the land to its owner's graveyard (CR 701.16) — both happen
    /// atomically in the mana ability's activation (CR 605.1, no stack). This
    /// mirrors <c>GemstoneMineFactory</c>'s shape exactly (the factory is
    /// test-only; lands bind through this binder in prod).</para>
    ///
    /// <para>ETB: a self-<see cref="TriggeredAbility"/> over
    /// <see cref="Triggers.OnEnterBattlefieldSelf"/> that adds N mining
    /// counters. A true CR 614.1d "enters with N counters" replacement only
    /// models +1/+1 counters today, so non-+1/+1 counter loads use the
    /// trigger-shape (same posture as Blast Zone / Aether Hub).</para>
    /// </summary>
    private static void BindGemstoneMine(Land land, string text, Player controller)
    {
        // ETB: "This land enters with three mining counters on it."
        var etbMatch = EntersWithMiningCountersRegex.Match(text);
        if (etbMatch.Success)
        {
            var n = WordToInt(etbMatch.Groups["n"].Value);
            if (n > 0)
            {
                var etbEffect = new Effect(
                    $"{land.Name}: enters with {n} mining counters",
                    () =>
                    {
                        if (land.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;
                        land.Counters.Add(Majik.Core.Counters.CounterType.Mining, n);
                    });

                land.AddAbility(new TriggeredAbility(
                    source: land,
                    controller: controller,
                    condition: Triggers.OnEnterBattlefieldSelf(land),
                    effects: new IEffect[] { etbEffect },
                    activeZones: new[] { Majik.Core.Zones.ZoneType.Battlefield }));
            }
        }

        // {T}, Remove a mining counter from this land: Add one mana of any
        // color. If there are no mining counters on this land, sacrifice it.
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            land.AddAbility(new ManaAbility(
                source: land,
                controller: controller,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () =>
                    !land.IsTapped
                    && land.Zone == Majik.Core.Zones.ZoneType.Battlefield
                    && land.Counters.Count(Majik.Core.Counters.CounterType.Mining) >= 1,
                additionalCostPayer: _ => RemoveMiningCounterAndMaybeSacrifice(land, controller)));
        }
    }

    /// <summary>
    /// Pay Gemstone Mine's "Remove a mining counter from this land" activation
    /// cost, then enforce "If there are no mining counters on this land,
    /// sacrifice it" (CR 701.16). Uses the inline self-sacrifice move
    /// (controller's battlefield → owner's graveyard) — the same posture every
    /// other binder/factory self-sacrifice takes, since the generic sacrifice
    /// path is a no-op stub.
    /// </summary>
    private static void RemoveMiningCounterAndMaybeSacrifice(Land land, Player controller)
    {
        land.Counters.Remove(Majik.Core.Counters.CounterType.Mining, 1);

        if (land.Counters.Count(Majik.Core.Counters.CounterType.Mining) > 0) return;
        if (land.Zone != Majik.Core.Zones.ZoneType.Battlefield) return;

        var holder = land.Controller ?? controller;
        var graveyardOwner = land.Owner ?? controller;
        holder.Zones.Battlefield.RemoveCard(land);
        graveyardOwner.Zones.Graveyard.AddCard(land);
        land.SetZone(Majik.Core.Zones.ZoneType.Graveyard);
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var v) ? v : 0,
        };
}
