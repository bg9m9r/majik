using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Maha, Its Feathers Night (Bloomburrow,
/// {3}{B}{B}). Legendary Creature — Elemental Bird 6/5. Oracle text
/// (verified against Scryfall):
///   "Flying, trample
///    Ward—Discard a card.
///    Creatures your opponents control have base toughness 1."
///
/// The card's base shape (name, Legendary supertype, Elemental Bird
/// subtypes, {3}{B}{B}, 6/5) is materialised from the embedded JSON
/// definition (<c>maha-its-feathers-night.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Flying/Trample keywords, Ward—Discard, the opponents'-base-toughness
/// static) are layered on top here — the JSON <c>AbilityDefinition</c>
/// schema doesn't express keyword markers, Ward triggers, or dynamic-group
/// statics, so they live in the factory (same posture as
/// <see cref="StormscaleScionFactory"/> and the other JSON-backed cards
/// whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9) + Trample (CR 702.19)</b> — <see cref="KeywordAbility"/>
///   markers consumed by <c>CombatAbilities</c> / <c>CombatValidator</c>.
/// - <b>Ward—Discard a card (CR 702.21)</b> — a non-mana ward. Shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker (uniform discovery surface)
///   PLUS a real <see cref="TriggeredAbility"/> over
///   <see cref="Majik.Core.Domain.DomainEvents.TargetsChosenEvent"/> wired by
///   the shared <see cref="WardTriggerWiring.Attach"/> helper. The bound
///   <see cref="WardEffect"/> charges a <see cref="DiscardACardCost"/>
///   (CR 702.21c) when an opponent's spell OR ability targets Maha; if the
///   opponent can't (or won't) discard, the spell/ability is countered
///   (CR 701.5b). Maha's printed ward reads "a spell or ability" (the default
///   <see cref="WardTriggerWiring.WardTriggerKind.SpellOrAbility"/>), unlike
///   Reality Smasher's spell-only ward.
/// - <b>Opponents'-base-toughness static (CR 613.7b)</b>: "Creatures your
///   opponents control have base toughness 1." Wired via the new
///   <see cref="BaseToughnessSetEffect"/> (Layer 7b set-base-toughness scoped
///   to a dynamic opponents'-creatures group — the toughness-only,
///   dynamic-group sibling of <see cref="BecomesPTEffect"/> built on the same
///   opponents-scope filter as <see cref="LordStaticEffect"/>). Only base
///   TOUGHNESS is overwritten; base power is left as printed, and Layer 7c
///   pump / +1/+1 counters still pile on top (CR 613.7). Registered only when
///   a <see cref="ContinuousEffectsService"/> is supplied; the effect's
///   <see cref="BaseToughnessSetEffect.IsActive"/> battlefield gate lifts the
///   debuff when Maha leaves play (same posture as
///   <see cref="StormscaleScionFactory"/>'s lord static).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — the overload <see cref="NamedCardFactory"/>
///   dispatches to. Keywords + Ward trigger are attached to the card shape so
///   the trigger registers via <see cref="TriggerManager"/> the first time
///   Maha crosses onto the battlefield (CR 603.6a); the base-toughness static
///   is NOT registered (no continuous-effects service).
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?)"/>
///   — fully-wired overload: registers the base-toughness static and the Ward
///   trigger against the supplied services.
/// </summary>
[CardName("Maha, Its Feathers Night")]
public static class MahaItsFeathersNightFactory
{
    public const string CardName = "Maha, Its Feathers Night";
    public const string Slug = "maha-its-feathers-night";

    /// <summary>The base toughness Maha sets opponents' creatures to.</summary>
    public const int OpponentBaseToughness = 1;

    /// <summary>
    /// CR 702.21 — Maha's printed Ward effect, bound to the supplied
    /// <paramref name="card"/>. The ward cost is the non-mana "discard a card"
    /// rider, modelled via <see cref="DiscardACardCost"/> (mana portion
    /// <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/>). Same shape as
    /// <see cref="RealitySmasherFactory.BuildWardEffect"/>.
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new DiscardACardCost());

    /// <summary>
    /// Construct Maha with no continuous-effects service: keyword markers +
    /// the Ward trigger are attached to the card shape (the trigger
    /// auto-registers on battlefield entry via <see cref="TriggerManager"/>);
    /// the base-toughness static is not registered. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Maha.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// opponents'-base-toughness static against. Pass null to skip it.</param>
    /// <param name="triggers">TriggerManager — when supplied the Ward trigger
    /// is registered so a matching <see cref="Majik.Core.Domain.DomainEvents.TargetsChosenEvent"/>
    /// surfaces as pending. May be null — the trigger is still attached to the
    /// card shape.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Elemental Bird, {3}{B}{B}, 6/5). The JSON carries no abilities —
        // Flying / Trample / Ward / the static are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. CR 702.19 — Trample. CR 702.21 — Ward (marker).
        // Flying/Trample are keyword markers consumed by CombatAbilities /
        // CombatValidator; the Ward marker pairs with the real triggered
        // ability wired below.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // ----------------------------------------------------------------
        // Ward—Discard a card — CR 702.21e/702.21c/701.5b.
        //   "Whenever this permanent becomes the target of a spell or ability
        //    an opponent controls, counter that spell or ability unless its
        //    controller discards a card."
        // Wired by the shared WardTriggerWiring helper (same as Kappa
        // Cannoneer). Default kind = SpellOrAbility (Maha's printed wording).
        // ----------------------------------------------------------------
        WardTriggerWiring.Attach(BuildWardEffect(card), owner, triggers: triggers);

        // ----------------------------------------------------------------
        // Opponents'-base-toughness static — CR 613.7b.
        //   "Creatures your opponents control have base toughness 1."
        // Dynamic-group Layer 7b set-base-toughness (toughness only; base
        // power is untouched). opponentsOnly: true scopes it to creatures
        // controlled by Maha's opponents (CR 109.5), recomputed live so
        // later-entering opponent creatures are covered.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new BaseToughnessSetEffect(
                source: card,
                baseToughness: OpponentBaseToughness,
                opponentsOnly: true));
        }

        return card;
    }
}
