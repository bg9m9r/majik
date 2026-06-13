using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Card-identity-agnostic rebuilder that parses a creature's oracle text into
/// fresh, RE-SOURCEABLE <see cref="ActivatedAbility"/>s built on a supplied
/// <c>bearer</c> permanent. Sibling to
/// <see cref="OracleManaBinder.ParseTapManaCosts"/>: where that binder rebuilds
/// the MANA-ability slice ("{T}: Add …") against a new source, this binder
/// rebuilds the common NON-mana activated-ability shapes that appear on real
/// creature cards, re-homed so the cost taps/sacrifices the BEARER and the
/// effect references the BEARER ("this creature" = the bearer).
///
/// <para>The canonical consumer is Agatha's Soul Cauldron's ability-grant
/// static (CR 613.1f / 702.49): "creatures you control with +1/+1 counters on
/// them have all activated abilities of all creature cards exiled with Agatha's
/// Soul Cauldron." Engine abilities are CLOSURES over their source
/// <see cref="Card"/> — you cannot copy an imprinted creature's
/// <see cref="ActivatedAbility"/> onto a bearer because its cost/effect would
/// reference the EXILED card. A granted ability must be a FRESH ability built
/// against the bearer; the only sound way to do that for an arbitrary creature
/// is to RECONSTRUCT it from oracle text. This binder reconstructs the shapes it
/// can do correctly and skips everything else (see "Soundness boundary").</para>
///
/// <h3>Shapes rebuilt</h3>
/// <list type="bullet">
///   <item><b>Firebreathing / self-pump</b> —
///     <c>"{cost}: This creature gets ±X/±Y until end of turn."</c> Rebuilt as a
///     no-target <see cref="ActivatedAbility"/> whose resolution registers a
///     <see cref="PumpUntilEndOfTurnEffect"/>(±X, ±Y) against the BEARER's own
///     <see cref="Creature.ActiveEffects"/> (CR 613.1f Layer 7c; CR 514.2
///     expiry) — the all-positive form is exactly the primitive Fiery Hellhound
///     uses; a signed delta (a negative power/toughness leg, as on Aetherling /
///     Canyon Crab / Darklit Gargoyle / the Flowstone cycle) is equally sound to
///     re-home because the effect adds the raw signed ints to the bearer.</item>
///   <item><b>Pinger</b> —
///     <c>"{cost}: This creature deals N damage to &lt;any target | target
///     creature | target player&gt;."</c> Rebuilt with a 1..1
///     <see cref="TargetRequest"/> and resolution through
///     <see cref="Fx.DealDamageAny"/> (Player / Creature / Planeswalker funnel,
///     CR 119.3 / 306.7) — exactly Endbringer's pinger.</item>
///   <item><b>Sacrifice-self pinger</b> —
///     <c>"Sacrifice this creature: It deals N damage to &lt;…&gt;."</c> Same as
///     above but the cost is <see cref="AdditionalCost.Sacrifice"/>(bearer)
///     instead of a mana/tap cost — exactly Mogg Fanatic.</item>
///   <item><b>Self-keyword grant</b> —
///     <c>"{cost}: This creature gains &lt;keyword&gt; until end of turn."</c>
///     (the keyword sibling of firebreathing). Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose resolution registers a
///     <see cref="GrantKeywordUntilEndOfTurnEffect"/> for the named keyword
///     against the BEARER's own <see cref="Creature.ActiveEffects"/> (CR 613.1f
///     Layer 6; CR 514.2 expiry). ONLY the closed set of simple,
///     parameter-free combat/evasion keywords is reconstructed (flying, first
///     strike, double strike, deathtouch, trample, lifelink, vigilance, haste,
///     reach, menace, indestructible, hexproof) — a parameterised or unknown
///     keyword is skipped as unsound. Sound to re-home: the effect targets the
///     bearer, never the exiled card.</item>
/// </list>
///
/// <h3>Cost grammar</h3>
/// A cost is a <c>", "</c>-separated list of mana-symbol RUNS (each a
/// concatenation of one or more pips the way real oracle text writes a
/// multi-symbol cost — <c>{2}</c>, <c>{R}</c>, <c>{1}{U}</c>, <c>{2}{R}</c>, …)
/// and/or the tap symbol (<c>{T}</c>). All the mana symbols are folded into a
/// single <see cref="ManaCostCost"/>; a <c>{T}</c> becomes
/// <see cref="AdditionalCost.Tap"/>(bearer). So <c>"{R}, {T}:"</c>,
/// <c>"{T}:"</c>, <c>"{2}:"</c>, and <c>"{1}{U}:"</c> are all handled. The
/// <c>"Sacrifice this creature:"</c> cost becomes
/// <see cref="AdditionalCost.Sacrifice"/>(bearer).
///
/// <h3>Soundness boundary (what is deliberately SKIPPED, not broken)</h3>
/// To never emit an ability that would behave incorrectly when re-homed, any
/// clause whose cost or effect this binder cannot model EXACTLY is skipped:
/// <list type="bullet">
///   <item>Non-mana / non-tap / non-sacrifice cost tokens — energy
///     (<c>{E}</c>), Phyrexian (<c>{R/P}</c>), snow (<c>{S}</c>), <c>{X}</c>,
///     "Pay N life", "Discard a card", "Remove a counter", etc. — the cost
///     can't be reconstructed soundly, so the whole clause is skipped.</item>
///   <item>"Activate only …" riders (sorcery-speed, "only if", "only once each
///     turn") — the gating predicate isn't reconstructable here.</item>
///   <item>Restricted damage targets ("target creature defending player
///     controls", "target attacking creature", "another target creature") —
///     the candidate filter isn't reconstructed; only the open
///     any-target / target-creature / target-player forms are rebuilt.</item>
///   <item>Every shape not in the list above (tutors, mode-bearing abilities,
///     token makers, anthem grants, "{T}: Draw", loyalty-style, bespoke
///     one-offs). These are unbounded and not generally reconstructable from
///     oracle text without per-card work — a correct partial beats a broken
///     "all".</item>
/// </list>
/// </summary>
public static class OracleActivatedAbilityBinder
{
    // A cost token is either {T} or a RUN of one-or-more concatenated mana pips
    // we can model exactly — generic digits and/or W/U/B/R/G/C — written the way
    // oracle text spells a multi-symbol cost ("{R}", "{2}", "{1}{U}", "{2}{R}").
    // Anything else ({X}, {E}, {S}, Phyrexian {R/P}, etc.) is intentionally NOT
    // matched so the clause is skipped as unsound. The cost is a ", "-separated
    // list of these (TryBuildCostList re-validates each token before folding).
    private const string CostToken = @"(?:\{T\}|(?:\{(?:\d+|[WUBRGC])\})+)";
    private const string CostList = CostToken + @"(?:\s*,\s*" + CostToken + @")*";

