using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anointed Peacekeeper (Dominaria United,
/// {1}{W}{W}).
///
/// Creature — Human Cleric 2/4. Oracle text:
///   "As Anointed Peacekeeper enters, look at an opponent's hand, then
///    choose any card name.
///    Activated abilities of sources with the chosen name cost {2} more
///    to activate unless they're mana abilities.
///    Spells with the chosen name cost {2} more to cast."
///
/// ## Why a named factory
/// "As ~ enters, choose a card name" gates a static cost-increase on the
/// chosen name across both spell-cast cost and activated-ability cost.
/// The spell-cast half plugs into the existing
/// <see cref="SpellCostIncreaseAbility"/> rider that
/// <see cref="CostReduction.GetEffectiveCost"/> consults at cast time
/// (same hook <see cref="ThaliaGuardianOfThrabenFactory"/> uses for its
/// "noncreature spells cost {1} more" tax). The activated-ability half
/// has no shared primitive in v1 — it's a documented gap.
///
/// ## Implemented (v1)
/// - 2/4 Creature — Human Cleric at {1}{W}{W}, owner / controller wired.
/// - <b>Spell cost increase</b> (the printed "Spells with the chosen
///   name cost {2} more to cast" half): a closure-captured chosen name
///   feeds a <see cref="SpellCostIncreaseAbility"/> on the card itself.
///   Predicate matches on <see cref="ICard.Name"/> equality (case-
///   sensitive — Magic card names are canonically cased per Oracle, so
///   no normalisation is needed for the printed-name comparison; same
///   posture as <see cref="PithingNeedleFactory"/>'s name registry).
///   <see cref="SpellCostIncreaseAbility.ExtraGeneric"/> returns +{2}
///   when the predicate matches and 0 otherwise. Symmetric across
///   players — Peacekeeper taxes ANY caster's spells whose name matches,
///   matching the printed text. <see cref="CostReduction.GetEffectiveCost"/>
///   walks every player's battlefield for these riders at cast time so
///   the increase fires irrespective of which player is casting.
/// - <b>Name selector</b> (the printed "As Peacekeeper enters... choose
///   any card name" half): the factory accepts a
///   <c>Func&lt;Player, string?&gt;</c> closure that resolves the chosen
///   name. Same selector shape as <see cref="PithingNeedleFactory"/> —
///   bots / tests supply the name directly. A null selector or null
///   return-value disables the cost increase (predicate returns false
///   for every cast). The "look at an opponent's hand" preamble is a
///   v1 no-op — the selector closure is given the controller and decides
///   the name however the agent wants; once
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> grows a
///   <c>ChooseCardNameAsync</c> prompt the closure simply forwards to
///   it, and the prompt UI can render the hand-peek alongside.
/// - <b>"As ~ enters" choice timing</b>: same observational shortcut as
///   <see cref="PithingNeedleFactory"/> — the choice is resolved at ETB
///   time rather than as part of the ETB replacement (CR 614.12).
///   Functionally equivalent in the engine's current pipeline.
///
/// ## Deferred (v1 gaps)
/// - <b>Activated-ability cost increase</b> (printed half "Activated
///   abilities of sources with the chosen name cost {2} more to activate
///   unless they're mana abilities" — CR 605 mana-ability exemption):
///   no shared primitive in v1. The closest analogue is
///   <see cref="PithingNeedleFactory"/>'s ActivatedAbilityRestrictions
///   registry, which fully suppresses activations rather than taxing
///   them — and Damping Sphere / Sphere of Resistance face the same wall
///   for their "the third spell each turn costs more to cast" /
///   "activated abilities cost {1} more" deltas. Tracked as a shared
///   future-fix; once an <c>ActivatedAbilityCostIncreaseAbility</c>
///   primitive lands, Peacekeeper wires it with the same closure-captured
///   name. Until then the activated-ability tax is a no-op and the
///   factory documents the gap in this xmldoc. Per the printed-text
///   "If too complex, ship Peacekeeper with just spell cost increase"
///   minimum, the spell-cost half alone makes the card playable in the
///   common cases (Bant Spirits' Cavern of Souls, Affinity's Arcbound
///   Ravager activations are NOT taxed, but Spell-named lock pieces —
///   Boseiju, Karakas, Wasteland — are not the v1 use case).
/// - <b>"Look at an opponent's hand" preamble</b>: agent receives no
///   peek into opponent hands prior to the name choice. Once
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> grows a
///   <c>PeekOpponentHandThenChooseCardNameAsync</c> the closure can
///   forward; not used by any other card in v1 so no shared primitive
///   exists yet.
/// - <b>LTB unregister</b>: the <see cref="SpellCostIncreaseAbility"/>
///   lives on the card itself, so when Peacekeeper leaves the
///   battlefield the rider stops affecting cost calculations
///   automatically (<see cref="CostReduction.GetEffectiveCost"/> only
///   walks battlefield permanents — same posture as Thalia, Guardian of
///   Thraben's tax).
/// </summary>
[CardName("Anointed Peacekeeper")]
public static class AnointedPeacekeeperFactory
{
    public const string CardName = "Anointed Peacekeeper";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 2;
    public const int Toughness = 4;
    public const int CostIncrease = 2;

    /// <summary>
    /// Construct Anointed Peacekeeper with no name selector — the
    /// SpellCostIncreaseAbility rider is attached but its predicate
    /// always returns false (no name is chosen at ETB), so no spell is
    /// taxed. Suitable for card-shape / dispatcher tests where the
    /// chosen-name machinery doesn't need to fire.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, nameSelector: null);

    /// <summary>
    /// Construct Anointed Peacekeeper wired with a name selector. The
    /// selector is invoked once at ETB time (lazily on first cast-cost
    /// query via <see cref="ResolveChosenName"/>) with Peacekeeper's
    /// controller; the returned string is captured and feeds the
    /// SpellCostIncreaseAbility predicate. Return <see langword="null"/>
    /// from the selector to leave the static effect disabled.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen card name when the
    /// Peacekeeper enters the battlefield. Called with the Peacekeeper's
    /// controller. May be null — the SpellCostIncreaseAbility rider is
    /// still attached but its predicate always returns false, so no
    /// spell is taxed.</param>
    public static Creature Create(Player owner, Func<Player, string?>? nameSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // Closure-captured chosen name. Null = not yet resolved (or
        // selector declined). Lazily filled by ResolveChosenName on the
        // first cost-query that calls into Predicate / ExtraGeneric so
        // that "As Peacekeeper enters" timing is observationally
        // correct (same shortcut as PithingNeedleFactory's deferred-ETB
        // approach).
        string? chosenName = null;
        bool resolved = false;

        // Local helper so both Predicate and ExtraGeneric force resolution
        // before reading the name. Mirrors the closure-state pattern in
        // <see cref="DampingSphereFactory"/>'s "Nth spell this turn"
        // counters.
        string? ResolveChosenName(Player controller)
        {
            if (resolved) return chosenName;
            resolved = true;
            if (nameSelector is null) return chosenName;
            chosenName = nameSelector(controller);
            return chosenName;
        }

        // ----------------------------------------------------------------
        // CR 117.7 / CR 601.2f — "Spells with the chosen name cost {2} more
        // to cast." Symmetric across players — the rider is consulted for
        // every cast by CostReduction.GetEffectiveCost.
        // ----------------------------------------------------------------
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c =>
            {
                var name = ResolveChosenName(owner);
                return name is not null && c.Name == name;
            },
            extraGeneric: (_, _) => CostIncrease,
            description: $"Spells with the chosen name cost {{{CostIncrease}}} more to cast."));

        return card;
    }
}
