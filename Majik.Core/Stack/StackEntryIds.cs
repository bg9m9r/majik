namespace Majik.Core.Stack;

/// <summary>
/// CR 603.2 / 603.3 — ids for repeat firings of one triggered ability. The
/// engine queues and pushes the registered ability object once per firing, so
/// an ability that triggered twice (Blade of the Bloodchief when two creatures
/// die together) is the same object twice. The first copy keeps the ability's
/// <see cref="IStackObject.Id"/>; the n-th repeat gets an id derived
/// deterministically from it. The order-triggers prompt and the stack use this
/// one derivation, so a client sees the same id for a firing in both.
/// </summary>
public static class StackEntryIds
{
    /// <summary>The id of repeat number <paramref name="salt"/> (1 = the second copy) of <paramref name="baseId"/>.</summary>
    public static Guid Repeat(Guid baseId, int salt)
    {
        var bytes = baseId.ToByteArray();
        var mix = BitConverter.GetBytes(salt * -1640531535); // golden-ratio mix of the repeat index
        for (var b = 0; b < 4; b++) bytes[12 + b] ^= mix[b];
        return new Guid(bytes);
    }
}