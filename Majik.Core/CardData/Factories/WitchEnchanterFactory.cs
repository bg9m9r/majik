using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Witch Enchanter // Witch-Blessed Meadow (Wilds of Eldraine, {3}{W}).
///
/// Creature — Human Warlock 2/2. Oracle text (front):
///   "When this creature enters, destroy target artifact or enchantment
///    an opponent controls."
///
/// Back face — <see cref="WitchBlessedMeadowFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Modal Double-Faced Card: each face has its own complete characteristics.
/// At cast / play time the controller chooses which face to use. Modelled
/// by giving each printed face its own <c>[CardName]</c>-dispatched factory:
/// <list type="bullet">
///   <item>Casting the front face → <see cref="NamedCardFactory"/>
///     resolves <c>"Witch Enchanter"</c> → this factory → a
///     <see cref="Creature"/> with the ETB destroy trigger.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/> resolves
///     <c>"Witch-Blessed Meadow"</c> → <see cref="WitchBlessedMeadowFactory"/>
///     → a painland-style <see cref="Land"/>.</item>
/// </list>
/// Both face cards carry an <see cref="MdfcState"/> tracker.
///
/// ## Implemented (v1)
/// - Creature {3}{W} 2/2, Human + Warlock subtypes. White (from the {W}
///   pip per CR 202.2c). Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Witch Enchanter", back =
///   "Witch-Blessed Meadow"); starts on the front face.
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this creature enters,
///   destroy target artifact or enchantment an opponent controls." Single
///   1..1 <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   The candidate gatherer is the union of every artifact + enchantment
///   <b>an opponent controls</b> across the battlefield. Same destroy
///   posture as <see cref="ReclamationSageFactory"/> but with the "an
///   opponent controls" restriction (CR 109.5 — "an opponent" = a player
///   other than this card's controller).
/// - Resolution reads <see cref="TriggeredAbility.ChosenTargets"/>; validates
///   the chosen target is still an artifact OR enchantment on the
///   battlefield NOT controlled by this card's controller (CR 608.2b —
///   illegal target → clean no-op); destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible cancels
///   per CR 702.12, regeneration shield consumed per CR 701.15).
///
/// ## Deferred (v1 gaps)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> from an agent prompt
///   before triggers resolve. The factory falls back to the first legal
///   opponent-controlled target deterministically when no agent picked
///   (mirrors <see cref="ReclamationSageFactory"/>'s no-agent posture).
/// - <b>Target legality in ActionValidator</b>: the validator does not
///   filter to "opponent's artifact or enchantment" at announcement;
///   resolution-time guard handles illegal targets (CR 608.2b). Same
///   posture as <see cref="ReclamationSageFactory"/>.
/// </summary>
[CardName("Witch Enchanter")]
public static class WitchEnchanterFactory
{
    public const string CardName = "Witch Enchanter";
    public const string BackName = "Witch-Blessed Meadow";
    public const string PrintedManaCost = "{3}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Witch Enchanter with no live wiring. The ETB trigger is
    /// attached for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>, and no opponents are known for the
    /// deterministic fallback. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, opponents: null);

    /// <summary>
    /// Construct Witch Enchanter with a known opponent list (used by the
    /// deterministic single-arg fallback to find an opponent-controlled
    /// artifact / enchantment when no agent set a target).
    /// </summary>
    public static Creature Create(Player owner, IEnumerable<Player>? opponents) =>
        Create(owner, triggers: null, opponents: opponents);

    /// <summary>
    /// Construct Witch Enchanter with optional <see cref="TriggerManager"/>
    /// wiring and an optional opponent list. When <paramref name="triggers"/>
    /// is supplied the ETB ability is registered for bus-driven firing.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEnumerable<Player>? opponents)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var opponentList = opponents?.ToList() ?? new List<Player>();

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warlock });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Witch-Blessed Meadow) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, destroy target artifact or
        //    enchantment an opponent controls."
        //
        // Bespoke 1..1 TargetRequest restricted to artifacts + enchantments
        // an opponent controls. Live gatherer enumerates the battlefield
        // across every OTHER player so the agent's picker sees an up-to-date
        // legal set at resolution (CR 109.5 — "an opponent").
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy target artifact or enchantment an opponent controls",
            () => ResolveDestroy(card, owner, opponentList, etb));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// Snapshot the opponent-controlled legal-target set from the supplied
    /// opponent list. Production callers refresh via
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution; this
    /// single-arg snapshot powers the deterministic no-agent fallback.
    /// </summary>
    private static IReadOnlyList<Permanent> GatherOpponentTargets(IReadOnlyList<Player> opponents)
    {
        return opponents
            .SelectMany(p => p.Zones.Battlefield.GetCards())
            .OfType<Permanent>()
            .Where(c => c.HasType(CardType.Artifact)
                     || c.HasType(CardType.Enchantment))
            .ToList();
    }

    /// <summary>
    /// Resolve the ETB destroy. Honours <see cref="TriggeredAbility.ChosenTargets"/>
    /// when set by the agent; otherwise falls back to the first legal
    /// opponent-controlled artifact / enchantment (deterministic single-arg
    /// dispatcher posture). Validates the chosen target is still a legal
    /// artifact / enchantment on the battlefield that the Witch Enchanter's
    /// controller does NOT control (CR 109.5 / CR 608.2b) before destroying
    /// (CR 701.7).
    /// </summary>
    private static void ResolveDestroy(
        Creature enchanter,
        Player owner,
        IReadOnlyList<Player> opponents,
        TriggeredAbility? etb)
    {
        Permanent? picked = null;

        // 1) Honour agent-set target (production path).
        if (etb != null
            && etb.ChosenTargets.Count > 0
            && etb.ChosenTargets[0].Count > 0
            && etb.ChosenTargets[0][0] is Permanent chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first legal opponent-controlled
        //    artifact / enchantment (no-agent dispatcher posture).
        picked ??= GatherOpponentTargets(opponents).FirstOrDefault();

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact)
              || picked.HasType(CardType.Enchantment))) return;
        // CR 109.5 — "an opponent controls": the controller's own permanent
        // is never a legal target.
        if (ReferenceEquals(picked.Controller, enchanter.Controller)) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }
}
