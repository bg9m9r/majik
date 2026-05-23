using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plague Engineer (Core Set 2020 / reprints,
/// Creature — Human Rogue {2}{B} 2/2).
///
/// Oracle text:
///   "Deathtouch.
///    As Plague Engineer enters, choose a creature type.
///    Creatures of the chosen type your opponents control get -1/-1."
///
/// ## Implemented (v1)
/// - 2/2 Human Rogue with mana cost {2}{B} and correct identity / owner /
///   controller.
/// - <b>Deathtouch</b> wired as a <see cref="KeywordAbility"/> marker
///   (CR 702.2). <see cref="Majik.Core.Combat.CombatAbilities.HasDeathtouch"/>
///   consumes this marker.
/// - <b>ETB type choice</b> (CR 614.12 — "as ~ enters, choose a creature
///   type"): the chosen subtype is resolved eagerly via a
///   <c>Func&lt;Player, CardSubtype&gt;</c> selector on the 2-arg
///   <see cref="Create(Player, ContinuousEffectsService?, Func{Player, CardSubtype}?)"/>
///   overload. Same shape as
///   <see cref="CavernOfSoulsFactory"/>'s typeChooser — observationally
///   equivalent in the current ETB pipeline (engine has no
///   ChooseSubtype agent prompt yet). The choice is exposed via
///   <see cref="GetChosenType(Creature)"/> for tests/introspection.
/// - <b>Static "Creatures of the chosen type your opponents control get
///   -1/-1"</b>: wired via <see cref="LordStaticEffect"/> with
///   <c>opponentsOnly: true</c> and <c>power: -1, toughness: -1</c>.
///   Layer 7c (CR 613.7c — P/T modifications). The effect's
///   <see cref="ContinuousEffect.IsActive"/> already gates on the
///   source being on the battlefield, so LTB/flicker naturally lifts
///   the debuff (mirrors <see cref="ColossusHammerFactory"/>'s no-LTB
///   cleanup pattern). Same controller-capture caveat as
///   <see cref="OpponentArtifactActivatedSuppressionEffect"/>: the
///   "your opponents" set is whoever was not Plague Engineer's
///   controller at register time; control-change mid-game is a
///   follow-up.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-prompt integration</b>: <see cref="Majik.Core.Players.Agents.IPlayerAgent"/>
///   doesn't yet declare a ChooseSubtype prompt. Until that lands, the
///   factory accepts a <c>Func&lt;Player, CardSubtype&gt;</c> selector
///   closure — bots and tests supply the chosen type directly. Same
///   pattern as Pithing Needle's <c>nameSelector</c> and Cavern of
///   Souls's <c>typeChooser</c>.
/// - <b>Choice timing</b>: CR 614.12 says the choice is part of the ETB
///   replacement; v1 captures it eagerly at factory-build time.
///   Observationally equivalent in the current ETB pipeline (same note
///   as Cavern of Souls and Pithing Needle).
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; its <see cref="ContinuousEffect.IsActive"/> check
///   short-circuits when Plague Engineer isn't on the battlefield, so
///   the debuff lifts correctly, but a future Prune pass could drop
///   the entry. Same shape as Colossus Hammer.
/// </summary>
public static class PlagueEngineerFactory
{
    public const string CardName = "Plague Engineer";
    public const string Cost = "{2}{B}";
    public const int Power = 2;
    public const int Toughness = 2;

    // Per-card chosen type — same ConditionalWeakTable pattern as
    // CavernOfSoulsFactory. Keyed by the Creature instance so flickers
    // (which produce a new object) get a fresh choice.
    private static readonly
        System.Runtime.CompilerServices.ConditionalWeakTable<Creature, ChoiceBox>
        _chosenType = new();

    private sealed class ChoiceBox { public CardSubtype Value; }

    /// <summary>
    /// Construct a Plague Engineer with no live continuous-effects wiring
    /// and no ETB type choice resolved. Suitable for card-shape /
    /// dispatcher tests — the chosen-type slot is unset and
    /// <see cref="GetChosenType"/> returns null; no debuff effect is
    /// registered.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, typeChooser: null);

    /// <summary>
    /// Construct a fully-wired Plague Engineer. When
    /// <paramref name="continuousEffects"/> AND
    /// <paramref name="typeChooser"/> are both supplied, the ETB choice
    /// is resolved eagerly and a <see cref="LordStaticEffect"/> with
    /// <c>opponentsOnly: true</c> and <c>power: -1, toughness: -1</c> is
    /// registered against the layers service. Either being null skips
    /// effect registration (the card shape + Deathtouch are always
    /// wired).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// -1/-1 static effect against. May be null — no live debuff.</param>
    /// <param name="typeChooser">Resolves the chosen creature subtype
    /// at ETB time. Called with the Plague Engineer's controller. May
    /// be null — no live debuff (no chosen type means no scope).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<Player, CardSubtype>? typeChooser)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Rogue });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.2 — Deathtouch. CombatAbilities.HasDeathtouch consumes
        // the KeywordAbility marker.
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));

        if (typeChooser != null)
        {
            // v1: eager-resolve at factory time. CR 614.12 strictly says
            // the choice is made as part of the ETB replacement;
            // observationally equivalent in the current ETB pipeline
            // (mirrors Cavern of Souls / Pithing Needle).
            var chosen = typeChooser(owner);
            _chosenType.Add(card, new ChoiceBox { Value = chosen });

            if (continuousEffects != null)
            {
                // CR 613.7c — P/T modification. opponentsOnly flips the
                // controller filter so the debuff hits creatures NOT
                // controlled by Plague Engineer's controller. Source's
                // controller is captured by LordStaticEffect at register
                // time (control-change re-eval is a follow-up — same
                // caveat as OpponentArtifactActivatedSuppressionEffect).
                continuousEffects.Register(new LordStaticEffect(
                    source: card,
                    matchingSubtype: chosen,
                    power: -1,
                    toughness: -1,
                    grantedKeywords: null,
                    includeSelf: false,
                    opponentsOnly: true));
            }
        }

        return card;
    }

    /// <summary>
    /// Returns the chosen creature subtype if one was resolved at
    /// construction time, else null. Per-card (not per-factory) — a
    /// flickered Plague Engineer is a new object and chooses again.
    /// </summary>
    public static CardSubtype? GetChosenType(Creature plagueEngineer)
    {
        ArgumentNullException.ThrowIfNull(plagueEngineer);
        return _chosenType.TryGetValue(plagueEngineer, out var box) ? box.Value : null;
    }
}
