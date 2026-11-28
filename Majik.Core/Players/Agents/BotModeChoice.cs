namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 700.2 / 700.2d — the simplest legal multi-mode answer for a bot: the one
/// mode its single-mode scoring likes best, then the earliest other modes until
/// the minimum is met. A "one or both" / "one or more" spell therefore gets
/// exactly the pick the bot made before multi-mode prompts existed; a "choose
/// two" spell adds the first other mode.
/// </summary>
public static class BotModeChoice
{
    public static IReadOnlyList<int> FromBestMode(int best, int modeCount, int minModes)
    {
        if (modeCount <= 0) return Array.Empty<int>();
        var picked = new List<int> { Math.Clamp(best, 0, modeCount - 1) };
        var need = Math.Clamp(minModes, 1, modeCount);
        for (var i = 0; i < modeCount && picked.Count < need; i++)
        {
            if (!picked.Contains(i)) picked.Add(i);
        }
        return picked;
    }
}