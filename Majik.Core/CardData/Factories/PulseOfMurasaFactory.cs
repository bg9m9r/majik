using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pulse of Murasa (Battle for Zendikar, {2}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Return target creature or land card from a graveyard to its owner's
///    hand. You gain 6 life."
///
/// ## Why it gets its own factory
/// Combines two primitives that already ship:
/// - The graveyard-return effect from <see cref="BalaGedRecoveryFactory"/> /
///   <see cref="EternalWitnessFactory"/> ("return target card from a
///   graveyard to its owner's hand"), here filtered to creature-or-land
///   cards (CR 700.6 — the printed "creature or land card" restriction) and
///   sourced from <b>any</b> graveyard (not just the controller's), returning
///   the card to <b>its owner's</b> hand (CR 109.4 / 400.3).
/// - A flat life gain of 6 (CR 119.3), unconditional — same shape as the
///   guarded gain in <see cref="TimelyReinforcementsFactory"/> but with no
///   condition.
///
/// No new engine mechanic is required: the target shape mirrors
/// <see cref="ClingToDustFactory"/>'s "target card in a graveyard" request
/// (any graveyard, resolver-driven), the return-to-hand resolution mirrors
/// <see cref="BalaGedRecoveryFactory.ResolveReturnToHand"/>, and the lifegain
/// is a direct <see cref="Player.GainLife(int)"/> call.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{G}, green. Card shape comes from the
///   embedded JSON (<c>pulse-of-murasa.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - One 1..1 "target creature or land card in a graveyard" request. The
///   candidate pool is the union of every supplied player's graveyard,
///   pre-filtered to creature/land cards (CR 700.6). Production callers
///   refresh candidates / the chosen target at cast time via the agent
///   prompt (same posture as Bala Ged Recovery / Cling to Dust).
/// - <b>Return clause (CR 608.2 / 400.3)</b>: on resolution the chosen card
///   is moved Graveyard → its OWNER's Hand (CR 109.4 — a card's owner is the
///   player it started the game under; cards return to the owner's hand, not
///   the caster's). Routed through <see cref="ZoneService.MoveCard"/> when
///   supplied so zone-change triggers fire (CR 603.6a / CR 701.20); otherwise
///   direct-zone mutation. An illegal-on-resolution target — no longer in a
///   graveyard, or no longer a creature/land card — is a clean no-op
///   (CR 608.2b).
/// - <b>Life clause (CR 119.3)</b>: "You gain 6 life." Unconditional; the
///   caster gains 6 life on resolution regardless of whether the return
///   happened (the printed text does not gate the lifegain on the return).
///
/// ## Rules citations
/// - CR 608.2 / 608.2b — one-shot spell resolution + illegal-target check.
/// - CR 700.6 — "target creature or land card" type restriction on the target.
/// - CR 109.4 / 400.3 — a card returns to ITS OWNER's hand, not the caster's.
/// - CR 119.3 — gaining life.
/// - CR 603.6a / 701.20 — zone-change events route through ZoneService.
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire the
///   chosen target from an agent prompt before the spell resolves; the
///   first-candidate fallback is the dispatcher-path safety net (same posture
///   as Bala Ged Recovery / Eternal Witness).
/// - <b>Live player provider</b>: like the other resolve-time multi-graveyard
///   factories, the searchable player set (whose graveyards are scanned) is
///   passed in explicitly rather than read off a live game accessor inside the
///   closure.
/// </summary>
[CardName("Pulse of Murasa")]
public static class PulseOfMurasaFactory
{
    public const string CardName = "Pulse of Murasa";
    public const string Slug = "pulse-of-murasa";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>CR 119.3 — "You gain 6 life."</summary>
    public const int LifeGain = 6;

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Return target creature or land card from a graveyard to its owner's " +
        "hand. You gain 6 life.";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// CR 700.6 — Pulse of Murasa can only target a creature card or a land
    /// card in a graveyard. True when <paramref name="card"/> is in a
    /// graveyard and is a creature or land card.
    /// </summary>
    public static bool IsLegalTarget(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return card.Zone == ZoneType.Graveyard
            && (card.HasType(CardType.Creature) || card.HasType(CardType.Land));
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Pulse of
    /// Murasa. One 1..1 "target creature or land card in a graveyard" request
    /// (candidate pool = union of <paramref name="searchablePlayers"/>'
    /// graveyards, filtered to creature/land cards); the resolve body returns
    /// the chosen card to its owner's hand and gains the caster 6 life.
    /// </summary>
    /// <param name="caster">Spell controller — gains the 6 life (CR 119.3).
    /// Note the returned card goes to ITS OWNER's hand (CR 109.4), which may be
    /// a different player.</param>
    /// <param name="searchablePlayers">Players whose graveyards are scanned for
    /// legal targets — "a graveyard" means any graveyard in the game (CR
    /// 109.4 / 400.1). Pass all players in the game. May be empty (shape-only
    /// paths); the request then has no candidates and the return is a vacuous
    /// no-op.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="zoneService">When supplied, the Graveyard → Hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so any downstream
    /// zone-change triggers fire (CR 603.6a / CR 701.20). When null, a
    /// direct-zone mutation is used.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        IReadOnlyList<Player> searchablePlayers,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(searchablePlayers);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 700.6 — candidate pool: every creature/land card across all
        // supplied graveyards. Production callers refresh this at cast time.
        var candidates = searchablePlayers
            .Where(p => p != null)
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .Where(IsLegalTarget)
            .Cast<object>()
            .ToList();

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or land card in a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: candidates,
                    Intent: BotIntent.CardAdvantage),
            },
            EffectFactory: chosen =>
            {
                object? rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: return target creature or land card to its owner's hand; gain {LifeGain} life",
                        () => Resolve(caster, searchablePlayers, rawTarget, targetResolver, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolve Pulse of Murasa (CR 608.2): return the chosen creature/land
    /// card to its owner's hand, then gain the caster 6 life.
    /// </summary>
    public static void Resolve(
        Player caster,
        IReadOnlyList<Player> searchablePlayers,
        object? rawTarget,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(searchablePlayers);
        ArgumentNullException.ThrowIfNull(targetResolver);

        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (rawTarget != null && targetResolver(rawTarget) is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first legal creature/land card across
        // the supplied graveyards (single-arg dispatcher / no-agent posture,
        // mirrors Bala Ged Recovery / Eternal Witness).
        picked ??= searchablePlayers
            .Where(p => p != null)
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .FirstOrDefault(IsLegalTarget);

        // CR 608.2b — illegal-on-resolution check: the target must still be a
        // creature/land card in a graveyard. Illegal target → the return does
        // nothing, but the lifegain (an independent, non-targeted effect)
        // still happens (CR 608.2c).
        if (picked != null && IsLegalTarget(picked))
        {
            // CR 109.4 / 400.3 — a card returns to ITS OWNER's hand, not the
            // caster's. Owner falls back to the caster only if somehow unset.
            var ownerOf = picked.Owner ?? caster;

            if (zoneService != null)
            {
                zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, ownerOf);
            }
            else
            {
                ownerOf.Zones.Graveyard.RemoveCard(picked);
                ownerOf.Zones.Hand.AddCard(picked);
                picked.SetZone(ZoneType.Hand);
            }
        }

        // CR 119.3 — "You gain 6 life." Unconditional; runs whether or not the
        // return resolved (the lifegain is not gated on a legal target).
        caster.GainLife(LifeGain);
    }
}
