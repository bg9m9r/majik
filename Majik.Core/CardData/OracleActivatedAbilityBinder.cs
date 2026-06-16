using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
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
///   <item><b>Same-name-spillover pinger</b> —
///     <c>"{cost}: This creature deals N damage to target creature and each
///     other creature with the same name as that creature."</c> Izzet
///     Staticaster's shape. Rebuilt with a 1..1 <see cref="TargetRequest"/>; the
///     chosen creature takes N, then every OTHER battlefield creature whose
///     EFFECTIVE name (CR 707.2) matches — read off the live game at resolution —
///     also takes N (CR 109.2). Sound to re-home: the BEARER is only the source /
///     cost-payer; the spill set depends only on the chosen name + the current
///     board, never the exiled imprinted card.</item>
///   <item><b>Power-pinger</b> —
///     <c>"{cost}: This creature deals damage equal to its power to &lt;any
///     target | target creature | target player&gt;."</c> The variable-amount
///     sibling of the fixed pinger (Spikeshot Goblin). Rebuilt with a 1..1
///     <see cref="TargetRequest"/>; the damage amount is read off the BEARER's
///     <see cref="Permanent.GetEffectivePower"/> AT RESOLUTION (CR 608.2h — the
///     amount is determined as the ability resolves), so a pumped / animated /
///     counter-grown bearer scales the damage with ITS own power, never the
///     exiled card's printed power. This is the exact case that motivated the
///     re-sourceable representation: a "deal damage equal to its power" closure
///     MUST read the bearer.</item>
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
///   <item><b>Token-copy-of-target (hasty copy)</b> —
///     <c>"{cost}: Create a token that's a copy of (another) target nonlegendary
///     creature you control, except it has haste. Sacrifice it at the beginning
///     of the next end step."</c> Kiki-Jiki, Mirror Breaker / Reflection of
///     Kiki-Jiki's shape (CR 707.2 copy / 702.10 haste / 603.7 delayed sacrifice
///     / 701.16). Rebuilt with a 1..1 <see cref="TargetRequest"/>; on resolution
///     the BEARER's CONTROLLER (read off the live
///     <see cref="ResolutionContext.Source"/> / <see cref="ResolutionContext.Controller"/>)
///     mints a copy of the CHOSEN target — copiable values snapshotted per
///     CR 706.2 (name, base P/T, subtypes, keyword names, colour) — with haste
///     added, and a one-shot end-step sacrifice scheduled via the ambient
///     <see cref="TriggerManagerRegistry"/>. Sound to re-home: the BEARER is only
///     the source / cost-payer; the "another" / "nonlegendary" / "you control"
///     restrictions gate at resolve (CR 608.2b) measured against the live source +
///     controller, so the copy means "a creature the BEARER's controller controls"
///     — never the exiled imprinted card. Mirrors the bespoke
///     <see cref="Factories.KikiJikiMirrorBreakerFactory"/>'s re-home posture.</item>
///   <item><b>Tap / untap target</b> —
///     <c>"{cost}: Tap target creature."</c> /
///     <c>"{cost}: Untap target creature."</c> Rebuilt with a 1..1
///     <see cref="TargetRequest"/> and resolution through
///     <see cref="Fx.Tap(Permanent, Player?)"/> /
///     <see cref="Fx.Untap(Permanent)"/> (CR 701.21a / 701.27a) — exactly Master
///     Decoy / Goldmeadow Harrier. Sound to re-home: the BEARER is only the
///     source / cost-payer (its own {T} cost taps it); the effect (un)taps the
///     CHOSEN creature, never the exiled card and never the bearer. Only the OPEN
///     "target creature" filter is reconstructed (a restricted filter is skipped,
///     consistent with the pinger / fight restricted-target boundary). Not
///     creature-only: any permanent bearer can pay to tap a chosen creature.</item>
///   <item><b>Return target to hand</b> —
///     <c>"{cost}: Return target creature/permanent to its owner's hand."</c>
///     (CR 701.20.) Temporal Adept's
///     <c>"{U}{U}{U}, {T}: Return target permanent to its owner's hand."</c>
///     shape — the targeted-BOUNCE sibling of the tapper. Rebuilt with a 1..1
///     <see cref="TargetRequest"/> and resolution through
///     <see cref="Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>.
///     Sound to re-home: the BEARER is only the source / cost-payer; the effect
///     bounces the CHOSEN target to ITS OWNER's hand (never the bearer's
///     controller's, never the exiled card). Only the OPEN "target creature" /
///     "target permanent" filters are reconstructed (a restricted filter like
///     "you control" / "an opponent controls" is skipped, consistent with the
///     pinger / tap-target restricted-target boundary). Not creature-only: any
///     permanent bearer can pay to bounce a chosen target.</item>
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
///   <item><b>Targeted keyword grant</b> —
///     <c>"{cost}: (Another) target creature gains &lt;keyword&gt; until end of
///     turn."</c> (the grant-OTHER sibling of the self-keyword grant — Heliod,
///     Sun-Crowned's "{1}{W}: Another target creature gains lifelink until end of
///     turn."). Rebuilt with a 1..1 <see cref="TargetRequest"/> whose resolution
///     registers a <see cref="GrantKeywordUntilEndOfTurnEffect"/> for the named
///     keyword against the CHOSEN TARGET creature's own
///     <see cref="Creature.ActiveEffects"/> (CR 613.1f Layer 6; CR 514.2 expiry).
///     The optional printed "Another" (CR 601.2c) is re-checked at resolve to
///     exclude the bearer. ONLY the closed <see cref="GrantableKeywords"/> set is
///     reconstructed; a parameterised / unknown keyword is skipped as unsound.
///     Sound to re-home onto any permanent bearer (the keyword lands on the chosen
///     creature, never the exiled card or the bearer).</item>
///   <item><b>Self-counter</b> —
///     <c>"{cost}: Put a/N +1/+1 counter(s) on this creature."</c> Rebuilt as a
///     no-target <see cref="ActivatedAbility"/> whose resolution adds the
///     +1/+1 counter(s) to the BEARER's own
///     <see cref="Majik.Core.Counters.CounterCollection"/> (CR 122.1 /
///     613.1f). Sound to re-home: the counter is placed on the bearer, never the
///     exiled card. (Especially apt for the Cauldron: the grown bearer is, by
///     construction, a creature you control with a +1/+1 counter.)</item>
///   <item><b>Targeted-counter</b> —
///     <c>"{cost}: Put a/N +1/+1 counter(s) on target creature."</c> The
///     to-TARGET counterpart of the self-counter shape (Hangarback Walker /
///     Walking Ballista / Ivy Lane Denizen-class counter sources). Rebuilt with a
///     1..1 <see cref="TargetRequest"/> whose resolution adds the +1/+1
///     counter(s) to the CHOSEN target creature's own
///     <see cref="Majik.Core.Counters.CounterCollection"/> (CR 122.1 / 613.1f).
///     Sound to re-home: the BEARER is only the source / cost-payer; the counter
///     lands on the chosen creature, never the exiled imprinted card and never
///     the bearer. Only the OPEN "target creature" filter is reconstructed
///     (consistent with the pump-other / tap-target restricted-target boundary);
///     not creature-only (any permanent bearer can pay to grow a chosen
///     creature).</item>
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
///   <item><b>Target-player draw</b> —
///     <c>"{cost}: Target player draws a card."</c> / <c>"… draws N cards."</c>
///     The TARGETED-player sibling of draw-a-card (Endbringer's
///     <c>"{C}, {T}: Target player draws a card."</c>; Reckoner Bankbuster's
///     charge-counter draw). Rebuilt with a 1..1 target-player
///     <see cref="TargetRequest"/>; resolution reads the chosen player off
///     <see cref="ActivatedAbility.ChosenTargets"/> and draws via
///     <see cref="Majik.Core.Primitives.Fx.DrawCards"/> (CR 121 / 613.1f) — the
///     chosen player draws from THEIR OWN library, so the BEARER is only the
///     source / cost-payer, exactly the way the pinger's "target player" leg
///     routes a chosen-player effect. Count parsed like draw-a-card; an
///     unrecognised count is skipped. Sound on any permanent bearer.</item>
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
///   <item><b>Scry-self</b> —
///     <c>"{cost}: Scry N."</c> Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose resolution scries over the BEARER's
///     CONTROLLER's own library (<see cref="Majik.Core.Primitives.Fx.Scry"/> /
///     <see cref="Majik.Core.Keywords.ScryAction"/>, CR 701.20 / 613.1f) — like
///     draw / gain-life one of the soundest re-homes: "scry N" has NO "this
///     creature" / source reference, so re-homing is a clean controller-scoped
///     operation. The agent's scry decision is read off the live
///     <see cref="ResolutionContext"/> (the async-bodied <see cref="Effect"/>
///     ctor + <c>ctx.Agent</c>) — the SAME way the declarative scry_self verb
///     resolves it — so the closure prompts the live agent rather than needing
///     a captured one (no-agent fallback sends every peeked card to the bottom).
///     Count is "a"/"one" ⇒ 1, the spelled-out "two"/"three", or a bare digit;
///     an unrecognised count is skipped. Sound on any permanent bearer, not just
///     creatures.</item>
///   <item><b>Surveil-self</b> —
///     <c>"{cost}: Surveil N."</c> Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose ResolutionContext-aware resolution
///     surveils the BEARER's CONTROLLER's own library (CR 701.42), consulting the
///     live agent off <see cref="ResolutionContext.Agent"/> (falling back to
///     <see cref="AgentRegistry"/>, then the all-to-graveyard default) — the EXACT
///     posture of the declarative <c>surveil_self</c> verb
///     (<see cref="Definitions.SurveilSelfEffectDef"/>). Sound to re-home onto any
///     permanent bearer: surveil references the controller's OWN library, with NO
///     "this creature" / source reference (Sinister Starfish, Rune-Sealed Wall).
///     The earlier "needs an agent decision the closure can't prompt for" note is
///     now stale — the resolution context carries the agent.</item>
///   <item><b>Mill-self</b> —
///     <c>"{cost}: Mill N."</c> Rebuilt as a no-target
///     <see cref="ActivatedAbility"/> whose resolution mills the BEARER's
///     CONTROLLER's own library via <see cref="Fx.Mill"/> (CR 701.13). No agent
///     decision (the cards move unconditionally), so the soundest of the two
///     library shapes. Count "a"/"one" ⇒ 1, "two"/"three", or a bare digit; an
///     unrecognised count is skipped. Sound on any permanent bearer (Excavated
///     Wall, Molt Tender, Skull Prophet).</item>
///   <item><b>Target-player mill</b> —
///     <c>"{cost}: Target player mills N."</c> The TARGETED-player sibling of
///     mill-self. Rebuilt with a 1..1 target-player
///     <see cref="TargetRequest"/>; resolution mills the CHOSEN player off
///     <see cref="ActivatedAbility.ChosenTargets"/> via <see cref="Fx.Mill"/> —
///     the chosen player mills from THEIR OWN library (CR 701.13), so the BEARER
///     is only the source / cost-payer, exactly the way the pinger's "target
///     player" leg routes a chosen-player effect.</item>
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
/// <see cref="AdditionalCost.Sacrifice"/>(bearer). A
/// <c>"Remove N +1/+1 counters from this creature"</c> leg becomes
/// <see cref="AdditionalCost.RemoveCounters"/>(bearer, +1/+1, N) — the
/// re-source-safe counter-removal additional cost (CR 118.3) that rebinds onto
/// the bearer via <see cref="AdditionalCost.RebindSource"/>, exactly like the
/// bespoke Etched Oracle. So <c>"{1}, Remove four +1/+1 counters from this
/// creature: Target player draws three cards."</c> is reconstructed (the
/// counter-removal cost riding the existing target-player-draw verb).
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
///     token makers, anthem grants, loyalty-style, bespoke one-offs). (Scry /
///     mill / surveil, listed here previously, ARE now reconstructed above: the
///     library-selection shapes read their agent decision off the live
///     <see cref="ResolutionContext"/> the same way the declarative
///     scry_self / surveil_self verbs resolve it — see the scry-self / surveil-self
///     / mill-self shapes above.) These are unbounded and not generally
///     reconstructable from oracle text without
///     per-card work — a correct partial beats a broken "all".</item>
/// </list>
/// </summary>
public static class OracleActivatedAbilityBinder
{
    // A cost token is one of:
    //   * {T} — the tap symbol;
    //   * a RUN of one-or-more concatenated mana pips we can model exactly —
    //     generic digits and/or W/U/B/R/G/C — written the way oracle text spells
    //     a multi-symbol cost ("{R}", "{2}", "{1}{U}", "{2}{R}");
    //   * a "Remove N +1/+1 counters from this creature" counter-removal token
    //     (CR 118.3) — a re-source-safe additional cost (rides
    //     AdditionalCost.RemoveCounters, which rebinds onto the bearer via
    //     RebindSource, exactly like the bespoke Etched Oracle). N is a/one ⇒ 1,
    //     a small spelled-out word, or a bare digit.
    // Anything else ({X}, {E}, {S}, Phyrexian {R/P}, "Pay N life", "Discard a
    // card", a counter type other than +1/+1, …) is intentionally NOT matched so
    // the clause is skipped as unsound. The cost is a ", "-separated list of
    // these (TryBuildCostList re-validates each token before folding). The
    // counter-removal token contains no comma, so the ", " split is safe.
    private const string RemoveCountersToken =
        @"Remove (?:a|one|two|three|four|five|\d+) \+1/\+1 counters? from this creature";
    private const string CostToken =
        @"(?:\{T\}|(?:\{(?:\d+|[WUBRGC])\})+|" + RemoveCountersToken + @")";
    private const string CostList = CostToken + @"(?:\s*,\s*" + CostToken + @")*";

