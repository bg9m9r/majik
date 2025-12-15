using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;

namespace Majik.Core.Stack;

/// <summary>
/// Manages the spell/ability stack.
/// Implements LIFO (Last In, First Out) structure per Magic rules.
/// </summary>
public class Stack
{
    private readonly System.Collections.Generic.Stack<IStackObject> _objects = new();
    private readonly IEventBus? _eventBus;

    /// <summary>
    /// Whether the stack is empty.
    /// </summary>
    public bool IsEmpty => _objects.Count == 0;

    /// <summary>
    /// Number of objects on the stack.
    /// </summary>
    public int Count => _objects.Count;

    /// <summary>
    /// The top object on the stack (most recently added).
    /// </summary>
    public IStackObject? Top => _objects.Count > 0 ? _objects.Peek() : null;

    public Stack(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Add an object to the top of the stack.
    /// </summary>
    public void Push(IStackObject stackObject)
    {
        if (stackObject == null)
        {
            throw new ArgumentNullException(nameof(stackObject));
        }

        _objects.Push(stackObject);
        _eventBus?.Publish(new StackObjectAddedEvent(stackObject));
    }

    /// <summary>
    /// Remove and return the top object from the stack.
    /// </summary>
    public IStackObject? Pop()
    {
        if (_objects.Count == 0)
        {
            return null;
        }

        return _objects.Pop();
    }

    /// <summary>
    /// Get all objects on the stack (from top to bottom).
    /// Returns a read-only list for encapsulation.
    /// </summary>
    public IReadOnlyList<IStackObject> GetAll()
    {
        return _objects.ToArray().Reverse().ToList().AsReadOnly(); // Return from top to bottom
    }

    /// <summary>
    /// Clear all objects from the stack.
    /// </summary>
    public void Clear()
    {
        _objects.Clear();
        _eventBus?.Publish(new StackClearedEvent());
    }
}
