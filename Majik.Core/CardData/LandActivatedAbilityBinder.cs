using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Production binder for the <b>generic utility-land activated abilities</b>
/// that no other binder covers — the long tail of "draw a card", "scry N",
/// "+1/+1 counter", "create a token", "deal N damage / gain N life",
/// "creatures you control gain &lt;keyword&gt; until end of turn", "return
/// from graveyard", and "destroy / search-for-basic land" activated abilities
/// printed on nonbasic lands (Castle Vantress, Gavony Township, Castle
/// Ardenvale, Ramunap Ruins, Buried Ruin, Ghost Quarter, the Panorama cycle, …).
///
/// <para><b>Why this binder exists.</b> Lands are NEVER routed through their
/// <c>[CardName]</c> factory in production: <see cref="Majik.Core.Api.GameFacade"/>'s
/// deck-build path (<c>BuildDeckCard</c>) gates the factory instance-swap on
/// <c>!shell.HasType(CardType.Land)</c>. So every utility land's bespoke
/// activated ability — implemented in its factory and exercised only by a
/// green factory-direct unit test — is <b>dead in real games</b>. ONLY the
/// binder chain runs for lands. <see cref="OracleLandActivatedAbilityBinder"/>
/// covers the fetch / Horizon-canopy / basic-fetch patterns; this binder adds
/// the rest of the verb families so they finally fire on the live table
/// (v1-deferrals #12).</para>
///
/// <para><b>Cost-line parse.</b> The activation line is
/// <c>[{mana}][, {T}][, Sacrifice this land/&lt;typed&gt;]: &lt;effect&gt;.</c>.
/// A <see cref="AdditionalCost.Tap(Permanent)"/> is always added; a
/// <see cref="ManaCostCost"/> is added when the cost carries mana symbols (the
/// bare <c>{T}: Add …</c> mana line is left to OracleManaBinder); an
/// <see cref="AdditionalCost.Sacrifice(Permanent)"/> is added only for a
/// "Sacrifice this land / &lt;self&gt;" clause. Non-self typed-sacrifice costs
/// ("Sacrifice an artifact" / "Sacrifice a Desert") have no binder-reachable
/// typed-sacrifice-chooser primitive yet; the mana + tap cost + effect still
/// bind and the typed-sac rider is deferred (xmldoc per branch).</para>
///
/// <para><b>Targeted variants</b> (Cave of Temptation, Buried Ruin, Riptide
/// Laboratory, Barbarian Ring, the destroy-target-land cycle) ride the
/// <see cref="TargetRequest"/> + <c>CandidateGatherer</c> path already drained
/// in production by the live ability-activation pipeline. The effect reads the
/// agent-chosen target off the ability's <see cref="ActivatedAbility.ChosenTargets"/>
/// with a CR 608.2b resolve-time legality recheck.</para>
///
/// <para><b>Channel</b> (CR 702.74; Boseiju Who Endures, Otawara, Takenuma,
/// Eiganjo, Sokenzan) — the activation cost is "{cost}, Discard this card", a
/// discard-from-HAND activation, NOT a battlefield {T} activation. NOW BOUND
/// here via <see cref="BindChannel"/>: a <see cref="DiscardSelfCost"/> (the
/// hand-zone activation seam, CR 702.74a) sits alongside a
/// <see cref="ManaCostCost"/> in the standard activated-ability cost list, and
/// each cycle member's effect maps to an existing one-shot verb
/// (destroy / bounce / mill / damage / token). The "costs {1} less per
/// legendary creature you control" cost-reduction rider (CR 118.9) and the
/// per-member follow-up riders (Boseiju's basic-land search, Eiganjo's live
/// combat-state gate, Takenuma's "may") are deferred — see the
/// <see cref="BindChannel"/> xmldoc.</para>
///
/// <para><b>Deferred families / riders.</b>
/// <list type="bullet">
///   <item><b>Crawling Barrens</b> — "Put two +1/+1 counters on this land.
///     Then you may have it become a 0/0 Elemental creature" — the animate is
///     conditional on a prior counter step (same posture
///     <see cref="ManlandBinder"/> defers).</item>
///   <item><b>Count-linked / attack-rider / richer-shape token riders — ALL NOW
///     BIND here.</b> Treasure Vault's "{X}{X}, {T}, Sacrifice this land: Create
///     X Treasure tokens" binds via <see cref="BindCreateXTreasures"/> — the
///     count is the activation's X (read at resolution off the per-activation X
///     ledger, <see cref="Majik.Core.Abilities.ResolutionContext.ChosenX"/>,
///     GAP 2). Mirrex's "{3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact
///     creature token with toxic 1 and \"This token can't block.\"" binds via
///     <see cref="BindCreateMiteToken"/> (the richer artifact-creature + toxic +
///     can't-block shape — the can't-block rider is now enforced at block
///     declaration, CR 509.1a). Dalkovan Encampment's "{2}{W}, {T}: Whenever you
///     attack this turn, create two 1/1 red Warrior creature tokens …" binds via
///     <see cref="BindAttackRiderTokens"/> — the activated ability's resolution
///     installs a one-turn "whenever you attack" trigger (CR 508.1f) auto-cleaned
///     at the next end step (CR 603.7). v1 defers only the printed "tapped and
///     attacking" combat-insertion + per-token EOT sacrifice (same gap as Geist
///     of Saint Traft / Goblin Rabblemaster — no mid-combat creature insertion
///     surface).</item>
///   <item><b>Desert</b> — "{T}: deal 1 damage to target attacking creature.
///     Activate only during the end of combat step" — fully modelled by the
///     bespoke <see cref="Factories.DesertFactory"/>: the "end of combat step"
///     timing rider (CR 602.5b) is the ability's context-aware
///     <c>canActivateCheckCtx</c> gate, reading the live step off
///     <see cref="Game.GameContext.CurrentPhase"/>. (The binder path itself
///     still has no step-aware emit, but Desert no longer needs one.)</item>
/// </list>
/// </para>
/// </summary>
public static class LandActivatedAbilityBinder
{
    // Activation-line shell. Captures the cost segment (everything before the
    // first ':'), then the effect clause. We only act on lines whose cost
    // contains {T} (a battlefield tap activation) — the bare "{T}: Add …" mana
    // line is filtered out in code because its effect clause begins with "Add".
    // Channel lines ("Channel — {cost}, Discard this card: …") never match the
    // {T} gate because their cost has no {T}. An optional leading ability-word
    // keyword prefix ("Threshold — ", CR 702.84) is tolerated so the activation
    // line behind it still binds.
    private static readonly Regex ActivationLine = new(
        @"^(?:(?<keyword>[A-Z][a-zA-Z'’ ]*?)\s+[—-]\s+)?" +
        @"(?<cost>(?:\{[^}]+\}|,|\s|Pay\s+\d+\s+life|Sacrifice[^:]*)*?):\s*(?<effect>.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Mana symbols inside the cost segment ({2}{U}{U}, {3}, {X}{X}, …). The
    // bare tap symbol {T} is NOT mana and is stripped before this is tested.
    private static readonly Regex ManaSymbol = new(
        @"\{(?:\d+|[WUBRGCXSP]|[WUBRG]/[WUBRGP]|C/[WUBRG])\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Sacrifice this land" / "Sacrifice <SelfName>" — a SELF sacrifice cost.
    private static readonly Regex SacrificeSelf = new(
        @"Sacrifice\s+(?:this\s+land|this\s+permanent)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Channel (CR 702.74). "Channel — {cost}, Discard this card: <effect>".
    // The cost segment is the mana before ", Discard this card"; the effect is
    // everything after the colon up to the next sentence boundary that begins a
    // separate clause. We capture the whole effect clause and dispatch it to a
    // per-cycle-member builder. Unlike every other line this binder handles, the
    // Channel cost has NO {T} — it is a discard-from-HAND activation, so it is
    // recognised by its own regex BEFORE the {T} gate in Bind().
    private static readonly Regex ChannelLine = new(
        @"^Channel\s+[—-]\s+(?<cost>(?:\{[^}]+\})+)\s*,\s*Discard\s+this\s+card\s*:\s*(?<effect>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    // --- Effect-clause dispatch regexes -----------------------------------

    private static readonly Regex ScryEffect = new(
        @"^Scry\s+(?<n>\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Draw a card" / "Draw two cards" — optional trailing riders handled in code.
    private static readonly Regex DrawEffect = new(
        @"^Draw\s+(?<n>a|one|two|three)\s+cards?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CounterEachYouControl = new(
        @"Put\s+a\s+\+1/\+1\s+counter\s+on\s+each\s+creature\s+you\s+control",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Put one/two +1/+1 counters on target creature."
    private static readonly Regex CounterOnTargetCreature = new(
        @"Put\s+(?<n>a|one|two|three)\s+\+1/\+1\s+counters?\s+on\s+target\s+creature\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Create a 1/1 white Human creature token" — simple single fixed token.
    private static readonly Regex CreateSimpleToken = new(
        @"^Create\s+a\s+(?<p>\d+)/(?<t>\d+)\s+(?<rest>.+?)\s+creature\s+token\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Create X Treasure tokens" — the count-linked Treasure mint (Treasure
    // Vault's "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens").
    // The count is the activation's X (CR 107.3 — read at RESOLUTION off the
    // per-activation X ledger, ResolutionContext.ChosenX, GAP 2). CR 111.10 —
    // each Treasure is a colourless artifact with "{T}, Sacrifice this artifact:
    // Add one mana of any color." Bound here because the X-count + Treasure
    // shape has no fixed-token spec the simple-token path can parse.
    private static readonly Regex CreateXTreasures = new(
        @"^Create\s+X\s+Treasure\s+tokens?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Create a 1/1 colorless Phyrexian Mite artifact creature token with toxic 1
    // and \"This token can't block.\"" — Mirrex's richer-shape token (CR 111.1 /
    // 111.4). The artifact-creature + toxic + can't-block riders make the simple
    // CreateSimpleToken path reject it (TryParseTokenSpec bails on the non-subtype
    // words "artifact"/"with"/"toxic"), so it gets its own recognizer. We capture
    // the P/T, the toxic arg, and (implicitly) the can't-block rider.
    private static readonly Regex CreateMiteToken = new(
        @"^Create\s+a\s+(?<p>\d+)/(?<t>\d+)\s+colorless\s+Phyrexian\s+Mite\s+artifact\s+creature\s+token" +
        @"(?:\s+with\s+toxic\s+(?<toxic>\d+))?" +
        @"(?<cantblock>.*?can't\s+block\.?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Whenever you attack this turn, create two 1/1 red Warrior creature tokens
    // that are tapped and attacking. Sacrifice them at the beginning of the next
    // end step." — Dalkovan Encampment's attack-rider delayed-token effect. The
    // activated ability's RESOLUTION installs a one-turn "whenever a creature you
    // control attacks" trigger that mints N P/T <color> <Subtype> tokens
    // (CR 508.1f / CR 603.7). Captures the count word, P/T, colour, subtype.
    private static readonly Regex AttackRiderTokens = new(
        @"^Whenever\s+you\s+attack\s+this\s+turn,\s*create\s+(?<count>a|one|two|three)\s+" +
        @"(?<p>\d+)/(?<t>\d+)\s+(?<color>white|blue|black|red|green)\s+(?<subtype>[A-Za-z]+)\s+creature\s+tokens?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Sacrifice them at the beginning of the next end step." — the per-token
    // delayed-sacrifice rider trailing Dalkovan Encampment's attack-token clause
    // (CR 603.7). Detected on the FULL effect text (the AttackRiderTokens regex
    // stops at "creature tokens"); when present, the minted tokens are scheduled
    // for sacrifice at the next end step.
    private static readonly Regex SacrificeThemNextEndStep = new(
        @"Sacrifice\s+them\s+at\s+the\s+beginning\s+of\s+the\s+next\s+end\s+step",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "This land deals N damage to each opponent."
    private static readonly Regex DamageEachOpponent = new(
        @"deals?\s+(?<n>\d+)\s+damage\s+to\s+each\s+opponent",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "(It|This land) deals N damage to any target."
    private static readonly Regex DamageAnyTarget = new(
        @"deals?\s+(?<n>\d+)\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "You gain N life." (standalone gain-life utility-land effect)
    private static readonly Regex GainLifeEffect = new(
        @"^You\s+gain\s+(?<n>\d+)\s+life",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Return target <Wizard you control|artifact card from your graveyard|…> to
    // (its owner's|your) hand." Two shapes we bind: graveyard-return and
    // controller-permanent bounce.
    private static readonly Regex ReturnArtifactFromGraveyard = new(
        @"Return\s+target\s+artifact\s+card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReturnTypedYouControlToHand = new(
        @"Return\s+target\s+(?<type>\w+)\s+you\s+control\s+to\s+its\s+owner'?s\s+hand",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Search your library for a basic <A>, <B>, or <C> card, put it onto the
    // battlefield tapped, then shuffle." — the Panorama cycle / Bountiful &
    // Twisted Landscape (three named basic subtypes; OracleLandActivatedAbilityBinder
    // only handles the two-subtype + "Pay 1 life" fetchland and the any-basic /
    // no-mana sac-fetch forms, so the {1}-cost three-named-basic Panorama form
    // is unbound by it and lands here).
    private static readonly Regex SearchNamedBasicsTapped = new(
        @"Search\s+your\s+library\s+for\s+a\s+basic\s+(?<a>Plains|Island|Swamp|Mountain|Forest)\s*,\s*(?<b>Plains|Island|Swamp|Mountain|Forest)\s*,\s*or\s+(?<c>Plains|Island|Swamp|Mountain|Forest)\s+card\s*,\s*put\s+it\s+onto\s+the\s+battlefield\s+tapped",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Destroy target [nonbasic] land [an opponent controls]." Search-for-basic
    // riders are honoured for the controller side where simple.
    private static readonly Regex DestroyTargetLand = new(
        @"Destroy\s+target\s+(?<nonbasic>nonbasic\s+)?land\b(?<opp>[^.]*opponent\s+controls)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Demolition Field's both-players search-for-basic rider clauses that ride
    // AFTER "Destroy target nonbasic land an opponent controls." (CR 701.19 /
    // CR 701.20a). Two distinct "may search … for a basic land card, put it
    // onto the battlefield, then shuffle" clauses: one for the destroyed land's
    // controller ("That land's controller may search their library …"), one for
    // the activator ("You may search your library …"). Note: NO "tapped" — the
    // basics enter untapped (unlike the Panorama tapped fetch).
    private static readonly Regex DestroyedControllerSearchBasic = new(
        @"That\s+land'?s\s+controller\s+may\s+search\s+their\s+library\s+for\s+a\s+basic\s+land\s+card\s*,\s*put\s+it\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ActivatorSearchBasic = new(
        @"You\s+may\s+search\s+your\s+library\s+for\s+a\s+basic\s+land\s+card\s*,\s*put\s+it\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Creatures you control gain <keyword>[ and <keyword>] until end of turn."
    // The mass until-EOT keyword-grant family (Vault of the Archangel). The
    // <kw> capture is the keyword list ("deathtouch and lifelink", "trample",
    // "first strike and vigilance"); it is split + canonicalized in code. CR
    // 613.1c Layer 6 grant, CR 514.2 cleanup expiry.
    private static readonly Regex CreaturesYouControlGainKeywords = new(
        @"Creatures\s+you\s+control\s+gain\s+(?<kw>.+?)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The keywords reachable through the generic mass-grant binder. Combat-
    // relevant evasion / damage keywords that the engine's combat + damage
    // paths already honour off a creature's effective keyword set (CR 702).
    // A grant line naming a keyword OUTSIDE this set is NOT bound here (it
    // would need a primitive the binder can't reach) — the whole line defers.
    private static readonly Dictionary<string, string> GrantableKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["deathtouch"] = "Deathtouch",
            ["lifelink"] = "Lifelink",
            ["trample"] = "Trample",
            ["vigilance"] = "Vigilance",
            ["first strike"] = "First strike",
            ["double strike"] = "Double strike",
            ["haste"] = "Haste",
            ["flying"] = "Flying",
            ["menace"] = "Menace",
            ["reach"] = "Reach",
            ["hexproof"] = "Hexproof",
            ["indestructible"] = "Indestructible",
        };

    /// <summary>
    /// Inspect <paramref name="entity"/>'s oracle text and bind every generic
    /// utility-land activated ability it can recognise to <paramref name="card"/>.
    /// No-op unless the card is a <see cref="Land"/>.
    /// </summary>
    /// <returns><c>true</c> if at least one ability was bound.</returns>
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

        var boundAny = false;

        // --- Channel (CR 702.74) — a discard-from-HAND activation. Recognised
        //     FIRST because its cost has no {T} and would be filtered by the
        //     {T} gate below. Each cycle member binds exactly one Channel
        //     ActivatedAbility (mana + DiscardSelfCost). ---
        foreach (Match channelMatch in ChannelLine.Matches(text))
        {
            if (BindChannel(
                    land, controller,
                    channelMatch.Groups["cost"].Value,
                    channelMatch.Groups["effect"].Value.Trim()))
            {
                boundAny = true;
            }
        }

        foreach (Match line in ActivationLine.Matches(text))
        {
            var cost = line.Groups["cost"].Value;
            var effectText = line.Groups["effect"].Value.Trim();

            // Channel lines are handled above by their own recognizer; the
            // generic ActivationLine regex also matches "Channel — …: …", so
            // skip it here to avoid a duplicate (effect-less) ability.
            if (line.Groups["keyword"].Value.Trim()
                    .Equals("Channel", StringComparison.OrdinalIgnoreCase)) continue;

            // The bare mana line ("{T}: Add {U}", "{T}: Add one mana of any
            // color") is OracleManaBinder's job — skip it here.
            if (effectText.StartsWith("Add ", StringComparison.OrdinalIgnoreCase)) continue;

            // The cost must contain {T} (a battlefield tap activation). Channel
            // lines (discard-from-hand) and pure no-tap mana abilities are
            // skipped — Channel is a deferred family (no discard-activation seam).
            if (!cost.Contains("{T}", StringComparison.OrdinalIgnoreCase)) continue;

            if (BindLine(land, controller, effects, triggers, cost, effectText))
                boundAny = true;
        }

        return boundAny;
    }

    /// <summary>Build the cost list for an activation line: always Tap, plus a
    /// ManaCostCost when mana symbols are present, plus a self-Sacrifice when
    /// the cost says "Sacrifice this land". Returns the parsed mana text (or
    /// null) so callers can include it in effect descriptions.</summary>
    private static List<ICost> BuildCosts(Land land, string cost, out bool sacrificesSelf)
    {
        var costs = new List<ICost> { AdditionalCost.Tap(land) };

        // Strip the tap symbol before testing for mana so {T} isn't mistaken
        // for a mana symbol.
        var manaPart = cost.Replace("{T}", "", StringComparison.OrdinalIgnoreCase);
        var manaMatches = ManaSymbol.Matches(manaPart);
        if (manaMatches.Count > 0)
        {
            var manaText = string.Concat(manaMatches.Select(m => m.Value));
            costs.Add(new ManaCostCost(manaText));
        }

        sacrificesSelf = SacrificeSelf.IsMatch(cost);
        if (sacrificesSelf)
        {
            costs.Add(AdditionalCost.Sacrifice(land));
        }

        return costs;
    }

    // ======================================================================
    // Channel (CR 702.74) — discard-from-HAND activated ability.
    //
    // The Channel cost is "{cost}, Discard this card" — a discard-from-hand
    // activation, NOT a battlefield {T} tap. The seam is the existing
    // DiscardSelfCost (CR 702.74a — gates payment to the controller's Hand
    // zone) sitting alongside a ManaCostCost in the standard activated-ability
    // cost list; the ability is attached to the land object regardless of zone
    // (the same card instance moves Hand → Graveyard via the discard cost, then
    // resolves). The effect bodies reuse the existing one-shot verbs
    // (Fx.MoveToGraveyard / BounceToHand / Mill / DealDamageAny / TokenFactory)
    // — exactly the verbs the rest of this binder already emits.
    //
    // CR 118.9 cost-reduction rider — NOW BOUND. "This ability costs {1} less
    // to activate for each legendary creature you control" (Boseiju + the whole
    // Channel cycle) binds the Channel mana cost as a
    // DynamicGenericReductionManaCost whose generic component drops by the count
    // of legendary creatures the controller controls, computed at payment
    // against live board state (ChannelLegendaryReductionRider regex +
    // LegendaryCreaturesControlled below).
    //
    // Deferred riders (each consistent with the deferrals the rest of this
    // binder already takes; none affects which Channel ability binds):
    //   - Boseiju — the "that player may search their library for a basic land"
    //     follow-up after destroying a land (an opponent's optional search).
    //   - Eiganjo — the live "attacking or blocking" combat-state target gate
    //     (resolve is permissive: any chosen creature is dealt the damage).
    //
    // Takenuma is fully bound (mill 3, then return a creature/planeswalker
    // card from the graveyard to hand). The current oracle text has NO "may":
    // the return is MANDATORY whenever an eligible card exists (CR 608.2c) and
    // only the WHICH-card choice is the controller's, taken from the registered
    // agent via ChooseLibraryPickAsync. The earlier "may rider auto-accepts"
    // note referred to stale Kamigawa wording and does not apply.
    // ======================================================================
    // CR 118.9 cost-reduction rider on a Channel ability: "This ability costs
    // {1} less to activate for each legendary creature you control." The
    // reduction count is the number of legendary creatures the controller
    // controls, evaluated at activation/payment against live board state.
    private static readonly Regex ChannelLegendaryReductionRider = new(
        @"This\s+ability\s+costs\s+\{1\}\s+less\s+to\s+activate\s+for\s+each\s+legendary\s+creature\s+you\s+control",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// CR 118.9 — count the legendary creatures <paramref name="player"/>
    /// controls (battlefield only), the reduction amount for the Channel
    /// "costs {1} less per legendary creature you control" rider. A legendary
    /// non-creature (the Channel land itself, a legendary artifact) and a
    /// non-legendary creature do NOT count.
    /// </summary>
    private static int LegendaryCreaturesControlled(Player player)
    {
        var count = 0;
        foreach (var card in player.Zones.Battlefield.GetCards())
        {
            if (card is Creature c
                && c.HasType(CardType.Creature)
                && c.HasSupertype(CardSupertype.Legendary))
            {
                count++;
            }
        }
        return count;
    }

    private static bool BindChannel(
        Land land, Player controller, string costSegment, string effectText)
    {
        // CR 118.9 — the Channel mana cost binds as a self-reducing cost when
        // the "costs {1} less to activate for each legendary creature you
        // control" rider is present (Boseiju and the whole Channel cycle). The
        // reduction is computed against the PAYING player's live board at
        // activation/payment, transparently through the standard
        // CanPay/Pay surface (no dispatch-site change needed — same seam every
        // cost-payment path consults). Without the rider the plain fixed
        // ManaCostCost binds.
        ManaCostCost channelManaCost = ChannelLegendaryReductionRider.IsMatch(effectText)
            ? new DynamicGenericReductionManaCost(costSegment, LegendaryCreaturesControlled)
            : new ManaCostCost(costSegment);

        var costs = new List<ICost> { channelManaCost, new DiscardSelfCost(land) };

        IEffect? effect = null;
        TargetRequest[] targets = Array.Empty<TargetRequest>();
        ActivatedAbility? ability = null;

        // --- Boseiju — destroy target artifact/enchantment/nonbasic land ----
        if (Regex.IsMatch(effectText,
                @"^Destroy\s+target\s+artifact,\s*enchantment,\s*or\s+nonbasic\s+land",
                RegexOptions.IgnoreCase))
        {
            effect = new Effect(
                $"{land.Name} (Channel): destroy target artifact, enchantment, or nonbasic land an opponent controls",
                () =>
                {
                    if (FirstChosen(ability) is not ICard target) return;
                    if (target.Zone != ZoneType.Battlefield) return;
                    if (!(target.HasType(CardType.Artifact) || target.HasType(CardType.Enchantment)
                          || (target.HasType(CardType.Land) && !target.HasSupertype(CardSupertype.Basic)))) return;
                    Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                });
            targets = new[]
            {
                new TargetRequest(
                    Description: "target artifact, enchantment, or nonbasic land an opponent controls",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherBoseijuTargets(ctx, land, controller)),
            };
        }
        // --- Otawara — return target permanent to its owner's hand ----------
        else if (Regex.IsMatch(effectText,
                     @"^Return\s+target\s+artifact,\s*creature,\s*enchantment,\s*or\s+planeswalker\s+to\s+its\s+owner'?s\s+hand",
                     RegexOptions.IgnoreCase))
        {
            effect = new Effect(
                $"{land.Name} (Channel): return target artifact, creature, enchantment, or planeswalker to its owner's hand",
                () =>
                {
                    if (FirstChosen(ability) is not ICard target) return;
                    if (target.Zone != ZoneType.Battlefield) return;
                    if (target.HasType(CardType.Land)) return; // nonland gate
                    Fx.BounceToHand(target, ZoneServiceRegistry.Get(land.Controller ?? controller));
                });
            targets = new[]
            {
                new TargetRequest(
                    Description: "target artifact, creature, enchantment, or planeswalker",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: ctx => GatherOtawaraTargets(ctx)),
            };
        }
        // --- Eiganjo — deal 4 damage to target attacking/blocking creature --
        else if (Regex.Match(effectText,
                     @"^It\s+deals\s+(?<n>\d+)\s+damage\s+to\s+target\s+attacking\s+or\s+blocking\s+creature",
                     RegexOptions.IgnoreCase) is { Success: true } eig)
        {
            var n = int.Parse(eig.Groups["n"].Value);
            effect = new Effect(
                $"{land.Name} (Channel): deal {n} damage to target attacking or blocking creature",
                () =>
                {
                    if (FirstChosen(ability) is not Creature target) return;
                    if (target.Zone != ZoneType.Battlefield) return;
                    Fx.DealDamageAny(target, n);
                });
            targets = new[]
            {
                new TargetRequest(
                    Description: "target attacking or blocking creature",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherAllCreatures(ctx)),
            };
        }
        // --- Takenuma — mill 3, then return a creature/PW from your graveyard
        else if (Regex.IsMatch(effectText,
                     @"^Mill\s+three\s+cards,\s*then\s+return\s+a\s+creature\s+or\s+planeswalker\s+card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
                     RegexOptions.IgnoreCase))
        {
            effect = new Effect(
                $"{land.Name} (Channel): mill three cards, then return a creature or planeswalker card from your graveyard to your hand",
                async ctx =>
                {
                    var ctrl = land.Controller ?? controller;
                    Fx.Mill(ctrl, 3);

                    var eligible = ctrl.Zones.Graveyard.GetCards()
                        .Where(c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker))
                        .ToList();
                    if (eligible.Count == 0) return;

                    // CR 608.2 — the controller chooses WHICH eligible card to
                    // return (agent-driven). The return itself is MANDATORY: the
                    // current oracle text has no "may", so when at least one
                    // eligible card exists the effect must return one (CR 608.2c).
                    // A null agent pick (an agent that declines) is therefore
                    // forced to the first eligible card rather than skipping the
                    // return.
                    var agent = ctx.Agent ?? AgentRegistry.Get(ctrl);
                    var pick = agent != null
                        ? await agent.ChooseLibraryPickAsync(ctx.Game, eligible, "creature or planeswalker card").ConfigureAwait(false)
                        : eligible[0];
                    pick ??= eligible[0];

                    Fx.ReturnFromGraveyardToHand(pick, ZoneServiceRegistry.Get(ctrl));
                });
        }
        // --- Sokenzan — create two 1/1 colorless Spirit tokens w/ haste -----
        else if (Regex.IsMatch(effectText,
                     @"^Create\s+two\s+1/1\s+colorless\s+Spirit\s+creature\s+tokens",
                     RegexOptions.IgnoreCase))
        {
            effect = new Effect(
                $"{land.Name} (Channel): create two 1/1 colorless Spirit creature tokens with haste",
                () =>
                {
                    var ctrl = land.Controller ?? controller;
                    var zones = ZoneServiceRegistry.Get(ctrl);
                    for (var i = 0; i < 2; i++)
                    {
                        TokenFactory.CreateOnBattlefield(
                            new TokenFactory.TokenSpec(
                                Name: "Spirit",
                                Power: 1,
                                Toughness: 1,
                                Subtypes: new[] { CardSubtype.Spirit },
                                Keywords: new[] { "Haste" },
                                Colors: Array.Empty<ManaColor>()),
                            ctrl, zones);
                    }
                });
        }

        if (effect is null) return false;

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect }, targetRequests: targets);
        land.AddAbility(ability);
        return true;
    }

    private static IReadOnlyList<object> GatherBoseijuTargets(
        GameContext ctx, Land land, Player controller)
    {
        var you = land.Controller ?? controller;
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            if (ReferenceEquals(p, you)) continue; // "an opponent controls"
            foreach (var c in p.Zones.Battlefield.GetCards())
            {
                if (c.HasType(CardType.Artifact) || c.HasType(CardType.Enchantment)
                    || (c.HasType(CardType.Land) && !c.HasSupertype(CardSupertype.Basic)))
                {
                    result.Add(c);
                }
            }
        }
        return result;
    }

    private static IReadOnlyList<object> GatherOtawaraTargets(GameContext ctx)
    {
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
            foreach (var c in p.Zones.Battlefield.GetCards())
                if (!c.HasType(CardType.Land)) result.Add(c);
        return result;
    }

    private static bool BindLine(
        Land land, Player controller, ContinuousEffectsService effects,
        TriggerManager? triggers, string cost, string effectText)
    {
        // --- Scry (Castle Vantress) ---------------------------------------
        if (ScryEffect.Match(effectText) is { Success: true } sm)
        {
            var n = int.Parse(sm.Groups["n"].Value);
            BindScry(land, controller, cost, n);
            return true;
        }

        // --- Draw (Sea Gate Wreckage, Castle Locthwain, Bonders' Enclave,
        //     Memorial to Genius, Agna Qel'a, Roadside Reliquary) ----------
        if (DrawEffect.Match(effectText) is { Success: true } dm)
        {
            var n = WordToInt(dm.Groups["n"].Value);
            BindDraw(land, controller, cost, n, effectText);
            return true;
        }

        // --- +1/+1 counter on EACH creature you control (Gavony Township) --
        if (CounterEachYouControl.IsMatch(effectText))
        {
            BindCounterEachYouControl(land, controller, cost);
            return true;
        }

        // --- +1/+1 counter(s) on TARGET creature (Cave of Temptation) ------
        if (CounterOnTargetCreature.Match(effectText) is { Success: true } cm)
        {
            var n = WordToInt(cm.Groups["n"].Value);
            BindCounterOnTargetCreature(land, controller, cost, n);
            return true;
        }

        // --- Damage to each opponent (Ramunap Ruins) -----------------------
        if (DamageEachOpponent.Match(effectText) is { Success: true } dem)
        {
            var n = int.Parse(dem.Groups["n"].Value);
            var gain = ExtractGainLife(effectText);
            BindDamageEachOpponent(land, controller, cost, n, gain);
            return true;
        }

        // --- Damage to any target (Barbarian Ring) -------------------------
        if (DamageAnyTarget.Match(effectText) is { Success: true } dam)
        {
            var n = int.Parse(dam.Groups["n"].Value);
            BindDamageAnyTarget(land, controller, cost, n);
            return true;
        }

        // --- You gain N life (Phyrexia's Core) -----------------------------
        if (GainLifeEffect.Match(effectText) is { Success: true } gm)
        {
            var n = int.Parse(gm.Groups["n"].Value);
            BindGainLife(land, controller, cost, n);
            return true;
        }

        // --- Mass until-EOT keyword grant to your creatures (Vault of the
        //     Archangel) -------------------------------------------------------
        if (CreaturesYouControlGainKeywords.Match(effectText) is { Success: true } kwm
            && TryParseGrantedKeywords(kwm.Groups["kw"].Value, out var grantedKeywords))
        {
            BindGrantKeywordsToCreaturesYouControl(land, controller, effects, cost, grantedKeywords!);
            return true;
        }

        // --- Return target artifact from your graveyard (Buried Ruin) ------
        if (ReturnArtifactFromGraveyard.IsMatch(effectText))
        {
            BindReturnArtifactFromGraveyard(land, controller, cost);
            return true;
        }

        // --- Return target <typed> you control to hand (Riptide Laboratory)
        if (ReturnTypedYouControlToHand.Match(effectText) is { Success: true } rm)
        {
            if (Enum.TryParse<CardSubtype>(rm.Groups["type"].Value, ignoreCase: true, out var st))
            {
                BindReturnTypedYouControlToHand(land, controller, cost, st);
                return true;
            }
        }

        // --- Create X Treasure tokens (Treasure Vault) — count-linked mint -
        if (CreateXTreasures.IsMatch(effectText))
        {
            BindCreateXTreasures(land, controller, cost);
            return true;
        }

        // --- Create a Phyrexian Mite token (Mirrex) — richer token shape ----
        //     (artifact-creature + toxic + can't-block). Checked before the
        //     simple-token path (which rejects the richer riders).
        if (CreateMiteToken.Match(effectText) is { Success: true } mite)
        {
            BindCreateMiteToken(land, controller, cost, mite);
            return true;
        }

        // --- Attack-rider delayed token trigger (Dalkovan Encampment) -------
        //     "{cost}: Whenever you attack this turn, create N P/T <color>
        //     <Subtype> tokens …". The activated ability installs a one-turn
        //     attack trigger with the live TriggerManager (CR 508.1f / 603.7).
        if (AttackRiderTokens.Match(effectText) is { Success: true } rider)
        {
            BindAttackRiderTokens(land, controller, cost, triggers, rider, effectText);
            return true;
        }

        // --- Create a simple fixed creature token (Castle Ardenvale) -------
        if (CreateSimpleToken.Match(effectText) is { Success: true } tm &&
            TryParseTokenSpec(tm, out var spec))
        {
            BindCreateToken(land, controller, cost, spec!);
            return true;
        }

        // --- Search for a basic of one of three named subtypes (Panorama
        //     cycle / Bountiful & Twisted Landscape) ----------------------
        if (SearchNamedBasicsTapped.Match(effectText) is { Success: true } searchm)
        {
            var subtypes = new[]
            {
                ParseBasicSubtype(searchm.Groups["a"].Value),
                ParseBasicSubtype(searchm.Groups["b"].Value),
                ParseBasicSubtype(searchm.Groups["c"].Value),
            };
            BindSearchNamedBasicsTapped(land, controller, cost, subtypes);
            return true;
        }

        // --- Destroy target [nonbasic] land (Ghost Quarter cycle) ----------
        if (DestroyTargetLand.Match(effectText) is { Success: true } destm)
        {
            var nonbasicOnly = destm.Groups["nonbasic"].Success;
            var opponentOnly = destm.Groups["opp"].Success;
            // Demolition Field's both-players search-for-basic rider: detect the
            // "controller may search" + "you may search" follow-up clauses on
            // the SAME ability so they sequence after the destroy.
            var searchRider = ParseDestroyBasicSearchRider(effectText);
            BindDestroyTargetLand(land, controller, cost, nonbasicOnly, opponentOnly, searchRider);
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------------
    // Scry — Castle Vantress "{2}{U}{U}, {T}: Scry 2." (CR 701.20)
    // ----------------------------------------------------------------------
    private static void BindScry(Land land, Player controller, string cost, int n)
    {
        var costs = BuildCosts(land, cost, out _);
        var effect = new Effect(
            $"{land.Name}: scry {n}",
            async ctx =>
            {
                var ctrl = land.Controller ?? controller;
                var peeked = ScryAction.Peek(ctrl, n);
                if (peeked.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(ctrl);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked).ConfigureAwait(false);
                }
                else
                {
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>());
                }
                Fx.Scry(ctrl, peeked.Count, decision);
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Draw — Sea Gate Wreckage / Castle Locthwain / Bonders' Enclave /
    // Memorial to Genius / Agna Qel'a / Roadside Reliquary. (CR 120)
    //
    // Bound: the draw + the simple, deterministic riders we can model from
    // text: "you lose life equal to the number of cards in your hand"
    // (Castle Locthwain), "then discard a card" (Agna Qel'a). Hand-restriction
    // ("Activate only if you have no cards in hand") + power/control gates are
    // wired as canActivateCheck. Roadside Reliquary's per-permanent-conditional
    // double-draw is bound as its base "draw a card" with the conditional
    // riders deferred (xmldoc) — the card-advantage signal is covered.
    // ----------------------------------------------------------------------
    private static void BindDraw(Land land, Player controller, string cost, int n, string effectText)
    {
        var costs = BuildCosts(land, cost, out _);

        // Castle Locthwain: "Draw a card, then you lose life equal to the
        // number of cards in your hand." Model the life loss after the draw.
        var lifeLossEqualHand = effectText.Contains(
            "lose life equal to the number of cards in your hand", StringComparison.OrdinalIgnoreCase);
        // Agna Qel'a: "Draw a card, then discard a card."
        var thenDiscard = Regex.IsMatch(effectText, @"then discard a card", RegexOptions.IgnoreCase);

        // Hand-empty restriction (Sea Gate Wreckage).
        Func<bool>? canActivate = null;
        if (effectText.Contains("Activate only if you have no cards in hand", StringComparison.OrdinalIgnoreCase))
        {
            canActivate = () => !(land.Controller ?? controller).Zones.Hand.GetCards().Any();
        }

        var effect = new Effect(
            $"{land.Name}: draw {n} card(s)" +
            (lifeLossEqualHand ? ", then lose life equal to cards in hand" : "") +
            (thenDiscard ? ", then discard a card" : ""),
            () =>
            {
                var ctrl = land.Controller ?? controller;
                Fx.DrawCards(ctrl, n);
                if (lifeLossEqualHand)
                {
                    Fx.LoseLife(ctrl, ctrl.Zones.Hand.GetCards().Count());
                }
                if (thenDiscard)
                {
                    Fx.Discard(ctrl, 1);
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect }, canActivateCheck: canActivate));
    }

    // ----------------------------------------------------------------------
    // +1/+1 counter on EACH creature you control — Gavony Township. (CR 122)
    // ----------------------------------------------------------------------
    private static void BindCounterEachYouControl(Land land, Player controller, string cost)
    {
        var costs = BuildCosts(land, cost, out _);
        var effect = new Effect(
            $"{land.Name}: put a +1/+1 counter on each creature you control",
            () =>
            {
                var ctrl = land.Controller ?? controller;
                foreach (var c in ctrl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                {
                    c.Counters.Add(CounterType.PlusOnePlusOne, 1);
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Mass until-EOT keyword grant to "creatures you control" — Vault of the
    // Archangel "{2}{W}{B}, {T}: Creatures you control gain deathtouch and
    // lifelink until end of turn." (CR 613.1c Layer 6 ability addition;
    // CR 514.2 cleanup-step expiry).
    //
    // This is the GROUP-apply form of the existing single-target
    // grant_keyword_until_eot_target verb
    // (CardDefRuntime.BuildGrantKeywordUntilEotTargetEffect): instead of one
    // chosen creature it walks the controller's battlefield at RESOLUTION
    // (CR 611.2c — a one-shot grant; creatures that enter later this turn are
    // not retroactively granted) and registers a
    // GrantKeywordUntilEndOfTurnEffect per keyword on each creature's OWN
    // ActiveEffects layer service (CR 613.1c). Both grants auto-expire in the
    // cleanup step via ContinuousEffect.ExpiresAtEndOfTurn (CR 514.2). The
    // controller's Battlefield zone already scopes "you control" (CR — only
    // the activating player's creatures). Opponent creatures are untouched.
    //
    // Resolution-time read of land.Controller carries the ability across a
    // control change (CR 109.5); falls back to the bind-time controller.
    // ----------------------------------------------------------------------
    private static void BindGrantKeywordsToCreaturesYouControl(
        Land land, Player controller, ContinuousEffectsService effects,
        string cost, IReadOnlyList<string> keywords)
    {
        var costs = BuildCosts(land, cost, out _);
        var label = string.Join(" and ", keywords).ToLowerInvariant();
        var effect = new Effect(
            $"{land.Name}: creatures you control gain {label} until end of turn",
            () =>
            {
                var ctrl = land.Controller ?? controller;
                foreach (var creature in ctrl.Zones.Battlefield.GetCards()
                             .OfType<Creature>().ToList())
                {
                    // CR 613 — register on each creature's own layer service. In
                    // prod every battlefield permanent already carries the shared
                    // ContinuousEffectsService; wire the binder's service for any
                    // creature that was added without one so the grant is honoured
                    // and torn down at cleanup.
                    var svc = creature.ActiveEffects ?? effects;
                    creature.ActiveEffects ??= svc;
                    foreach (var kw in keywords)
                    {
                        svc.Register(new GrantKeywordUntilEndOfTurnEffect(creature, kw));
                    }
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // +1/+1 counter(s) on TARGET creature — Cave of Temptation. (CR 122 /
    // CR 608.2b). TargetRequest-driven; the agent's pick is read on resolution.
    // Cave of Temptation's "Activate only as a sorcery" timing is deferred
    // (no binder-reachable sorcery-speed seam on this generic path; the
    // counter itself binds + targets correctly).
    // ----------------------------------------------------------------------
    private static void BindCounterOnTargetCreature(Land land, Player controller, string cost, int n)
    {
        var costs = BuildCosts(land, cost, out _);

        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{land.Name}: put {n} +1/+1 counter(s) on target creature",
            () =>
            {
                if (FirstChosen(ability) is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                Fx.PlaceCounter(target, CounterType.PlusOnePlusOne, n);
            });

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    CandidateGatherer: ctx => GatherAllCreatures(ctx)),
            });

        land.AddAbility(ability);
    }

    // ----------------------------------------------------------------------
    // Damage to each opponent (+ optional gain N life) — Ramunap Ruins.
    // (CR 119). Reads opponents off the LIVE resolution context (never a
    // captured resolver — resolver-null inert-on-prod bug class). The typed
    // "Sacrifice a Desert" non-self sacrifice cost is deferred (no
    // typed-sacrifice-chooser primitive); the {2}{R}{R} + {T} cost + damage
    // effect bind.
    // ----------------------------------------------------------------------
    private static void BindDamageEachOpponent(Land land, Player controller, string cost, int n, int gain)
    {
        var costs = BuildCosts(land, cost, out _);
        var effect = new Effect(
            $"{land.Name}: deal {n} damage to each opponent" + (gain > 0 ? $"; gain {gain} life" : ""),
            ctx =>
            {
                var ctrl = land.Controller ?? controller;
                foreach (var opp in ContextOpponents.Of(ctx, ctrl))
                {
                    Fx.DealDamageAny(opp, n);
                }
                if (gain > 0) Fx.GainLife(ctrl, gain);
                return ValueTask.CompletedTask;
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Damage to any target — Barbarian Ring "{R}, {T}, Sacrifice this land: It
    // deals 2 damage to any target." (CR 119). TargetRequest over players +
    // creatures + planeswalkers. The "seven or more cards in graveyard"
    // threshold timing gate is deferred (no binder-reachable canActivate seam
    // for graveyard-count); the damage itself binds + targets.
    // ----------------------------------------------------------------------
    private static void BindDamageAnyTarget(Land land, Player controller, string cost, int n)
    {
        var costs = BuildCosts(land, cost, out _);

        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{land.Name}: deal {n} damage to any target",
            () =>
            {
                if (FirstChosen(ability) is not { } target) return;
                if (target is Permanent p && p.Zone != ZoneType.Battlefield) return;
                Fx.DealDamageAny(target, n);
            });

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    CandidateGatherer: ctx => GatherAnyDamageTargets(ctx)),
            });

        land.AddAbility(ability);
    }

    // ----------------------------------------------------------------------
    // You gain N life — Phyrexia's Core "{1}, {T}, Sacrifice an artifact: You
    // gain 1 life." (CR 119.3). The "Sacrifice an artifact" non-self typed
    // sacrifice cost is deferred (no typed-sacrifice-chooser primitive); the
    // {1} + {T} cost + gain-life effect bind.
    // ----------------------------------------------------------------------
    private static void BindGainLife(Land land, Player controller, string cost, int n)
    {
        var costs = BuildCosts(land, cost, out _);
        var effect = new Effect(
            $"{land.Name}: gain {n} life",
            () => Fx.GainLife(land.Controller ?? controller, n));

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Return target artifact card from YOUR graveyard to hand — Buried Ruin.
    // (CR 608.2b). TargetRequest over the controller's graveyard artifacts.
    // ----------------------------------------------------------------------
    private static void BindReturnArtifactFromGraveyard(Land land, Player controller, string cost)
    {
        var costs = BuildCosts(land, cost, out _);

        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{land.Name}: return target artifact card from your graveyard to your hand",
            () =>
            {
                if (FirstChosen(ability) is not ICard card) return;
                if (card.Zone != ZoneType.Graveyard) return;
                if (!card.HasType(CardType.Artifact)) return;
                Fx.ReturnFromGraveyardToHand(card, ZoneServiceRegistry.Get(land.Controller ?? controller));
            });

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact card in your graveyard",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    CandidateGatherer: _ => GatherControllerGraveyardArtifacts(land, controller)),
            });

        land.AddAbility(ability);
    }

    // ----------------------------------------------------------------------
    // Return target <typed> you control to its owner's hand — Riptide
    // Laboratory "Return target Wizard you control to its owner's hand."
    // (CR 608.2b). TargetRequest over the controller's battlefield permanents
    // of the named subtype.
    // ----------------------------------------------------------------------
    private static void BindReturnTypedYouControlToHand(Land land, Player controller, string cost, CardSubtype subtype)
    {
        var costs = BuildCosts(land, cost, out _);

        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"{land.Name}: return target {subtype} you control to its owner's hand",
            () =>
            {
                if (FirstChosen(ability) is not Permanent perm) return;
                if (perm.Zone != ZoneType.Battlefield) return;
                if (!perm.HasSubtype(subtype)) return;
                var you = land.Controller ?? controller;
                if (!ReferenceEquals(perm.Controller, you)) return;
                Fx.BounceToHand(perm, ZoneServiceRegistry.Get(you));
            });

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: $"target {subtype} you control",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: _ => GatherControllerSubtype(land, controller, subtype)),
            });

        land.AddAbility(ability);
    }

    // ----------------------------------------------------------------------
    // Create a simple fixed creature token — Castle Ardenvale. (CR 111).
    // Mirrex's toxic Mite + Dalkovan's attack-rider tokens fail TryParseTokenSpec
    // (unknown keyword phrasing / "tapped and attacking" rider) and fall through
    // to deferred (xmldoc) — the simple white Human / colored token binds.
    // ----------------------------------------------------------------------
    private static void BindCreateToken(Land land, Player controller, string cost, TokenFactory.TokenSpec spec)
    {
        var costs = BuildCosts(land, cost, out _);
        var effect = new Effect(
            $"{land.Name}: create a {spec.Power}/{spec.Toughness} {spec.Name} token",
            () =>
            {
                var ctrl = land.Controller ?? controller;
                TokenFactory.CreateOnBattlefield(spec, ctrl, ZoneServiceRegistry.Get(ctrl));
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Create X Treasure tokens — Treasure Vault
    // "{X}{X}, {T}, Sacrifice this land: Create X Treasure tokens." (CR 602)
    //
    // The cost {X}{X} parses into a ManaCostCost via BuildCosts (the
    // ManaSymbol regex covers {X}); + Tap + Sacrifice-self (BuildCosts reads
    // "Sacrifice this land"). The live activation flow expands the {X} cost and
    // stamps the chosen X onto the ability (TurnDriver →
    // VariableXCostExpansion + ActivatedAbility.SetChosenX), surfacing it to
    // resolution via ResolutionContext.ChosenX (GAP 2 — the same ledger Steel
    // Hellkite / Lair of the Hydra read). At resolution the effect reads
    // ctx.ChosenX (null ⇒ 0, the legal-but-useless "activate for X=0" path) and
    // mints that many Treasure tokens (CR 111.10 — each a colourless artifact
    // with "{T}, Sacrifice this artifact: Add one mana of any color") under the
    // source's live controller, routed through ZoneService so each Treasure's
    // ETB CardMovedEvent fires.
    //
    // v1 simplification (consistent with the rest of the codebase's {X}{X}
    // handling, e.g. Blast Zone): ManaCost tracks only a HasX flag, not the
    // X-pip COUNT, so the {X}{X} cost expands to X (not 2X) generic at payment.
    // The token COUNT — the deferral's count-linked primitive — is correct
    // (X tokens for the chosen X); the {X}{X} double-payment exactness is a
    // separate, pre-existing engine-wide concern.
    // ----------------------------------------------------------------------
    private static void BindCreateXTreasures(Land land, Player controller, string cost)
    {
        var costs = BuildCosts(land, cost, out _);

        var effect = new Effect(
            $"{land.Name}: create X Treasure tokens",
            ctx =>
            {
                var ctrl = land.Controller ?? controller;
                var x = ctx.ChosenX ?? 0;
                var zones = ZoneServiceRegistry.Get(ctrl);
                for (var i = 0; i < x; i++)
                {
                    TokenFactory.CreateTreasure(ctrl, zones);
                }
                return ValueTask.CompletedTask;
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Create a Phyrexian Mite token — Mirrex
    // "{3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature token
    //  with toxic 1 and \"This token can't block.\"" (CR 111.1 / 111.4).
    //
    // The richer token shape the simple-token path can't carry: the Mite is an
    // ARTIFACT creature (CR 111.1 — Artifact stamped additively after the
    // Creature shell), colourless (empty colour override), and carries two
    // markers — toxic N (CR 702.180, parameterised KeywordAbility honoured by
    // the damage system) and "can't block" (CR 509.1a, a "CantBlock" KeywordAbility
    // now enforced at block-declaration by CombatValidator via
    // CombatAbilities.HasCantBlock). Minted through the TokenCreationIntent path
    // so token-doublers (Doubling Season) can rewrite the count.
    // ----------------------------------------------------------------------
    private static void BindCreateMiteToken(Land land, Player controller, string cost, Match m)
    {
        var costs = BuildCosts(land, cost, out _);
        var power = int.Parse(m.Groups["p"].Value);
        var toughness = int.Parse(m.Groups["t"].Value);
        var toxic = m.Groups["toxic"].Success ? int.Parse(m.Groups["toxic"].Value) : 0;
        var cantBlock = m.Groups["cantblock"].Success
            && m.Groups["cantblock"].Value.Length > 0;

        var effect = new Effect(
            $"{land.Name}: create a {power}/{toughness} colorless Phyrexian Mite artifact creature token",
            () =>
            {
                var ctrl = land.Controller ?? controller;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Phyrexian Mite",
                    Power: power, Toughness: toughness,
                    Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Mite },
                    Keywords: null,
                    Colors: Array.Empty<ManaColor>());

                var tokens = TokenFactory.CreateOnBattlefield(
                    spec, ctrl, count: 1,
                    zones: ZoneServiceRegistry.Get(ctrl),
                    replacements: null);

                foreach (var token in tokens)
                {
                    // CR 111.1 — Artifact stamped additively (artifact creature).
                    token.AddCardType(CardType.Artifact);
                    if (toxic > 0)
                        token.AddAbility(new KeywordAbility("toxic", token, ctrl, arg: toxic));
                    if (cantBlock)
                        token.AddAbility(new KeywordAbility("CantBlock", token, ctrl));
                }
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // ----------------------------------------------------------------------
    // Attack-rider delayed token trigger — Dalkovan Encampment
    // "{2}{W}, {T}: Whenever you attack this turn, create two 1/1 red Warrior
    //  creature tokens that are tapped and attacking. Sacrifice them at the
    //  beginning of the next end step." (CR 508.1f / CR 603.7).
    //
    // The activated ability's RESOLUTION installs a one-turn "whenever a creature
    // you control attacks" TriggeredAbility with the live TriggerManager that
    // mints N P/T <color> <Subtype> tokens per attack, plus a companion
    // DelayedTriggeredAbility (CR 603.7) that unregisters the attack trigger at
    // the start of the next end step (modelling the "this turn" duration). When
    // no TriggerManager is threaded (shape-only build) the activation is an inert
    // no-op — same posture as DalkovanEncampmentFactory.
    //
    // v1 deferral (mirroring DalkovanEncampmentFactory): the printed tokens enter
    // "tapped and attacking" — the engine has no surface to insert a creature into
    // an in-progress combat mid-trigger (same gap as Geist of Saint Traft's Angel /
    // Goblin Rabblemaster), so the tokens land on the battlefield untapped and not
    // in an attacker slot. The "Sacrifice them at the beginning of the next end
    // step" rider IS now wired (CR 603.7): each batch of minted tokens schedules a
    // DelayedTriggeredAbility that sacrifices exactly those tokens at the next end
    // step (move to owner's graveyard; SBA 704.5d / CR 111.7 then ceases them).
    // ----------------------------------------------------------------------
    private static void BindAttackRiderTokens(
        Land land, Player controller, string cost, TriggerManager? triggers, Match m,
        string effectText)
    {
        var sacrificeAtEndStep = SacrificeThemNextEndStep.IsMatch(effectText);
        var costs = BuildCosts(land, cost, out _);
        var count = ParseCountWord(m.Groups["count"].Value);
        var power = int.Parse(m.Groups["p"].Value);
        var toughness = int.Parse(m.Groups["t"].Value);
        TryParseColor(m.Groups["color"].Value, out var color);
        Enum.TryParse<CardSubtype>(m.Groups["subtype"].Value, ignoreCase: true, out var subtype);

        var spec = new TokenFactory.TokenSpec(
            Name: subtype.ToString(),
            Power: power, Toughness: toughness,
            Subtypes: new[] { subtype },
            Keywords: null,
            Colors: new[] { color });

        var effect = new Effect(
            $"{land.Name}: install this-turn attack trigger → {count} {power}/{toughness} {color} {subtype} tokens per attack",
            () =>
            {
                if (triggers == null) return;
                var ctrl = land.Controller ?? controller;

                var tokenEffect = new Effect(
                    $"{land.Name}: create {count} {power}/{toughness} {color} {subtype} tokens (attack trigger)",
                    () =>
                    {
                        var c = land.Controller ?? controller;
                        var minted = TokenFactory.CreateOnBattlefield(
                            spec, c, count: count,
                            zones: ZoneServiceRegistry.Get(c),
                            replacements: null);

                        // CR 603.7 — "Sacrifice them at the beginning of the next
                        // end step." Schedule a delayed sacrifice of exactly THIS
                        // batch of tokens (each attack mints + schedules its own).
                        if (sacrificeAtEndStep && minted.Count > 0)
                        {
                            ScheduleEndStepSacrifice(
                                land, land.Controller ?? controller, triggers!, minted);
                        }
                    });

                var attackTrigger = new TriggeredAbility(
                    source: land,
                    controller: ctrl,
                    condition: new EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>(
                        (e, _) => ReferenceEquals(e.Attacker.Controller, land.Controller ?? controller)),
                    effects: new IEffect[] { tokenEffect },
                    activeZones: new[] { ZoneType.Battlefield });

                triggers.RegisterTriggeredAbility(attackTrigger);

                // CR 603.7 — unregister the attack trigger at the next end step,
                // modelling the printed "this turn" duration.
                var resolvedAt = LogicalClockScope.Current.NextTimestamp();
                var cleanup = new DelayedTriggeredAbility(
                    source: land,
                    controller: ctrl,
                    condition: new EventTriggerCondition<StepStartedEvent>(
                        (e, _) => e.StepType == Majik.Core.StateMachine.StepStateType.End
                                  && e.Timestamp > resolvedAt),
                    effects: new IEffect[]
                    {
                        new Effect(
                            $"{land.Name}: unregister attack-token trigger (EOT cleanup)",
                            () => triggers.UnregisterTriggeredAbility(attackTrigger)),
                    });
                triggers.RegisterDelayed(cleanup);
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    // CR 603.7 — register a DelayedTriggeredAbility that, at the start of the next
    // end step, sacrifices exactly <paramref name="tokens"/> (the batch minted by
    // one attack). Sacrifice = move each token to its owner's graveyard (CR 701.16);
    // SBA 704.5d / CR 111.7 then removes the token entirely (it ceases to exist).
    private static void ScheduleEndStepSacrifice(
        Land land, Player controller, TriggerManager triggers, IReadOnlyList<Creature> tokens)
    {
        var batch = tokens.ToList();
        var resolvedAt = LogicalClockScope.Current.NextTimestamp();

        var sacrifice = new DelayedTriggeredAbility(
            source: land,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == Majik.Core.StateMachine.StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[]
            {
                new Effect(
                    $"{land.Name}: sacrifice {batch.Count} attack-rider token(s) (next end step)",
                    () =>
                    {
                        foreach (var token in batch)
                        {
                            var owner = token.Owner ?? controller;
                            if (token.Zone != ZoneType.Battlefield) continue;
                            var holder = token.Controller ?? owner;
                            holder.Zones.Battlefield.RemoveCard(token);
                            owner.Zones.Graveyard.AddCard(token);
                            token.SetZone(ZoneType.Graveyard);
                        }
                    }),
            });
        triggers.RegisterDelayed(sacrifice);
    }

    private static int ParseCountWord(string word) =>
        word.ToLowerInvariant() switch
        {
            "a" or "one" => 1,
            "two" => 2,
            "three" => 3,
            _ => 1,
        };

    // The two search-for-basic riders that follow a "Destroy target nonbasic
    // land an opponent controls" half on a SINGLE ability (Demolition Field):
    // the destroyed land's controller and the activator each "may search …
    // for a basic land card, put it onto the battlefield, then shuffle".
    private readonly record struct DestroyBasicSearchRider(
        bool DestroyedControllerSearches, bool ActivatorSearches)
    {
        public bool Any => DestroyedControllerSearches || ActivatorSearches;
    }

    /// <summary>Detect Demolition Field's both-players search-for-basic rider in
    /// the effect clause that follows the destroy half.</summary>
    private static DestroyBasicSearchRider ParseDestroyBasicSearchRider(string effectText)
        => new(
            DestroyedControllerSearchBasic.IsMatch(effectText),
            ActivatorSearchBasic.IsMatch(effectText));

    // ----------------------------------------------------------------------
    // Destroy target [nonbasic] land [an opponent controls] — Ghost Quarter /
    // Field of Ruin / Tectonic Edge / Demolition Field / Encroaching Wastes.
    // (CR 701.7 / CR 608.2b). TargetRequest over battlefield lands (filtered to
    // nonbasic / opponent-controlled per the printed clause).
    //
    // Demolition Field's both-players search-for-basic rider (CR 701.19 /
    // CR 701.20a) NOW BINDS here (v1-deferrals demolition-field-search-rider):
    // after the destroy, the destroyed land's controller (only if a land was
    // actually destroyed — CR 608.2b illegal-target → no "that land's
    // controller") then the activator each "may search their/your library for a
    // basic land card, put it onto the battlefield, then shuffle". The basics
    // enter UNTAPPED (Demolition Field prints no "tapped"). Each is a "may"
    // search routed through the player's IPlayerAgent (decline / empty library
    // still shuffles, CR 701.20a). The other timing gates (Tectonic Edge's
    // "opponent controls four or more lands") remain deferred.
    // ----------------------------------------------------------------------
    private static void BindDestroyTargetLand(
        Land land, Player controller, string cost,
        bool nonbasicOnly, bool opponentOnly, DestroyBasicSearchRider rider = default)
    {
        var costs = BuildCosts(land, cost, out _);

        ActivatedAbility? ability = null;
        var label = $"{land.Name}: destroy target {(nonbasicOnly ? "nonbasic " : "")}land" +
                    (opponentOnly ? " an opponent controls" : "");
        if (rider.Any) label += "; then search-for-basic rider";

        var effect = new Effect(
            label,
            async ctx =>
            {
                Player? destroyedLandController = null;
                if (FirstChosen(ability) is ICard target
                    && target.Zone == ZoneType.Battlefield
                    && target.HasType(CardType.Land)
                    && !(nonbasicOnly && target.HasSupertype(CardSupertype.Basic))
                    && !(opponentOnly && target is Permanent p &&
                         ReferenceEquals(p.Controller, land.Controller ?? controller)))
                {
                    destroyedLandController = (target as Permanent)?.Controller;
                    Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
                }

                if (!rider.Any) return;

                // Tutor riders in printed order: the destroyed land's controller
                // first (only if a land was actually destroyed), then the
                // activator. Each is a "may" search; the basics enter untapped.
                if (rider.DestroyedControllerSearches && destroyedLandController != null)
                {
                    await TutorBasicLandUntapped(ctx, destroyedLandController).ConfigureAwait(false);
                }
                if (rider.ActivatorSearches)
                {
                    await TutorBasicLandUntapped(ctx, land.Controller ?? controller).ConfigureAwait(false);
                }
            });

        ability = new ActivatedAbility(
            source: land, controller: controller, costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1, MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => GatherLands(ctx, land, controller, nonbasicOnly, opponentOnly)),
            });

        land.AddAbility(ability);
    }

    /// <summary>CR 701.19a / 701.20a — <paramref name="player"/> may search
    /// their library for a basic land card, put it onto the battlefield
    /// UNTAPPED, then shuffle (the shuffle fires whether or not a card is found,
    /// CR 701.20a). The pick is routed through the player's agent; an empty
    /// candidate pool / declined search still shuffles.</summary>
    private static async ValueTask TutorBasicLandUntapped(ResolutionContext ctx, Player player)
    {
        if (player == null) return;

        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic land card").ConfigureAwait(false);

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
            }
        }

        LibraryShuffle.ShuffleLibrary(player, "demolition-field-tutor");
    }

    // ----------------------------------------------------------------------
    // Search for a basic of one of three named subtypes, onto battlefield
    // tapped — Panorama cycle / Bountiful & Twisted Landscape. (CR 701.19 /
    // CR 305 / 614). Self-sacrifice is inlined in the resolve closure (the
    // AdditionalCost.Sacrifice Pay() is a no-op stub — same posture as
    // OracleLandActivatedAbilityBinder's sac-fetch). The chosen basic enters
    // TAPPED via ZoneService (ETB-tapped replacements + CardMovedEvent fire),
    // then the library is shuffled (CR 701.20a — whether or not found).
    // ----------------------------------------------------------------------
    private static void BindSearchNamedBasicsTapped(
        Land land, Player controller, string cost, CardSubtype[] subtypes)
    {
        var costs = BuildCosts(land, cost, out _);
        var label = string.Join(", ", subtypes);

        var effect = new Effect(
            $"{land.Name}: sac self + tutor a basic {label} -> battlefield tapped, shuffle",
            async ctx =>
            {
                var ctrl = land.Controller ?? controller;

                // Self-sacrifice first (CR 701.16) — before the search so the
                // source is gone (mirrors OracleLandActivatedAbilityBinder).
                SacrificeToOwnersGraveyard(land);

                var candidates = ctrl.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land)
                             && c.HasSupertype(CardSupertype.Basic)
                             && subtypes.Any(c.HasSubtype))
                    .ToList();

                var pick = await LibrarySearch.PromptOnlyAsync(
                    ctx, ctrl, candidates, "basic land card").ConfigureAwait(false);

                if (pick != null)
                {
                    var zones = ZoneServiceRegistry.Get(ctrl);
                    if (zones != null)
                    {
                        zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, ctrl);
                        if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
                    }
                    else
                    {
                        ctrl.Zones.Library.RemoveCard(pick);
                        ctrl.Zones.Battlefield.AddCard(pick);
                        pick.SetZone(ZoneType.Battlefield);
                        pick.SetController(ctrl);
                        if (pick is Permanent perm) perm.Tap();
                    }
                }

                LibraryShuffle.ShuffleLibrary(ctrl, "panorama-fetch");
            });

        land.AddAbility(new ActivatedAbility(
            source: land, controller: controller, costs: costs, effects: new IEffect[] { effect }));
    }

    /// <summary>CR 701.16 — move the source land to its owner's graveyard
    /// (sacrifice). Mirrors OracleLandActivatedAbilityBinder.</summary>
    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var owner = self.Owner;
        if (owner == null) return;
        if (self.Zone != ZoneType.Battlefield) return;
        var holder = self.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(self);
        owner.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    private static CardSubtype ParseBasicSubtype(string word) =>
        word.ToLowerInvariant() switch
        {
            "plains" => CardSubtype.Plains,
            "island" => CardSubtype.Island,
            "swamp" => CardSubtype.Swamp,
            "mountain" => CardSubtype.Mountain,
            "forest" => CardSubtype.Forest,
            _ => CardSubtype.Plains,
        };

    // ---- helpers ----------------------------------------------------------

    /// <summary>First chosen target across the ability's first request, or null.</summary>
    private static object? FirstChosen(ActivatedAbility? ability)
    {
        if (ability is null) return null;
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null;
        return chosen[0][0];
    }

    /// <summary>Parse the body of "Create a P/T &lt;colors&gt; &lt;Subtype&gt;
    /// creature token" into a <see cref="TokenFactory.TokenSpec"/>. Returns false
    /// for tokens carrying keyword riders / quoted abilities / "tapped and
    /// attacking" (Mirrex's toxic Mite, Dalkovan Encampment) — those defer.</summary>
    private static bool TryParseTokenSpec(Match m, out TokenFactory.TokenSpec? spec)
    {
        spec = null;
        if (!int.TryParse(m.Groups["p"].Value, out var power) ||
            !int.TryParse(m.Groups["t"].Value, out var toughness))
        {
            return false;
        }

        // rest = "<colors> [artifact] <Subtype>" (e.g. "white Human", "red
        // Warrior"). Split colours from the trailing subtype token.
        var tokens = m.Groups["rest"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (tokens.Count == 0) return false;

        var colors = new List<ManaColor>();
        var subtypes = new List<CardSubtype>();
        foreach (var tok in tokens)
        {
            if (TryParseColor(tok, out var col)) { colors.Add(col); continue; }
            if (tok.Equals("and", StringComparison.OrdinalIgnoreCase)) continue;
            if (tok.Equals("colorless", StringComparison.OrdinalIgnoreCase)) continue;
            // A multi-type / non-creature-subtype word (artifact, with, toxic …)
            // means this is a richer token shape — defer.
            if (Enum.TryParse<CardSubtype>(tok, ignoreCase: true, out var st)) subtypes.Add(st);
            else return false;
        }

        if (subtypes.Count == 0) return false;

        var name = string.Join(" ", subtypes);
        spec = new TokenFactory.TokenSpec(
            Name: name, Power: power, Toughness: toughness,
            Subtypes: subtypes, Keywords: null, Colors: colors);
        return true;
    }

    private static bool TryParseColor(string word, out ManaColor color)
    {
        switch (word.ToLowerInvariant())
        {
            case "white": color = ManaColor.White; return true;
            case "blue": color = ManaColor.Blue; return true;
            case "black": color = ManaColor.Black; return true;
            case "red": color = ManaColor.Red; return true;
            case "green": color = ManaColor.Green; return true;
            default: color = default; return false;
        }
    }

    /// <summary>Parse the keyword list captured from "Creatures you control gain
    /// &lt;list&gt; until end of turn" (e.g. "deathtouch and lifelink",
    /// "trample", "first strike and vigilance") into the engine's canonical
    /// keyword names. Splits on " and " / "," and canonicalizes each token via
    /// <see cref="GrantableKeywords"/>. Returns <c>false</c> (so the line defers
    /// rather than binds wrong) when the list is empty or names ANY keyword
    /// outside the binder-reachable grantable set.</summary>
    private static bool TryParseGrantedKeywords(string list, out IReadOnlyList<string>? keywords)
    {
        keywords = null;
        var tokens = Regex.Split(list.Trim(), @"\s*,\s*|\s+and\s+", RegexOptions.IgnoreCase)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (tokens.Count == 0) return false;

        var canonical = new List<string>();
        foreach (var tok in tokens)
        {
            if (!GrantableKeywords.TryGetValue(tok, out var name)) return false;
            if (!canonical.Contains(name)) canonical.Add(name);
        }

        keywords = canonical;
        return true;
    }

    /// <summary>Pull a trailing "gain N life" rider out of an effect clause
    /// (Ramunap Ruins has none, but the loop family supports it). 0 = none.</summary>
    private static int ExtractGainLife(string effectText)
    {
        var m = Regex.Match(effectText, @"gain\s+(?<n>\d+)\s+life", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups["n"].Value) : 0;
    }

    private static IReadOnlyList<object> GatherAllCreatures(GameContext ctx)
    {
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
            foreach (var c in p.Zones.Battlefield.GetCards().OfType<Creature>())
                if (!result.Any(r => ReferenceEquals(r, c))) result.Add(c);
        return result;
    }

    private static IReadOnlyList<object> GatherAnyDamageTargets(GameContext ctx)
    {
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            result.Add(p);
            foreach (var c in p.Zones.Battlefield.GetCards())
                if (c is Creature || c is Planeswalker) result.Add(c);
        }
        return result;
    }

    private static IReadOnlyList<object> GatherControllerGraveyardArtifacts(Land land, Player controller)
    {
        var ctrl = land.Controller ?? controller;
        return ctrl.Zones.Graveyard.GetCards()
            .Where(c => c.HasType(CardType.Artifact)).Cast<object>().ToList();
    }

    private static IReadOnlyList<object> GatherControllerSubtype(Land land, Player controller, CardSubtype subtype)
    {
        var ctrl = land.Controller ?? controller;
        return ctrl.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(c => c.HasSubtype(subtype))
            .Cast<object>().ToList();
    }

    private static IReadOnlyList<object> GatherLands(
        GameContext ctx, Land land, Player controller, bool nonbasicOnly, bool opponentOnly)
    {
        var you = land.Controller ?? controller;
        var result = new List<object>();
        foreach (var p in ctx.AllPlayers)
        {
            if (opponentOnly && ReferenceEquals(p, you)) continue;
            foreach (var c in p.Zones.Battlefield.GetCards())
            {
                if (!c.HasType(CardType.Land)) continue;
                if (ReferenceEquals(c, land)) continue;
                if (nonbasicOnly && c.HasSupertype(CardSupertype.Basic)) continue;
                result.Add(c);
            }
        }
        return result;
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
