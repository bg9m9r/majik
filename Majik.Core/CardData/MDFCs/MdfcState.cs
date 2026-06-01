namespace Majik.Core.CardData.MDFCs;

/// <summary>
/// CR 711 — modal double-faced cards / transform cards. Two faces: front
/// and back; one is "active" at any time. Transform swaps active face.
///
/// <para>The face-tracker also carries the BACK face's printed
/// characteristics (<see cref="BackFace"/>), captured at build time by the
/// DFC's named-card factory. When <see cref="IsBackFace"/> is true the
/// Layer-0 seed in
/// <see cref="Majik.Core.Effects.ContinuousEffectsService.Compute(Majik.Core.Cards.Permanent)"/>
/// uses those back-face values (name / types / subtypes / supertypes / P/T /
/// keywords / colour) instead of the front-printed Card values, so a
/// transformed permanent's effective body reflects its back face (CR 712).
/// Flipping back to the front face reverts automatically.</para>
/// </summary>
public sealed class MdfcState
{
    /// <summary>
    /// Invoked after every face flip so the owning permanent can invalidate
    /// the CR 613 layer-system memoization cache (the Layer-0 seed changes
    /// when the active face changes). Wired by the
    /// <see cref="Majik.Core.Cards.Card.MdfcState"/> setter. Null until then.
    /// </summary>
    internal Action? OnTransformed { get; set; }

    public string FrontFaceName { get; }
    public string BackFaceName { get; }
    public bool IsBackFace { get; private set; }

    /// <summary>
    /// CR 712 — the back face's printed copiable characteristics, read by the
    /// Layer-0 face-replacement seed while <see cref="IsBackFace"/> is true.
    /// Null when the factory did not supply a back-face characteristic set
    /// (legacy DFCs — e.g. modal land/spell faces handled by a separate
    /// factory); those retain the front-printed seed when flipped, as before.
    /// </summary>
    public BackFaceCharacteristics? BackFace { get; }

    public MdfcState(string frontFaceName, string backFaceName)
        : this(frontFaceName, backFaceName, backFace: null)
    {
    }

    public MdfcState(string frontFaceName, string backFaceName, BackFaceCharacteristics? backFace)
    {
        if (string.IsNullOrWhiteSpace(frontFaceName)) throw new ArgumentException(nameof(frontFaceName));
        if (string.IsNullOrWhiteSpace(backFaceName)) throw new ArgumentException(nameof(backFaceName));
        FrontFaceName = frontFaceName;
        BackFaceName = backFaceName;
        BackFace = backFace;
    }

    public string ActiveFaceName => IsBackFace ? BackFaceName : FrontFaceName;

    public void Transform()
    {
        IsBackFace = !IsBackFace;
        // CR 613 — the active-face flip changes the Layer-0 characteristic
        // seed; invalidate the owning permanent's memoization cache so the
        // next Compute re-seeds from the now-active face.
        OnTransformed?.Invoke();
    }
}
