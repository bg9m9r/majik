using Majik.Core.Players.Agents;

namespace Majik.Core.Game;

/// <summary>
/// CR 700.2 / 601.2c — the target-slot layout of a modal spell whose target
/// requests each carry a <see cref="TargetRequest.ModeIndex"/>. Slots are laid
/// out mode by mode, in printed order: mode <c>m</c> owns
/// <c>max(1, requests of m)</c> consecutive slots, so a mode with at most one
/// request sits at slot <c>m</c> (<c>Targets[ModeIndex]</c>, what Cryptic
/// Command and split cards read) and a mode with several requests (a fight
/// mode) gets one slot per request, in declaration order. Unchosen modes keep
/// empty slots. <see cref="SpellCastFlow"/> fills this layout and
/// <c>ModalChooseOneTemplate</c> slices it, so both sides agree.
/// </summary>
internal static class ModalTargetLayout
{
    /// <summary>First slot of each mode; <paramref name="slotCount"/> is the
    /// total slot count. Requests whose ModeIndex is null or out of range own
    /// no slot.</summary>
    public static int[] Offsets(int modeCount, IReadOnlyList<TargetRequest> requests, out int slotCount)
    {
        var counts = new int[modeCount];
        foreach (var r in requests)
        {
            if (r.ModeIndex is int m && m >= 0 && m < modeCount) counts[m]++;
        }
        var offsets = new int[modeCount];
        var next = 0;
        for (var m = 0; m < modeCount; m++)
        {
            offsets[m] = next;
            next += Math.Max(1, counts[m]);
        }
        slotCount = next;
        return offsets;
    }
}
