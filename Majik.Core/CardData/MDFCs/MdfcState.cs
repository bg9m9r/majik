using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.MDFCs;

/// <summary>
/// CR 711 — modal double-faced cards / transform cards. Two faces: front
/// and back; one is "active" at any time. Transform swaps active face.
/// MVP: tracks current face name + flag; characteristic-replacement
/// (Layer 1/4/etc.) deferred to a richer layer-system integration.
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
/// carry an optional <see cref="BackFace"/> descriptor (<see cref="MdfcFace"/>)
/// describing the OTHER (back) face's castable definition. When present,
/// the cast flow (<see cref="MdfcCastFlow"/>) offers a face choice; the
/// front face is cast through the normal path, while the back face is
/// materialized as its own card instance (a land back is played as a land
/// with no stack; a spell back goes on the stack with its own cost / effect).
/// No transform machinery is involved — the other face simply isn't there.
/// </summary>
public sealed class MdfcState
{
    public string FrontFaceName { get; }
    public string BackFaceName { get; }
    public bool IsBackFace { get; private set; }

    /// <summary>
    /// CR 712.3 — descriptor for the OTHER castable face of this MDFC, when
    /// known. On a front-face card this is the back face's castable
    /// definition (land / spell / permanent) so the cast flow can offer "cast
    /// either face". Null on the minimal face-tracker posture (older MDFC
    /// factories that only record the back-face NAME without a castable
    /// definition) and on back-face card instances (which are already the
    /// chosen face).
    ///
    /// <para>Distinct from <see cref="BackFaceCharacteristics"/>: this slot
    /// drives the #3 MDFC cast-either-face flow (the two MDFC faces do NOT
    /// transform — CR 712.4), whereas <see cref="BackFaceCharacteristics"/>
    /// drives the #19 transform-DFC Layer-0 face replacement (CR 711, the
    /// faces DO transform into each other).</para>
    /// </summary>
    public MdfcFace? CastableBackFace { get; }

    /// <summary>
    /// CR 711 — the printed characteristics of the TRANSFORM DFC's back face
    /// (P/T, types, subtypes, supertypes, keywords, colours). Non-null on a
    /// transform DFC whose factory supplies both faces; consumed by
    /// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> as the
    /// Layer-0 printed seed while <see cref="IsBackFace"/> is true.
    /// </summary>
    public BackFaceCharacteristics? BackFaceCharacteristics { get; }

    /// <summary>
    /// Callback fired immediately after <see cref="Transform"/> flips the
    /// active face. Wired by <see cref="Majik.Core.Cards.Card.MdfcState"/>'s
    /// setter to bump the <see cref="Majik.Core.Effects.ContinuousEffectsService"/>
    /// generation so the layered + scalar-P/T memoization caches invalidate on
    /// a face flip (the back-face Layer-0 seed changes the computed
    /// characteristics). Null on cards never wired to a CES.
    /// </summary>
    public Action? OnTransformed { get; set; }

    /// <summary>True when this card carries a castable back-face definition
    /// so the controller can choose either face at cast time (CR 712.3).</summary>
    public bool CanCastEitherFace => !IsBackFace && CastableBackFace != null;

    public MdfcState(string frontFaceName, string backFaceName)
        : this(frontFaceName, backFaceName, castableBackFace: null, backFaceCharacteristics: null)
    {
    }

    /// <summary>
    /// Construct the face tracker with an optional castable back-face
    /// descriptor (CR 712.3 — real cast-either-face). Supply
    /// <paramref name="castableBackFace"/> on a FRONT-face card so the cast
    /// flow can offer the choice.
    /// </summary>
    public MdfcState(string frontFaceName, string backFaceName, MdfcFace? castableBackFace)
        : this(frontFaceName, backFaceName, castableBackFace, backFaceCharacteristics: null)
    {
    }

