using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Sink into Stupor // Soporific Springs (Bloomburrow, {1}{U}{U}).
///
/// Instant. Oracle text (front):
///   "Return target spell or nonland permanent an opponent controls to its
///    owner's hand."
///
/// Back face — <see cref="SoporificSpringsFactory"/> (Land — {T}: Add {U};
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// This card is a Modal Double-Faced Card: the two faces share a physical
/// card but each face has its own complete characteristics (cost, type,
/// effect). At cast / play time the controller chooses which face to use,
/// the cost / effect of that face is what applies, and the resulting
/// stack object / permanent is the chosen face (no transform happens on
/// the battlefield — the OTHER face simply isn't there).
///
/// v1 cast-either-face is modelled by giving each printed face its own
/// <c>[CardName]</c>-dispatched factory:
/// <list type="bullet">
///   <item>Casting the front face → <see cref="NamedCardFactory"/>
///     resolves <c>"Sink into Stupor"</c> → this factory → an
///     <see cref="Instant"/> with the bounce effect.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/>
///     resolves <c>"Soporific Springs"</c> →
///     <see cref="SoporificSpringsFactory"/> → a <see cref="Land"/> with
///     the painland-style ETB + {T}: Add {U}.</item>
/// </list>
/// Both face cards carry an <see cref="MdfcState"/> tracker so callers
/// (hand UI / bot policy / serialisation) can see the printed back-face
/// name without holding two object handles. The state is informational —
/// the engine treats each face as its own freshly-built card on the
/// chosen path, matching the minimal-MDFC posture used elsewhere in the
/// engine (<see cref="DelverOfSecretsFactory"/> for the transform variant).
///
/// ## Implemented (v1)
/// - Instant identity at {1}{U}{U}, blue (mono-U from the printed pips),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Sink into Stupor",
///   back = "Soporific Springs") so the back-face name is observable from
///   the front-face card object.
/// - <b>Front-face effect</b> — single 1..1 <see cref="TargetRequest"/>
///   whose candidate gatherer enumerates:
///     <list type="bullet">
///       <item>Spells on the stack whose controller is NOT the Sink
///         caster — i.e. opponent-controlled spells (CR 608.2b — re-checked
///         at resolution).</item>
///       <item>Nonland permanents on the battlefield whose controller is
///         NOT the Sink caster — printed predicate "an opponent controls"
///         excludes self-controlled permanents.</item>
///     </list>
///   Land permanents and own-side spells / permanents are NEVER offered.
/// - <b>Resolve</b> — branches on the resolved target:
///     <list type="bullet">
///       <item>If the target is a <see cref="ISpell"/> still on the stack,
///         remove it via <see cref="OracleSpellBinder.RemoveFromStack"/>
///         (CR 701.16 — equivalent to countering, but the card goes to
///         its owner's hand instead of the graveyard). Same raw-zone
///         redirect pattern as <see cref="RemandFactory"/> (no draw
///         rider — Sink has none).</item>
///       <item>If the target is a <see cref="Permanent"/> still on the
///         battlefield AND is not a land AND its controller is not the
///         caster, move it to its owner's hand (CR 701.20). Same shape
///         as <see cref="BoomerangFactory"/> / <see cref="RegressFactory"/>.</item>
///       <item>Otherwise (CR 608.2b illegal-target re-check at resolution),
///         the effect does nothing.</item>
///     </list>
/// </summary>
[CardName("Sink into Stupor")]
public static class SinkIntoStuporFactory
{
    public const string CardName = "Sink into Stupor";
    public const string BackName = "Soporific Springs";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>
    /// Construct the front face of Sink into Stupor as an Instant card
    /// with owner / controller wired and the <see cref="MdfcState"/>
    /// face tracker attached. The resolve-time <see cref="SpellDefinition"/>
    /// is built on demand via <see cref="BuildDefinition"/> (mirrors the
    /// Remand / Boomerang / Regress factory posture).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Soporific Springs) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target spell or nonland permanent an
    /// opponent controls to its owner's hand" <see cref="SpellDefinition"/>.
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the chosen
    /// target is no longer a legal candidate (spell gone from the stack;
    /// permanent gone from the battlefield; permanent's controller is now
    /// the caster), the effect does nothing.
    /// </summary>
    /// <param name="caster">Sink into Stupor's controller; used to filter
    /// candidate spells / permanents to "an opponent controls".</param>
    /// <param name="targetResolver">Resolves the raw target token to the
    /// live engine object (spell or permanent).</param>
    /// <param name="stack">Active stack; required to remove a countered
    /// spell. May be null when the target is exclusively expected to be a
    /// permanent.</param>
    /// <param name="zoneService">Optional ZoneService — used for the
    /// permanent-bounce half so replacement-bus listeners observe the move.
    /// Raw zone manipulation when null (shape tests).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target spell or nonland permanent an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    CandidateGatherer: ctx =>
                    {
                        var pool = new List<object>();

                        // Opponent-controlled spells on the stack.
                        if (stack != null)
                        {
                            foreach (var obj in stack.GetAll())
                            {
                                if (obj is ISpell sp
                                    && !ReferenceEquals(sp.Controller, caster))
                                {
                                    pool.Add(sp);
                                }
                            }
                        }

                        // Opponent-controlled nonland permanents on the
                        // battlefield. Lands are excluded — printed text
                        // is "nonland permanent".
                        foreach (var p in ctx.AllPlayers)
                        {
                            if (ReferenceEquals(p, caster)) continue;
                            foreach (var perm in p.Zones.Battlefield
                                .GetCards()
                                .OfType<Permanent>())
                            {
                                if (perm.HasType(CardType.Land)) continue;
                                pool.Add(perm);
                            }
                        }

                        return pool;
                    }),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — return target spell or nonland permanent an opponent controls to its owner's hand",
                        () => Resolve(resolved, caster, stack, zoneService)),
                };
            });
    }

    /// <summary>
    /// Branch on the resolved target.
    ///
    /// CR 608.2b — every illegal-target gate here means "do nothing":
    ///   * Spell target: caster owns it (changed controller mid-flight)
    ///     or it's no longer on the stack.
    ///   * Permanent target: it's a Land, it's not on the battlefield, or
    ///     its controller is the caster.
    /// </summary>
    private static void Resolve(
        object resolved,
        Player caster,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zoneService)
    {
        switch (resolved)
        {
            case ISpell spell:
                ResolveSpellTarget(spell, caster, stack);
                break;
            case Permanent perm:
                ResolvePermanentTarget(perm, caster, zoneService);
                break;
            default:
                // CR 608.2b — target is no longer a spell or permanent;
                // do nothing.
                return;
        }
    }

    /// <summary>
    /// CR 701.16-style spell-to-hand redirect: counter the targeted spell
    /// (remove from the stack), then route the underlying card to its
    /// owner's hand instead of the graveyard. Mirrors
    /// <see cref="RemandFactory"/>'s pop-and-redirect shape (no draw
    /// rider).
    /// </summary>
    private static void ResolveSpellTarget(
        ISpell spell,
        Player caster,
        Majik.Core.Stack.Stack? stack)
    {
        if (stack == null) return;

        // CR 608.2b — target must still be on the stack.
        if (!stack.GetAll().Contains(spell)) return;

        // CR 608.2b — printed predicate "an opponent controls". A spell
        // whose controller is now the caster (rare, but possible via mid-
        // resolution control changes) is an illegal target → do nothing.
        if (ReferenceEquals(spell.Controller, caster)) return;

        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;

        // Card → owner's hand (NOT graveyard). Raw zone mutation so no
        // ETB events fire for the hand zone. Same shape Remand uses.
        var card = spell.Card;
        var owner = card.Owner ?? spell.Controller;
        if (owner == null)
        {
            card.SetZone(ZoneType.Hand);
            return;
        }
        owner.Zones.Graveyard.RemoveCard(card);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// CR 701.20 — return the targeted nonland, opponent-controlled
    /// permanent to its owner's hand. Mirrors
    /// <see cref="BoomerangFactory"/> / <see cref="RegressFactory"/>.
    /// </summary>
    private static void ResolvePermanentTarget(
        Permanent target,
        Player caster,
        ZoneService? zoneService)
    {
        // CR 608.2b illegal-target re-check.
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.HasType(CardType.Land)) return;
        if (ReferenceEquals(target.Controller, caster)) return;

        var owner = target.Owner;
        if (owner == null) return;

        var controller = target.Controller ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(owner);
        }
    }
}
