using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonfire of the Damned (Avacyn Restored,
/// {X}{X}{R}).
///
/// Sorcery. Oracle text:
///   "Bonfire of the Damned deals X damage to target player or
///    planeswalker and each creature that player or that planeswalker's
///    controller controls.
///    Miracle {X}{R} (You may cast this card for its miracle cost when
///    you draw it if it's the first card you drew this turn.)"
///
/// ## Implemented (v1)
///
/// - <b>Sorcery</b> at <c>{X}{X}{R}</c>, owner/controller wired.
/// - <b>X-keyed targeted damage (CR 107.3 / 119.2)</b>: built via
///   <see cref="BuildSpellDefinition"/>. <see cref="SpellDefinition.HasVariableX"/>
///   = true so the cast flow prompts for X (the cost is <c>{X}{X}{R}</c>;
///   the player commits 2X+1 mana). One 1..1 target request, "target
///   player or planeswalker" — same target string + posture as
///   <see cref="LavaSpikeFactory"/>. Resolution reads
///   <c>ChosenSpellParams.X</c> as the damage amount and uses
///   <see cref="Fx.DealDamageAny"/> so a Planeswalker target loses X
///   loyalty (CR 306.7) and a Player target loses X life (CR 119.2).
///   After the primary damage, the resolution sweeps every creature on
///   the controlling player's battlefield (the target Player itself, or
///   the Planeswalker's <see cref="Permanent.Controller"/>) and deals X
///   damage to each via <see cref="Creature.TakeDamage"/> (CR 119.2 +
///   CR 704.5f — SBAs sweep lethal damage on the next priority pass).
///   The creature list is snapshotted before the sweep so victims that
///   die from the primary damage to a planeswalker controller don't
///   skew enumeration.
/// - <b>Miracle {X}{R} (CR 702.94)</b> — no Miracle primitive exists in
///   the engine today (no top-of-library reveal-on-draw hook, no
///   miracle-cast alternative-cost permission slot). Surfaced as a
///   <see cref="KeywordAbility"/>("Miracle") marker so a downstream
///   Miracle primitive picks Bonfire up without re-touching this
///   factory — same posture as
///   <see cref="PhyrexianCrusaderFactory"/>'s Infect / Inkmoth Nexus's
///   Infect markers. Until that primitive lands, Bonfire ships as a
///   plain {X}{X}{R} Sorcery; the bot won't choose to cast it for its
///   miracle cost.
///
/// ## CR notes
///
/// - <b>"That player or that planeswalker's controller"</b>: when the
///   target is a Player, the controller is the target itself; when the
///   target is a Planeswalker, the controller is its
///   <see cref="Permanent.Controller"/>. Either way the sweep scans
///   that single player's battlefield (CR 109.5).
/// - <b>X for the cost vs. X for the damage</b>: CR 107.3 — X is locked
///   in at cast time. The same X feeds both the primary damage and the
///   per-creature sweep; we read <c>ChosenSpellParams.X</c> once and
///   pipe it through both branches.
/// - <b>Damage prevention / replacement subscribers</b>: the primary
///   damage routes through <see cref="Fx.DealDamageAny"/> (which delegates
///   to <see cref="OracleSpellBinder.DealDamage"/>) — same shape every
///   X-burn ships. The creature sweep uses <see cref="Creature.TakeDamage"/>
///   directly; future "deal damage" event-bus unification picks both
///   paths up for free.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Miracle as a real alternative-cost primitive</b>: needs (a) a
///   top-of-library-on-draw reveal hook (Player.DrawCards stamps a
///   "was first card drawn this turn" flag — currently no such flag
///   exists), (b) a <c>MiracleAlternativeCost</c> (CR 118.9) wired
///   through <see cref="SpellCastFlow"/>, (c) a "may cast" trigger on
///   the draw event (CR 702.94b). Same family as Plot, Cascade, Suspend
///   — all alt-cost primitive clusters parked behind the same cast-flow
///   refactor.
/// </summary>
[CardName("Bonfire of the Damned")]
public static class BonfireOfTheDamnedFactory
{
    public const string CardName = "Bonfire of the Damned";
    public const string PrintedManaCost = "{X}{X}{R}";
    public const string MiracleCostText = "{X}{R}";

    /// <summary>
    /// Construct Bonfire of the Damned. The Miracle keyword marker is
    /// attached for shape; the X-keyed damage body is built on demand
    /// via <see cref="BuildSpellDefinition"/> because the resolution
    /// needs the caller's target resolver and the all-players list.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.94 — Miracle. Keyword marker; alternative-cost wiring
        // deferred (see class xmldoc). Surfacing the keyword now means
        // a future Miracle primitive picks Bonfire up for free.
        card.AddAbility(new KeywordAbility("Miracle", card, owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Bonfire of the Damned
    /// uses on resolution. <see cref="SpellDefinition.HasVariableX"/> is
    /// true so the cast flow prompts for X at cast time; resolution
    /// reads <c>ChosenSpellParams.X</c> as the damage value, deals it
    /// to the chosen target (player / planeswalker) via
    /// <see cref="Fx.DealDamageAny"/>, then sweeps X damage across each
    /// creature on the controlling player's battlefield.
    /// </summary>
    /// <param name="caster">Spell controller — used as a defensive
    /// owner-of-record only.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game
    /// object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest("target player or planeswalker", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var x = chosen.X ?? 0;
                var rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? resolver(chosen.Targets[0][0])
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: X={x} damage to target + each creature that player/PW controller controls.",
                        () =>
                        {
                            if (x <= 0) return;
                            if (rawTarget == null) return;

                            // CR 119.2 + CR 306.7 — primary damage to player
                            // or planeswalker. DealDamageAny routes PW → loyalty.
                            Fx.DealDamageAny(rawTarget, x);

                            // CR 109.5 — "that player or that planeswalker's
                            // controller" → resolve to a single Player.
                            var sweepPlayer = rawTarget switch
                            {
                                Player p => p,
                                Planeswalker pw => pw.Controller,
                                _ => null,
                            };
                            if (sweepPlayer == null) return;

                            // Snapshot before sweep — primary damage above
                            // may have queued zone moves but SBAs haven't
                            // swept yet; we still want a stable enumeration.
                            var creatures = sweepPlayer.Zones.Battlefield
                                .GetCards()
                                .OfType<Creature>()
                                .ToList();

                            foreach (var c in creatures)
                            {
                                c.TakeDamage(x);
                            }
                        }),
                };
            });
    }
}