    // "{cost}: This creature gets ±X/±Y until end of turn."
    // Each delta carries its OWN sign so signed-pump shapes — a negative
    // toughness/power leg as on Aetherling ({1}: +1/-1), Canyon Crab
    // ({1}{U}: +2/-2), Darklit Gargoyle ({B}: +2/-1) and the Flowstone cycle
    // ({R}: +1/-1) — are reconstructed too, not just the all-positive
    // firebreathing form. A negative delta is sound to re-home: the rebuilt
    // PumpUntilEndOfTurnEffect adds the signed ints to the BEARER's
    // characteristics (CR 613.1f Layer 7c).
    private static readonly Regex SelfPumpRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature gets ([+-]\d+)/([+-]\d+) until end of turn\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature deals N damage to <target form>."
    private static readonly Regex PingerRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature deals (\d+) damage to (any target|target creature|target player)\.$",
        RegexOptions.IgnoreCase);

    // "Sacrifice this creature: It deals N damage to <target form>."
    private static readonly Regex SacPingerRegex = new(
        @"^Sacrifice this creature:\s*It deals (\d+) damage to (any target|target creature|target player)\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature gains <keyword> until end of turn."
    private static readonly Regex SelfKeywordGrantRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature gains (.+?) until end of turn\.$",
        RegexOptions.IgnoreCase);

    // The closed set of simple, parameter-free keywords this binder will grant
    // via a self-keyword-grant ability. Each maps the oracle spelling → the
    // canonical keyword name CreatureCharacteristics.Keywords stores (the set is
    // OrdinalIgnoreCase, so casing is for readability). Parameterised keywords
    // (ward N, protection from X) and anything not here is skipped as unsound —
    // a granted keyword must be reconstructable EXACTLY (CR 613.1f).
    private static readonly IReadOnlyDictionary<string, string> GrantableKeywords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["flying"] = "Flying",
            ["first strike"] = "First Strike",
            ["double strike"] = "Double Strike",
            ["deathtouch"] = "Deathtouch",
            ["trample"] = "Trample",
            ["lifelink"] = "Lifelink",
            ["vigilance"] = "Vigilance",
            ["haste"] = "Haste",
            ["reach"] = "Reach",
            ["menace"] = "Menace",
            ["indestructible"] = "Indestructible",
            ["hexproof"] = "Hexproof",
        };

    // A single tap symbol inside a cost list.
    private static readonly Regex TapTokenRegex = new(@"^\{T\}$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse <paramref name="oracleText"/> into fresh non-mana
    /// <see cref="ActivatedAbility"/>s re-homed to <paramref name="bearer"/>.
    /// Mana abilities are NOT produced here — those come from
    /// <see cref="OracleManaBinder.ParseTapManaCosts"/>. Returns an empty list
    /// when the text has no rebuildable non-mana activated clause (the common
    /// case: a vanilla creature, or one whose only abilities are outside the
    /// soundly-rebuildable set).
    /// </summary>
    /// <param name="oracleText">The imprinted creature card's oracle text.</param>
    /// <param name="bearer">The permanent the rebuilt abilities are sourced on.
    /// Must be a <see cref="Creature"/> for self-pump (pump is a creature-only
    /// Layer-7c effect); damage abilities re-home onto any permanent.</param>
    /// <param name="controller">The bearer's controller (the ability's
    /// controller + the player who pays its costs).</param>
    public static IReadOnlyList<ActivatedAbility> RebuildActivatedAbilities(
        string? oracleText,
        Permanent bearer,
        Player controller)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        ArgumentNullException.ThrowIfNull(controller);

        var result = new List<ActivatedAbility>();
        if (string.IsNullOrWhiteSpace(oracleText)) return result;

        foreach (var rawLine in oracleText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var pump = SelfPumpRegex.Match(line);
            if (pump.Success)
            {
                var ability = TryBuildSelfPump(pump, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var ping = PingerRegex.Match(line);
            if (ping.Success)
            {
                var costs = TryBuildCostList(ping.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var amount = int.Parse(ping.Groups[2].Value);
                result.Add(BuildPinger(costs, amount, ping.Groups[3].Value, bearer, controller));
                continue;
            }

            var sacPing = SacPingerRegex.Match(line);
            if (sacPing.Success)
            {
                var amount = int.Parse(sacPing.Groups[1].Value);
                var costs = new List<ICost> { AdditionalCost.Sacrifice(bearer) };
                result.Add(BuildPinger(costs, amount, sacPing.Groups[2].Value, bearer, controller));
                continue;
            }

            var kwGrant = SelfKeywordGrantRegex.Match(line);
            if (kwGrant.Success)
            {
                var ability = TryBuildSelfKeywordGrant(kwGrant, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }
        }

        return result;
    }

    /// <summary>
    /// Build the firebreathing / self-pump ability. Pump is a creature-only
    /// Layer-7c effect (CR 613.1f) registered against the bearer's
    /// <see cref="Creature.ActiveEffects"/>, so a non-creature bearer can't
    /// carry it soundly — returns null in that case (skip, don't emit broken).
    /// </summary>
    private static ActivatedAbility? TryBuildSelfPump(
        Match pump, Permanent bearer, Player controller)
    {
        if (bearer is not Creature creatureBearer) return null;

        var costs = TryBuildCostList(pump.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var p = int.Parse(pump.Groups[2].Value);
        var t = int.Parse(pump.Groups[3].Value);

        var pumpEffect = new Effect(
            $"Granted: this creature gets +{p}/+{t} until end of turn",
            () =>
            {
                // CR 613.1f Layer 7c — register against the BEARER's own
                // effects service. null ActiveEffects (shape-only path) → the
                // pump silently no-ops, same posture as Fiery Hellhound.
                creatureBearer.ActiveEffects?.Register(
                    new PumpUntilEndOfTurnEffect(creatureBearer, p, t));
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { pumpEffect });
    }

    /// <summary>
    /// Build a self-keyword grant: "{cost}: This creature gains &lt;keyword&gt;
    /// until end of turn." Like firebreathing this is a creature-only Layer-6
    /// effect (CR 613.1f) registered against the bearer's own
    /// <see cref="Creature.ActiveEffects"/>, so a non-creature bearer can't
    /// carry it soundly — returns null then (skip, don't emit broken). Only the
    /// closed <see cref="GrantableKeywords"/> set is reconstructed; an unknown or
    /// parameterised keyword is skipped (CR 613.1f — a granted ability must be
    /// modelled exactly).
    /// </summary>
    private static ActivatedAbility? TryBuildSelfKeywordGrant(
        Match match, Permanent bearer, Player controller)
    {
        if (bearer is not Creature creatureBearer) return null;

        var rawKeyword = match.Groups[2].Value.Trim();
        if (!GrantableKeywords.TryGetValue(rawKeyword, out var keyword)) return null;

        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var grantEffect = new Effect(
            $"Granted: this creature gains {keyword} until end of turn",
            () =>
            {
                // CR 613.1f Layer 6 — register against the BEARER's own effects
                // service. null ActiveEffects (shape-only path) → the grant
                // silently no-ops, same posture as the self-pump rebuild.
                creatureBearer.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creatureBearer, keyword));
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { grantEffect });
    }

    /// <summary>
    /// Build a pinger: "deals N damage to &lt;target form&gt;", re-homed so the
    /// SOURCE is the bearer. Resolution funnels through
    /// <see cref="Fx.DealDamageAny"/>; the cost (mana/tap/sacrifice) already
    /// taps/sacrifices the bearer.
    /// </summary>
    private static ActivatedAbility BuildPinger(
        List<ICost> costs,
        int amount,
        string targetForm,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var damageEffect = new Effect(
            $"Granted: this creature deals {amount} damage to {targetForm}",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                var target = ability.ChosenTargets[0][0];
                // CR 119 / 306.7 — Player / Creature / Planeswalker funnel.
                // The bearer is the damage source (it dealt the damage).
                Fx.DealDamageAny(target, amount, bearer as Creature);
            });

        var (description, intent) = targetForm.ToLowerInvariant() switch
        {
            "target creature" => ("target creature", BotIntent.Removal),
            "target player" => ("target player", BotIntent.None),
            _ => ("any target", BotIntent.Burn),
        };

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: description,
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: intent),
            });

        return ability;
    }

    /// <summary>
    /// Parse a ", "-separated cost list of mana symbols + {T} into the engine's
    /// cost objects, re-homed so any {T} taps the <paramref name="bearer"/>. The
    /// mana symbols are folded into a single <see cref="ManaCostCost"/>; a {T}
    /// becomes <see cref="AdditionalCost.Tap"/>(bearer). Returns null if any
    /// token is not a sound, reconstructable mana pip or {T} (the caller then
    /// skips the whole clause rather than emit a broken ability).
    /// </summary>
    private static List<ICost>? TryBuildCostList(
        string costList, Permanent bearer, Player controller)
    {
        var costs = new List<ICost>();
        var manaSymbols = new System.Text.StringBuilder();
        var tapped = false;

        foreach (var rawToken in costList.Split(','))
        {
            var token = rawToken.Trim();
            if (token.Length == 0) continue;

            if (TapTokenRegex.IsMatch(token))
            {
                // CR 602.2 — only one tap symbol is meaningful; a duplicate {T}
                // would be an unmodellable cost, so reject it.
                if (tapped) return null;
                tapped = true;
                continue;
            }

            // A run of one or more mana pips we can model exactly, written
            // CONCATENATED the way real oracle text spells a multi-symbol cost:
            // {N} generic and/or W/U/B/R/G/C coloured pips (e.g. "{1}{U}",
            // "{2}{R}", "{B}"). The whole token must be nothing BUT such pips —
            // a stray {E}/{S}/{R/P}/{X} makes the run unmodellable and the clause
            // is skipped below.
            if (Regex.IsMatch(token, @"^(?:\{(?:\d+|[WUBRGC])\})+$", RegexOptions.IgnoreCase))
            {
                manaSymbols.Append(token);
                continue;
            }

            // Any other token is not soundly reconstructable — skip the clause.
            return null;
        }

        if (manaSymbols.Length > 0)
        {
            costs.Add(new ManaCostCost(manaSymbols.ToString()));
        }

        if (tapped)
        {
            costs.Add(AdditionalCost.Tap(bearer));
        }

        // A pinger / pump must have SOME cost; an empty cost list would mean we
        // parsed a malformed line. (All real shapes have at least a {T} or mana.)
        return costs.Count > 0 ? costs : null;
    }
}
