using Majik.Bot.Search;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Heuristic;

/// <summary>
/// CR 712.3 face choice for MDFC casts. Deadlock-killer default: the spell
/// face wins when its cost is affordable from available mana
/// (<see cref="LegalActionEnumerator.UntappedManaSources"/> — enumerator-symmetric);
/// otherwise the land face wins while the land drop is available. Refinement
/// (curve targets, holding spells) is a later eval concern.
///
/// <para>This kills the 0-mana paralysis on MDFC-land hands (Belcher trace,
/// 2026-06-12): when the bot enumerates the back-land play and the engine
/// raises the face prompt, this policy picks the land at 0 mana so a real land
/// hits the battlefield and mana starts flowing.</para>
/// </summary>
internal static class MdfcFacePolicy
{
    /// <summary>
    /// Detect an MDFC face prompt (every candidate is an
    /// <see cref="MdfcFaceChoice"/>) and, if so, pick a face via
    /// <see cref="Pick"/>. Returns <c>false</c> for any other ChooseAsync prompt
    /// so the caller's default routing is untouched. Shared by both agent paths
    /// (<see cref="Majik.Bot.BotPlayerAgent"/> + <see cref="Search.SearchAgent"/>).
    /// </summary>
    public static bool TryPick(
        GameContext ctx,
        Player self,
        IReadOnlyList<object> candidates,
        out MdfcFaceChoice picked)
    {
        picked = default!;
        if (candidates.Count == 0)
            return false;

        var faces = new List<MdfcFaceChoice>(candidates.Count);
        foreach (var c in candidates)
        {
            if (c is not MdfcFaceChoice face)
                return false; // mixed / non-face prompt — not ours.
            faces.Add(face);
        }

        picked = Pick(ctx, self, faces);
        return true;
    }

    /// <summary>
    /// Pick the face to play from the prompt's <paramref name="faces"/>
    /// (front-first, as <see cref="Majik.Core.Game.MdfcCastFlow.ResolveFaceAsync"/>
    /// orders them). Falls back to the first face if the prompt has no land
    /// back face (defensive — the enumerator only surfaces land-back MDFCs).
    /// </summary>
    public static MdfcFaceChoice Pick(
        GameContext ctx, Player self, IReadOnlyList<MdfcFaceChoice> faces)
    {
        var front = faces.FirstOrDefault(f => !f.IsBack);
        var landBack = faces.FirstOrDefault(f => f.IsBack);

        // No back land face to fall back to → cast the front (or whatever's first).
        if (landBack is null)
            return front ?? faces[0];

        // No front face in the prompt → only the back land is castable here.
        if (front is null)
            return landBack;

        // Spell face wins when affordable from available mana — symmetric with the
        // enumerator's UntappedManaSources gate (colour-blind CMC ≤ sources).
        var frontCmc = ManaCost.Parse(front.ManaCost ?? string.Empty).TotalValue;
        var manaAvailable = LegalActionEnumerator.UntappedManaSources(self);
        if (frontCmc <= manaAvailable)
            return front;

        // Front unaffordable. Play the land while the drop is available (the
        // deadlock-killer); otherwise the spell face is the only thing the prompt
        // could resolve to — keep it (the engine rejects an illegal land play).
        return ctx.LandPlayAvailable ? landBack : front;
    }
}
