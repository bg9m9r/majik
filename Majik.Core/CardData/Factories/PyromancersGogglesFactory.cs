using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pyromancer's Goggles (Magic Origins, {5}).
///
/// Legendary Artifact — {5}. Oracle text:
///   "{T}: Add {R}. When you spend this mana to cast an instant or sorcery
///    spell, copy that spell. You may choose new targets for the copy."
///
/// ## Implementation (v1)
/// - Card identity: Legendary Artifact with mana cost {5}.
/// - <b>{T}: Add {R}</b> — wired as a single <see cref="ManaAbility"/>
///   (same shape as Phyrexian Tower's colourless side, Urza's Mine, etc.).
/// - <b>Copy rider</b> — surfaced as a structural <see cref="TriggeredAbility"/>
///   over <see cref="SpellCastEvent"/> gated on (a) the spell's controller
///   being the Goggles' controller AND (b) the spell being Instant or Sorcery
///   (CR 706.10 — "copy" places a copy on the stack). The effect is a
///   <see cref="Effect"/> with a deliberately no-op delegate — the engine
///   has no per-mana provenance ledger yet, so we cannot tell whether the
///   {R} from this specific source was the mana spent on the spell. The
///   ability is attached so structural shape tests can observe it and so
///   downstream provenance work has a hook to flip the gate on.
///
/// ## Deferred (v1 gaps)
/// - Mana-provenance gate ("when you spend this mana"). CR 106.6 — mana
///   produced by a permanent doesn't carry source identity through the
///   mana pool in any data-driven way today. Same blocker as Cavern of
///   Souls' "creature spell of the chosen type" rider and the Mana
///   Confluence / City of Brass life-on-spend riders.
/// - Stack-copy primitive ("copy that spell"). CR 707.10 — copies a spell
///   on the stack with the same controller; new-target opt-in (CR 706.10c).
///   No <c>SpellCopier</c> service yet.
/// - "Choose new targets" prompt — depends on the copy primitive above
///   plus an agent-driven retarget surface.
/// </summary>
[CardName("Pyromancer's Goggles")]
public static class PyromancersGogglesFactory
{
    public const string CardName = "Pyromancer's Goggles";
    public const string Cost = "{5}";

    /// <summary>
    /// Construct Pyromancer's Goggles owned and controlled by
    /// <paramref name="owner"/>. The mana ability fires; the copy rider
    /// is attached structurally but its effect is a no-op pending a
    /// mana-provenance ledger + stack-copy primitive (see factory xmldoc).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var goggles = new Artifact(
            CardName,
            Cost,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        goggles.SetOwner(owner);
        goggles.SetController(owner);

        // --------------------------------------------------------------
        // {T}: Add {R} — single ManaAbility.
        // Untap gate via the standard !IsTapped predicate; the activator
        // taps the source.
        // --------------------------------------------------------------
        goggles.AddAbility(new ManaAbility(
            source: goggles,
            controller: owner,
            manaGenerated: ManaCost.Parse("R"),
            canActivateCheck: () => !goggles.IsTapped));

        // --------------------------------------------------------------
        // Copy rider — structural only (v1).
        // Predicate: controller-match AND spell is Instant or Sorcery.
        // Effect: no-op until mana provenance + stack-copy primitive ship.
        // --------------------------------------------------------------
        var copyCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, goggles.Controller)) return false;
            var spellCard = e.Spell.Card;
            return spellCard.HasType(CardType.Instant) || spellCard.HasType(CardType.Sorcery);
        });

        var copyEffect = new Effect(
            "Pyromancer's Goggles — copy the cast instant/sorcery (deferred: needs mana-provenance ledger + stack-copy primitive)",
            () => { /* no-op — see factory xmldoc */ });

        var copyTrigger = new TriggeredAbility(
            source: goggles,
            controller: owner,
            condition: copyCondition,
            effects: new IEffect[] { copyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        goggles.AddAbility(copyTrigger);

        return goggles;
    }
}
