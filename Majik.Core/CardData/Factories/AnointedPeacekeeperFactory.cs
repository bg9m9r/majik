using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Anointed Peacekeeper (Dominaria United,
/// {1}{W}{W} — Creature — Human Cleric 2/3).
///
/// Oracle text:
///   "Vigilance.
///    As Anointed Peacekeeper enters the battlefield, look at an
///    opponent's hand, then choose any card name.
///    Activated abilities of sources with the chosen name cost {2} more
///    to activate unless they're mana abilities.
///    Spells with the chosen name cost {2} more to cast."
///
/// ## Implemented (v1)
/// - Creature shape: Legendary-class identity is NOT printed (Anointed
///   Peacekeeper is not Legendary) — Human Cleric 2/3 with {1}{W}{W}.
/// - <b>Vigilance</b> (CR 702.20) — wired as a <see cref="KeywordAbility"/>
///   marker; combat code reads
///   <see cref="Majik.Core.Combat.CombatAbilities.HasVigilance"/>.
/// - <b>Spell-name cost increase</b> (CR 117.7 / CR 601.2f) — Anointed
///   Peacekeeper's "spells with the chosen name cost {2} more to cast"
///   half is wired via <see cref="SpellCostIncreaseAbility"/> whose
///   predicate compares the cast spell's <see cref="ICard.Name"/> against
///   the chosen name closure. The increase is a flat <c>+{2}</c> generic
///   when the predicate matches (symmetric — applies to either player's
///   matching spells, same posture as <see cref="ThaliaGuardianOfThrabenFactory"/>
///   and <see cref="DampingSphereFactory"/>).
///   <see cref="Majik.Core.Costs.CostReduction.GetEffectiveCost"/> walks
///   every player's battlefield for <see cref="SpellCostIncreaseAbility"/>
///   riders, so the {2} tax applies regardless of whose turn it is.
///
/// ## Look-at-opponent-hand + name choice
///
/// The factory accepts a <c>nameSelector</c> closure
/// (<c>Func&lt;Player, string&gt;</c>) the same way
/// <see cref="PithingNeedleFactory"/> does. Tests and bots supply the
/// chosen name directly. The <see cref="SpellCostIncreaseAbility"/>
/// predicate captures the closure-returned name lazily on first evaluation
/// — the name is resolved at most once per Anointed Peacekeeper instance,
/// matching the "as ~ enters" timing.
///
/// The "look at an opponent's hand" rider is observational in v1 — the
/// engine doesn't yet emit a peek event the agent layer can pipe to a UI.
/// The name choice happens with the hand-look context elided; bots that
/// want to read opponent hand contents to inform the choice can do so
/// directly through <see cref="Player.Zones.Hand"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>"Activated abilities of sources with the chosen name cost {2}
///   more to activate unless they're mana abilities."</b> There is no
///   reusable activated-ability cost-tax primitive yet — the cost is
///   computed inside
///   <see cref="Majik.Core.Abilities.ActivatedAbility"/>'s own activation
///   path, which doesn't consult a battlefield-wide scanner the way
///   <see cref="Majik.Core.Costs.CostReduction.GetEffectiveCost"/> does
///   for spells. Tracked alongside Damping Sphere's "second activated
///   ability per turn taxed" gap and Sphere of Resistance's activated-
///   ability companion clause as a single
///   <c>ActivatedAbilityCostIncreaseAbility</c> primitive future-fix.
///   The shipped Peacekeeper is shape-correct for the spell-tax half;
///   the activated-tax half is the documented gap.
/// - <b>"As ~ enters" timing</b>: same posture as
///   <see cref="PithingNeedleFactory"/> — the choice resolves on first
///   predicate evaluation rather than during the ETB replacement
///   (CR 614.12). Observationally equivalent in the current pipeline.
/// - <b>"Look at an opponent's hand"</b>: no peek-event emission (same
///   gap as Aven Mindcensor / Drannith Magistrate companion riders).
/// - <b>Flicker reprompt</b>: a flickered Anointed Peacekeeper re-enters
///   as a new object and would re-choose. Today the closure caches its
///   first answer for the lifetime of the factory instance; a fresh
///   Create call reprompts. Acceptable until the
///   <see cref="PithingNeedleStaticEffect"/>-style lifecycle is extended
///   to spell-cost riders.
/// </summary>
[CardName("Anointed Peacekeeper")]
public static class AnointedPeacekeeperFactory
{
    public const string CardName = "Anointed Peacekeeper";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Anointed Peacekeeper with no name-selector wired. The
    /// <see cref="SpellCostIncreaseAbility"/> is still attached for shape
    /// but its predicate matches no spell (no name to compare against),
    /// so the tax is dormant. Suitable for card-shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, nameSelector: null);

    /// <summary>
    /// Construct a fully-wired Anointed Peacekeeper.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen card name. Called
    /// with the Peacekeeper's controller on first predicate evaluation
    /// and cached for the lifetime of this instance. May be null — the
    /// spell-tax rider is dormant in that case.</param>
    public static Creature Create(Player owner, Func<Player, string>? nameSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.20 — Vigilance.
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // CR 117.7 / CR 601.2f — "Spells with the chosen name cost {2}
        // more to cast." Captured-name closure with one-shot resolution:
        // first time the predicate is consulted, the selector is invoked
        // with the Peacekeeper's controller; the resolved name is cached
        // for the lifetime of this instance.
        string? chosenName = null;
        bool selectorAttempted = false;

        bool MatchesChosenName(ICard spell)
        {
            if (nameSelector == null) return false;
            if (!selectorAttempted)
            {
                selectorAttempted = true;
                var picked = nameSelector(owner);
                chosenName = string.IsNullOrEmpty(picked) ? null : picked;
            }
            if (chosenName == null) return false;
            return string.Equals(spell.Name, chosenName, StringComparison.Ordinal);
        }

        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: MatchesChosenName,
            extraGeneric: (_, _) => 2,
            description: "Spells with the chosen name cost {2} more to cast."));

        return card;
    }
}
