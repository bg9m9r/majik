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

    // CR 614.12 — per-land "as it enters, choose a color" holder. Keyed off
    // the land so the synthesized chosen-colour ManaAbility (which reads the
    // holder at activation) and the ETB ChooseColorReplacement (which stamps
    // it as the land enters) share one instance without a public mutable
    // property on Land. ConditionalWeakTable mirrors UnclaimedTerritory's
    // chosen-creature-type idiom; entries are GC'd with the land.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Land, ColorChoice>
        _chosenColors = new();

    /// <summary>
    /// CR 614.12 — the <see cref="ColorChoice"/> a "choose a color" land
    /// (Sunken Citadel, Temple of the Dragon Queen) created when its mana
    /// abilities were bound, or <c>null</c> for any other land. The
    /// binder-chain ETB choose-color replacement (registered in
    /// <see cref="ScryfallCardFactory"/>) looks this up to wire the agent
    /// prompt that stamps the chosen colour.
    /// </summary>
    public static ColorChoice? GetColorChoice(Land land)
    {
        ArgumentNullException.ThrowIfNull(land);
        return _chosenColors.TryGetValue(land, out var choice) ? choice : null;
    }

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

    // "{T}: Add one mana of any type that a land you control could produce."
    //   — Reflecting Pool (Tempest). The set of producible mana types is NOT
    // fixed — it is recomputed at every legality check from the lands the
    // controller currently controls. Modelled as six fixed-type ManaAbility
    // instances (WUBRG + {C}), each gated so it is legal ONLY while some OTHER
    // land the controller controls could produce that type (CR 605.1a). Lands
    // are never routed through their [CardName] factory, so this binder is the
    // ONLY prod binding path — it ports the (test-only) ReflectingPoolFactory
    // logic verbatim. A "type" is one of W/U/B/R/G plus colorless (CR 107.4c).
    private static readonly Regex ReflectingPoolManaRegex = new(
        @"\{T\}\s*:\s*Add\s+one\s+mana\s+of\s+any\s+type\s+that\s+a\s+land\s+you\s+control\s+could\s+produce",
        RegexOptions.IgnoreCase);

    // "{T}: Add {A} or {B}. Each opponent gains 1 life." — Grove of the
    // Burnwillows (Future Sight), the "reverse painland". The coloured modes
    // produce one of two colours AND give each opponent 1 life (CR 119.3). This
    // is a bare-{T} modal line, so TapForModalManaRegex would otherwise bind the
    // two coloured options WITHOUT the lifegain rider — the rider must be
    // intercepted here. Mana abilities resolve immediately with no
    // ResolutionContext (CR 605.3), so the "each opponent" set is read off the
    // ambient GamePlayersRegistry at activation (CR 102.4 — controller excluded).
    // Group 1/2 = the two producible colours.
    private static readonly Regex EachOpponentGainsLifeDualManaRegex = new(
        @"\{T\}\s*:\s*Add\s+(\{[WUBRGC]\})\s+or\s+(\{[WUBRGC]\})\.\s*Each\s+opponent\s+gains\s+1\s+life",
        RegexOptions.IgnoreCase);

    // "{T}, Pay 2 life: Add {C}." — Boseiju, Who Shelters All (Champions of
    // Kamigawa). A {C}-producing mana ability with an additional "lose 2 life"
    // activation cost (CR 605.1a / 119.4). The "spent on an instant/sorcery →
    // can't be countered" rider (CR 701.5b / 106.4) is wired via the produced
    // {C}'s ManaAbility.ProvenanceReaction (per-slot mana provenance →
    // PendingCastUncounterable → Spell.CannotBeCountered), in lock-step with
    // BoseijuWhoSheltersAllFactory. The pay-life cost prefix is
    // "{T}, Pay 2 life:" (NOT a bare {T}), so the bare tap-for-mana regexes
    // never match it. Ports BoseijuWhoSheltersAllFactory.
    private static readonly Regex PayTwoLifeColorlessManaRegex = new(
        @"\{T\}\s*,\s*Pay\s+2\s+life\s*:\s*Add\s+\{C\}",
        RegexOptions.IgnoreCase);

    // "{T}: Add one mana of the chosen color." — Sunken Citadel /
    // Temple of the Dragon Queen "as it enters, choose a color" cycle
    // (CR 614.12). The chosen colour is decided as the land enters by the ETB
    // ChooseColorReplacement (registered via ChooseColorLandBinder on the
    // ScryfallCardFactory replacement bus), which prompts the controller's
    // agent (IPlayerAgent.ChooseColorAsync) and stamps the pick onto a shared
    // ColorChoice holder. The synthesized ManaAbility reads that holder through
    // a dynamic generator at activation, so exactly the SINGLE chosen colour is
    // producible — the printed restriction, not the old over-permissive
    // five-WUBRG binding. See BindChosenColorLand below.
    private static readonly Regex ChosenColorOneManaRegex = new(
        @"\{T\}\s*:\s*Add\s+one\s+mana\s+of\s+the\s+chosen\s+color",
        RegexOptions.IgnoreCase);

    // "{T}: Add two mana of the chosen color. Spend this mana only to activate
    // abilities of land sources." — Sunken Citadel's restricted double-mana
    // ability (CR 605.1a / 106.4). One dynamic-output double-pip ManaAbility
    // reading the shared ColorChoice (the chosen colour decided at ETB), stamped
    // with a land-ability-only SpendRestriction enforced by the
    // ManaPaymentResolver (CR 106.4).
    private static readonly Regex ChosenColorTwoManaLandAbilityRegex = new(
        @"\{T\}\s*:\s*Add\s+two\s+mana\s+of\s+the\s+chosen\s+color\.\s*Spend\s+this\s+mana\s+only\s+to\s+activate\s+abilities\s+of\s+land\s+sources",
        RegexOptions.IgnoreCase);

    // CR 106.4 — Sunken Citadel's "Spend this mana only to activate abilities
    // of land sources." The SpendRestriction predicate is spell-side only
    // (Func<ISpell,bool>); this mana can never pay a spell pip, so it denies
    // every spell. Shared instance keeps the rider structurally stable
    // (SpendRestriction equality is by-reference).
    private static readonly Majik.Core.Mana.SpendRestriction SunkenCitadelLandAbilitiesOnly =
        new("land source ability", _ => false);

    // The five colours plus colorless — the complete set of mana "types" a
    // Reflecting Pool can ever reflect (CR 107.4c / 106.1b).
    private static readonly string[] ReflectingPoolManaTypes = { "W", "U", "B", "R", "G", "C" };

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
            // Grove of the Burnwillows — "{T}: Add {R} or {G}. Each opponent
            // gains 1 life." The two coloured modes carry an "each opponent
            // gains 1 life" rider (CR 119.3); the printed line is also a bare-{T}
            // modal, so TapForModalManaRegex would otherwise bind {R}/{G} WITHOUT
            // the rider (the deferral). Bind the riderful coloured modes here,
            // then strip the clause so the remaining "{T}: Add {C}." mode binds
            // normally below. Mana abilities have no ResolutionContext (CR 605.3),
            // so the opponent set is read off the ambient GamePlayersRegistry at
            // activation (CR 102.4 — controller excluded). Lands are never routed
            // through their [CardName] factory, so this binder is the only prod
            // path for the rider (v1-deferrals #3b residual b).
            var grove = EachOpponentGainsLifeDualManaRegex.Match(text);
            if (grove.Success)
            {
                var colorA = grove.Groups[1].Value.Replace("{", "").Replace("}", "");
                var colorB = grove.Groups[2].Value.Replace("{", "").Replace("}", "");
                AttachOpponentGainColoredMana(payLifeLand, controller, colorA);
                AttachOpponentGainColoredMana(payLifeLand, controller, colorB);
                // Drop the modal clause so the residual "{T}: Add {C}." mode is
                // bound (once) by the bare-{T} fallback at the end of this method
                // (which reads the local `text`, not entity.OracleText).
                text = EachOpponentGainsLifeDualManaRegex.Replace(text, string.Empty);
            }

            // Reflecting Pool — six dynamic-output mana abilities (WUBRG + {C}),
            // each gated on some OTHER land the controller controls being able
            // to produce that type (recomputed every legality check). Ports the
            // test-only ReflectingPoolFactory into the prod binder path.
            if (ReflectingPoolManaRegex.IsMatch(text))
            {
                BindReflectingPool(payLifeLand, controller);
                return;
            }

            // Boseiju, Who Shelters All — "{T}, Pay 2 life: Add {C}. If that
            // mana is spent on an instant or sorcery spell, that spell can't be
            // countered." A {C} mana ability with a lose-2-life additional cost
            // (CR 605.1a / 119.4) PLUS the pay-time uncounterable rider
            // (CR 701.5b / 106.4). Lands are never routed through their
            // [CardName] factory, so this binder is the only LIVE path — it must
            // wire the same ManaAbility.ProvenanceReaction
            // BoseijuWhoSheltersAllFactory does: the produced {C} slot fires the
            // reaction when ManaPaymentResolver consumes it, and if the object it
            // paid for is an instant/sorcery card, stamps PendingCastUncounterable
            // (which SpellCastFlow copies onto Spell.CannotBeCountered, then
            // clears). Strictly per-pip / per-spell, the same slot-provenance
            // seam as Arena of Glory's exert→haste rider.
            if (PayTwoLifeColorlessManaRegex.IsMatch(text))
            {
                var boseiju = new ManaAbility(
                    source: payLifeLand,
                    controller: controller,
                    manaGenerated: ManaCost.Parse("C"),
                    canActivateCheck: () =>
                    {
                        if (payLifeLand.IsTapped) return false;
                        var c = payLifeLand.Controller ?? controller;
                        return c.LifeTotal > 2;
                    },
                    additionalCostPayer: p => p.LoseLife(2));
                boseiju.ProvenanceReaction = MarkUncounterableIfInstantOrSorcery;
                payLifeLand.AddAbility(boseiju);
                return;
            }

            // Sunken Citadel / Temple of the Dragon Queen — "{T}: Add one mana
            // of the chosen color" (+ Sunken Citadel's restricted double-mana
            // ability). The chosen colour is decided "as it enters" (CR 614.12)
            // by the ETB ChooseColorReplacement and read by a dynamic-output
            // ManaAbility, so exactly the single chosen colour is producible.
            // The double-mana clause additionally stamps a land-ability-only
            // SpendRestriction (CR 106.4).
            if (ChosenColorOneManaRegex.IsMatch(text))
            {
                BindChosenColorLand(payLifeLand, text, controller);
                return;
            }

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
    /// Attach a Grove-of-the-Burnwillows coloured mode:
    /// <c>{T}: Add &lt;color&gt;. Each opponent gains 1 life.</c>
    ///
    /// <para>Built on the existing additional-cost overload of
    /// <see cref="ManaAbility"/>: tapping pays {T} (CR 605.1a); the
    /// <c>additionalCostPayer</c> then walks the opponents of
    /// <paramref name="controller"/> — read from the live
    /// <see cref="Majik.Core.Game.GamePlayersRegistry"/> AT ACTIVATION (mana
    /// abilities resolve immediately with no ResolutionContext, CR 605.3) — and
    /// grants each 1 life (CR 119.3). CR 102.4 — the controller is never its own
    /// opponent; <see cref="Majik.Core.Game.GamePlayersRegistry.OpponentsOf"/>
    /// already excludes it. No life-floor gate — granting opponents life is never
    /// a "pay" cost. When no live game scope is installed (shape-only paths) the
    /// opponent set is empty, so the rider is a safe no-op while the mana + tap
    /// still fire.</para>
    /// </summary>
    private static void AttachOpponentGainColoredMana(Land land, Player controller, string color)
    {
        const int lifeGainPerOpponent = 1;
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerated: ManaCost.Parse(color),
            canActivateCheck: () => !land.IsTapped,
            additionalCostPayer: activator =>
            {
                // CR 102.4 — "each opponent" of the ability's controller, read
                // from the live player set at activation. The controller passed
                // to the payer (`activator`) IS the ability's controller.
                foreach (var opp in Majik.Core.Game.GamePlayersRegistry.OpponentsOf(activator))
                {
                    opp.GainLife(lifeGainPerOpponent);
                }
            }));
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

    /// <summary>
    /// Wire Reflecting Pool's six dynamic-output mana abilities (CR 605.1a).
    /// One per W/U/B/R/G/{C}, each legal ONLY while some OTHER land the
    /// controller currently controls could produce that type — recomputed at
    /// every legality check, so it tracks control changes / lands entering and
    /// leaving. Ports <c>ReflectingPoolFactory.Create</c> into the prod binder
    /// path (lands are never routed through their [CardName] factory).
    /// </summary>
    private static void BindReflectingPool(Land land, Player controller)
    {
        foreach (var type in ReflectingPoolManaTypes)
        {
            var thisType = type; // capture per iteration
            land.AddAbility(new ManaAbility(
                source: land,
                controller: controller,
                manaGenerated: ManaCost.Parse(thisType),
                canActivateCheck: () => !land.IsTapped
                                        && land.Zone == Majik.Core.Zones.ZoneType.Battlefield
                                        && ControllerCanProduce(land, thisType)));
        }
    }

    /// <summary>
    /// True when some land the <paramref name="pool"/>'s current controller
    /// controls — other than <paramref name="pool"/> itself (and any other
    /// Reflecting Pool, to break the circular self-reference) — has a mana
    /// ability that produces <paramref name="typeSymbol"/> (one of W/U/B/R/G/C).
    /// This is the "any type that a land you control could produce" gate
    /// (CR 605.1a). Mirrors <c>ReflectingPoolFactory.ControllerCanProduce</c>.
    /// </summary>
    private static bool ControllerCanProduce(Land pool, string typeSymbol)
    {
        var target = ManaCost.Parse(typeSymbol).ToString();
        var c = pool.Controller;
        if (c == null) return false;

        foreach (var card in c.Zones.Battlefield.GetCards())
        {
            if (ReferenceEquals(card, pool)
                || card is not Land otherLand
                || !otherLand.HasType(CardType.Land)
                || otherLand.Name == ReflectingPoolName)
            {
                continue;
            }

            var produces = otherLand.Abilities
                .OfType<ManaAbility>()
                .Any(ma => ma.ManaGenerated.ToString() == target);
            if (produces) return true;
        }
        return false;
    }

    private const string ReflectingPoolName = "Reflecting Pool";

    /// <summary>
    /// Wire the "{T}: Add one mana of the chosen color" cycle (Sunken Citadel /
    /// Temple of the Dragon Queen — CR 614.12). Creates one shared
    /// <see cref="ColorChoice"/> holder (retrievable via
    /// <see cref="GetColorChoice"/>) and a single dynamic-output
    /// <see cref="ManaAbility"/> whose generator reads it, so exactly the SINGLE
    /// chosen colour is producible — the printed restriction, not the old
    /// over-permissive five-WUBRG binding. The colour is decided "as it enters"
    /// by the ETB <see cref="Majik.Core.Effects.ChooseColorReplacement"/>
    /// (registered via <see cref="ChooseColorLandBinder"/>), which prompts the
    /// controller's agent and stamps the pick onto the holder. When the oracle
    /// also carries Sunken Citadel's "{T}: Add two mana of the chosen color.
    /// Spend this mana only to activate abilities of land sources." clause, a
    /// second dynamic double-pip ability is added, reading the same holder and
    /// stamped with a land-ability-only
    /// <see cref="Majik.Core.Mana.SpendRestriction"/> (CR 106.4).
    /// </summary>
    private static void BindChosenColorLand(Land land, string text, Player controller)
    {
        // CR 614.12 — one shared per-land choice holder. Seeded to a
        // deterministic default (White) so a pre-ETB / no-agent activation
        // produces exactly ONE colour (strictly narrower than the old
        // over-permissive five-WUBRG binding); the ETB ChooseColorReplacement
        // (registered in ScryfallCardFactory) stamps the agent's real pick as
        // the land enters, and these dynamic abilities then produce it.
        var choice = new ColorChoice(ManaColor.White);
        _chosenColors.AddOrUpdate(land, choice);

        // "{T}: Add one mana of the chosen color." — a single dynamic-output
        // ManaAbility reading the holder (CR 605.1a). The printed seed (the
        // current chosen colour's pip) lets pre-activation inspectors — the
        // bot's mana picker, UI mana-source hints — see a real colour instead
        // of Zero.
        land.AddAbility(new ManaAbility(
            source: land,
            controller: controller,
            manaGenerator: () => choice.SinglePip(),
            canActivateCheck: () => !land.IsTapped,
            printedManaGenerated: choice.SinglePip(),
            spendRestriction: null,
            // CR 614.12 — the colour is stamped onto the holder AFTER this
            // ability is bound (the ETB ChooseColorReplacement). A live preview
            // keeps ManaGenerated reading the CURRENT chosen colour so the bot's
            // mana picker / UI hints never see the stale bind-time default. The
            // generator is pure (just reads the holder), so previewing is safe.
            livePreview: () => choice.SinglePip()));

        if (ChosenColorTwoManaLandAbilityRegex.IsMatch(text))
        {
            // "{T}: Add two mana of the chosen color. Spend this mana only to
            // activate abilities of land sources." — one dynamic double-pip
            // ability, same holder, carrying the land-ability-only spend rider
            // (CR 106.4). The live preview keeps ManaGenerated reading the
            // current chosen colour so the resolver's spend-restriction gate
            // (ManaPaymentResolver.CountBlocked, which inspects ManaGenerated
            // BEFORE activation) withholds the RIGHT colour's restricted units.
            land.AddAbility(new ManaAbility(
                source: land,
                controller: controller,
                manaGenerator: () => choice.DoublePip(),
                canActivateCheck: () => !land.IsTapped,
                printedManaGenerated: choice.DoublePip(),
                spendRestriction: SunkenCitadelLandAbilitiesOnly,
                livePreview: () => choice.DoublePip()));
        }
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var v) ? v : 0,
        };

    /// <summary>
    /// CR 701.5b — Boseiju, Who Shelters All's provenance reaction. When one of
    /// Boseiju's {C} units is spent on an instant or sorcery <i>spell</i>, stamp
    /// the pay-time uncounterable flag on the underlying card so
    /// <c>SpellCastFlow.StampSpellAndCardSentinels</c> marks the resulting spell
    /// <see cref="Majik.Core.Spells.ISpell.CannotBeCountered"/>. No-op for a
    /// creature/other spell or a non-spell (ability-cost) context
    /// (<paramref name="spentOn"/> is null or not an instant/sorcery card). Kept
    /// in lock-step with <c>BoseijuWhoSheltersAllFactory</c>'s identical reaction.
    /// </summary>
    private static void MarkUncounterableIfInstantOrSorcery(ICard? spentOn)
    {
        if (spentOn is not Card card) return;
        if (!card.HasType(CardType.Instant) && !card.HasType(CardType.Sorcery)) return;
        card.MarkPendingCastUncounterable();
    }
}
