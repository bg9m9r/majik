namespace Majik.Core.Abilities;

/// <summary>
/// Base implementation of an effect.
/// Effects are executed when spells or abilities resolve.
/// </summary>
public class Effect : IEffect
{
    private readonly Action _executeAction;

    public string Description { get; }

    public Effect(string description, Action executeAction)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        _executeAction = executeAction ?? throw new ArgumentNullException(nameof(executeAction));
    }

    public void Execute()
    {
        _executeAction();
    }
}
