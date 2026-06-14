using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// PLAN 01 (Slice F) — translates the free-form <c>TargetFilter</c> /
/// <c>target</c> strings carried by the JSON / DSL targeting effects
/// (<see cref="DealDamageEffectDef"/> / <see cref="DestroyTargetEffectDef"/> /
/// <see cref="UntapTargetEffectDef"/>) into a
/// <see cref="TargetRequest"/> for the shared
/// <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline.
///
/// <para>
/// Each request carries a 1..1 cardinality and a
/// <see cref="TargetRequest.CandidateGatherer"/> that enumerates the live
/// legal candidates against the resolving <see cref="GameContext"/> — exactly
/// what a hand-written factory supplies. The gatherer's predicate IS the
/// resolution-time legality the effect re-checks (CR 608.2b), so the agent is
/// only ever offered legal picks and an illegal pick fizzles cleanly.
/// </para>
/// </summary>
public static class TargetFilters
{
    /// <summary>
    /// Build the <see cref="TargetRequest"/> for the given filter string.
    /// <paramref name="verb"/> is woven into the request description only
    /// (e.g. "destroy", "untap", "deal 1 damage"). Unknown filters fall back
    /// to the broadest legal pool (any target) rather than throwing, so a new
    /// JSON card never hard-fails on an unrecognised filter — it simply
    /// targets widely (the resolution-time guard still enforces the printed
    /// rule for the verbs that gate on type).
    /// </summary>
    /// <param name="optional">When <c>true</c>, declares the request with
    /// <c>MinTargets: 0</c> — the "you MAY [verb] target …" shape (CR 603.3d /
    /// CR 115.1b). The agent may decline by choosing no target, in which case
    /// the dependent effect (and any linked "if you do" rider) does not happen.
    /// Default <c>false</c> = a mandatory single target.</param>
    public static TargetRequest ToTargetRequest(string? filter, string verb, BotIntent intent = BotIntent.None, bool optional = false)
    {
        var normalized = (filter ?? "").Trim().ToLowerInvariant();
        var (description, predicate) = Resolve(normalized, verb);
        // CR 109.5 — control-scoped creature filters ("you control" / "you
        // don't control", e.g. Prey Upon / Pounce fight targets) gate on the
        // resolving controller (ctx.Self), which the context-free predicate
        // cannot see. The gatherer applies the control rider on top of the
        // base creature predicate so the agent is only offered legal picks.
        // The resolution-time re-check (Matches) treats these as plain
        // "creature" — control is locked at announcement (CR 601.2c) and not
        // re-checked unless the printed effect says so.
        var controlScope = ControlScopeOf(normalized);
        return new TargetRequest(
            Description: description,
            MinTargets: optional ? 0 : 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: intent,
            CandidateGatherer: ctx => Gather(ctx, o => predicate(o) && MatchesControl(controlScope, o, ctx)));
    }

    private enum ControlScope { Any, YouControl, YouDontControl }

    private static ControlScope ControlScopeOf(string filter) => filter switch
    {
        "creature_you_control" => ControlScope.YouControl,
        // CR 109.5 — "creature and/or planeswalker you control" (Semester's
        // End's mixed-batch exile-with-return). The base predicate gates on a
        // battlefield creature OR planeswalker; the control rider is applied
        // context-aware here.
        "creature_or_planeswalker_you_control" => ControlScope.YouControl,
        "creature_you_dont_control" or "creature_you_don't_control" => ControlScope.YouDontControl,
        // CR 109.5 — "tapped creature an opponent controls" (Harbinger of the
        // Tides). The base predicate gates on a TAPPED battlefield creature;
        // the "an opponent controls" rider is applied context-aware here
        // (anyone but the resolving controller), since Resolve/Matches have no
        // resolving-player context. Reuses YouDontControl — every player other
        // than ctx.Self is an opponent in 1v1 (and the rider already means
        // "not controlled by you", the CR-defined opponent set for targeting).
        "tapped_creature_opponent_controls" => ControlScope.YouDontControl,
        // CR 109.5 / 205.3m — a "Wizard you control" is a battlefield creature
        // with the Wizard subtype under the resolving controller. Canonical
        // case: Riptide Laboratory — "{1}{U}, {T}: Return target Wizard you
        // control to its owner's hand." The base predicate gates on the Wizard
        // subtype; the control rider is applied context-aware here.
        "wizard_you_control" => ControlScope.YouControl,
        _ => ControlScope.Any,
    };

