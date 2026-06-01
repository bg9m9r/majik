using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

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
///
/// ## Real cast-either-face (CR 712.3 / 712.4) — deferral #3
///
/// A Modal Double-Faced Card is NOT a transform card: its two faces don't
/// transform into each other (CR 712.4). Instead, when the card is cast /
/// played from hand the controller CHOOSES which face to cast (CR 712.3);
/// the chosen face's mana cost / type / effect is what applies and the
/// resulting stack object / permanent is that face.
///
/// To support the rules-correct path, the FRONT-face card of an MDFC may
/// carry an optional <see cref="CastableBackFace"/> descriptor
/// (<see cref="MdfcFace"/>) describing the OTHER (back) face's castable
/// definition. When present, the cast flow (<see cref="MdfcCastFlow"/>)
/// offers a face choice; the front face is cast through the normal path,
/// while the back face is materialized as its own card instance (a land
/// back is played as a land with no stack; a spell / permanent back goes on
/// the stack with its own cost / effect, entering as that face). No
/// transform machinery is involved — the other face simply isn't there.
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
    /// <para>Distinct from <see cref="CastableBackFace"/>: this drives the
    /// in-play characteristic body of a <em>transformed</em> DFC permanent,
    /// while <see cref="CastableBackFace"/> drives the cast-time face choice
    /// of an MDFC (no transform).</para>
    /// </summary>
    public BackFaceCharacteristics? BackFace { get; }

    /// <summary>
    /// CR 712.3 — descriptor for the OTHER castable face of this MDFC, when
    /// known. On a front-face card this is the back face's castable
    /// definition (land / spell / permanent) so the cast flow can offer "cast
    /// either face". Null on the minimal face-tracker posture (older MDFC
    /// factories that only record the back-face NAME without a castable
    /// definition) and on back-face card instances (already the chosen face).
    /// </summary>
    public MdfcFace? CastableBackFace { get; }

    /// <summary>True when this card carries a castable back-face definition
    /// so the controller can choose either face at cast time (CR 712.3).</summary>
    public bool CanCastEitherFace => !IsBackFace && CastableBackFace != null;

    public MdfcState(string frontFaceName, string backFaceName)
        : this(frontFaceName, backFaceName, backFace: (BackFaceCharacteristics?)null)
    {
    }

    /// <summary>
    /// Construct the face tracker with the back face's printed characteristics
    /// (CR 712 — transformed-permanent Layer-0 seed). Supply
    /// <paramref name="backFace"/> on a transform DFC so its body reflects the
    /// back face while flipped.
    /// </summary>
    public MdfcState(string frontFaceName, string backFaceName, BackFaceCharacteristics? backFace)
    {
        if (string.IsNullOrWhiteSpace(frontFaceName)) throw new ArgumentException(nameof(frontFaceName));
        if (string.IsNullOrWhiteSpace(backFaceName)) throw new ArgumentException(nameof(backFaceName));
        FrontFaceName = frontFaceName;
        BackFaceName = backFaceName;
        BackFace = backFace;
    }

    /// <summary>
    /// Construct the face tracker with an optional castable back-face
    /// descriptor (CR 712.3 — real cast-either-face). Supply
    /// <paramref name="castableBackFace"/> on a FRONT-face card so the cast
    /// flow can offer the choice. Optionally also carry the back face's
    /// printed <paramref name="backFace"/> characteristics for a permanent
    /// back that, once in play, is a transform target.
    /// </summary>
    public MdfcState(
        string frontFaceName,
        string backFaceName,
        MdfcFace? castableBackFace,
        BackFaceCharacteristics? backFace = null)
    {
        if (string.IsNullOrWhiteSpace(frontFaceName)) throw new ArgumentException(nameof(frontFaceName));
        if (string.IsNullOrWhiteSpace(backFaceName)) throw new ArgumentException(nameof(backFaceName));
        FrontFaceName = frontFaceName;
        BackFaceName = backFaceName;
        CastableBackFace = castableBackFace;
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

/// <summary>
/// CR 712.3 — castable definition of one face of a Modal Double-Faced Card.
/// Carries everything the cast flow needs to play that face: its name, the
/// printed mana cost paid for it, whether it is a land (played with no
/// stack) or a spell / permanent (goes on the stack with its own effect),
/// and a builder that materializes a fresh runtime card instance for the face.
///
/// <para>The <see cref="BuildCard"/> delegate returns the live card instance
/// for the face (e.g. the back-face Land for Soporific Springs, a spell card
/// for a spell back, or a permanent card for a creature / artifact /
/// enchantment back). For a non-land face, <see cref="BuildDefinition"/>
/// supplies the resolve-time <see cref="SpellDefinition"/> (cost / type /
/// effect). For a land face, <see cref="BuildDefinition"/> is null — the
/// face is simply played as a land.</para>
/// </summary>
public sealed class MdfcFace
{
    /// <summary>The printed name of this face (e.g. "Soporific Springs").</summary>
    public string Name { get; }

    /// <summary>The printed mana cost paid to cast this face. Empty / "{0}"
    /// for a land face (lands are played, not cast — CR 305.1).</summary>
    public string ManaCost { get; }

    /// <summary>True when this face is a land (played with no stack — CR 305).
    /// False when it is a spell / permanent that goes on the stack.</summary>
    public bool IsLand { get; }

    /// <summary>True when this face is a PERMANENT spell (creature / artifact /
    /// enchantment / planeswalker) — it goes on the stack and, on resolution,
    /// enters the battlefield as that permanent face (CR 608.3). False for a
    /// land face (no stack) and for instant / sorcery spell faces (no
    /// permanent enters).</summary>
    public bool IsPermanent { get; }

    private readonly Func<Player, ReplacementBus?, ICard> _buildCard;

    private readonly Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition>? _buildDefinition;

    /// <summary>
    /// Construct a land face: played as a land (no stack). <paramref name="buildCard"/>
    /// materializes the live land instance (e.g. Soporific Springs), wired to
    /// the supplied <see cref="ReplacementBus"/> so its ETB replacement
    /// ("pay 3 life or enter tapped") registers. The bus may be null in
    /// shape tests.
    /// </summary>
    public static MdfcFace Land(string name, Func<Player, ReplacementBus?, ICard> buildCard) =>
        new(name, manaCost: "", isLand: true, isPermanent: false, buildCard, buildDefinition: null);

    /// <summary>
    /// Construct a spell face: cast onto the stack with its own cost / effect.
    /// <paramref name="buildCard"/> materializes the live spell card;
    /// <paramref name="buildDefinition"/> supplies the resolve-time
    /// <see cref="SpellDefinition"/>.
    /// </summary>
    public static MdfcFace Spell(
        string name,
        string manaCost,
        Func<Player, ICard> buildCard,
        Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition> buildDefinition) =>
        new(name, manaCost, isLand: false, isPermanent: false, (owner, _) => buildCard(owner), buildDefinition);

    /// <summary>
    /// Construct a PERMANENT face (creature / artifact / enchantment /
    /// planeswalker): cast onto the stack with its own cost; on resolution
    /// the materialized permanent enters the battlefield as this face (CR
    /// 608.3, 712.3). <paramref name="buildCard"/> materializes the live
    /// permanent card (wired to the <see cref="ReplacementBus"/> so any ETB
    /// replacement registers); <paramref name="buildDefinition"/> supplies the
    /// resolve-time <see cref="SpellDefinition"/> that puts that permanent onto
    /// the battlefield.
    /// </summary>
    public static MdfcFace Permanent(
        string name,
        string manaCost,
        Func<Player, ReplacementBus?, ICard> buildCard,
        Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition> buildDefinition) =>
        new(name, manaCost, isLand: false, isPermanent: true, buildCard, buildDefinition);

    private MdfcFace(
        string name,
        string manaCost,
        bool isLand,
        bool isPermanent,
        Func<Player, ReplacementBus?, ICard> buildCard,
        Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition>? buildDefinition)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
        Name = name;
        ManaCost = manaCost ?? "";
        IsLand = isLand;
        IsPermanent = isPermanent;
        _buildCard = buildCard ?? throw new ArgumentNullException(nameof(buildCard));
        _buildDefinition = buildDefinition;
    }

    /// <summary>Materialize a fresh runtime card instance for this face,
    /// owned by <paramref name="owner"/>, wired to <paramref name="replacements"/>
    /// (used by a land / permanent face's ETB replacement; ignored by
    /// instant / sorcery spell faces).</summary>
    public ICard BuildCard(Player owner, ReplacementBus? replacements = null) =>
        _buildCard(owner, replacements);

    /// <summary>Build the resolve-time spell definition for a non-land face.
    /// Throws for a land face (lands have no <see cref="SpellDefinition"/>).</summary>
    public SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        ZoneService? zones)
    {
        if (_buildDefinition == null)
        {
            throw new InvalidOperationException(
                $"MDFC face '{Name}' is a land face and has no spell definition.");
        }
        return _buildDefinition(caster, targetResolver, stack, zones);
    }
}