    /// <summary>
    /// Construct the face tracker with an optional transform back-face
    /// characteristics carrier (CR 711 — Layer-0 face replacement). Supply
    /// <paramref name="backFaceCharacteristics"/> on a transform DFC so the
    /// continuous-effects service seeds the back face while back-face up.
    /// </summary>
    public MdfcState(string frontFaceName, string backFaceName, BackFaceCharacteristics? backFaceCharacteristics)
        : this(frontFaceName, backFaceName, castableBackFace: null, backFaceCharacteristics)
    {
    }

    public MdfcState(
        string frontFaceName,
        string backFaceName,
        MdfcFace? castableBackFace,
        BackFaceCharacteristics? backFaceCharacteristics)
    {
        if (string.IsNullOrWhiteSpace(frontFaceName)) throw new ArgumentException(nameof(frontFaceName));
        if (string.IsNullOrWhiteSpace(backFaceName)) throw new ArgumentException(nameof(backFaceName));
        FrontFaceName = frontFaceName;
        BackFaceName = backFaceName;
        CastableBackFace = castableBackFace;
        BackFaceCharacteristics = backFaceCharacteristics;
    }

    public string ActiveFaceName => IsBackFace ? BackFaceName : FrontFaceName;

    public void Transform()
    {
        IsBackFace = !IsBackFace;
        // CR 711 — the active face's characteristics just changed; invalidate
        // any CES memoization keyed on the old face's seed.
        OnTransformed?.Invoke();
    }
}

/// <summary>
/// CR 712.3 — castable definition of one face of a Modal Double-Faced Card.
/// Carries everything the cast flow needs to play that face: its name, the
/// printed mana cost paid for it, whether it is a land (played with no
/// stack) or a spell (goes on the stack with its own effect), and a builder
/// that materializes a fresh runtime card instance for the face.
///
/// <para>The <see cref="BuildCard"/> delegate returns the live card instance
/// for the face (e.g. the back-face Land for Soporific Springs, or a spell
/// card for a spell back). For a spell face, <see cref="BuildDefinition"/>
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
    /// False when it is a spell that goes on the stack.</summary>
    public bool IsLand { get; }

    /// <summary>True when this face is a nonland PERMANENT (artifact / creature
    /// / enchantment / planeswalker) cast as a spell that resolves onto the
    /// battlefield AS that permanent (CR 712.3 / 608.3). Distinct from
    /// <see cref="IsLand"/> (played, no stack) and from a non-permanent spell
    /// back (instant / sorcery — false for both flags).</summary>
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
        new(name, manaCost: "", isLand: true, buildCard, buildDefinition: null);

    /// <summary>
    /// Construct a nonland PERMANENT back face (CR 712.3 / 608.3): cast onto
    /// the stack as a spell with its own <paramref name="manaCost"/> / effect,
    /// resolving onto the battlefield AS that permanent (the
    /// <see cref="Majik.Core.Services.StackResolver"/> routes a permanent card
    /// to the battlefield by type). <paramref name="buildCard"/> materializes
    /// the live permanent instance (artifact / creature / enchantment);
    /// <paramref name="buildDefinition"/> supplies the resolve-time
    /// <see cref="SpellDefinition"/> (e.g. <see cref="SpellDefinition.Vanilla"/>
    /// for an ETB-less permanent). No transform — only the chosen face exists
    /// (CR 712.4).
    /// </summary>
    public static MdfcFace Permanent(
        string name,
        string manaCost,
        Func<Player, ICard> buildCard,
        Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition> buildDefinition) =>
        new(name, manaCost, isLand: false, (owner, _) => buildCard(owner), buildDefinition, isPermanent: true);

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
        new(name, manaCost, isLand: false, (owner, _) => buildCard(owner), buildDefinition);

    private MdfcFace(
        string name,
        string manaCost,
        bool isLand,
        Func<Player, ReplacementBus?, ICard> buildCard,
        Func<Player, Func<object, object>, Majik.Core.Stack.Stack?, ZoneService?, SpellDefinition>? buildDefinition,
        bool isPermanent = false)
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
    /// (used by a land face's ETB replacement; ignored by spell faces).</summary>
    public ICard BuildCard(Player owner, ReplacementBus? replacements = null) =>
        _buildCard(owner, replacements);

    /// <summary>Build the resolve-time spell definition for a SPELL face.
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
