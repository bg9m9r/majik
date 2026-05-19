namespace Majik.Core.CardData.MDFCs;

/// <summary>
/// CR 711 — modal double-faced cards / transform cards. Two faces: front
/// and back; one is "active" at any time. Transform swaps active face.
/// MVP: tracks current face name + flag; characteristic-replacement
/// (Layer 1/4/etc.) deferred to a richer layer-system integration.
/// </summary>
public sealed class MdfcState
{
    public string FrontFaceName { get; }
    public string BackFaceName { get; }
    public bool IsBackFace { get; private set; }

    public MdfcState(string frontFaceName, string backFaceName)
    {
        if (string.IsNullOrWhiteSpace(frontFaceName)) throw new ArgumentException(nameof(frontFaceName));
        if (string.IsNullOrWhiteSpace(backFaceName)) throw new ArgumentException(nameof(backFaceName));
        FrontFaceName = frontFaceName;
        BackFaceName = backFaceName;
    }

    public string ActiveFaceName => IsBackFace ? BackFaceName : FrontFaceName;

    public void Transform() => IsBackFace = !IsBackFace;
}
