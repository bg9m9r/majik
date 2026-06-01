using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Emeria's Call // Emeria, Shattered Skyclave (Zendikar Rising,
/// {4}{W}{W}{W}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Create two 4/4 white Angel Warrior creature tokens with flying.
///    Non-Angel creatures you control gain indestructible until your next
///    turn."
///
/// Back face — <see cref="EmeriaShatteredSkyclaveFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AgadeemsAwakeningFactory"/> /
/// <see cref="AgadeemTheUndercryptFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>emerias-call.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor token creation).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{4}{W}{W}{W}</c>, mono-white (three {W} pips).
/// - <see cref="MdfcState"/> attached (front = "Emeria's Call",
///   back = "Emeria, Shattered Skyclave"); starts on the front face.
/// - No modes, no X, no target requests — resolves entirely on the caster.
/// - Resolution:
///     <list type="bullet">
///       <item>Create two 4/4 white Angel Warrior creature tokens with
///         flying via <see cref="TokenFactory.CreateOnBattlefield"/>
///         (CR 111 / 111.4) — same path as
///         <see cref="KrenkosCommandFactory"/>.</item>
///       <item>"Non-Angel creatures you control gain indestructible" —
///         enumerate the caster's battlefield creatures, EXCLUDE Angels
///         (CR 205.3 subtype check), and register a
///         <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///         "Indestructible" (CR 613.1f Layer 6) on each. Same indestructible-
///         grant body as <see cref="BorosCharmFactory"/>'s mode 1.</item>
///     </list>
///   The non-Angel grant is computed from the battlefield state BEFORE the
///   two Angel tokens are minted, so the freshly-created Angels are never
///   captured (they are Angels anyway and thus excluded). Order is irrelevant
///   to correctness — the Angel-subtype filter excludes them either way.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Until your next turn" duration</b>: collapsed to until-end-of-turn
///   (<see cref="GrantKeywordUntilEndOfTurnEffect.ExpiresAtEndOfTurn"/> = true,
///   expiring in the cleanup step CR 514.2). The exact controller-keyed
///   "your next turn" boundary is not modelled by
///   <see cref="ContinuousEffectsService"/> — same documented approximation
///   as <see cref="KarnTheGreatCreatorFactory"/> /
///   <see cref="KarnAnimateArtifactEffect"/> and The One Ring. This is a
///   shorter-than-correct duration (indestructible falls off at this turn's
///   cleanup instead of the start of the caster's next turn), but it is the
///   established posture for "until your next turn" effects in v1.
/// - <b>Indestructible grant scope</b>: requires a live
///   <see cref="ContinuousEffectsService"/>; the single-arg shape-only path
///   creates the tokens but performs no layer registration (mirrors
///   <see cref="BorosCharmFactory"/>).
///
/// ## References
///
/// - <see cref="AgadeemsAwakeningFactory"/> — companion ZNR MDFC front face
///   with the same MdfcState shape.
/// - <see cref="KrenkosCommandFactory"/> — "create N tokens" resolve body.
/// - <see cref="BorosCharmFactory"/> — indestructible-grant body.
/// </summary>
[CardName("Emeria's Call")]
public static class EmeriasCallFactory
{
    public const string CardName = "Emeria's Call";
    public const string BackName = "Emeria, Shattered Skyclave";

    public const int TokenPower = 4;
    public const int TokenToughness = 4;

    /// <summary>
    /// Construct Emeria's Call as a Sorcery (identity from JSON) with the
    /// <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("emerias-call");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (deferral #3, real cast-either-face). The
        // back face is the LAND back face played with no stack; MdfcCastFlow
        // offers the controller a face choice at cast time and materializes
        // a fresh back-face land instance when chosen. No transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                EmeriaShatteredSkyclaveFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. No modes, no X,
    /// no target requests — the body resolves entirely on the caster.
    /// </summary>
    /// <param name="caster">Spell controller — tokens enter under, and the
    /// indestructible grant applies to creatures controlled by, this
    /// player.</param>
    /// <param name="zones">Optional. When supplied the token creation routes
    /// through <see cref="ZoneService.MoveCardTo"/> so ETB triggers fire
    /// (CR 603.6a).</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service. Required for the indestructible grant to register the layer-6
    /// effects; when null the grant performs no registration (shape-only
    /// path — tokens are still created).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zones = null,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, zones, continuousEffects));
    }

    /// <summary>
    /// Build the resolve effects: grant indestructible to the caster's
    /// non-Angel creatures, then create the two Angel tokens.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zones = null,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: create two 4/4 white Angel Warrior tokens with flying; non-Angel creatures you control gain indestructible.",
                () =>
                {
                    // "Non-Angel creatures you control gain indestructible
                    // until your next turn." Computed against the battlefield
                    // BEFORE the Angel tokens are minted (the new tokens are
                    // Angels and thus excluded regardless of ordering).
                    GrantIndestructibleToNonAngels(caster, continuousEffects);

                    // "Create two 4/4 white Angel Warrior creature tokens with
                    // flying." (CR 111 / 111.4)
                    var spec = new TokenFactory.TokenSpec(
                        Name: "Angel Warrior",
                        Power: TokenPower,
                        Toughness: TokenToughness,
                        Subtypes: new[] { CardSubtype.Angel, CardSubtype.Warrior },
                        Keywords: new[] { "Flying" },
                        Colors: new[] { ManaColor.White });

                    // CR 111 — one token per "create"; create two.
                    TokenFactory.CreateOnBattlefield(spec, caster, zones);
                    TokenFactory.CreateOnBattlefield(spec, caster, zones);
                }),
        };
    }

    /// <summary>
    /// CR 613.1f / 702.12 — grant indestructible to every non-Angel creature
    /// the caster controls. "Until your next turn" is approximated as until
    /// end of turn (see class doc); the grant requires a live
    /// <see cref="ContinuousEffectsService"/>.
    /// </summary>
    private static void GrantIndestructibleToNonAngels(
        Player caster,
        ContinuousEffectsService? continuousEffects)
    {
        if (continuousEffects == null) return;

        foreach (var creature in caster.Zones.Battlefield
            .GetCards()
            .OfType<Creature>()
            // CR 205.3 — "non-Angel": exclude any creature with the Angel
            // subtype on its printed type line.
            .Where(c => !c.HasSubtype(CardSubtype.Angel))
            .ToList())
        {
            continuousEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
        }
    }
}
