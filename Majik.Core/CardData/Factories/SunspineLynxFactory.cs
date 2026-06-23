using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunspine Lynx (Outlaws of Thunder Junction,
/// {2}{R}{R}).
///
/// Creature — Elemental Cat 5/4. Oracle text (verified against Scryfall):
///   "Players can't gain life.
///    Damage can't be prevented.
///    When this creature enters, it deals damage to each player equal to the
///    number of nonbasic lands that player controls."
///
/// The base shape (name, Creature, Elemental + Cat subtypes, {2}{R}{R}, 5/4)
/// is materialised from the embedded JSON definition
/// (<c>sunspine-lynx.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player)"/> (same
/// posture as <see cref="RampagingFerocidonFactory"/>). The three printed
/// abilities are layered on here:
///
/// ## Implemented (v1)
///
/// - <b>"Players can't gain life" static (CR 119.6 / CR 614.1)</b> — when a
///   <see cref="ReplacementBus"/> is supplied, register a
///   <see cref="LifeGainIntent"/> replacement that rewrites every gain to a
///   zero-amount intent. Identical wiring to
///   <see cref="RampagingFerocidonFactory"/> / <see cref="SulfuricVortexFactory"/>.
///   Without a bus the static silently no-ops.
///
/// - <b>ETB damage-to-each-player (CR 603.6a)</b>: when Sunspine Lynx enters,
///   it deals damage to each player equal to the number of nonbasic lands
///   that player controls. A nonbasic land is a permanent that
///   <c>HasType(Land)</c> and is NOT <c>HasSupertype(Basic)</c> (CR 205.4 —
///   "Basic" is the only land supertype that matters here, e.g. basic Forest
///   vs. shockland / fetchland). The resolving effect reads the live player
///   set off <c>ctx.Game.AllPlayers</c> (falling back to owner-only when no
///   live game is wired — shape-only tests), counts each player's nonbasic
///   lands at resolution, and routes the damage through
///   <see cref="Fx.DealDamageAny(object, int, Creature?)"/> with this Lynx as
///   the source. Damage to a player lands as life loss (CR 119.3); a
///   zero-count player takes nothing.
///
/// ## Deferred (v1 gap)
///
/// - <b>"Damage can't be prevented" (CR 615 prevention-suppression)</b>:
///   NO-OP. The engine has prevention <i>shields</i> but no
///   prevention-<i>suppression</i> surface to disable them, so there is
///   nothing to wire — same documented posture as
///   <see cref="QuestingBeastFactory"/> / <see cref="SkullcrackFactory"/>.
///   This only matters opposite an active prevention effect (rare in Modern).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; no <see cref="TriggerManager"/> /
///   <see cref="ReplacementBus"/> registration.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?)"/> — fully
///   wired. <paramref name="triggers"/> picks the ETB off the bus;
///   <paramref name="replacements"/> registers the life-gain blocker.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Sunspine Lynx")]
public static class SunspineLynxFactory
{
    public const string CardName = "Sunspine Lynx";
    public const string Slug = "sunspine-lynx";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Sunspine Lynx with no live runtime services. The ETB trigger
    /// is attached for shape observability; nothing is registered on a trigger
    /// manager or replacement bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Sunspine Lynx with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager — when supplied, the ETB
    /// damage-to-each-player registers so the bus drives it
    /// automatically.</param>
    /// <param name="replacements">Replacement bus — when supplied, the
    /// "players can't gain life" static registers as a
    /// <see cref="LifeGainIntent"/> replacement (CR 119.6 / 614.1) that
    /// rewrites every gain to zero. Without a bus the static silently
    /// no-ops.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental + Cat, {2}{R}{R}, 5/4). No abilities in the JSON — the
        // life-gain static + ETB damage are layered on below.
        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // ETB damage-to-each-player — CR 603.6a.
        //   "When this creature enters, it deals damage to each player equal
        //    to the number of nonbasic lands that player controls."
        // Fires when this Lynx enters the battlefield. On resolution we read
        // the live player set off ctx.Game.AllPlayers (owner-only fallback for
        // shape-only tests), count each player's nonbasic lands
        // (HasType(Land) && !HasSupertype(Basic), CR 205.4), and deal that much
        // damage to that player via Fx.DealDamageAny (this Lynx as the source).
        // CR 119.3 — damage to a player is life loss; a zero-count player takes
        // nothing.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: deal damage to each player = their nonbasic lands",
            ctx =>
            {
                if (card.Zone != ZoneType.Battlefield) return ValueTask.CompletedTask;

                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    if (p == null || p.HasLost) continue;

                    var nonbasicLands = p.Zones.Battlefield.GetCards()
                        .Count(c => c.HasType(CardType.Land)
                                    && !c.HasSupertype(CardSupertype.Basic));

                    if (nonbasicLands <= 0) continue;

                    Fx.DealDamageAny(p, nonbasicLands, card);
                }

                return ValueTask.CompletedTask;
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "Players can't gain life" static — CR 119.6 / CR 614.1.
        //   Register a LifeGainIntent replacement that rewrites every gain to
        //   a zero-amount intent. Same shape as Rampaging Ferocidon.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register(new LambdaReplacement<LifeGainIntent>(
                applies: (_, _) => true,
                replace: (intent, _) => intent with { Amount = 0 },
                oneShot: false,
                tag: card));
        }

        // CR 615 — "Damage can't be prevented" is a documented no-op: the
        // engine has no prevention-suppression surface (same posture as
        // Questing Beast / Skullcrack). Nothing to wire.

        return card;
    }
}
