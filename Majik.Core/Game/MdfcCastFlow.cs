using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// CR 712.3 / 712.4 — real "cast either face" entry for Modal Double-Faced
/// Cards (deferral #3). When a player casts an MDFC from hand whose front
/// face carries a castable back-face descriptor
/// (<see cref="MdfcState.CanCastEitherFace"/>), this helper prompts the
/// controller to CHOOSE which face to play (CR 712.3) and routes accordingly:
///
/// <list type="bullet">
///   <item><b>Front face</b> — cast through the normal spell path with the
///     front face's printed cost / type / effect. The caller keeps casting
///     the original front card; <see cref="ResolveFaceAsync"/> returns null.</item>
///   <item><b>Back land face</b> (Soporific Springs) — played as a LAND with
///     no stack (CR 305). The back-face land instance is materialized,
///     enters the battlefield (its ETB replacements — e.g. "pay 3 life or
///     enter tapped" — fire), and the land-for-turn is consumed.</item>
///   <item><b>Back spell face</b> — cast onto the stack with the back face's
///     own cost / type / effect. The back-face spell instance is materialized
///     and the caller casts it via the normal flow.</item>
/// </list>
///
/// No transform machinery is involved — MDFC faces do not transform
/// (CR 712.4). The chosen face is the only object that exists; the unchosen
/// face simply isn't there. The front-face card never enters the battlefield
/// when the back face is chosen — the materialized back-face instance does,
/// preserving the same card identity / owner.
/// </summary>
public static class MdfcCastFlow
{
    /// <summary>
    /// CR 712.3 — prompt the caster to choose which face of an MDFC to cast.
    /// Returns:
    /// <list type="bullet">
    ///   <item><c>null</c> — cast the FRONT face (the card the caller already
    ///     holds). Returned when the card is not an MDFC with a castable back
    ///     face, or when the caster picks the front face.</item>
    ///   <item>a non-null <see cref="MdfcFace"/> — the BACK face the caster
    ///     chose to play (land or spell).</item>
    /// </list>
    /// The choice flows through the single declarative
    /// <see cref="IPlayerAgent.ChooseAsync"/> sink (CR 712.3 — a face choice,
    /// not a target / mode). Candidates are the two <see cref="MdfcFace"/>-like
    /// descriptors wrapped as <see cref="MdfcFaceChoice"/> tokens so the
    /// prompt carries both face names + costs.
    /// </summary>
    public static async Task<MdfcFace?> ResolveFaceAsync(
        ICard card,
        Player caster,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(ctx);

        var state = (card as Card)?.MdfcState;
        if (state is not { CanCastEitherFace: true } || state.BackFace is not { } back)
        {
            // Not an MDFC with a castable back face — cast the front normally.
            return null;
        }

        var frontChoice = new MdfcFaceChoice(state.FrontFaceName, card.ManaCost ?? "", IsBack: false);
        var backChoice = new MdfcFaceChoice(back.Name, back.ManaCost, IsBack: true);

        var request = new ChoiceRequest(
            ChoiceKind.PickOne,
            $"Choose which face of {state.FrontFaceName} // {state.BackFaceName} to cast (CR 712.3)",
            Min: 1,
            Max: 1,
            Candidates: new object[] { frontChoice, backChoice },
            Intent: BotIntent.None,
            Optional: false);

        var picked = await agent.ChooseAsync(ctx, request, ct);
        if (picked.Count > 0 && picked[0] is MdfcFaceChoice chosen && chosen.IsBack)
        {
            return back;
        }

        // Front face (explicit pick, decline, or any non-back result).
        return null;
    }

    /// <summary>
    /// CR 305 / 712.3 — play the chosen BACK LAND face of an MDFC. The
    /// front-face card <paramref name="frontCard"/> is removed from the
    /// caster's hand and a fresh back-face land instance is materialized via
    /// <see cref="MdfcFace.BuildCard"/>, wired to the live
    /// <paramref name="replacements"/> bus (so its ETB replacement fires) and
    /// moved onto the battlefield through <paramref name="zones"/> (firing
    /// enters-the-battlefield events). The land-for-turn is consumed via
    /// <paramref name="landDropTracker"/>.
    ///
    /// <para>Pre-checks the land-drop legality (CR 305.2) when
    /// <paramref name="landDropTracker"/> is supplied; if the player has
    /// already played their land this turn the play is rejected and the front
    /// card stays in hand (returns false).</para>
    /// </summary>
    /// <returns><c>true</c> when the land was played; <c>false</c> when the
    /// land-drop was illegal (front card untouched).</returns>
    public static bool PlayBackLandFace(
        ICard frontCard,
        MdfcFace backFace,
        Player caster,
        ZoneService zones,
        ReplacementBus? replacements,
        LandDropTracker? landDropTracker,
        Player activePlayer,
        Majik.Core.StateMachine.PhaseStateType phase,
        bool stackEmpty,
        ContinuousEffectsService? effects = null)
    {
        ArgumentNullException.ThrowIfNull(frontCard);
        ArgumentNullException.ThrowIfNull(backFace);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(zones);
        if (!backFace.IsLand)
        {
            throw new InvalidOperationException(
                $"MDFC face '{backFace.Name}' is not a land face; use the spell-cast path.");
        }

        // CR 305.2 — land-drop legality. When a tracker is supplied, gate on
        // it; without one (shape tests) skip the gate (PriorityLoop applies
        // the same null-tracker fallback for ordinary land plays).
        if (landDropTracker != null
            && !landDropTracker.CanPlayLand(caster, activePlayer, phase, stackEmpty, out _))
        {
            return false;
        }

        // Build the live back-face land instance, wiring its ETB replacement.
        var land = backFace.BuildCard(caster, replacements);
        land.SetOwner(caster);
        if (land is Permanent perm && effects != null)
        {
            perm.ActiveEffects = effects;
        }

        // CR 712.3 — the front-face card is not the object that enters; remove
        // it from hand so the chosen back-face land replaces it. Both share the
        // same owner / printed identity ("Sink into Stupor // Soporific
        // Springs"); only the chosen face exists on the battlefield.
        if (frontCard.Zone == ZoneType.Hand && frontCard.Owner != null)
        {
            frontCard.Owner.Zones.Hand.RemoveCard(frontCard);
        }

        land.SetZone(ZoneType.Hand);
        caster.Zones.Hand.AddCard(land);
        zones.MoveCardTo(land, ZoneType.Battlefield, controller: caster);

        landDropTracker?.RecordLandPlayed(caster);
        return true;
    }
}

/// <summary>
/// CR 712.3 — a face-choice token surfaced through the declarative
/// <see cref="IPlayerAgent.ChooseAsync"/> prompt when casting an MDFC. Carries
/// the face name + printed cost so a UI / bot policy can present both faces;
/// <see cref="IsBack"/> distinguishes the back face from the front.
/// </summary>
public sealed record MdfcFaceChoice(string FaceName, string ManaCost, bool IsBack)
{
    public override string ToString() =>
        string.IsNullOrEmpty(ManaCost) ? FaceName : $"{FaceName} {ManaCost}";
}
