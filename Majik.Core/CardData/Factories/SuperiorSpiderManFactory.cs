using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Superior Spider-Man (Marvel's Spider-Man, {2}{U}{B}).
///
/// ## Card text (verified against Scryfall 2026-06-14)
/// Legendary Creature — Spider Human Hero, 4/4.
///   "Mind Swap — You may have Superior Spider-Man enter as a copy of any
///    creature card in a graveyard, except his name is Superior Spider-Man
///    and he's a 4/4 Spider Human Hero in addition to his other types. When
///    you do, exile that card."
///
/// ## Card identity comes from JSON
/// Name / Legendary supertype / Creature / Spider Human Hero subtypes / printed
/// cost {2}{U}{B} / printed 4/4 P/T are loaded from the embedded JSON definition
/// (<c>superior-spider-man.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The Mind Swap enters-as-copy
/// replacement is attached in code (the JSON ability schema models none of the
/// copy/override/exile riders).
///
/// ## Implemented (v1)
/// - 4/4 Legendary Creature — Spider Human Hero, {2}{U}{B}. UNLIKE the Clone /
///   Phantasmal Image family (printed 0/0 that dies to SBA when it fails to
///   copy, CR 704.5f), Superior Spider-Man is intrinsically a 4/4 — its printed
///   P/T is 4/4 and the "he's a 4/4" rider re-asserts 4/4 even after a copy. So
///   if Mind Swap finds no graveyard creature, he simply enters as a vanilla
///   4/4 Spider Human Hero rather than dying.
/// - <b>Mind Swap enters-as-copy (CR 706.9 / 706.2 / 706.3)</b> via a bespoke
///   <see cref="MindSwapReplacement"/> on the <see cref="ReplacementBus"/>:
///   <list type="bullet">
///     <item>Picks a creature card in a graveyard (v1 lossy — the controller's
///           graveyard only, matching
///           <see cref="EntersAsCopyReplacement.CopyPool.GraveyardAny"/>'s
///           documented "any graveyard → controller's graveyard" posture).</item>
///     <item>Registers a <see cref="CopyEffect"/> (Layer 1, CR 707.2) so the
///           copy mirrors the source creature's printed P/T + keywords.</item>
///     <item><b>"his name is Superior Spider-Man" (CR 706.3)</b> — name is NOT
///           a copiable value our <see cref="CopyEffect"/> overwrites (it copies
///           P/T + keywords only), so the printed name survives the copy with no
///           extra rider needed.</item>
///     <item><b>"he's a 4/4" (CR 706.3 / 613.7b)</b> — a Layer-7b
///           <see cref="BecomesPTEffect"/>(4, 4) re-asserts 4/4 ON TOP of the
///           Layer-1 copied P/T, so he is a 4/4 regardless of the copied
///           creature's size.</item>
///     <item><b>"Spider Human Hero in addition to its other types" (CR 706.3 /
///           613.1d Layer 4)</b> — three <see cref="AddSubtypeEffect"/> riders
///           (Spider, Human, Hero). The printed subtypes are already on the
///           card; the riders keep them present under a future CopyEffect that
///           mirrors subtypes (today CopyEffect handles P/T + keywords only).</item>
///     <item><b>"When you do, exile that card"</b> — the picked source card is
///           moved Graveyard → Exile via
///           <see cref="OracleSpellBinder.MoveToExile"/> as part of the
///           replacement (modelled inline rather than as a separate reflexive
///           trigger, since the exile is a consequence of the same
///           enters-as-copy event — CR 614 replacement plus its rider).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>"any graveyard"</b> — the pick scans the controller's graveyard only
///   (same lossiness as <see cref="EntersAsCopyReplacement.CopyPool.GraveyardAny"/>;
///   plumbing an all-players resolver into a replacement is the broader gap).
/// - <b>"You may" choice</b> — auto-yes when any candidate exists; no agent
///   prompt yet (shared posture with <see cref="EntersAsCopyReplacement"/>).
///   Tests model "decline" by leaving the graveyard empty → enters as the
///   printed vanilla 4/4.
/// </summary>
[CardName("Superior Spider-Man")]
public static class SuperiorSpiderManFactory
{
    public const string CardName = "Superior Spider-Man";
    public const string Slug = "superior-spider-man";

    /// <summary>
    /// Shape-only overload dispatched by <see cref="NamedCardFactory"/>. Identity
    /// (name / Legendary / Creature / Spider Human Hero / {2}{U}{B} / 4/4) comes
    /// from the embedded JSON; the Mind Swap replacement is NOT registered on
    /// this path (no <see cref="ReplacementBus"/> available).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, effects: null);

    /// <summary>
    /// Construct Superior Spider-Man with optional replacement-bus +
    /// continuous-effects wiring. When both are supplied the Mind Swap
    /// enters-as-copy replacement (CR 706.9 / 706.2 / 706.3) is registered.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Spider Human Hero subtypes, {2}{U}{B}, printed 4/4). The
        // JSON carries no abilities — the Mind Swap replacement is layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        if (replacements != null && effects != null)
        {
            replacements.Register(new MindSwapReplacement(card, effects));

            // Plumb ContinuousEffects into the card so P/T / subtype reads
            // consult the layer system (CR 613). The CopyEffect +
            // BecomesPTEffect + AddSubtypeEffects registered by the
            // replacement's Replace() callback are read back via
            // Creature.GetPower/GetToughness / HasSubtype.
            card.ActiveEffects = effects;
        }

        return card;
    }

    /// <summary>
    /// CR 706.9 / 706.2 / 706.3 — "Mind Swap": Superior Spider-Man may enter as
    /// a copy of a creature card in a graveyard, except his name is Superior
    /// Spider-Man and he's a 4/4 Spider Human Hero in addition to his other
    /// types; when he does, that card is exiled. Modelled as a dedicated ETB
    /// replacement rather than reusing <see cref="EntersAsCopyReplacement"/>
    /// because no existing <see cref="EntersAsCopyReplacement.Options"/> shape
    /// combines a fixed-P/T override, a three-subtype add, AND the
    /// exile-the-source rider — building those into the generalized replacement
    /// would over-fit it to this one card.
    /// </summary>
    private sealed class MindSwapReplacement : IReplacementEffect<ZoneMoveIntent>
    {
        private readonly Creature _card;
        private readonly ContinuousEffectsService _effects;

        public MindSwapReplacement(Creature card, ContinuousEffectsService effects)
        {
            _card = card ?? throw new ArgumentNullException(nameof(card));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        public bool OneShot => false;
        public object? Tag => this;

        public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
            ReferenceEquals(intent.Card, _card)
            && intent.ToZone == ZoneType.Battlefield
            && intent.FromZone != ZoneType.Battlefield;

        public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history)
        {
            var controller = intent.Controller ?? _card.Owner;
            var source = PickGraveyardCreature(controller);

            // "You may" — auto-yes when a candidate exists; with none, Superior
            // Spider-Man simply enters as the printed vanilla 4/4 (no SBA death
            // unlike the printed-0/0 Clone family — he is intrinsically a 4/4).
            if (source == null) return intent;

            // CR 707.2 — Layer 1 copy of the source creature's printed P/T +
            // keywords. Name is NOT mirrored by CopyEffect, so "his name is
            // Superior Spider-Man" (CR 706.3) holds with no extra rider.
            _effects.Register(new CopyEffect(_card, source));

            // CR 706.3 / 613.7b — "he's a 4/4". Layer 7b re-asserts 4/4 ON TOP
            // of the Layer-1 copied P/T, so the copied creature's size is
            // overridden.
            _effects.Register(new BecomesPTEffect(_card, 4, 4));

            // CR 706.3 / 613.1d Layer 4 — "Spider Human Hero in addition to its
            // other types".
            _effects.Register(new AddSubtypeEffect(_card, CardSubtype.Spider));
            _effects.Register(new AddSubtypeEffect(_card, CardSubtype.Human));
            _effects.Register(new AddSubtypeEffect(_card, CardSubtype.Hero));

            // "When you do, exile that card." — CR 614 rider: the source card is
            // exiled from its graveyard as a consequence of the copy.
            OracleSpellBinder.MoveToExile(source);

            return intent;
        }

        // v1 lossy: "any graveyard" → the controller's graveyard only (same
        // posture as EntersAsCopyReplacement.CopyPool.GraveyardAny). Deterministic
        // first-candidate pick (no agent picker through the replacement bus yet).
        private Creature? PickGraveyardCreature(Player? controller)
        {
            if (controller == null) return null;
            return controller.Zones.Graveyard.GetCards()
                .OfType<Creature>()
                .FirstOrDefault(c => !ReferenceEquals(c, _card));
        }
    }
}