    private static bool MatchesControl(ControlScope scope, object o, GameContext ctx)
    {
        if (scope == ControlScope.Any) return true;
        if (o is not Permanent p) return false;
        var youControl = ReferenceEquals(p.Controller, ctx.Self);
        return scope == ControlScope.YouControl ? youControl : !youControl;
    }

    /// <summary>
    /// CR 608.2b — does <paramref name="target"/> still satisfy the
    /// <paramref name="filter"/> at resolution time? This is the SAME predicate
    /// the candidate gatherer used to offer legal picks, so a targeted effect
    /// (e.g. <see cref="ExileTargetEffectDef"/>) re-checks the full legality
    /// (type + zone + property), not merely battlefield presence — a target
    /// that has changed colour / type / zone since the ability went on the
    /// stack fizzles cleanly. Returns <c>false</c> for a <c>null</c> pick.
    /// </summary>
    public static bool Matches(string? filter, object? target)
    {
        if (target is null) return false;
        var normalized = (filter ?? "").Trim().ToLowerInvariant();
        var (_, predicate) = Resolve(normalized, "exile");
        return predicate(target);
    }

    private static (string Description, Func<object, bool> Predicate) Resolve(string filter, string verb) =>
        filter switch
        {
            "any" or "any_target" or "creature_or_player" =>
                ($"{verb} to any target", IsAnyTarget),
            // CR 120.3 — "any target that was dealt damage this turn"
            // (Needle Drop). A creature / planeswalker / player that has the
            // per-turn "was dealt damage" flag set. Both the candidate gatherer
            // and the CR 608.2b resolution re-check apply the flag, so a target
            // that loses the property (impossible mid-turn, but the cleanup
            // sweep clears it) fizzles cleanly.
            "any_target_dealt_damage_this_turn" =>
                ($"{verb} to any target that was dealt damage this turn",
                    o => IsAnyTarget(o) && WasDealtDamageThisTurn(o)),
            "player" =>
                ($"target player to {verb}", o => o is Player),
            "creature" =>
                ($"{verb} target creature", o => o is Creature c && OnBattlefield(c)),
            // CR 109.5 / 701.20 — "tapped creature an opponent controls"
            // (Harbinger of the Tides ETB). Base predicate: a TAPPED
            // battlefield creature. The "an opponent controls" rider is layered
            // on in the candidate gatherer (ToTargetRequest, control-scoped to
            // "you don't control") so the resolving controller can't pick their
            // own creature; the CR 608.2b resolution re-check applies the
            // tapped+creature predicate (control is locked at announcement,
            // CR 601.2c, and not re-checked).
            "tapped_creature_opponent_controls" =>
                ($"{verb} target tapped creature an opponent controls",
                    o => o is Creature c && OnBattlefield(c) && c.IsTapped),
            // CR 109.5 — control-scoped creature filters. The base predicate is
            // a battlefield creature; the "you control" / "you don't control"
            // rider is applied context-aware in the candidate gatherer
            // (ToTargetRequest), since Matches/Resolve have no resolving-player
            // context. Used by the fight family (Prey Upon, Pounce).
            "creature_you_control" =>
                ($"target creature you control to {verb}", o => o is Creature c && OnBattlefield(c)),
            // CR 109.5 — "creature and/or planeswalker you control" (Semester's
            // End). Base predicate: a battlefield creature OR planeswalker; the
            // "you control" rider is applied context-aware in the candidate
            // gatherer (ControlScopeOf above).
            "creature_or_planeswalker_you_control" =>
                ($"target creature and/or planeswalker you control to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && (p.HasType(CardType.Creature) || p.HasType(CardType.Planeswalker))),
            "creature_you_dont_control" or "creature_you_don't_control" =>
                ($"target creature you don't control to {verb}", o => o is Creature c && OnBattlefield(c)),
            // CR 205.3m — a "Wizard" is a creature with the Wizard subtype. The
            // "you control" rider is applied context-aware in the candidate
            // gatherer (ToTargetRequest). Canonical case: Riptide Laboratory.
            "wizard_you_control" =>
                ($"target Wizard you control to {verb}",
                    o => o is Creature c && OnBattlefield(c) && c.HasSubtype(CardSubtype.Wizard)),
            "permanent" =>
                ($"target permanent to {verb}", o => o is Permanent p && OnBattlefield(p)),
            "legendary_permanent" =>
                ($"target legendary permanent to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasSupertype(CardSupertype.Legendary)),
            "legendary_creature" =>
                ($"target legendary creature to {verb}",
                    o => o is Creature c && OnBattlefield(c) && c.HasSupertype(CardSupertype.Legendary)),
            "nonbasic_land" =>
                ($"target nonbasic land to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && p.HasType(CardType.Land) && !p.HasSupertype(CardSupertype.Basic)),
            "nonland_permanent" =>
                ($"target nonland permanent to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && !p.HasType(CardType.Land)),
            "artifact_enchantment_nonbasic_land" =>
                ($"target artifact, enchantment, or nonbasic land to {verb}",
                    IsArtifactEnchantmentOrNonbasicLand),
            "artifact" =>
                ($"target artifact to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasType(CardType.Artifact)),
            "artifact_or_enchantment" =>
                ($"target artifact or enchantment to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && (p.HasType(CardType.Artifact) || p.HasType(CardType.Enchantment))),
            // CR 109.5 — Haywire Mite's "noncreature artifact or noncreature
            // enchantment": an artifact OR enchantment that is NOT also a
            // creature (an artifact creature / enchantment creature is
            // excluded). Both the gatherer and the CR 608.2b resolution
            // re-check apply the !Creature gate.
            "noncreature_artifact_or_enchantment" =>
                ($"target noncreature artifact or noncreature enchantment to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && !p.HasType(CardType.Creature)
                         && (p.HasType(CardType.Artifact) || p.HasType(CardType.Enchantment))),
            "enchantment" =>
                ($"target enchantment to {verb}",
                    o => o is Permanent p && OnBattlefield(p) && p.HasType(CardType.Enchantment)),
            // CR 109.5 — Get Lost's "creature, enchantment, or planeswalker"
            // (any one of the three types). Both the candidate gatherer and the
            // CR 608.2b resolution re-check apply the OR predicate.
            "creature_enchantment_or_planeswalker" =>
                ($"target creature, enchantment, or planeswalker to {verb}",
                    o => o is Permanent p && OnBattlefield(p)
                         && (p.HasType(CardType.Creature)
                             || p.HasType(CardType.Enchantment)
                             || p.HasType(CardType.Planeswalker))),
            // Conditional battlefield filters — the description is verbatim so
            // converted factories keep their printed-text TargetRequest wording.
            // The predicate also gates resolution (CR 608.2b) via Matches, so a
            // target that no longer satisfies the condition fizzles cleanly.
            "black_or_red_permanent" =>
                ("target black or red permanent",
                    o => o is Permanent p && OnBattlefield(p)
                         && (CardColors.GetColors(p).Contains(ManaColor.Black)
                             || CardColors.GetColors(p).Contains(ManaColor.Red))),
            "permanent_mana_value_ge_4" =>
                ("target permanent with mana value 4 or greater",
                    o => o is Permanent p && OnBattlefield(p)
                         && ManaValueOf(p) >= 4),
            "creature_toughness_ge_4" =>
                ("target creature with toughness 4 or greater",
                    o => o is Creature c && OnBattlefield(c) && c.Toughness >= 4),
            // Graveyard-zone targets (CR 406 / 701.21 — "exile target card from
            // a graveyard"). The predicate gates on Graveyard zone, so the same
            // verb that exiles a battlefield permanent also exiles a graveyard
            // card; the gatherer scans graveyards via Gather's graveyard pass.
            "card_in_graveyard" or "card_in_target_graveyard" =>
                ($"target card in a graveyard to {verb}",
                    o => o is ICard c && InGraveyard(c)),
            "creature_card_in_graveyard" =>
                ($"target creature card in a graveyard to {verb}",
                    o => o is ICard c && InGraveyard(c) && c.HasType(CardType.Creature)),
            // Unknown filter — fall back to any target (broadest legal pool).
            _ => ($"{verb} target ({filter})", IsAnyTarget),
        };

    /// <summary>
    /// CR 115.3 — "any target" = a creature, a player, a planeswalker, or a
    /// battle. Battles are not modelled, so this is creature / player /
    /// planeswalker.
    /// </summary>
    private static bool IsAnyTarget(object o) => o switch
    {
        Player => true,
        Planeswalker pw => OnBattlefield(pw),
        Creature c => OnBattlefield(c),
        _ => false,
    };

    /// <summary>
    /// CR 120.3 — has this object been dealt damage during the current turn?
    /// Reads <see cref="Player.WasDealtDamageThisTurn"/> for players and
    /// <see cref="Permanent.WasDealtDamageThisTurn"/> for permanents (creatures
    /// / planeswalkers). Anything else is never "dealt damage". Exposed so
    /// hand-written factories (Needle Drop) reuse the same predicate the JSON /
    /// DSL filter uses.
    /// </summary>
    public static bool WasDealtDamageThisTurn(object o) => o switch
    {
        Player p => p.WasDealtDamageThisTurn,
        Permanent perm => perm.WasDealtDamageThisTurn,
        _ => false,
    };

    private static bool IsArtifactEnchantmentOrNonbasicLand(object o)
    {
        if (o is not Permanent p || !OnBattlefield(p)) return false;
        if (p.HasType(CardType.Artifact)) return true;
        if (p.HasType(CardType.Enchantment)) return true;
        return p.HasType(CardType.Land) && !p.HasSupertype(CardSupertype.Basic);
    }

    /// <summary>
    /// CR 701.5 — build the 1..1 "counter target [type] spell" request. Unlike
    /// the battlefield/graveyard filters, the candidate pool is the live STACK:
    /// the gatherer scans <see cref="GameContext.Stack"/> for every
    /// <see cref="Majik.Core.Spells.ISpell"/> that matches the optional type
    /// rider (a spell can only target ANOTHER object on the stack — the
    /// resolving counterspell is itself the top object and is excluded). The
    /// same <see cref="SpellMatches"/> predicate gates the CR 608.2b resolution
    /// re-check, so a target that has left the stack or changed type fizzles
    /// cleanly.
    /// </summary>
    public static TargetRequest SpellOnStackRequest(bool noncreature, bool creature)
    {
        var qualifier = noncreature ? "noncreature " : creature ? "creature " : "";
        return new TargetRequest(
            Description: $"counter target {qualifier}spell",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Counter,
            CandidateGatherer: ctx => GatherSpellsOnStack(ctx, noncreature, creature));
    }

    private static IReadOnlyList<object> GatherSpellsOnStack(
        GameContext ctx, bool noncreature, bool creature)
    {
        var result = new List<object>();
        foreach (var obj in ctx.Stack.GetAll())
        {
            if (obj is Majik.Core.Spells.ISpell spell && SpellMatches(noncreature, creature, spell))
            {
                result.Add(spell);
            }
        }
        return result;
    }

    /// <summary>
    /// CR 707.10 — build the 1..1 "target instant or sorcery spell" request
    /// for the spell-copy family (Twincast / Reverberate). Like
    /// <see cref="SpellOnStackRequest"/> the candidate pool is the live STACK,
    /// filtered to instant / sorcery spells (the only legal copy targets for
    /// these cards). <see cref="InstantOrSorcerySpellMatches"/> double-gates the
    /// CR 608.2b resolution re-check, so a target that has left the stack or is
    /// no longer an instant/sorcery fizzles cleanly.
    /// </summary>
    public static TargetRequest InstantOrSorcerySpellOnStackRequest() =>
        new TargetRequest(
            Description: "target instant or sorcery spell",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Buff,
            CandidateGatherer: GatherInstantOrSorcerySpellsOnStack);

    private static IReadOnlyList<object> GatherInstantOrSorcerySpellsOnStack(GameContext ctx)
    {
        var result = new List<object>();
        foreach (var obj in ctx.Stack.GetAll())
        {
            if (obj is Majik.Core.Spells.ISpell spell && InstantOrSorcerySpellMatches(spell))
            {
                result.Add(spell);
            }
        }
        return result;
    }

    /// <summary>
    /// CR 608.2b — does <paramref name="target"/> still satisfy "instant or
    /// sorcery spell" at resolution? A non-spell (the chosen object left the
    /// stack) or a non-instant/sorcery spell never matches.
    /// </summary>
    public static bool InstantOrSorcerySpellMatches(object? target)
    {
        if (target is not Majik.Core.Spells.ISpell spell) return false;
        return spell.Card.HasType(CardType.Instant)
            || spell.Card.HasType(CardType.Sorcery);
    }

    /// <summary>
    /// CR 608.2b — does <paramref name="target"/> still satisfy the counter
    /// verb's type rider at resolution? A non-spell (the chosen object left the
    /// stack and is no longer an <see cref="Majik.Core.Spells.ISpell"/>) never
    /// matches. The <paramref name="noncreature"/> / <paramref name="creature"/>
    /// gates mirror the bespoke Negate / Essence Scatter resolution checks.
    /// </summary>
    public static bool SpellMatches(bool noncreature, bool creature, object? target)
    {
        if (target is not Majik.Core.Spells.ISpell spell) return false;
        var isCreature = spell.Card.HasType(CardType.Creature);
        if (noncreature && isCreature) return false;
        if (creature && !isCreature) return false;
        return true;
    }

    /// <summary>
    /// CR 109.5 / 603.3 — build the lazy candidate gatherer for the
    /// exile-until-leaves removal cluster (<c>exile_until_leaves</c>: Oblivion
    /// Ring / Banishing Light / Cast Out / Leyline Binding / Glass Casket /
    /// Portable Hole / Detention Sphere). The gatherer enumerates the live legal
    /// ETB targets against the resolving <see cref="GameContext"/> so the shared
    /// targeting pipeline (<see cref="Majik.Core.Targeting.TargetCollection"/>)
    /// can prompt the controller's agent — WITHOUT it the live trigger drain
    /// offers an empty pool and the ETB silently fizzles in the prod path.
    ///
    /// <para>The composed predicate IS the printed restriction set, layered on
    /// top of the base <paramref name="filter"/>:</para>
    /// <list type="bullet">
    ///   <item><paramref name="opponentControlsOnly"/> — "an opponent controls"
    ///   (CR 109.5): only permanents NOT controlled by the resolving controller
    ///   (<see cref="GameContext.Self"/>).</item>
    ///   <item><paramref name="maxManaValue"/> — "with mana value N or less"
    ///   (CR 202.3): mana-value cap (Glass Casket = 3, Portable Hole = 2).</item>
    /// </list>
    /// "another" (<c>excludeSelf</c> — Oblivion Ring) and the same-name sweep are
    /// NOT folded in here: they need the live source-card reference, which this
    /// context-free gatherer lacks. The runtime's resolution-time legality
    /// re-check (CR 608.2b) still enforces them, so an over-offered self pick
    /// fizzles cleanly. The mana-value cap and opponent rider — which the
    /// resolving context CAN see — are applied so the agent is offered the right
    /// pool for the common removal cases.
    /// </summary>
    public static Func<GameContext, IReadOnlyList<object>> ExileUntilLeavesCandidates(
        string? filter, bool opponentControlsOnly, int? maxManaValue)
    {
        var normalized = (filter ?? "").Trim().ToLowerInvariant();
        var (_, predicate) = Resolve(normalized, "exile");
        return ctx => Gather(ctx, o =>
        {
            if (!predicate(o)) return false;
            if (opponentControlsOnly
                && o is Permanent p
                && ReferenceEquals(p.Controller, ctx.Self))
            {
                return false;
            }
            if (maxManaValue is int cap && o is Card mv
                && ManaValueOf(mv) > cap)
            {
                return false;
            }
            return true;
        });
    }

    private static bool OnBattlefield(ICard card) => card.Zone == ZoneType.Battlefield;

    /// <summary>
    /// CR 202.3b / 707.2 — the mana value to compare a card against. For a
    /// battlefield <see cref="Permanent"/> this routes through
    /// <see cref="Permanent.GetEffectiveManaValue"/> so a clone is measured by
    /// its COPIED identity (a Spark Double of a 6-mana creature reads mana value
    /// 6, not its printed {3}{U}); off the battlefield no copy effect can be
    /// active, so the printed <see cref="Card.ManaCostValue"/> total is used.
    /// </summary>
    private static int ManaValueOf(Card card) =>
        card is Permanent p && p.Zone == ZoneType.Battlefield
            ? p.GetEffectiveManaValue()
            : card.ManaCostValue.TotalValue;

    private static bool InGraveyard(ICard card) => card.Zone == ZoneType.Graveyard;

    /// <summary>
    /// Enumerate the live legal candidates from the resolving context:
    /// every player plus every battlefield permanent AND every graveyard card
    /// across all players, filtered by <paramref name="predicate"/>. The
    /// graveyard pass lets graveyard-zone filters (<c>card_in_graveyard</c> /
    /// <c>creature_card_in_graveyard</c>) be offered the right pool while the
    /// predicate's own zone gate keeps battlefield filters from picking up
    /// graveyard cards and vice-versa.
    /// </summary>
    private static IReadOnlyList<object> Gather(GameContext ctx, Func<object, bool> predicate)
    {
        var result = new List<object>();
        foreach (var player in ctx.AllPlayers)
        {
            if (predicate(player)) result.Add(player);

            var battlefield = player.Zones?.Battlefield;
            if (battlefield != null)
            {
                foreach (var card in battlefield.GetCards())
                {
                    if (predicate(card)) result.Add(card);
                }
            }

            var graveyard = player.Zones?.Graveyard;
            if (graveyard != null)
            {
                foreach (var card in graveyard.GetCards())
                {
                    if (predicate(card)) result.Add(card);
                }
            }
        }
        return result;
    }
}
