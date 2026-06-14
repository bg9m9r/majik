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
///   <item><b>Fight</b> —
///     <c>"{cost}: This creature fights target creature."</c> Rebuilt with a
///     1..1 <see cref="TargetRequest"/> and resolution through
///     <see cref="Fx.Fight"/> (CR 701.12). Sound to re-home: the BEARER is the
///     fight source — it deals its power to, and takes the power of, the chosen
///     creature, never the exiled card. Only the OPEN "target creature" filter is
///     reconstructed (a restricted filter like "target creature you don't
///     control" is skipped, consistent with the pinger's restricted-target
///     boundary). Creature-only (only a creature can fight).</item>
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
///   <item><b>Self-counter</b> —
///     <c>"{cost}: Put a/N +1/+1 counter(s) on this creature."</c> Rebuilt as a
///     no-target <see cref="ActivatedAbility"/> whose resolution adds the
///     +1/+1 counter(s) to the BEARER's own
///     <see cref="Majik.Core.Counters.CounterCollection"/> (CR 122.1 /
///     613.1f). Sound to re-home: the counter is placed on the bearer, never the
///     exiled card. (Especially apt for the Cauldron: the grown bearer is, by
///     construction, a creature you control with a +1/+1 counter.)</item>
///   <item><b>Regenerate-self</b> —
///     <c>"{cost}: Regenerate this creature."</c> Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose resolution creates a regeneration
///     shield on the BEARER via <see cref="Permanent.AddRegenerationShield"/>
///     (CR 701.18 / 701.15a) — the SAME shield primitive River Boa / Drudge
///     Skeletons / Mortivore / Lotleth Troll use. Sound to re-home onto any
///     permanent bearer (the shield is a <see cref="Permanent"/>-level
///     replacement, not creature-only). Trailing reminder text is stripped
///     before matching so both the bare and the reminder-bearing printings are
///     recognised.</item>
///   <item><b>Draw-a-card</b> —
///     <c>"{cost}: Draw a card."</c> / <c>"Draw N cards."</c> Rebuilt as a
///     no-target <see cref="ActivatedAbility"/> whose resolution draws for the
///     BEARER's CONTROLLER via <see cref="Majik.Core.Primitives.Fx.DrawCards"/>
///     (CR 121.1 / 613.1f) — the soundest re-home of all the non-mana shapes
///     because a draw has NO "this creature" / source reference (the controller
///     draws from their own library, never the exiled card). Count "a"/"one" ⇒
///     1, plus the spelled-out "two"/"three" and a bare digit; an unrecognised
///     count word is skipped. Sound on any permanent bearer, not just
///     creatures.</item>
///   <item><b>Gain-life</b> —
///     <c>"{cost}: You gain N life."</c> Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose resolution gains life for the
///     BEARER's CONTROLLER via <see cref="Majik.Core.Primitives.Fx.GainLife"/>
///     (CR 119.3 / 613.1f) — like draw, one of the soundest re-homes: "you gain
///     N life" references the controller's OWN life total, with NO "this
///     creature" / source reference at all, so re-homing is a clean
///     controller-scoped operation. Count is "a"/"one" ⇒ 1, the spelled-out
///     "two"/"three", or a bare digit; an unrecognised count is skipped. Sound
///     on any permanent bearer, not just creatures.</item>
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
///     token makers, anthem grants, scry/mill/surveil — which need an agent
///     decision the closure can't prompt for — loyalty-style, bespoke
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

    // "{cost}: This creature fights target creature." (CR 701.12.)
    // A self-source fight is a common activated payoff on real creature cards
    // (a {cost}: ~ fights target creature ability). Sound to re-home: the SOURCE
    // of the fight is the BEARER — it both deals its power to, and takes the
    // power of, the chosen creature (Fx.Fight), never the exiled imprinted card.
    // Only the OPEN "target creature" filter is reconstructed (no restricted
    // candidate filter like "target creature you don't control"), consistent
    // with the restricted-target soundness boundary of the pinger shape.
    private static readonly Regex FightRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature fights target creature\.$",
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

    // "{cost}: Put a/N +1/+1 counter(s) on this creature."
    // A self-source counter-placement ability (e.g. a creature with a
    // {cost}: grow-itself ability). Sound to re-home: the counter is placed on
    // the BEARER's own CounterCollection — the granted ability never touches the
    // exiled card. "Put a +1/+1 counter" → N = 1; an explicit "Put 2 +1/+1
    // counters" → N = the stated count.
    private static readonly Regex SelfCounterRegex = new(
        @"^(" + CostList + @")\s*:\s*Put (?:a|one|(\d+)) \+1/\+1 counters? on this creature\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Draw a card." / "Draw N cards." (CR 121.) A self-source draw is
    // one of the most common activated card-advantage shapes on real creature
    // cards (Arcanis the Omnipotent "{T}: Draw three cards.", a host of
    // {T}: Draw payoffs). Sound to re-home: a draw references the BEARER's
    // CONTROLLER's own library/hand (Fx.DrawCards), NEVER the exiled imprinted
    // card — no "this creature" / source reference at all, so this is the
    // soundest re-home of any non-mana shape. The count is "a"/"one" ⇒ 1, an
    // explicit digit ("draw 2 cards"), or a small spelled-out word
    // ("two"/"three" — the only forms that appear on real activated draw
    // abilities). An unrecognised count word makes the clause unsound and is
    // skipped. Trailing reminder text is stripped before matching.
    private static readonly Regex DrawCardsRegex = new(
        @"^(" + CostList + @")\s*:\s*Draw (a|one|two|three|\d+) cards?\.$",
        RegexOptions.IgnoreCase);

    // Spelled-out small counts that appear on real "Draw N cards" activated
    // abilities. "a"/"one" ⇒ 1. Larger counts on activated draw abilities are
    // always written as a word in this range (no real card says "draw 9 cards"
    // on an activated ability), but a bare digit is also accepted for safety.
    private static readonly IReadOnlyDictionary<string, int> DrawCountWords =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
        };

    // "{cost}: You gain N life." (CR 119.3.) A self-source lifegain is a common
    // activated payoff on real creature cards (a cleric / soul-warden style
    // {T}: gain payoff, Staff-of-Domination-style {N}, {T}: you gain N life).
    // Sound to re-home: "you gain N life" references the BEARER's CONTROLLER's
    // OWN life total (Fx.GainLife), NEVER the exiled imprinted card — no "this
    // creature" / source reference at all, so this is as sound a re-home as
    // draw. The count is "a"/"one" ⇒ 1, an explicit digit ("you gain 5 life"),
    // or a small spelled-out word ("two"/"three"). An unrecognised count word
    // makes the clause unsound and is skipped. Trailing reminder text is
    // stripped before matching. (Note: "Pay N life" is a COST, not an effect,
    // and is never matched here — this only recognises gaining life.)
    private static readonly Regex GainLifeRegex = new(
        @"^(" + CostList + @")\s*:\s*You gain (a|one|two|three|\d+) life\.$",
        RegexOptions.IgnoreCase);

    // Spelled-out small counts that appear on real "You gain N life" activated
    // abilities ("a"/"one" ⇒ 1). A bare digit is also accepted for larger
    // amounts; an unrecognised word makes the clause unsound and is skipped.
    private static readonly IReadOnlyDictionary<string, int> LifeCountWords =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
        };

    // "{cost}: Regenerate this creature." (CR 701.18 / 701.15a.)
    // A self-source regeneration ability — one of the most common activated
    // abilities on real creature cards (River Boa, Drudge Skeletons, Wall of
    // Bone, Twisted Abomination, Lotleth Troll, Mortivore, …). Sound to re-home:
    // a regeneration shield protects the BEARER (Permanent.AddRegenerationShield),
    // never the exiled card. Reminder text in parentheses ("(The next time this
    // creature would be destroyed …)") is stripped before matching so both the
    // bare and the reminder-bearing oracle spellings are recognised. The
    // self-source "Regenerate this creature" / "Regenerate {self name}" forms
    // are matched; a target-OTHER regenerate ("Regenerate target creature") is
    // NOT — its target candidate filter isn't reconstructed here, so it is
    // skipped as unsound (consistent with the restricted-target boundary above).
    private static readonly Regex RegenerateSelfRegex = new(
        @"^(" + CostList + @")\s*:\s*Regenerate this creature\.$",
        RegexOptions.IgnoreCase);

    // A single tap symbol inside a cost list.
    private static readonly Regex TapTokenRegex = new(@"^\{T\}$", RegexOptions.IgnoreCase);

    // Trailing parenthetical reminder text on an oracle line, e.g.
    // "(The next time this creature would be destroyed this turn, …)". Reminder
    // text is non-rules flavour (CR 207.2) and varies across printings, so it is
    // stripped before a line is matched against the shape regexes — otherwise a
    // card whose regenerate line carries reminder text (Drudge Skeletons, Wall
    // of Bone) would fail to match a regex anchored to the rules text. Stripping
    // is conservative: it only removes a parenthesised run that ENDS the line.
    private static readonly Regex TrailingReminderRegex = new(
        @"\s*\([^)]*\)\s*$", RegexOptions.IgnoreCase);

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
            // Strip any trailing reminder text (CR 207.2) so a shape regex
            // anchored to the rules text still matches a printing that carries
            // it (e.g. "{B}: Regenerate this creature. (The next time …)").
            var line = TrailingReminderRegex.Replace(rawLine.Trim(), string.Empty).Trim();
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

            var fight = FightRegex.Match(line);
            if (fight.Success)
            {
                var costs = TryBuildCostList(fight.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var ability = BuildFight(costs, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var kwGrant = SelfKeywordGrantRegex.Match(line);
            if (kwGrant.Success)
            {
                var ability = TryBuildSelfKeywordGrant(kwGrant, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var selfCounter = SelfCounterRegex.Match(line);
            if (selfCounter.Success)
            {
                var ability = TryBuildSelfCounter(selfCounter, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var regenerate = RegenerateSelfRegex.Match(line);
            if (regenerate.Success)
            {
                var ability = TryBuildRegenerateSelf(regenerate, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var draw = DrawCardsRegex.Match(line);
            if (draw.Success)
            {
                var ability = TryBuildDrawCards(draw, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var gainLife = GainLifeRegex.Match(line);
            if (gainLife.Success)
            {
                var ability = TryBuildGainLife(gainLife, bearer, controller);
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
    /// Build a self-counter ability: "{cost}: Put a/N +1/+1 counter(s) on this
    /// creature." Re-homed so the counter is placed on the BEARER's own
    /// <see cref="Creature.Counters"/> (CR 122.1 / 613.1f) — never on the exiled
    /// imprinted card. A creature-only shape (a +1/+1 counter on a non-creature
    /// is meaningless here), so a non-creature bearer returns null (skip). This
    /// shape is doubly synergistic with Agatha's Soul Cauldron: the bearer it
    /// grows is, by definition, a creature you control with a +1/+1 counter, so
    /// staying in the grant's scope (CR 611.2c) is reinforced.
    /// </summary>
    private static ActivatedAbility? TryBuildSelfCounter(
        Match match, Permanent bearer, Player controller)
    {
        if (bearer is not Creature creatureBearer) return null;

        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        // Group 2 is the explicit count ("Put 2 +1/+1 counters …"); absent for
        // the "Put a +1/+1 counter …" / "Put one …" forms ⇒ a single counter.
        var count = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;

        var counterEffect = new Effect(
            $"Granted: put {count} +1/+1 counter(s) on this creature",
            () =>
            {
                // CR 122.1 / 613.1f — place the counter directly on the BEARER's
                // own counter collection. No effects-service dependency: a +1/+1
                // counter is a persistent characteristic-defining object on the
                // permanent, not an until-end-of-turn registration.
                creatureBearer.Counters.Add(
                    Counters.CounterType.PlusOnePlusOne, count);
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { counterEffect });
    }

    /// <summary>
    /// Build a regenerate-self ability: "{cost}: Regenerate this creature."
    /// Re-homed so the regeneration shield is created on the BEARER's own
    /// <see cref="Permanent.AddRegenerationShield"/> (CR 701.18 / 701.15a) — the
    /// next destroy of the BEARER this turn is replaced (tap, remove from combat,
    /// heal damage), never the exiled imprinted card. This is the SAME shield
    /// primitive the Mortivore / Lotleth Troll / River Boa factories use. Sound
    /// to re-home onto ANY permanent bearer (the shield is a
    /// <see cref="Permanent"/>-level replacement, not creature-only), so unlike
    /// the pump / keyword / counter shapes this one does not gate on
    /// <see cref="Creature"/>.
    /// </summary>
    private static ActivatedAbility? TryBuildRegenerateSelf(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var regenerateEffect = new Effect(
            "Granted: regenerate this creature (CR 701.18)",
            // CR 701.15a — create a regeneration shield on the BEARER. No
            // effects-service dependency: the shield is a self-contained
            // replacement counter on the permanent.
            () => bearer.AddRegenerationShield());

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { regenerateEffect });
    }

    /// <summary>
    /// Build a draw-a-card ability: "{cost}: Draw a/N card(s)." Re-homed so the
    /// draw is for the BEARER's CONTROLLER's own library/hand
    /// (<see cref="Fx.DrawCards"/>, CR 121.1 / 613.1f) — never the exiled
    /// imprinted card. This is the soundest non-mana re-home of all: a draw has
    /// no "this creature" / source reference at all, so re-homing is a clean
    /// controller-scoped operation. Does NOT gate on <see cref="Creature"/> — a
    /// draw is sound on any permanent bearer (the controller draws, not the
    /// permanent). The count is "a"/"one" ⇒ 1, a spelled-out "two"/"three", or a
    /// bare digit; an unrecognised count word is skipped (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildDrawCards(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var countToken = match.Groups[2].Value.Trim();
        int count;
        if (!DrawCountWords.TryGetValue(countToken, out count)
            && !int.TryParse(countToken, out count))
        {
            return null; // unrecognised count — skip as unsound
        }
        if (count <= 0) return null;

        var drawEffect = new Effect(
            $"Granted: draw {count} card(s)",
            // CR 121.1 / 613.1f — the BEARER's controller draws from their own
            // library. No source-card reference, so re-homing is trivially sound.
            () => Fx.DrawCards(controller, count));

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { drawEffect });
    }

    /// <summary>
    /// Build a gain-life ability: "{cost}: You gain N life." Re-homed so the
    /// life is gained by the BEARER's CONTROLLER (<see cref="Fx.GainLife"/>,
    /// CR 119.3 / 613.1f) — never the exiled imprinted card. Like draw, this has
    /// no "this creature" / source reference, so re-homing is a clean
    /// controller-scoped operation; it does NOT gate on <see cref="Creature"/> (a
    /// lifegain is sound on any permanent bearer — the controller gains the life,
    /// not the permanent). The count is "a"/"one" ⇒ 1, a spelled-out
    /// "two"/"three", or a bare digit; an unrecognised count word is skipped
    /// (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildGainLife(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var countToken = match.Groups[2].Value.Trim();
        int amount;
        if (!LifeCountWords.TryGetValue(countToken, out amount)
            && !int.TryParse(countToken, out amount))
        {
            return null; // unrecognised count — skip as unsound
        }
        if (amount <= 0) return null;

        var gainEffect = new Effect(
            $"Granted: you gain {amount} life",
            // CR 119.3 / 613.1f — the BEARER's controller gains life. No
            // source-card reference, so re-homing is trivially sound.
            () => Fx.GainLife(controller, amount));

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { gainEffect });
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
    /// Build a fight ability: "{cost}: This creature fights target creature."
    /// Re-homed so the SOURCE of the fight is the bearer (CR 701.12) — the bearer
    /// deals its power to, and takes the power of, the chosen creature; the exiled
    /// imprinted card never participates. A fight is creature-only (only a
    /// creature can fight), so a non-creature bearer returns null (skip, don't
    /// emit broken). Resolution funnels through <see cref="Fx.Fight"/>; the cost
    /// (mana/tap) already taps the bearer. Mirrors <see cref="BuildPinger"/>'s
    /// 1..1 single-creature target request.
    /// </summary>
    private static ActivatedAbility? BuildFight(
        List<ICost> costs,
        Permanent bearer,
        Player controller)
    {
        if (bearer is not Creature creatureBearer) return null;

        ActivatedAbility? ability = null;
        var fightEffect = new Effect(
            "Granted: this creature fights target creature",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.12 — the BEARER is the fight source (it both deals and
                // takes the fight damage). A non-creature chosen target no-ops.
                var target = ability.ChosenTargets[0][0] as Creature;
                Fx.Fight(creatureBearer, target);
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { fightEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
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
