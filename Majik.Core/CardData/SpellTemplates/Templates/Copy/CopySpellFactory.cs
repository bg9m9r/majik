using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;

namespace Majik.Core.CardData.SpellTemplates.Templates.Copy;

/// <summary>
/// Shared builder for the "copy target instant or sorcery spell" family
/// (Twincast / Reverberate; the Fork / Increasing Vengeance class once their
/// riders are modeled).
///
/// CR 707.10 / 706.10a — at resolution the target spell is copied: a distinct,
/// independent copy spell is put on the stack above it (via
/// <see cref="SpellCopier.PushCopyOfTopSpell"/>), controlled by the copying
/// spell's controller. The copy resolves first and then ceases to exist
/// (CR 707.10c). The copying spell itself then finishes resolving and goes to
/// its owner's graveyard the usual way.
///
/// ## Choosing new targets for the copy (CR 707.10a)
/// "You may choose new targets for the copy" is honoured: at resolution the
/// copy effect calls <see cref="SpellCopier.PushCopyOfTopSpellAsync"/> with the
/// live agent + game from the <see cref="ResolutionContext"/>, which re-prompts
/// the copier for new targets using the targeted spell's retained per-slot
/// requests (<see cref="Majik.Core.Spells.Spell.RetargetRequests"/>). Declining
/// a slot keeps the original target.
/// </summary>
internal static class CopySpellFactory
{
    internal static SpellDefinition CopyTargetInstantOrSorcery(
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { TargetFilters.InstantOrSorcerySpellOnStackRequest() },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = resolver(raw);
                return new IEffect[]
                {
                    new Effect("copy target instant or sorcery spell (CR 707.10)", async rc =>
                    {
                        // Stack required to push the copy; shape-only / pre-bind
                        // contexts pass null → no-op.
                        if (stack == null) return;

                        // CR 608.2b — the chosen target must still be an
                        // instant/sorcery spell on the stack at resolution; a
                        // target that left the stack or changed type fizzles.
                        if (resolved is not ISpell spell) return;
                        if (!TargetFilters.InstantOrSorcerySpellMatches(spell)) return;

                        // CR 707.10 / 706.10a — put a distinct copy on the stack
                        // above the original, controlled by the copier (CR 707.10
                        // — the copy's controller is the controller of the
                        // copying effect, not the original spell's controller).
                        // CR 707.10a — re-prompt the copier for new targets via
                        // the live agent + game; a shape-only resolve (no agent /
                        // game) falls back to verbatim target reuse.
                        await SpellCopier.PushCopyOfTopSpellAsync(
                            stack, spell, rc.Agent, rc.Game,
                            copyController: caster, ct: rc.Ct)
                            .ConfigureAwait(false);
                    }),
                };
            });
    }
}
