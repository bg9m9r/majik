using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Bala Ged Recovery // Bala Ged Sanctuary (Zendikar Rising, {2}{G}).
///
/// Sorcery. Oracle text (front):
///   "Return target card from your graveyard to your hand."
///
/// Back face — <see cref="BalaGedSanctuaryFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {G}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Two-factory dispatch — same architecture as
/// <see cref="LegionLeadershipFactory"/> / <see cref="LegionStrongholdFactory"/>
/// (MDFC spell-front + tapland-back). Casting the front face resolves
/// "Bala Ged Recovery" → this factory → a <see cref="Sorcery"/> with the
/// graveyard-return effect. Playing the back face resolves
/// "Bala Ged Sanctuary" → <see cref="BalaGedSanctuaryFactory"/> → a simple
/// tapland.
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{2}{G}</c>, mono-green, owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Bala Ged Recovery",
///   back = "Bala Ged Sanctuary"); starts on the front face.
/// - One 1..1 "target card in your graveyard" request — ANY card type
///   (the oracle says "card", with no type restriction). Same bespoke
///   graveyard-card target shape as <see cref="EternalWitnessFactory"/>.
/// - Resolution returns the chosen card Graveyard → Hand via
///   <see cref="ZoneService.MoveCard"/> when supplied (so any zone-change
///   triggers fire per CR 603.6a / CR 701.20), otherwise direct-zone
///   mutation. Reuses <see cref="EternalWitnessFactory"/>'s shared
///   <c>ResolveReturnToHand</c> posture: validate the chosen card is STILL
///   in the controller's graveyard (CR 608.2b — illegal-on-resolution →
///   clean no-op), deterministic first-card fallback when no agent-set
///   target is present (single-arg / no-agent posture).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Real agent-driven target prompt</b>: production callers wire the
///   chosen target from an agent prompt before the spell resolves; the
///   first-card fallback is the dispatcher-path safety net (same posture
///   as Eternal Witness).
///
/// ## References
///
/// - <see cref="EternalWitnessFactory"/> — identical "return target card
///   from your graveyard to your hand" effect; this factory mirrors its
///   <c>ResolveReturnToHand</c> body for the spell-resolution path.
/// - <see cref="LegionLeadershipFactory"/> / <see cref="LegionStrongholdFactory"/>
///   — companion MDFC spell-front + tapland-back pair showing the same
///   two-factory architecture.
/// </summary>
[CardName("Bala Ged Recovery")]
public static class BalaGedRecoveryFactory
{
    public const string CardName = "Bala Ged Recovery";
    public const string BackName = "Bala Ged Sanctuary";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Return target card from your graveyard to your hand.";

    /// <summary>
    /// Construct the front face of Bala Ged Recovery as a Sorcery with
    /// owner / controller wired and the <see cref="MdfcState"/> face
    /// tracker attached (starts on the front face). Suitable for
    /// identity / shape / dispatcher tests. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Bala Ged Sanctuary) is observable from the
        // front-face card object. Starts on the front face.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                BalaGedSanctuaryFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target card from your graveyard to
    /// your hand" <see cref="SpellDefinition"/>.
    ///
    /// Single 1..1 "target card in your graveyard" request (ANY card
    /// type — CR 700.6, the oracle says "card" with no restriction). On
    /// resolution the chosen card is moved Graveyard → Hand; an
    /// illegal-on-resolution target (no longer in the controller's
    /// graveyard) is a clean no-op (CR 608.2b).
    /// </summary>
    /// <param name="owner">Spell controller — "your graveyard" is the
    /// controller's graveyard (CR 110.2). The candidate pool is the
    /// controller's graveyard at the time the definition is built;
    /// production callers refresh it at cast time via the agent prompt
    /// (same posture as Eternal Witness).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target
    /// token to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="zoneService">When supplied, the Graveyard → Hand move
    /// routes through <see cref="ZoneService.MoveCard"/> so any downstream
    /// zone-change triggers fire (CR 603.6a / CR 701.20). When null, a
    /// direct-zone mutation is used.</param>
    public static SpellDefinition BuildDefinition(
        Player owner,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: owner.Zones.Graveyard.GetCards()
                        .Cast<object>().ToList(),
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
                        $"{CardName}: return target card from your graveyard to your hand",
                        () => ResolveReturnToHand(owner, rawTarget, targetResolver, zoneService)),
                };
            });
    }

    /// <summary>
    /// Resolution helper for the graveyard return. Honours the agent-set
    /// target when present (production path); falls back to the first card
    /// in the controller's graveyard when none was set (deterministic
    /// single-arg / no-agent posture — mirrors
    /// <see cref="EternalWitnessFactory"/>'s fallback). Validates the
    /// chosen card is STILL in the controller's graveyard at resolution
    /// (CR 608.2b — illegal target → clean no-op). Moves the card
    /// Graveyard → Hand via <see cref="ZoneService.MoveCard"/> when
    /// supplied; otherwise direct-zone mutation.
    /// </summary>
    private static void ResolveReturnToHand(
        Player owner,
        object? rawTarget,
        Func<object, object> targetResolver,
        ZoneService? zoneService)
    {
        ICard? picked = null;

        // 1) Honour the agent-set target if present (production path).
        if (rawTarget != null && targetResolver(rawTarget) is ICard chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first card in the controller's
        // graveyard (single-arg dispatcher path / no-agent posture).
        picked ??= owner.Zones.Graveyard.GetCards().FirstOrDefault();

        // Empty graveyard → clean no-op (CR 608.2b).
        if (picked == null) return;

        // CR 608.2b illegal-on-resolution check — target must still be in
        // the controller's graveyard. (Cards leaving the graveyard between
        // cast and resolution fizzle the return.)
        if (picked.Zone != ZoneType.Graveyard) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(picked)) return;

        // Move Graveyard → Hand. ZoneService path publishes a
        // CardMovedEvent so any "leaves graveyard" triggers fire
        // (CR 603.6a / CR 701.20).
        if (zoneService != null)
        {
            zoneService.MoveCard(picked, ZoneType.Graveyard, ZoneType.Hand, owner);
        }
        else
        {
            owner.Zones.Graveyard.RemoveCard(picked);
            owner.Zones.Hand.AddCard(picked);
            picked.SetZone(ZoneType.Hand);
        }
    }
}