    // Matches a single counter-removal cost token and captures its count word.
    private static readonly Regex RemoveCountersTokenRegex = new(
        @"^Remove (a|one|two|three|four|five|\d+) \+1/\+1 counters? from this creature$",
        RegexOptions.IgnoreCase);

    // Spelled-out small counts that appear on real "Remove N +1/+1 counters"
    // additional costs ("a"/"one" ⇒ 1). A bare digit is also accepted; an
    // unrecognised word makes the cost unsound and the clause is skipped.
    private static readonly IReadOnlyDictionary<string, int> CounterCountWords =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
        };

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

    // "{cost}: Target creature gets ±X/±Y until end of turn."
    // A TARGETED pump (pump-OTHER) — the activated sibling of firebreathing where
    // the buff lands on a CHOSEN creature rather than the source itself. A common
    // self-source combat-trick / on-demand-lord payoff on real creature cards.
    // Sound to re-home: the BEARER is only the source / cost-payer; the
    // PumpUntilEndOfTurnEffect (CR 613.1f Layer 7c) registers against the CHOSEN
    // TARGET creature's own ActiveEffects, never the exiled imprinted card. Like
    // the self-pump shape each delta carries its OWN sign, so a signed targeted
    // pump ("+2/-2") is reconstructed too. Only the OPEN "target creature" filter
    // is reconstructed (no restricted candidate filter like "target creature you
    // control"), consistent with the pinger / fight restricted-target boundary.
    private static readonly Regex PumpOtherRegex = new(
        @"^(" + CostList + @")\s*:\s*Target creature gets ([+-]\d+)/([+-]\d+) until end of turn\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature deals N damage to <target form>."
    private static readonly Regex PingerRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature deals (\d+) damage to (any target|target creature|target player)\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature deals N damage to target creature and each other
    // creature with the same name as that creature." (Izzet Staticaster's
    // spillover shape, CR 109.2 exact-name match / CR 707.2 copy-effect name.)
    // A targeted ping that SPILLS to every OTHER battlefield creature sharing the
    // chosen creature's EFFECTIVE name. Re-source-safe to reconstruct: the BEARER
    // is ONLY the source / cost-payer (its own {T} cost taps it); the damage lands
    // on the chosen creature + the same-name set read off the LIVE game at
    // resolution — never the exiled imprinted card. The same-name sweep reads
    // every player's battlefield off rc.Game (the re-homed source's live game),
    // so it scopes to the whole board the way the printed card does, with no
    // dependence on the source card's own identity. Group 1 = cost, group 2 = N.
    private static readonly Regex SameNameSpilloverPingerRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature deals (\d+) damage to target creature and each other creature with the same name as that creature\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature deals damage equal to its power to <target form>."
    // The POWER-pinger sibling of the fixed-amount pinger (Spikeshot Goblin's
    // "{R}, {T}: This creature deals damage equal to its power to any target.").
    // Re-source-safe to reconstruct, and the EXACT case that motivated the
    // re-sourceable representation: the damage amount must read the BEARER's
    // power at resolution (CR 608.2h — determined as the ability resolves), NOT
    // the exiled imprinted card's. The rebuilt effect reads
    // <see cref="Permanent.GetEffectivePower"/> off the BEARER source, so an
    // animated-land or pumped bearer scales the damage with ITS own power, never
    // the exiled card's printed power. Group 1 = cost, group 2 = target form.
    private static readonly Regex PowerPingerRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature deals damage equal to its power to (any target|target creature|target player)\.$",
        RegexOptions.IgnoreCase);

    // "Sacrifice this creature: It deals N damage to <target form>."
    private static readonly Regex SacPingerRegex = new(
        @"^Sacrifice this creature:\s*It deals (\d+) damage to (any target|target creature|target player)\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Tap target creature." / "{cost}: Untap target creature."
    // (CR 701.21a / 701.27a.) A self-source tapper / untapper is one of the most
    // common activated control shapes on real creature cards (Master Decoy,
    // Goldmeadow Harrier, Icatian Javelineers-style tappers; the untap leg covers
    // Pestermite-style "untap target" payoffs). Sound to re-home: the BEARER is
    // ONLY the source / cost-payer (its own {T} cost taps it); the effect taps /
    // untaps the CHOSEN target creature via Fx.Tap / Fx.Untap — the verb has NO
    // "this creature" / source reference at all, so re-homing is a clean tap of a
    // chosen permanent, never the exiled imprinted card. Only the OPEN "target
    // creature" filter is reconstructed (no restricted candidate filter), consistent
    // with the pinger / fight / pump-other restricted-target boundary. Group 2 is
    // the verb ("Tap" | "Untap").
    private static readonly Regex TapTargetRegex = new(
        @"^(" + CostList + @")\s*:\s*(Tap|Untap) target creature\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Return target creature to its owner's hand." /
    // "{cost}: Return target permanent to its owner's hand." (CR 701.20.)
    // A self-source targeted BOUNCE is a classic activated tempo shape on real
    // creature cards (Temporal Adept's "{U}{U}{U}, {T}: Return target permanent
    // to its owner's hand."; the bounce sibling of the tapper / pinger). Sound to
    // re-home: the BEARER is ONLY the source / cost-payer (its own {T}/mana cost);
    // the effect returns the CHOSEN target to ITS OWNER's hand via
    // Fx.BounceToHand — the verb has NO "this creature" / source reference at all
    // and bounces to the TARGET's owner (never the bearer's controller), so
    // re-homing is a clean controller-agnostic bounce of a chosen permanent, never
    // the exiled imprinted card. Only the OPEN "target creature" / "target
    // permanent" filters are reconstructed (no restricted candidate filter like
    // "target permanent you control" or "an opponent controls"), consistent with
    // the pinger / fight / tap-target restricted-target boundary. The bearer need
    // NOT be a creature (any permanent bearer can pay to bounce a chosen target),
    // so this does not gate on Creature. Group 2 = the target filter
    // ("creature" | "permanent").
    private static readonly Regex ReturnToHandTargetRegex = new(
        @"^(" + CostList + @")\s*:\s*Return target (creature|permanent) to its owner's hand\.$",
        RegexOptions.IgnoreCase);

    // The OPEN, source-independent card-type forms a graveyard-recursion ability
    // ("Return target <X> card from your graveyard to your hand.") may reference.
    // Each maps to an unambiguous card-type membership predicate over a card in a
    // graveyard, with NO dependence on the source card's identity, so the
    // candidate filter reconstructs soundly (CR 602.1c). The bare "card" form (no
    // type word) matches any card. A typed sub-filter ("Zombie card", "Arcane
    // card", "basic land card") that is NOT a plain card-type test is NOT matched
    // — its candidate filter isn't reconstructed here, consistent with the
    // restricted-target soundness boundary of the battlefield shapes. The leading
    // optional group captures the type word ("" for the bare "card" form).
    private const string GraveyardCardTypeForm =
        @"(?:(creature or planeswalker|artifact or enchantment|instant or sorcery"
        + @"|creature|artifact|enchantment|land|planeswalker|instant|sorcery|permanent) )?";

    // "{cost}: Return target <X> card from your graveyard to your hand." (CR 701.20.)
    // The graveyard->hand graveyard-recursion sibling of the battlefield->hand
    // ReturnToHandTargetRegex bounce. A self-source graveyard-recursion body is a
    // classic activated card-advantage shape on real creature cards (Dowsing
    // Shaman's "{2}{G}, {T}: Return target enchantment card from your graveyard to
    // your hand."; Lord of the Undead; Salvage Scout). Sound to re-home onto ANY
    // permanent bearer: the BEARER is ONLY the source / cost-payer (its own
    // {T}/mana cost); the "your graveyard" scope (CR 109.5 / 400.7 — "your" = the
    // ability's controller) reads the BEARER's CONTROLLER's graveyard, never the
    // exiled imprinted card's graveyard, and the chosen card returns to that
    // controller's hand via Fx.ReturnFromGraveyardToHand — never the exiled card.
    // Only the OPEN card-type forms (GraveyardCardTypeForm) are reconstructed; a
    // typed sub-filter ("Zombie card", "basic land card", "Arcane card") is
    // skipped as unsound, and a restricted cost token skips the clause. Group 1 =
    // cost, group 2 = the optional card-type word (empty for the bare "card" form).
    private static readonly Regex ReturnFromGraveyardToHandRegex = new(
        @"^(" + CostList + @")\s*:\s*Return target " + GraveyardCardTypeForm
        + @"card from your graveyard to your hand\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Target creature you control gains protection from the color of
    // your choice until end of turn." (CR 702.16 / 601.2c.) Mother of Runes'
    // (and Giver of Runes / any "protection from <chosen color>" body's) shape.
    // A self-source on-demand protection grant is a classic activated payoff on
    // real creature cards. Sound to re-home: the BEARER is ONLY the source /
    // cost-payer (its own {T} cost taps it); the ProtectionAbility lands on the
    // CHOSEN target creature via a self-sourced GrantAbilityEffect against the
    // target's OWN ActiveEffects (CR 613.1f Layer 6) — never the exiled imprinted
    // card and never the bearer itself. The "you control" candidate filter scopes
    // to the bearer's controller-side creatures (a RESTRICTED filter, but a sound
    // one to reconstruct here — unlike the open "target creature" forms, "creature
    // YOU control" is unambiguous: it is exactly the controller's battlefield
    // creatures, with no source-card dependence). The chosen colour is a CR 601.2c
    // "of your choice" decision made as the ability is put on the stack; the
    // deterministic binder path has no agent to prompt, so it defaults to white
    // (first WUBRG) — the SAME posture as MotherOfRunesFactory's WhitePicker
    // default (the agent colour prompt is a documented v1 gap shared by both
    // surfaces). The "color of your choice" wording is matched explicitly; a
    // fixed-colour protection grant ("protection from red") is NOT this shape and
    // is skipped (it would need its own reconstruction, and is not the Cauldron's
    // canonical re-home target).
    private static readonly Regex ProtectionGrantRegex = new(
        @"^(" + CostList + @")\s*:\s*Target creature you control gains protection from the colou?r of your choice until end of turn\.$",
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

    // "{cost}: Create a token that's a copy of (another) target nonlegendary
    // creature you control, except it has haste. Sacrifice it at the beginning of
    // the next end step." (CR 707.2 token copy / 702.10 haste / 603.7 delayed
    // sacrifice.) Kiki-Jiki, Mirror Breaker's "{T}: …" line and Reflection of
    // Kiki-Jiki's "{1}, {T}: …another target…" line — the canonical "make a
    // hasty copy of a creature you control" payoff. Re-source-safe to reconstruct:
    // the BEARER is ONLY the source / cost-payer (its own {T}/mana cost); the
    // token copies the CHOSEN target (a creature read off ChosenTargets) and
    // enters under the BEARER's CONTROLLER (read off the live source at resolve),
    // never the exiled imprinted card. The printed restrictions
    // (another / nonlegendary / you-control / still on the battlefield) gate at
    // RESOLVE (CR 608.2b), measured against the live bearer + its controller — so
    // the re-homed copy means "another nonlegendary creature the BEARER's
    // controller controls" (exactly the bespoke KikiJikiMirrorBreakerFactory's
    // re-home posture). The optional printed "another" (CR 601.2c "another" =
    // "different from the source") is captured so resolve can exclude the bearer.
    // The token gains haste (CR 702.10 / "except it has haste") even when the
    // original lacks it, and the spawn schedules a one-shot end-step sacrifice
    // (CR 603.7 / 701.16) via the ambient TriggerManagerRegistry — shape-only
    // paths with no live manager skip the sacrifice (the token still entered),
    // matching the bespoke factory's two-mode posture. Group 1 = cost, group 2 =
    // the optional "another " prefix.
    private static readonly Regex TokenCopyOfTargetRegex = new(
        @"^(" + CostList + @")\s*:\s*Create a token that's a copy of (another )?"
        + @"target nonlegendary creature you control, except it has haste\.\s*"
        + @"Sacrifice it at the beginning of the next end step\.$",
        RegexOptions.IgnoreCase);

    // The OPEN, source-independent permanent-target forms a "Destroy/Exile
    // target <X>." ability may reference. Each maps to an unambiguous card-type
    // membership predicate with NO dependence on the source card's identity, so
    // the candidate filter reconstructs soundly (CR 602.1c / 608.2b). RESTRICTED
    // forms ("target tapped creature", "target creature an opponent controls",
    // "another target creature", "target permanent you don't control") are NOT
    // matched — their candidate filter isn't reconstructed here, consistent with
    // the pinger / fight / tap-target open-target soundness boundary. Group is
    // captured so the builder can scope the TargetRequest's candidate gatherer.
    private const string PermanentTargetForm =
        @"(creature or planeswalker|artifact or enchantment|nonland permanent"
        + @"|creature|artifact|enchantment|land|planeswalker|permanent)";

    // "{cost}: Destroy target <X>." with an optional "It can't be regenerated."
    // rider (Avatar of Woe / Visara — "{T}: Destroy target creature. It can't be
    // regenerated."). CR 701.7 — a "destroy" effect: the destroy gates on
    // Indestructible (CR 702.12b) and consumes a regeneration shield (CR 701.15c)
    // unless the rider suppresses it (DestroyNoRegeneration). The verb has NO
    // "this creature" / source reference, so re-homing is a clean destroy of the
    // CHOSEN permanent, never the exiled imprinted card. Group 1 = cost, group 2 =
    // target form, group 3 = the optional no-regen rider (non-empty when present).
    private static readonly Regex DestroyTargetRegex = new(
        @"^(" + CostList + @")\s*:\s*Destroy target " + PermanentTargetForm
        + @"\.(\s*It can't be regenerated\.)?$",
        RegexOptions.IgnoreCase);

    // "{cost}: Exile target <X>." CR 701.20 — move the CHOSEN permanent to its
    // owner's exile zone. Like the destroy leg the verb is source-independent, so
    // re-homing exiles the chosen permanent, never the exiled imprinted card.
    // Group 1 = cost, group 2 = target form.
    private static readonly Regex ExileTargetRegex = new(
        @"^(" + CostList + @")\s*:\s*Exile target " + PermanentTargetForm + @"\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: This creature gains <keyword> until end of turn."
    private static readonly Regex SelfKeywordGrantRegex = new(
        @"^(" + CostList + @")\s*:\s*This creature gains (.+?) until end of turn\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: (Another) target creature gains <keyword> until end of turn."
    // A TARGETED keyword grant (grant-OTHER) — the keyword sibling of the
    // targeted-pump shape (PumpOtherRegex) where the keyword lands on a CHOSEN
    // creature rather than the source itself. Heliod, Sun-Crowned's activated
    // half ("{1}{W}: Another target creature gains lifelink until end of turn.")
    // is the canonical case, but the bare "Target creature gains <keyword> …"
    // form is the same shape (a self-source on-demand keyword payoff is a common
    // activated ability on real creature cards). Sound to re-home: the BEARER is
    // only the source / cost-payer; the GrantKeywordUntilEndOfTurnEffect
    // (CR 613.1f Layer 6; CR 514.2 expiry) registers against the CHOSEN TARGET
    // creature's own ActiveEffects, never the exiled imprinted card and never the
    // bearer. The optional printed "Another" (CR 601.2c "another" = "different
    // from the source") is matched and recorded so the rebuilt target request can
    // exclude the bearer at resolve — same posture as the rest of the engine's
    // printed-"another" predicates that gate at resolve. Only the closed
    // GrantableKeywords set is reconstructed; an unknown or parameterised keyword
    // is skipped as unsound (CR 613.1f — a granted ability must be modelled
    // exactly), consistent with the self-keyword-grant soundness boundary. Group 2
    // = the optional "Another " prefix, group 3 = the keyword.
    private static readonly Regex KeywordGrantOtherRegex = new(
        @"^(" + CostList + @")\s*:\s*(Another )?target creature gains (.+?) until end of turn\.$",
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

    // "{cost}: Put a/N +1/+1 counter(s) on target creature."
    // The to-TARGET counterpart of the self-counter shape (SelfCounterRegex):
    // instead of growing the source, a CHOSEN creature gets the +1/+1
    // counter(s). Hangarback Walker / Walking Ballista / Ivy Lane Denizen-class
    // "{cost}: Put a +1/+1 counter on target creature" sources, and the
    // canonical Cauldron re-home of any imprinted creature whose activated
    // ability adds a +1/+1 counter to a target. Sound to re-home: the BEARER is
    // ONLY the source / cost-payer (its own {T}/mana cost); the
    // CounterCollection.Add (CR 122.1 / 613.1f) lands on the CHOSEN target
    // creature's OWN counters — never the exiled imprinted card and never the
    // bearer. Only the OPEN "target creature" filter is reconstructed (no
    // restricted candidate filter like "target creature you control"),
    // consistent with the pinger / fight / tap-target / pump-other
    // restricted-target boundary. The bearer need NOT be a creature (any
    // permanent bearer can pay to grow a chosen creature), so this does not gate
    // on Creature the way self-counter does. Group 1 = cost, group 2 = explicit
    // count (absent ⇒ "a"/"one" ⇒ 1).
    private static readonly Regex CounterOtherRegex = new(
        @"^(" + CostList + @")\s*:\s*Put (?:a|one|(\d+)) \+1/\+1 counters? on target creature\.$",
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

    // "{cost}: Target player draws a card." / "Target player draws N cards."
    // (CR 121 / 115.) The TARGETED-player sibling of the self-source draw shape
    // (DrawCardsRegex): instead of the controller drawing, a CHOSEN player draws.
    // Endbringer's "{C}, {T}: Target player draws a card." is the canonical case;
    // Reckoner Bankbuster's charge-counter mode draws this way too. Sound to
    // re-home: a draw has NO "this creature" / source reference — the chosen
    // player draws from THEIR OWN library, never the exiled imprinted card — so
    // the BEARER is only the source / cost-payer (its own {T}/mana cost). Rebuilt
    // with a 1..1 target-player TargetRequest and resolution through
    // Fx.DrawCards on the CHOSEN player (read off ChosenTargets), exactly the way
    // PingerRegex's "target player" leg routes a chosen-player effect. The count
    // is "a"/"one" ⇒ 1, a spelled-out "two"/"three", or a bare digit (reusing
    // DrawCountWords); an unrecognised count word is skipped as unsound. Group 1 =
    // cost, group 2 = count token.
    private static readonly Regex TargetPlayerDrawRegex = new(
        @"^(" + CostList + @")\s*:\s*Target player draws (a|one|two|three|\d+) cards?\.$",
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

    // "{cost}: Scry N." (CR 701.20.) A self-source scry is a common activated
    // library-smoothing shape on real creature cards (a {T}: Scry payoff). Sound
    // to re-home: scry references the BEARER's CONTROLLER's OWN library
    // (Fx.Scry / ScryAction), NEVER the exiled imprinted card — there is no "this
    // creature" / source reference at all, so this is as sound a re-home as draw
    // / gain-life. The agent's scry decision is read off the live
    // ResolutionContext (Effect's async-body ctor + ctx.Agent), exactly the way
    // the declarative scry_self verb (CardDefRuntime.BuildScrySelfEffect) and the
    // OracleTriggeredAbilityBinder's "Scry N" rider resolve it — so re-homing
    // captures no source-card identity. The count is "a"/"one" ⇒ 1, a
    // spelled-out "two"/"three", or a bare digit; an unrecognised count word
    // makes the clause unsound and is skipped. Does NOT gate on Creature (a scry
    // is sound on any permanent bearer — the controller scries, not the
    // permanent). Group 1 = cost, group 2 = count token.
    private static readonly Regex ScrySelfRegex = new(
        @"^(" + CostList + @")\s*:\s*Scry (a|one|two|three|\d+)\.$",
        RegexOptions.IgnoreCase);

    // Spelled-out small counts that appear on real "Scry N" activated abilities
    // ("a"/"one" ⇒ 1). A bare digit is also accepted for larger amounts; an
    // unrecognised word makes the clause unsound and is skipped.
    private static readonly IReadOnlyDictionary<string, int> ScryCountWords =
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

    // "{cost}: Surveil N." (CR 701.42.) A self-source surveil — look at the top
    // N cards of YOUR library, then put any number into your graveyard and the
    // rest back on top in any order. One of the most common activated
    // graveyard-fuel / card-selection shapes on real creature cards (Sinister
    // Starfish, Rune-Sealed Wall, Coastal Bulwark's "{2}, {T}: Surveil 1."). Sound
    // to re-home onto ANY permanent bearer: surveil references the BEARER's
    // CONTROLLER's own library, never the exiled imprinted card — there is no
    // "this creature" / source reference at all, so re-homing is a clean
    // controller-scoped operation (CR 701.42 / 613.1f). The earlier soundness
    // boundary note ("surveil needs an agent decision the closure can't prompt
    // for") is now stale: the ResolutionContext-aware effect body reads the live
    // agent off rc.Agent (falling back to AgentRegistry, then the
    // all-to-graveyard default) exactly the way the declarative surveil_self verb
    // does (CardDefRuntime.BuildSurveilSelfEffect). N is always written as a digit
    // on real surveil text ("Surveil 1", "Surveil 2"). Group 2 = N.
    private static readonly Regex SurveilSelfRegex = new(
        @"^(" + CostList + @")\s*:\s*Surveil (\d+)\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Mill N." (CR 701.13.) A self-source mill — put the top N cards of
    // YOUR library into your graveyard. A common activated graveyard-fuel shape on
    // real creature cards (Excavated Wall "{1}, {T}: Mill a card.", Molt Tender /
    // Skull Prophet "{T}: Mill a/two card(s)."). Sound to re-home onto ANY
    // permanent bearer: mill references the BEARER's CONTROLLER's own library,
    // never the exiled imprinted card — no "this creature" / source reference at
    // all, so re-homing is a clean controller-scoped operation (CR 701.13 /
    // 613.1f). Unlike surveil it needs no agent decision (the cards move
    // unconditionally to the graveyard), so it is the soundest of the two. The
    // count is "a"/"one" ⇒ 1, a spelled-out "two"/"three", or a bare digit
    // (reusing MillCountWords); an unrecognised count is skipped. Group 2 = count.
    private static readonly Regex MillSelfRegex = new(
        @"^(" + CostList + @")\s*:\s*Mill (a|one|two|three|\d+) cards?\.$",
        RegexOptions.IgnoreCase);

    // "{cost}: Target player mills N." (CR 701.13.) The TARGETED-player sibling of
    // the self-mill shape: a CHOSEN player mills rather than the controller. A
    // common activated mill/disruption shape. Sound to re-home: mill references
    // the chosen player's OWN library (Fx.Mill on ChosenTargets), never the exiled
    // imprinted card, so the BEARER is only the source / cost-payer — exactly the
    // way PingerRegex's "target player" leg routes a chosen-player effect. Count
    // parsed like self-mill; an unrecognised count is skipped. Group 2 = count.
    private static readonly Regex TargetPlayerMillRegex = new(
        @"^(" + CostList + @")\s*:\s*Target player mills (a|one|two|three|\d+) cards?\.$",
        RegexOptions.IgnoreCase);

    // Spelled-out small counts that appear on real "Mill N cards" activated
    // abilities ("a"/"one" ⇒ 1). A bare digit is also accepted; an unrecognised
    // word makes the clause unsound and is skipped.
    private static readonly IReadOnlyDictionary<string, int> MillCountWords =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 1,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
        };

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

            var pumpOther = PumpOtherRegex.Match(line);
            if (pumpOther.Success)
            {
                var ability = TryBuildPumpOther(pumpOther, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var powerPing = PowerPingerRegex.Match(line);
            if (powerPing.Success)
            {
                var costs = TryBuildCostList(powerPing.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                result.Add(BuildPowerPinger(costs, powerPing.Groups[2].Value, bearer, controller));
                continue;
            }

            var spillover = SameNameSpilloverPingerRegex.Match(line);
            if (spillover.Success)
            {
                var costs = TryBuildCostList(spillover.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var amount = int.Parse(spillover.Groups[2].Value);
                result.Add(BuildSameNameSpilloverPinger(costs, amount, bearer, controller));
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

            var tokenCopy = TokenCopyOfTargetRegex.Match(line);
            if (tokenCopy.Success)
            {
                var costs = TryBuildCostList(tokenCopy.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var requireAnother = tokenCopy.Groups[2].Success
                    && tokenCopy.Groups[2].Value.Trim().Length > 0;
                result.Add(BuildTokenCopyOfTarget(costs, requireAnother, bearer, controller));
                continue;
            }

            var destroy = DestroyTargetRegex.Match(line);
            if (destroy.Success)
            {
                var costs = TryBuildCostList(destroy.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var noRegen = destroy.Groups[3].Success
                    && destroy.Groups[3].Value.Trim().Length > 0;
                result.Add(BuildDestroyOrExileTarget(
                    costs, exile: false, noRegen: noRegen,
                    destroy.Groups[2].Value, bearer, controller));
                continue;
            }

            var exile = ExileTargetRegex.Match(line);
            if (exile.Success)
            {
                var costs = TryBuildCostList(exile.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                result.Add(BuildDestroyOrExileTarget(
                    costs, exile: true, noRegen: false,
                    exile.Groups[2].Value, bearer, controller));
                continue;
            }

            var tapTarget = TapTargetRegex.Match(line);
            if (tapTarget.Success)
            {
                var costs = TryBuildCostList(tapTarget.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                var untap = string.Equals(
                    tapTarget.Groups[2].Value, "Untap", StringComparison.OrdinalIgnoreCase);
                result.Add(BuildTapTarget(costs, untap, bearer, controller));
                continue;
            }

            var returnToHand = ReturnToHandTargetRegex.Match(line);
            if (returnToHand.Success)
            {
                var costs = TryBuildCostList(returnToHand.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                result.Add(BuildReturnToHandTarget(
                    costs, returnToHand.Groups[2].Value, bearer, controller));
                continue;
            }

            var returnFromGy = ReturnFromGraveyardToHandRegex.Match(line);
            if (returnFromGy.Success)
            {
                var costs = TryBuildCostList(returnFromGy.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                result.Add(BuildReturnFromGraveyardToHand(
                    costs, returnFromGy.Groups[2].Value, bearer, controller));
                continue;
            }

            var protGrant = ProtectionGrantRegex.Match(line);
            if (protGrant.Success)
            {
                var costs = TryBuildCostList(protGrant.Groups[1].Value, bearer, controller);
                if (costs == null) continue; // unsound cost token — skip
                result.Add(BuildProtectionGrant(costs, bearer, controller));
                continue;
            }

            var kwGrant = SelfKeywordGrantRegex.Match(line);
            if (kwGrant.Success)
            {
                var ability = TryBuildSelfKeywordGrant(kwGrant, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var kwGrantOther = KeywordGrantOtherRegex.Match(line);
            if (kwGrantOther.Success)
            {
                var ability = TryBuildKeywordGrantOther(kwGrantOther, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var counterOther = CounterOtherRegex.Match(line);
            if (counterOther.Success)
            {
                var ability = TryBuildCounterOther(counterOther, bearer, controller);
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

            var targetPlayerDraw = TargetPlayerDrawRegex.Match(line);
            if (targetPlayerDraw.Success)
            {
                var ability = TryBuildTargetPlayerDraw(targetPlayerDraw, bearer, controller);
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

            var scry = ScrySelfRegex.Match(line);
            if (scry.Success)
            {
                var ability = TryBuildScrySelf(scry, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var surveil = SurveilSelfRegex.Match(line);
            if (surveil.Success)
            {
                var ability = TryBuildSurveilSelf(surveil, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var targetPlayerMill = TargetPlayerMillRegex.Match(line);
            if (targetPlayerMill.Success)
            {
                var ability = TryBuildTargetPlayerMill(targetPlayerMill, bearer, controller);
                if (ability != null) result.Add(ability);
                continue;
            }

            var mill = MillSelfRegex.Match(line);
            if (mill.Success)
            {
                var ability = TryBuildMillSelf(mill, bearer, controller);
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
    /// Build a targeted pump: "{cost}: Target creature gets ±X/±Y until end of
    /// turn." (the pump-OTHER sibling of firebreathing). Re-homed so the BEARER is
    /// only the source / cost-payer; the <see cref="PumpUntilEndOfTurnEffect"/>
    /// (CR 613.1f Layer 7c) registers against the CHOSEN target creature's own
    /// <see cref="Creature.ActiveEffects"/> — never the exiled imprinted card. The
    /// bearer need NOT be a creature (a non-creature bearer can still tap/pay to
    /// pump a chosen creature), so this does not gate on <see cref="Creature"/>
    /// the way self-pump does. Mirrors <see cref="BuildPinger"/>'s 1..1
    /// single-creature target request.
    /// </summary>
    private static ActivatedAbility? TryBuildPumpOther(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var p = int.Parse(match.Groups[2].Value);
        var t = int.Parse(match.Groups[3].Value);

        ActivatedAbility? ability = null;
        var pumpEffect = new Effect(
            $"Granted: target creature gets +{p}/+{t} until end of turn",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 613.1f Layer 7c — register against the CHOSEN target
                // creature's OWN effects service. A non-creature chosen target,
                // or one with no ActiveEffects (shape-only path), silently
                // no-ops — same posture as the self-pump rebuild. The bearer is
                // untouched; only the chosen creature is pumped.
                if (ability.ChosenTargets[0][0] is Creature chosen)
                {
                    chosen.ActiveEffects?.Register(
                        new PumpUntilEndOfTurnEffect(chosen, p, t));
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { pumpEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff | BotIntent.CombatTrick),
            });

        return ability;
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
    /// Build a targeted keyword grant: "{cost}: (Another) target creature gains
    /// &lt;keyword&gt; until end of turn." (the grant-OTHER sibling of the
    /// self-keyword grant — Heliod, Sun-Crowned's "{1}{W}: Another target creature
    /// gains lifelink until end of turn."). Re-homed so the BEARER is only the
    /// source / cost-payer; the <see cref="GrantKeywordUntilEndOfTurnEffect"/>
    /// (CR 613.1f Layer 6; CR 514.2 expiry) registers against the CHOSEN target
    /// creature's own <see cref="Creature.ActiveEffects"/> — never the exiled
    /// imprinted card and never the bearer. The bearer need NOT be a creature (a
    /// non-creature bearer can still pay to grant a keyword to a chosen creature),
    /// so this does not gate on <see cref="Creature"/> the way self-keyword-grant
    /// does. Only the closed <see cref="GrantableKeywords"/> set is reconstructed;
    /// an unknown or parameterised keyword is skipped (returns null). When the
    /// printed "Another" prefix is present, the chosen target is re-checked at
    /// resolve to exclude the bearer (CR 601.2c — "another" = different from the
    /// source). Mirrors <see cref="TryBuildPumpOther"/>'s 1..1 single-creature
    /// target request.
    /// </summary>
    private static ActivatedAbility? TryBuildKeywordGrantOther(
        Match match, Permanent bearer, Player controller)
    {
        var another = match.Groups[2].Success
            && match.Groups[2].Value.Trim().Length > 0;
        var rawKeyword = match.Groups[3].Value.Trim();
        if (!GrantableKeywords.TryGetValue(rawKeyword, out var keyword)) return null;

        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        ActivatedAbility? ability = null;
        var grantEffect = new Effect(
            $"Granted: target creature gains {keyword} until end of turn",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                if (ability.ChosenTargets[0][0] is not Creature chosen) return;
                // CR 608.2b — target must still be a battlefield creature.
                if (chosen.Zone != ZoneType.Battlefield) return;
                // CR 601.2c — printed "Another" excludes the source at resolve.
                if (another && ReferenceEquals(chosen, bearer)) return;

                // CR 613.1f Layer 6 / CR 514.2 — register against the CHOSEN
                // target creature's OWN effects service. A target with no
                // ActiveEffects (shape-only path) silently no-ops — same posture
                // as the self-keyword-grant rebuild. The bearer is untouched;
                // only the chosen creature gains the keyword.
                chosen.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(chosen, keyword));
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: another ? "another target creature" : "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        return ability;
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
    /// Build a targeted-counter ability: "{cost}: Put a/N +1/+1 counter(s) on
    /// target creature." (the to-TARGET counterpart of
    /// <see cref="TryBuildSelfCounter"/>). Re-homed so the BEARER is only the
    /// source / cost-payer; the +1/+1 counter(s) land on the CHOSEN target
    /// creature's own <see cref="Majik.Core.Counters.CounterCollection"/>
    /// (CR 122.1 / 613.1f) — never the exiled imprinted card and never the
    /// bearer. The bearer need NOT be a creature (a non-creature bearer can still
    /// tap/pay to grow a chosen creature), so this does not gate on
    /// <see cref="Creature"/> the way self-counter does. Mirrors
    /// <see cref="TryBuildPumpOther"/>'s 1..1 single-creature target request and
    /// resolve-time chosen-target read.
    /// </summary>
    private static ActivatedAbility? TryBuildCounterOther(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        // Group 2 is the explicit count ("Put 2 +1/+1 counters …"); absent for
        // the "Put a +1/+1 counter …" / "Put one …" forms ⇒ a single counter.
        var count = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;

        ActivatedAbility? ability = null;
        var counterEffect = new Effect(
            $"Granted: put {count} +1/+1 counter(s) on target creature",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                if (ability.ChosenTargets[0][0] is not Creature chosen) return;
                // CR 608.2b — target must still be a battlefield creature.
                if (chosen.Zone != ZoneType.Battlefield) return;

                // CR 122.1 / 613.1f — place the counter(s) directly on the CHOSEN
                // target creature's OWN counter collection. The bearer is
                // untouched; only the chosen creature is grown.
                chosen.Counters.Add(Counters.CounterType.PlusOnePlusOne, count);
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        return ability;
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
    /// Build a target-player-draw ability: "{cost}: Target player draws a/N
    /// card(s)." (Endbringer's "{C}, {T}: Target player draws a card."; Reckoner
    /// Bankbuster's charge-counter draw.) The TARGETED-player sibling of
    /// <see cref="TryBuildDrawCards"/>: a CHOSEN player draws rather than the
    /// controller. Re-homed so the BEARER is ONLY the source / cost-payer (its own
    /// {T}/mana cost); the draw resolves on the CHOSEN player (read off
    /// <see cref="ActivatedAbility.ChosenTargets"/>) via <see cref="Fx.DrawCards"/>
    /// — the chosen player draws from THEIR OWN library (CR 121 / 613.1f), never
    /// the exiled imprinted card. Does NOT gate on <see cref="Creature"/> (a draw
    /// is sound on any permanent bearer). Mirrors <see cref="BuildPinger"/>'s 1..1
    /// "target player" request. The count is "a"/"one" ⇒ 1, a spelled-out
    /// "two"/"three", or a bare digit; an unrecognised count word is skipped
    /// (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildTargetPlayerDraw(
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

        ActivatedAbility? ability = null;
        var drawEffect = new Effect(
            $"Granted: target player draws {count} card(s)",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 121 / 613.1f — the CHOSEN player draws from THEIR OWN
                // library. No source-card reference, so re-homing is trivially
                // sound. A non-player chosen target (shape-only path) no-ops.
                if (ability.ChosenTargets[0][0] is Player chosen)
                {
                    Fx.DrawCards(chosen, count);
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { drawEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Draw),
            });

        return ability;
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
    /// Build a scry-self ability: "{cost}: Scry N." (CR 701.20.) Re-homed so the
    /// scry is over the BEARER's CONTROLLER's own library (<see cref="Fx.Scry"/>
    /// / <see cref="Majik.Core.Keywords.ScryAction"/>, CR 613.1f) — never the
    /// exiled imprinted card. Like draw / gain-life this has no "this creature" /
    /// source reference, so re-homing is a clean controller-scoped operation; it
    /// does NOT gate on <see cref="Creature"/> (a scry is sound on any permanent
    /// bearer — the controller scries, not the permanent). The agent's scry
    /// decision is read off the live <see cref="ResolutionContext"/> via the
    /// async-bodied <see cref="Effect"/> ctor (exactly the declarative scry_self
    /// verb in <c>CardDefRuntime.BuildScrySelfEffect</c> and the
    /// <c>OracleTriggeredAbilityBinder</c>'s "Scry N" rider) — with the
    /// no-agent fallback sending every peeked card to the bottom. The count is
    /// "a"/"one" ⇒ 1, a spelled-out "two"/"three", or a bare digit; an
    /// unrecognised count word is skipped (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildScrySelf(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var countToken = match.Groups[2].Value.Trim();
        int count;
        if (!ScryCountWords.TryGetValue(countToken, out count)
            && !int.TryParse(countToken, out count))
        {
            return null; // unrecognised count — skip as unsound
        }
        if (count <= 0) return null;

        var scryEffect = new Effect(
            $"Granted: scry {count}",
            async ctx =>
            {
                // CR 701.20 / 613.1f — peek the BEARER's controller's own top N.
                // No source-card reference, so re-homing is trivially sound.
                var peeked = Majik.Core.Keywords.ScryAction.Peek(controller, count);
                if (peeked.Count == 0) return;

                // Prompt the live agent off the resolution context; fall back to
                // the registry, then to the deterministic all-to-bottom default
                // (the SAME posture as the declarative scry_self verb).
                var agent = ctx.Agent
                    ?? Majik.Core.Players.Agents.AgentRegistry.Get(controller);
                Majik.Core.Keywords.ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = await agent
                        .ChooseScryDecisionAsync(ctx.Game, peeked, ctx.Ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    decision = new Majik.Core.Keywords.ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                Fx.Scry(controller, peeked.Count, decision);
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { scryEffect });
    }

    /// <summary>
    /// Build a self-surveil ability: "{cost}: Surveil N." (CR 701.42.) Re-homed so
    /// the BEARER's CONTROLLER surveils their own library — never the exiled
    /// imprinted card. Does NOT gate on <see cref="Creature"/> (surveil is sound on
    /// any permanent bearer — the controller surveils, not the permanent). Uses the
    /// ResolutionContext-aware effect body so the live agent is read off
    /// <see cref="ResolutionContext.Agent"/> (falling back to
    /// <see cref="AgentRegistry"/>, then the all-to-graveyard default) — the EXACT
    /// agent-consultation posture of the declarative <c>surveil_self</c> verb
    /// (<see cref="Definitions.SurveilSelfEffectDef"/> /
    /// <c>CardDefRuntime.BuildSurveilSelfEffect</c>). The controller is read off
    /// <see cref="ResolutionContext.Controller"/> when the ability resolves through
    /// the live path, else the captured <paramref name="controller"/> (the legacy
    /// sync path). N is always a digit on real surveil text. A non-positive N is
    /// skipped (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildSurveilSelf(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var amount = int.Parse(match.Groups[2].Value);
        if (amount <= 0) return null;

        var surveilEffect = new Effect(
            $"Granted: surveil {amount}",
            async (ResolutionContext rc) =>
            {
                // CR 701.42 / 613.1f — the BEARER's controller surveils their own
                // library. Read the controller off the live context, else the
                // captured controller (legacy sync path).
                var surveiller = rc.Controller ?? controller;

                var peeked = Keywords.SurveilAction.Peek(surveiller, amount);
                if (peeked.Count == 0) return;

                // Prompt the live agent off the resolution context; fall back to
                // the registry, then the all-to-graveyard default — the SAME
                // posture as CardDefRuntime.BuildSurveilSelfEffect.
                var agent = rc.Agent ?? AgentRegistry.Get(surveiller);
                Keywords.SurveilAction.SurveilDecision decision;
                if (agent != null)
                {
                    decision = await agent
                        .ChooseSurveilDecisionAsync(rc.Game, peeked, rc.Ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    decision = new Keywords.SurveilAction.SurveilDecision(
                        ToGraveyard: peeked.ToList(),
                        TopOrder: Array.Empty<Cards.ICard>());
                }
                Keywords.SurveilAction.Apply(surveiller, amount, decision);
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { surveilEffect });
    }

    /// <summary>
    /// Build a self-mill ability: "{cost}: Mill N." (CR 701.13.) Re-homed so the
    /// BEARER's CONTROLLER mills their own library — never the exiled imprinted
    /// card. Does NOT gate on <see cref="Creature"/> (mill is sound on any
    /// permanent bearer — the controller mills, not the permanent). Unlike surveil
    /// it needs no agent decision (the cards move unconditionally), so the body
    /// reads only the controller (off <see cref="ResolutionContext.Controller"/> on
    /// the live path, else the captured <paramref name="controller"/>) and routes
    /// through <see cref="Fx.Mill"/>. The count is "a"/"one" ⇒ 1, a spelled-out
    /// "two"/"three", or a bare digit; an unrecognised count is skipped (returns
    /// null).
    /// </summary>
    private static ActivatedAbility? TryBuildMillSelf(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var countToken = match.Groups[2].Value.Trim();
        int count;
        if (!MillCountWords.TryGetValue(countToken, out count)
            && !int.TryParse(countToken, out count))
        {
            return null; // unrecognised count — skip as unsound
        }
        if (count <= 0) return null;

        var millEffect = new Effect(
            $"Granted: mill {count} card(s)",
            (ResolutionContext rc) =>
            {
                // CR 701.13 / 613.1f — the BEARER's controller mills their own
                // library. No agent decision, no source-card reference.
                Fx.Mill(rc.Controller ?? controller, count);
                return ValueTask.CompletedTask;
            });

        return new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { millEffect });
    }

    /// <summary>
    /// Build a target-player-mill ability: "{cost}: Target player mills N."
    /// (CR 701.13.) The TARGETED-player sibling of <see cref="TryBuildMillSelf"/>:
    /// a CHOSEN player mills rather than the controller. Re-homed so the BEARER is
    /// ONLY the source / cost-payer; the mill resolves on the CHOSEN player (read
    /// off <see cref="ActivatedAbility.ChosenTargets"/>) via <see cref="Fx.Mill"/>
    /// — the chosen player mills from THEIR OWN library, never the exiled imprinted
    /// card. Does NOT gate on <see cref="Creature"/>. Mirrors
    /// <see cref="BuildPinger"/>'s 1..1 "target player" request. The count is
    /// "a"/"one" ⇒ 1, a spelled-out "two"/"three", or a bare digit; an
    /// unrecognised count is skipped (returns null).
    /// </summary>
    private static ActivatedAbility? TryBuildTargetPlayerMill(
        Match match, Permanent bearer, Player controller)
    {
        var costs = TryBuildCostList(match.Groups[1].Value, bearer, controller);
        if (costs == null) return null; // unsound cost token — skip

        var countToken = match.Groups[2].Value.Trim();
        int count;
        if (!MillCountWords.TryGetValue(countToken, out count)
            && !int.TryParse(countToken, out count))
        {
            return null; // unrecognised count — skip as unsound
        }
        if (count <= 0) return null;

        ActivatedAbility? ability = null;
        var millEffect = new Effect(
            $"Granted: target player mills {count} card(s)",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.13 / 613.1f — the CHOSEN player mills their OWN library.
                // A non-player chosen target (shape-only path) no-ops.
                if (ability.ChosenTargets[0][0] is Player chosen)
                {
                    Fx.Mill(chosen, count);
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { millEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None),
            });

        return ability;
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
    /// Build a same-name-spillover pinger: "{cost}: This creature deals N damage
    /// to target creature and each other creature with the same name as that
    /// creature." (Izzet Staticaster, CR 109.2 / 707.2.) Re-homed so the SOURCE is
    /// the bearer (the cost taps it). The chosen target creature takes N damage,
    /// then EACH OTHER creature on ANY battlefield whose EFFECTIVE name
    /// (<see cref="Permanent.GetEffectiveName"/> — CR 707.2, so a copy of the
    /// target counts) equals the chosen target's effective name also takes N. The
    /// same-name set is read off the LIVE game (<see cref="ResolutionContext.Game"/>)
    /// at resolution, never the exiled imprinted card — re-homing is sound because
    /// the sweep depends only on the chosen target's name + the current board, with
    /// no reference to the source card's identity. The bearer is the damage source
    /// (<see cref="ResolutionContext.Source"/> when resolved through the ability
    /// path, else the captured bearer). When no live game is wired (shape-only
    /// path) only the chosen target takes damage — the same posture as the
    /// IzzetStaticasterFactory single-arg overload.
    /// </summary>
    private static ActivatedAbility BuildSameNameSpilloverPinger(
        List<ICost> costs,
        int amount,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var damageEffect = new Effect(
            $"Granted: this creature deals {amount} damage to target creature and each other creature with the same name",
            (ResolutionContext rc) =>
            {
                if (ability == null) return ValueTask.CompletedTask;
                if (ability.ChosenTargets.Count == 0) return ValueTask.CompletedTask;
                if (ability.ChosenTargets[0].Count == 0) return ValueTask.CompletedTask;

                if (ability.ChosenTargets[0][0] is not Creature target)
                    return ValueTask.CompletedTask;

                // CR 113.7 — the damage source is the re-homed bearer
                // (rc.Source on the live ability path, else the captured bearer).
                var source = (rc.Source ?? bearer) as Creature;

                // Primary target takes the ping (CR 119 / 306.7).
                Fx.DealDamageAny(target, amount, source);

                // CR 109.2 / 707.2 — "each other creature with the same name":
                // sweep every battlefield off the live game and ping any creature
                // (other than the primary target) whose EFFECTIVE name matches.
                // No live game (shape-only path) → only the primary target is hit.
                if (rc.Game is null) return ValueTask.CompletedTask;

                var targetName = target.GetEffectiveName();
                foreach (var player in rc.Game.AllPlayers)
                {
                    foreach (var card in player.Zones.Battlefield.GetCards())
                    {
                        if (card is not Creature other) continue;
                        if (ReferenceEquals(other, target)) continue;
                        if (other.GetEffectiveName() != targetName) continue;
                        Fx.DealDamageAny(other, amount, source);
                    }
                }

                return ValueTask.CompletedTask;
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { damageEffect },
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
    /// Build a POWER-pinger: "deals damage equal to its power to &lt;target
    /// form&gt;", re-homed so the SOURCE is the bearer. The damage amount is read
    /// off the BEARER's <see cref="Permanent.GetEffectivePower"/> AT RESOLUTION
    /// (CR 608.2h — the amount is determined as the ability resolves), so a
    /// pumped / animated / counter-grown bearer scales the damage with ITS own
    /// power — never the exiled imprinted card's printed power. This is the exact
    /// case that motivated the re-sourceable representation (Spikeshot Goblin):
    /// a "deal damage equal to its power" closure MUST read the bearer. A
    /// non-positive (0 or floored) power deals no damage. Creature-only — only a
    /// creature has a power to read, so a non-creature bearer returns the ability
    /// unguarded but its <see cref="Permanent.GetEffectivePower"/> is 0 (no
    /// damage); we still emit it because an animated-land bearer DOES have a
    /// power. Resolution funnels through <see cref="Fx.DealDamageAny"/>; the cost
    /// (mana/tap) already taps the bearer.
    /// </summary>
    private static ActivatedAbility BuildPowerPinger(
        List<ICost> costs,
        string targetForm,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var damageEffect = new Effect(
            $"Granted: this creature deals damage equal to its power to {targetForm}",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 608.2h — read the BEARER's power AT RESOLUTION (the
                // re-homed source), never the exiled imprinted card's. 0 (or
                // floored) power deals no damage.
                var amount = bearer.GetEffectivePower();
                if (amount <= 0) return;

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
    /// Build a token-copy-of-target ability:
    /// "{cost}: Create a token that's a copy of (another) target nonlegendary
    /// creature you control, except it has haste. Sacrifice it at the beginning of
    /// the next end step." (CR 707.2 / 702.10 / 603.7 / 701.16.) Kiki-Jiki, Mirror
    /// Breaker / Reflection of Kiki-Jiki's shape. Re-homed so the BEARER is ONLY
    /// the source / cost-payer: the token copies the CHOSEN target (read off the
    /// resolving <see cref="ResolutionContext"/>) and enters under the BEARER's
    /// CONTROLLER (read off the live <see cref="ResolutionContext.Source"/> /
    /// <see cref="ResolutionContext.Controller"/>, falling back to the captured
    /// bearer/controller on the legacy synchronous path) — never the exiled
    /// imprinted card. The printed restrictions are re-checked at RESOLVE
    /// (CR 608.2b): the chosen target must still be a battlefield, nonlegendary
    /// creature the live controller controls, and — when the printed
    /// <paramref name="requireAnother"/> "another" rider is present (CR 601.2c) —
    /// must not be the bearer itself. The token gains haste (CR 702.10 / "except it
    /// has haste") and clears summoning sickness (CR 702.10b); the spawn schedules
    /// a one-shot end-step sacrifice (CR 603.7 / 701.16) via the ambient
    /// <see cref="TriggerManagerRegistry"/> (shape-only paths with no live manager
    /// skip the sacrifice — the token still entered). The token enters via the
    /// ambient <see cref="ZoneServiceRegistry"/> when one is wired so token-ETB
    /// triggers (Impact Tremors / Soul Warden) fire, else via a raw zone add. The
    /// bearer need NOT be a creature (any permanent bearer can pay to mint the
    /// copy), so this does not gate on <see cref="Creature"/>. v1 lossy: copiable
    /// values are snapshotted at resolve (name, base P/T, subtypes, keyword names,
    /// colour) — the same posture as the bespoke
    /// <see cref="Factories.KikiJikiMirrorBreakerFactory"/> and Splinter Twin.
    /// </summary>
    private static ActivatedAbility BuildTokenCopyOfTarget(
        List<ICost> costs,
        bool requireAnother,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var description = requireAnother
            ? "Granted: create a haste token copy of another target nonlegendary creature you control, sacrifice it EOT"
            : "Granted: create a haste token copy of target nonlegendary creature you control, sacrifice it EOT";

        var copyEffect = new Effect(
            description,
            ctx =>
            {
                // Read the chosen target off the resolving context, falling back to
                // the ability's recorded ChosenTargets for the legacy sync path.
                var chosen = ctx.ChosenTargets.Count > 0
                    ? ctx.ChosenTargets
                    : (ability?.ChosenTargets
                        ?? (IReadOnlyList<IReadOnlyList<object>>)Array.Empty<IReadOnlyList<object>>());
                if (chosen.Count == 0 || chosen[0].Count == 0) return ValueTask.CompletedTask;
                if (chosen[0][0] is not Creature original) return ValueTask.CompletedTask;

                // The live source after an Agatha RebindTo (= the bearer); the
                // captured bearer/controller are the ctx-less Execute() fallback.
                var self = (ctx.Source as Permanent) ?? bearer;
                var liveController = ctx.Controller ?? self.Controller ?? controller;

                // CR 608.2b — resolve-time legality recheck, measured against the
                // live source + controller (so under Agatha the re-home means
                // "another nonlegendary creature the BEARER's controller controls").
                if (original.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;
                if (requireAnother && ReferenceEquals(original, self)) return ValueTask.CompletedTask;  // "another"
                if (original.HasSupertype(CardSupertype.Legendary)) return ValueTask.CompletedTask;     // "nonlegendary"
                if (!ReferenceEquals(original.Controller, liveController)) return ValueTask.CompletedTask; // "you control"

                // CR 706.2 — snapshot copiable values: name, base P/T, subtypes,
                // keyword names, colour identity. v1 lossy (does not track later
                // changes to the original — same posture as the bespoke factory).
                var keywords = new List<string>(
                    original.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword));
                if (!keywords.Contains("Haste")) keywords.Add("Haste"); // CR 702.10

                var colours = CardColors.GetColors(original).ToList();

                var spec = new TokenFactory.TokenSpec(
                    Name: original.Name,
                    Power: original.BasePower,
                    Toughness: original.BaseToughness,
                    Subtypes: original.Subtypes.ToList(),
                    Keywords: keywords,
                    Colors: colours);

                // CR 701.21 — prefer the ambient ZoneService (token-ETB triggers
                // fire) when wired; else TokenFactory falls back to a raw zone add.
                var zones = ZoneServiceRegistry.Get(liveController);
                var token = TokenFactory.CreateOnBattlefield(spec, liveController, zones);

                // CR 702.10b — haste lets the token act immediately.
                token.HasSummoningSickness = false;

                // CR 603.7 / 701.16 — schedule the one-shot end-step sacrifice of
                // the spawned token. The live game's TriggerManager comes from the
                // ambient registry (the binder has no triggers parameter). A
                // shape-only path (no manager) skips the sacrifice — the token
                // still entered, matching the bespoke factory's two-mode posture.
                var triggers = TriggerManagerRegistry.Get();
                if (triggers != null)
                {
                    var resolvedAt = LogicalClockScope.Current.NextTimestamp();
                    var sacEffect = new Effect(
                        "Granted: sacrifice the token copy at the next end step (CR 701.16)",
                        () =>
                        {
                            // CR 603.7 / 701.16 — only act on the token if it is
                            // still a battlefield permanent its controller controls.
                            if (token.Zone != ZoneType.Battlefield) return;
                            var owner = token.Controller ?? liveController;
                            if (!owner.Zones.Battlefield.GetCards().Contains(token)) return;
                            var sacZones = ZoneServiceRegistry.Get(owner);
                            if (sacZones != null)
                            {
                                sacZones.MoveCard(token, ZoneType.Battlefield, ZoneType.Graveyard, owner);
                            }
                            else
                            {
                                owner.Zones.Battlefield.RemoveCard(token);
                                token.SetZone(ZoneType.Graveyard);
                            }
                        });

                    var delayed = new DelayedTriggeredAbility(
                        source: self,
                        controller: liveController,
                        condition: new EventTriggerCondition<StepStartedEvent>(
                            (e, _) => e.StepType == StepStateType.End
                                      && e.Timestamp > resolvedAt),
                        effects: new IEffect[] { sacEffect });

                    triggers.RegisterDelayed(delayed);
                }

                return ValueTask.CompletedTask;
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { copyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: requireAnother
                        ? "another target nonlegendary creature you control"
                        : "target nonlegendary creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        return ability;
    }

    /// <summary>
    /// Build a tap-target ability: "{cost}: Tap target creature." (or the untap
    /// sibling, when <paramref name="untap"/> is true). Re-homed so the BEARER is
    /// ONLY the source / cost-payer; the effect taps/untaps the CHOSEN target
    /// creature via <see cref="Fx.Tap(Permanent, Player?)"/> /
    /// <see cref="Fx.Untap(Permanent)"/> (CR 701.21a / 701.27a) — never the exiled
    /// imprinted card, and never the bearer itself (the bearer is only tapped by
    /// its own {T} cost, not by the effect). The bearer need NOT be a creature (a
    /// non-creature bearer can still pay to tap a chosen creature), so this does
    /// not gate on <see cref="Creature"/>. Mirrors <see cref="BuildPinger"/>'s 1..1
    /// single-creature target request.
    /// </summary>
    private static ActivatedAbility BuildTapTarget(
        List<ICost> costs,
        bool untap,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var verb = untap ? "untap" : "tap";
        var tapEffect = new Effect(
            $"Granted: {verb} target creature",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.21a / 701.27a — (un)tap the CHOSEN permanent. A non-permanent
                // chosen target (shape-only path) silently no-ops. The bearer is
                // untouched by the effect; only the chosen creature is (un)tapped.
                if (ability.ChosenTargets[0][0] is Permanent chosen)
                {
                    if (untap) Fx.Untap(chosen);
                    else Fx.Tap(chosen, controller);
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Tapping a creature denies a blocker/attacker (a removal-like
                    // tempo play); untapping is a utility move with no clean intent.
                    Intent: untap ? BotIntent.None : BotIntent.Removal),
            });

        return ability;
    }

    /// <summary>
    /// Build a return-to-hand-target ability: "{cost}: Return target
    /// creature/permanent to its owner's hand." (CR 701.20.) Re-homed so the
    /// BEARER is ONLY the source / cost-payer; the effect returns the CHOSEN target
    /// to ITS OWNER's hand via <see cref="Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
    /// — never the exiled imprinted card, and (unlike a controller-scoped draw /
    /// lifegain) the bounce lands in the TARGET's owner's hand, never the bearer's
    /// controller's. The bearer need NOT be a creature (any permanent bearer can
    /// pay to bounce a chosen target), so this does not gate on
    /// <see cref="Creature"/>. CR 608.2b — at resolution the chosen object is
    /// re-checked: it must still be a battlefield permanent whose type still
    /// matches the printed filter ("creature" ⇒ still a creature), otherwise the
    /// ability fizzles cleanly — the SAME legality re-check the
    /// <see cref="BuildTapTarget"/> / pinger shapes use. Mirrors
    /// <see cref="BuildPinger"/>'s 1..1 single-target request.
    /// </summary>
    private static ActivatedAbility BuildReturnToHandTarget(
        List<ICost> costs,
        string targetFilter,
        Permanent bearer,
        Player controller)
    {
        var creatureOnly = string.Equals(
            targetFilter, "creature", StringComparison.OrdinalIgnoreCase);

        ActivatedAbility? ability = null;
        var bounceEffect = new Effect(
            $"Granted: return target {targetFilter} to its owner's hand",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.20 — bounce the CHOSEN permanent to ITS OWNER's hand. A
                // non-permanent chosen target (shape-only path) silently no-ops.
                // CR 608.2b — re-check legality at resolution: the chosen object
                // must still be a battlefield permanent that still matches the
                // printed filter ("creature" ⇒ still a creature), else fizzle.
                if (ability.ChosenTargets[0][0] is not Permanent chosen) return;
                if (chosen.Zone != ZoneType.Battlefield) return;
                if (creatureOnly && !chosen.HasType(CardType.Creature)) return;

                // The bounce lands in the TARGET's OWNER's hand, never the
                // bearer's controller's — Fx.BounceToHand reads chosen.Owner.
                Fx.BounceToHand(chosen);
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { bounceEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: creatureOnly ? "target creature" : "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Bouncing a permanent is a tempo / removal-like play.
                    Intent: BotIntent.Removal),
            });

        return ability;
    }

    /// <summary>
    /// Build a return-from-graveyard-to-hand ability: "{cost}: Return target
    /// &lt;X&gt; card from your graveyard to your hand." (CR 701.20.) The
    /// graveyard-recursion sibling of <see cref="BuildReturnToHandTarget"/>'s
    /// battlefield bounce. Re-homed so the BEARER is ONLY the source / cost-payer
    /// (its own {T}/mana cost); the "your graveyard" scope (CR 109.5 / 400.7 —
    /// "your" = the ability's controller) reads the BEARER's CONTROLLER's
    /// graveyard, never the exiled imprinted card's, and the chosen card returns to
    /// that controller's hand via
    /// <see cref="Fx.ReturnFromGraveyardToHand(ICard, Majik.Core.Services.ZoneService?)"/>
    /// — never the exiled imprinted card. The bearer need NOT be a creature (any
    /// permanent bearer can pay to recur a chosen card), so this does not gate on
    /// <see cref="Creature"/>. <paramref name="cardTypeForm"/> is the optional
    /// printed type word ("enchantment", "creature", "instant or sorcery", …; empty
    /// for the bare "card" form), mapped to a source-independent card-type
    /// predicate. CR 608.2b — at resolution the chosen object is re-checked: it
    /// must still be a card in the controller's graveyard whose type still matches
    /// the printed filter, otherwise the ability fizzles cleanly. The candidate
    /// gatherer scopes to the controller's own graveyard, filtered by the printed
    /// type — mirroring <see cref="BuildProtectionGrant"/>'s controller-scoped
    /// gatherer.
    /// </summary>
    private static ActivatedAbility BuildReturnFromGraveyardToHand(
        List<ICost> costs,
        string cardTypeForm,
        Permanent bearer,
        Player controller)
    {
        Func<ICard, bool> predicate = GraveyardCardFormPredicate(cardTypeForm);
        var description = string.IsNullOrEmpty(cardTypeForm)
            ? "target card in your graveyard"
            : "target " + cardTypeForm.ToLowerInvariant() + " card in your graveyard";

        ActivatedAbility? ability = null;
        var returnEffect = new Effect(
            $"Granted: return {description} to your hand",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.20 — return the CHOSEN card from the controller's
                // graveyard to that controller's hand. CR 608.2b — re-check
                // legality at resolution: the chosen object must still be a card
                // in the controller's graveyard that still matches the printed
                // filter, else fizzle (shape-only / illegal chosen target no-ops).
                if (ability.ChosenTargets[0][0] is not ICard chosen) return;
                if (chosen.Zone != ZoneType.Graveyard) return;
                if (!ReferenceEquals(chosen.Owner, controller)) return;
                if (!predicate(chosen)) return;

                // Fx.ReturnFromGraveyardToHand reads chosen.Owner — which is the
                // controller (we re-checked above), so the card returns to the
                // BEARER's controller's hand, never the exiled imprinted card.
                Fx.ReturnFromGraveyardToHand(chosen);
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { returnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: description,
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Recurring a card from the graveyard is card advantage.
                    Intent: BotIntent.Draw,
                    // CR 602.1c / 109.5 — "your graveyard": the candidates are
                    // exactly the BEARER's controller's graveyard cards matching
                    // the printed type (re-sourced to the controller, never the
                    // exiled imprinted card's graveyard).
                    CandidateGatherer: _ => controller.Zones.Graveyard.GetCards()
                        .Where(predicate)
                        .Cast<object>()
                        .ToList()),
            });

        return ability;
    }

    /// <summary>
    /// Map an OPEN graveyard-card-type form (the captured group of
    /// <see cref="ReturnFromGraveyardToHandRegex"/>) to a source-independent
    /// card-type membership predicate. The bare "" form (the printed "Return
    /// target card …") matches any card. Each typed form is an unambiguous type
    /// test (CR 602.1c) with no dependence on the source card's identity, so it
    /// re-homes soundly. Defensive default (an unrecognised form) matches any card.
    /// </summary>
    private static Func<ICard, bool> GraveyardCardFormPredicate(string cardTypeForm)
        => cardTypeForm.ToLowerInvariant() switch
        {
            "" => _ => true,
            "creature" => c => c.HasType(CardType.Creature),
            "artifact" => c => c.HasType(CardType.Artifact),
            "enchantment" => c => c.HasType(CardType.Enchantment),
            "land" => c => c.HasType(CardType.Land),
            "planeswalker" => c => c.HasType(CardType.Planeswalker),
            "instant" => c => c.HasType(CardType.Instant),
            "sorcery" => c => c.HasType(CardType.Sorcery),
            "permanent" => c => c.HasType(CardType.Creature) || c.HasType(CardType.Artifact)
                || c.HasType(CardType.Enchantment) || c.HasType(CardType.Land)
                || c.HasType(CardType.Planeswalker),
            "creature or planeswalker" =>
                c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker),
            "artifact or enchantment" =>
                c => c.HasType(CardType.Artifact) || c.HasType(CardType.Enchantment),
            "instant or sorcery" =>
                c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery),
            _ => _ => true,
        };

    /// <summary>
    /// Map an OPEN permanent-target form (the captured group of
    /// <see cref="DestroyTargetRegex"/> / <see cref="ExileTargetRegex"/>) to a
    /// source-independent card-type membership predicate. Each form is an
    /// unambiguous type test (CR 602.1c) with no dependence on the source card's
    /// identity, so it re-homes soundly. Returns null for an unrecognised form
    /// (defensive — the regex only emits the eight handled forms).
    /// </summary>
    private static Func<ICard, bool>? PermanentFormPredicate(string targetForm)
        => targetForm.ToLowerInvariant() switch
        {
            "creature" => c => c.HasType(CardType.Creature),
            "artifact" => c => c.HasType(CardType.Artifact),
            "enchantment" => c => c.HasType(CardType.Enchantment),
            "land" => c => c.HasType(CardType.Land),
            "planeswalker" => c => c.HasType(CardType.Planeswalker),
            "permanent" => _ => true,
            "nonland permanent" => c => !c.HasType(CardType.Land),
            "creature or planeswalker" =>
                c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker),
            "artifact or enchantment" =>
                c => c.HasType(CardType.Artifact) || c.HasType(CardType.Enchantment),
            _ => null,
        };

    /// <summary>
    /// Build a destroy-target (or exile-target, when <paramref name="exile"/> is
    /// true) ability: "{cost}: Destroy/Exile target &lt;X&gt;." Re-homed so the
    /// BEARER is ONLY the source / cost-payer; the verb has no "this creature" /
    /// source reference, so the effect destroys (CR 701.7) or exiles (CR 701.20)
    /// the CHOSEN target permanent via <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// / <see cref="Fx.MoveToExile(ICard)"/> — never the exiled imprinted card and
    /// never the bearer itself (the bearer is only tapped by its own {T} cost, not
    /// by the effect). A "destroy … It can't be regenerated." rider
    /// (<paramref name="noRegen"/>) uses <see cref="ZoneMoveReason.DestroyNoRegeneration"/>
    /// so a regeneration shield is NOT consumed (CR 701.15 suppressed; CR 702.12b
    /// Indestructible still applies). The bearer need NOT be a creature (a
    /// non-creature bearer can still pay to destroy/exile a chosen permanent), so
    /// this does not gate on <see cref="Creature"/>. Mirrors
    /// <see cref="BuildTapTarget"/>'s 1..1 single-permanent target request, scoped
    /// to the open type form via <see cref="PermanentFormPredicate"/>.
    /// </summary>
    private static ActivatedAbility BuildDestroyOrExileTarget(
        List<ICost> costs,
        bool exile,
        bool noRegen,
        string targetForm,
        Permanent bearer,
        Player controller)
    {
        Func<ICard, bool> predicate = PermanentFormPredicate(targetForm) ?? (_ => true);
        var verb = exile ? "exile" : "destroy";
        var description = "target " + targetForm.ToLowerInvariant();

        ActivatedAbility? ability = null;
        var effect = new Effect(
            $"Granted: {verb} {description}",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                // CR 701.7 / 701.20 — destroy / exile the CHOSEN permanent. A
                // non-permanent chosen target (shape-only path) silently no-ops.
                // CR 608.2b — the target must still be a battlefield permanent.
                if (ability.ChosenTargets[0][0] is not Permanent chosen) return;
                if (chosen.Zone != ZoneType.Battlefield) return;

                if (exile)
                {
                    Fx.MoveToExile(chosen);
                }
                else
                {
                    Fx.MoveToGraveyard(
                        chosen,
                        noRegen ? ZoneMoveReason.DestroyNoRegeneration : ZoneMoveReason.Destroy);
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { effect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: description,
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // Destroy / exile of a chosen permanent is removal.
                    Intent: BotIntent.Removal,
                    // CR 602.1c — the open type form's candidates are every
                    // battlefield permanent of the matching type on ANY player's
                    // battlefield (re-sourced to the live game's roster), never
                    // the exiled card. The gatherer reads every player's
                    // battlefield off the live context.
                    CandidateGatherer: ctx => GatherPermanents(ctx, controller, predicate)),
            });

        return ability;
    }

    /// <summary>
    /// Gather every battlefield permanent (on any player's battlefield) matching
    /// <paramref name="predicate"/>. Reads the live game roster off the
    /// <see cref="GameContext"/> when available so the candidate set scopes to the
    /// whole board the printed card targets; falls back to the
    /// <paramref name="controller"/>'s own battlefield on the context-less path.
    /// </summary>
    private static IReadOnlyList<object> GatherPermanents(
        Majik.Core.Game.GameContext? ctx, Player controller, Func<ICard, bool> predicate)
    {
        IEnumerable<Player> roster = ctx?.AllPlayers is { Count: > 0 } all
            ? all
            : new[] { controller };
        return roster
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .Where(predicate)
            .Cast<object>()
            .ToList();
    }

    // The deterministic default protection-from-<colour> quality used by the
    // re-homed grant. CR 601.2c "of your choice" is an agent decision made as the
    // ability is put on the stack; the binder path has no agent to prompt, so it
    // defaults to "white" (first WUBRG) — the SAME default as
    // MotherOfRunesFactory.WhitePicker. The agent colour prompt is a documented v1
    // gap shared by both the bespoke factory and this reconstructed shape.
    private const string DefaultProtectionColor = "white";

    /// <summary>
    /// Build a protection-grant ability: "{cost}: Target creature you control
    /// gains protection from the color of your choice until end of turn."
    /// (Mother of Runes / Giver of Runes shape, CR 702.16 / 601.2c.) Re-homed so
    /// the BEARER is ONLY the source / cost-payer; the
    /// <see cref="ProtectionAbility"/> lands on the CHOSEN target creature via a
    /// self-sourced <see cref="GrantAbilityEffect"/> (CR 613.1f Layer 6, EOT
    /// expiry per CR 514.2) registered against the TARGET's own
    /// <see cref="Creature.ActiveEffects"/> — never the exiled imprinted card and
    /// never the bearer. The bearer need NOT be a creature (a non-creature bearer
    /// can still pay to grant protection to a chosen creature), so this does not
    /// gate on <see cref="Creature"/>. The chosen colour defaults to white (see
    /// <see cref="DefaultProtectionColor"/>); a no-layers shape-only target (null
    /// <see cref="Creature.ActiveEffects"/>) attaches the marker directly so it is
    /// still inspectable — the SAME posture as
    /// <see cref="Factories.MotherOfRunesFactory.Resolve"/>. Mirrors
    /// <see cref="TryBuildPumpOther"/>'s 1..1 single-creature target request,
    /// scoped to the controller's own creatures ("creature you control").
    /// </summary>
    private static ActivatedAbility BuildProtectionGrant(
        List<ICost> costs,
        Permanent bearer,
        Player controller)
    {
        ActivatedAbility? ability = null;
        var grantEffect = new Effect(
            "Granted: target creature you control gains protection from the chosen colour EOT",
            () =>
            {
                if (ability == null) return;
                if (ability.ChosenTargets.Count == 0) return;
                if (ability.ChosenTargets[0].Count == 0) return;

                if (ability.ChosenTargets[0][0] is not Creature chosen) return;
                // CR 608.2b — target must still be a battlefield creature.
                if (chosen.Zone != ZoneType.Battlefield) return;

                var protection = new ProtectionAbility(DefaultProtectionColor);
                if (chosen.ActiveEffects is not null)
                {
                    // CR 613.1f Layer 6 / CR 514.2 — self-sourced grant on the
                    // CHOSEN target's own effects service so EOT cleanup runs
                    // through the continuous-effects layer. Source = the target
                    // itself (the grant lives while the target is on the
                    // battlefield), never the exiled imprinted card.
                    var grant = new GrantAbilityEffect(
                        source: chosen,
                        target: chosen,
                        ability: protection,
                        expiresAtEndOfTurn: true);
                    chosen.ActiveEffects.Register(grant);
                    // Sync immediately so target legality reads the grant on the
                    // same priority window (CR 117.5 / CR 700.2a).
                    grant.Sync();
                }
                else
                {
                    // No layers service wired (shape-only path): attach the
                    // ProtectionAbility directly so the marker is inspectable.
                    chosen.AddAbility(protection);
                }
            });

        ability = new ActivatedAbility(
            source: bearer,
            controller: controller,
            costs: costs,
            effects: new IEffect[] { grantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // CR 602.1 — "target creature you control": the candidates are
                    // exactly the BEARER's controller-side battlefield creatures
                    // (re-sourced to the controller, never the exiled card).
                    CandidateGatherer: _ => controller.Zones.Battlefield.GetCards()
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
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
        var counterRemovalSeen = false;

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

            var removeCounters = RemoveCountersTokenRegex.Match(token);
            if (removeCounters.Success)
            {
                // CR 118.3 / CR 707.2 — a "Remove N +1/+1 counters from this
                // creature" additional cost, re-homed onto the BEARER. A duplicate
                // counter-removal leg in one cost is not a real shape — reject it.
                if (counterRemovalSeen) return null;
                var countWord = removeCounters.Groups[1].Value.Trim();
                if (!CounterCountWords.TryGetValue(countWord, out var count)
                    && !int.TryParse(countWord, out count))
                {
                    return null; // unrecognised count — skip the clause as unsound
                }
                if (count <= 0) return null;

                // AdditionalCost.RemoveCounters is re-source-safe: RebindTo
                // re-homes it onto the new bearer via RebindSource, so the
                // counters come off the BEARER, never the exiled imprinted card.
                costs.Add(AdditionalCost.RemoveCounters(
                    bearer, CounterType.PlusOnePlusOne, count));
                counterRemovalSeen = true;
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
