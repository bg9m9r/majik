using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Choke (Stronghold, {2}{G}).
///
/// Enchantment. Oracle text:
///   "Islands don't untap during their controllers' untap steps."
///
/// ## Implemented (v1)
/// - Enchantment shape with correct printed name, type, and mana cost
///   <see cref="PrintedManaCost"/>.
/// - Dispatches via the source-generated <see cref="NamedCardFactory"/>
///   table.
///
/// ## Deferred (v1 gap)
/// - <b>"Islands don't untap during their controllers' untap steps" static</b>:
///   the engine has no untap-step filter / "doesn't untap" replacement
///   primitive today. Grep for <c>"SkipNextUntap"</c> / <c>"DoesntUntap"</c>
///   / <c>"doesn't untap"</c> across <c>Majik.Core/</c> — nothing exists.
///   <see cref="ManaVaultFactory"/> documents the same gap and ships its
///   mana-vault-specific clause as a deferred rider for the same reason.
///   The untap-step logic that needs to gain the filter lives in
///   <see cref="Majik.Core.Game.TurnDriver"/>.<c>UntapStep</c> (the
///   <see cref="Majik.Core.Game.Phases.UntapStep"/> phase-state class is a
///   stub — the actual untap loop is in TurnDriver) — adding a filter
///   surface (per-permanent predicate hook + a static-effect registration
///   path) is a real chunk of engine work and is sequenced behind several
///   higher-priority pillars in <see cref="ManaVaultFactory"/>'s queue.
///
/// Until the primitive lands, Choke ships as a marker card: its identity
/// is correct (cost / type / name / dispatch) but the printed static is a
/// no-op. Practical impact for any consumer wiring this: Islands will
/// continue to untap normally on their controllers' untap steps. Tests
/// covering the static body are intentionally absent — once the engine
/// surface arrives the static will be wired here and the test suite will
/// land with it (mirrors the Mana Vault rollout posture).
/// </summary>
[CardName("Choke")]
public static class ChokeFactory
{
    public const string CardName = "Choke";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>
    /// Build Choke with correct identity owned and controlled by
    /// <paramref name="owner"/>. The printed "Islands don't untap" static
    /// is NOT wired (see the deferred-rider section in the class xmldoc) —
    /// this is a marker / shape-only factory until the engine grows an
    /// untap-step filter surface.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }
}
