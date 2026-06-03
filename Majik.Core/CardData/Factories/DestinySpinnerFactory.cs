using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Destiny Spinner (Theros Beyond Death, {1}{G}).
///
/// Enchantment Creature — Human 2/3. Oracle text (Scryfall, verified
/// 2026-06-02):
///   "Creature and enchantment spells you control can't be countered.
///    {3}{G}: Target land you control becomes an X/X Elemental creature with
///    trample and haste until end of turn, where X is the number of
///    enchantments you control. It's still a land."
///
/// Hand-rolled factory (not JSON-driven): the JSON
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
/// pipeline expresses neither the controller-scoped can't-be-countered static
/// nor the targeted dynamic-P/T land animate, so both are composed here.
///
/// ## Implemented (v1)
/// - 2/3 Enchantment Creature — Human at {1}{G}.
/// - <b>"Creature and enchantment spells you control can't be countered"
///   (CR 701.5b)</b> — wired as a controller-scoped
///   <see cref="UncounterableControllerStatic"/> marker covering
///   {<see cref="CardType.Creature"/>, <see cref="CardType.Enchantment"/>}.
///   <see cref="Majik.Core.Game.SpellCastFlow"/> scans the caster's
///   battlefield at cast time and, when a live marker covers the cast spell's
///   type, stamps <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> on
///   the resolving spell — which every counter primitive
///   (<c>Fx.Counter</c> + the counter templates) and
///   <c>OracleSpellBinder.RemoveFromStack</c> already honour. The marker is
///   battlefield-gated (only counts while Destiny Spinner is on the
///   battlefield) and controller-scoped (only the marker's controller
///   benefits).
/// - <b>"{3}{G}: Target land you control becomes an X/X Elemental creature
///   with trample and haste until end of turn, where X is the number of
///   enchantments you control. It's still a land." (CR 602 / CR 613)</b> —
///   an <see cref="ActivatedAbility"/> with a {3}{G}
///   <see cref="ManaCostCost"/> whose resolution animates the chosen target
///   land via the shared manland primitives: a
///   <see cref="ManlandCycleAnimateEffect"/> (Layer 4 — add Creature +
///   Elemental subtype + Trample + Haste; printed Land type stays, CR 613.1c)
///   and a <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b — set base P/T
///   X/X), both flagged <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/>.
///   X is recomputed at resolution time (CR 608.2 — "as the ability resolves")
///   as the number of enchantments the controller controls. Mirrors
///   <see cref="KothOfTheHammerFactory"/>'s "+1: animate target Mountain"
///   shape (resolver-supplied target, no agent target prompt yet).
///
/// ## v1 posture
/// - <b>Target selection</b> — like the manland animate cluster, the chosen
///   land is supplied by a resolver injected at construction rather than via an
///   agent <see cref="Majik.Core.Targeting.TargetRequest"/>; v1 picks the first
///   land the controller controls. The resolver must only return lands the
///   controller controls (CR 115.4 — "target land you control"). Same
///   restricted-target posture as Koth's +1.
/// - <b>Animated colour</b> — Theros prints no colour for the Elemental body,
///   so no Layer-5 colour grant is registered (the body inherits the land's
///   colour identity, typically colourless). No colour gap.
/// </summary>
[CardName("Destiny Spinner")]
public static class DestinySpinnerFactory
{
    public const string CardName = "Destiny Spinner";
    public const string PrintedManaCost = "{1}{G}";
    public const string ActivationCost = "{3}{G}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Destiny Spinner with no continuous-effects service / target
    /// resolver wired. The static marker is attached (so the can't-be-countered
    /// behaviour works through <see cref="Majik.Core.Game.SpellCastFlow"/>) and
    /// the activated ability is attached but its animate resolution is a no-op
    /// (no target, no effect service). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffects: null, targetLandResolver: null);

    /// <summary>
    /// Construct Destiny Spinner with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Continuous-effects service for the
    /// animate ability's Layer 4 / Layer 7b registration. May be null — the
    /// ability resolves but no animation is recorded.</param>
    /// <param name="targetLandResolver">Returns the candidate "target land you
    /// control" for the {3}{G} ability. v1 animates the first land returned.
    /// May be null — the ability no-ops.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        Func<IReadOnlyList<Land>>? targetLandResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human });

        // Destiny Spinner is an Enchantment Creature (CR 305 / CR 302).
        card.AddCardType(CardType.Enchantment);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Creature and enchantment spells you control can't be countered."
        // CR 701.5b — controller-scoped static. Read at cast time by
        // SpellCastFlow, which stamps Spell.CannotBeCountered on any creature
        // or enchantment spell this card's controller casts while Destiny
        // Spinner is on the battlefield.
        // ----------------------------------------------------------------
        card.AddAbility(new UncounterableControllerStatic(
            card,
            owner,
            cardTypes: new[] { CardType.Creature, CardType.Enchantment }));

        // ----------------------------------------------------------------
        // {3}{G}: Target land you control becomes an X/X Elemental creature
        // with trample and haste until end of turn, where X is the number of
        // enchantments you control. It's still a land.
        //
        // CR 602 (activated) + CR 613 (animate; "still a land", CR 613.1c).
        // X is recomputed at resolution (CR 608.2). v1 animates the first land
        // returned by the resolver (no agent target prompt yet — mirrors
        // Koth of the Hammer's +1).
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: target land you control becomes an X/X Elemental with trample + haste until EOT (X = enchantments you control; still a land)",
            () =>
            {
                if (continuousEffects == null) return; // shape-only path
                var candidates = targetLandResolver?.Invoke();
                if (candidates == null) return;

                var controller = card.Controller ?? owner;

                foreach (var land in candidates)
                {
                    if (land == null) continue;
                    if (land.Zone != ZoneType.Battlefield) continue;
                    // "land you control" (CR 115.4) — restrict to the controller's lands.
                    if (!ReferenceEquals(land.Controller, controller)) continue;

                    // X = number of enchantments the controller controls,
                    // computed as the ability resolves (CR 608.2).
                    int x = controller.Zones.Battlefield.GetCards()
                        .Count(c => c.HasType(CardType.Enchantment));

                    // Layer 4 — add Creature + Elemental subtype + Trample +
                    // Haste. Printed Land type stays ("It's still a land").
                    continuousEffects.Register(new ManlandCycleAnimateEffect(
                        land,
                        keywords: new[] { "Trample", "Haste" },
                        subtypes: new[] { CardSubtype.Elemental },
                        extraTypes: null));

                    // Layer 7b — set base P/T to X/X.
                    continuousEffects.Register(new ManlandCycleBecomesPTEffect(
                        land, x, x));

                    return; // "target land" — a single permanent.
                }
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationCost) },
            effects: new IEffect[] { animateEffect }));

        return card;
    }
}
