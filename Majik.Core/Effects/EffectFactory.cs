using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;

namespace Majik.Core.Effects;

/// <summary>
/// Factory for creating effect instances from database references.
/// </summary>
public class EffectFactory
{
    /// <summary>
    /// Create an effect instance from an effect reference entity.
    /// </summary>
    public IEffect? CreateEffect(EffectReferenceEntity effectRef, string parametersJson)
    {
        if (effectRef == null)
            throw new ArgumentNullException(nameof(effectRef));

        // Get the effect from the library
        var effect = EffectLibrary.GetEffect(effectRef.EffectId);
        if (effect == null)
        {
            // Effect not found in library - this shouldn't happen for built-in effects
            return null;
        }

        // Parse parameters
        Dictionary<string, object>? parameters = null;
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            try
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson);
            }
            catch (JsonException)
            {
                // Invalid JSON - return base effect without parameters
                return effect;
            }
        }

        // For now, return the base effect
        // TODO: In the future, we'll create parameterized effect instances
        // that use the parameters when executing
        return effect;
    }

    /// <summary>
    /// Create an effect instance from an effect reference ID and parameters.
    /// </summary>
    public IEffect? CreateEffect(string effectId, Dictionary<string, object>? parameters = null)
    {
        var effect = EffectLibrary.GetEffect(effectId);
        if (effect == null)
            return null;

        // For now, return the base effect
        // TODO: Create parameterized effect instances
        return effect;
    }

    /// <summary>
    /// Create multiple effects from a list of effect references.
    /// </summary>
    public List<IEffect> CreateEffects(IEnumerable<CardAbilityEffectEntity> abilityEffects)
    {
        var effects = new List<IEffect>();
        
        foreach (var abilityEffect in abilityEffects.OrderBy(e => e.EffectOrder))
        {
            var effect = CreateEffect(abilityEffect.EffectReference, abilityEffect.EffectParameters);
            if (effect != null)
            {
                effects.Add(effect);
            }
        }

        return effects;
    }
}
