using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Events;

public interface IGameEvent
{
}

public interface IGameEventHandler<in T> where T : IGameEvent
{
    void OnGameEvent(T eventArgs);
}

[AttributeUsage(AttributeTargets.Method)]
public class EarlyAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class LateAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class BeforeAttribute : Attribute
{
    public Type Type { get; }

    public BeforeAttribute(Type type)
    {
        Type = type;
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class AfterAttribute : Attribute
{
    public Type Type { get; }

    public AfterAttribute(Type type)
    {
        Type = type;
    }
}

public static class GameEvent
{
    private static Dictionary<Type, IReadOnlyDictionary<Type, int>> HandlerOrderingCache { get; } = new();

    public static void Dispatch<T>(this Node root, T eventArgs) where T : IGameEvent
    {
        var handlers = GetHandlers<T>(root).ToArray();
        DispatchToHandlers(handlers, eventArgs);
    }

    public static void DispatchGlobal<T>(T eventArgs) where T : IGameEvent
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        if (sceneTree?.Root != null)
            sceneTree.Root.Dispatch(eventArgs);
        else
            GD.PushWarning("Failed to dispatch global event: SceneTree or Root not found");
    }

    private static void DispatchToHandlers<T>(IGameEventHandler<T>[] handlers, T eventArgs) where T : IGameEvent
    {
        if (!HandlerOrderingCache.TryGetValue(typeof(T), out var ordering) || handlers.Any(x => !ordering.ContainsKey(x.GetType())))
            ordering = HandlerOrderingCache[typeof(T)] = GetHandlerOrdering(handlers);

        List<Exception> exceptions = null;

        foreach (var handler in handlers.OrderBy(h => ordering[h.GetType()]))
            try
            {
                handler.OnGameEvent(eventArgs);
            }
            catch (Exception e)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(e);
            }

        if (exceptions != null)
        {
            if (exceptions.Count == 1)
                ConsoleSystem.LogErr(exceptions[0].Message, ConsoleChannel.Entity);
            else
                ConsoleSystem.LogErr(new AggregateException(exceptions).Message, ConsoleChannel.Entity);
        }
    }

    private static IEnumerable<IGameEventHandler<T>> GetHandlers<T>(Node root) where T : IGameEvent
    {
        var queue = new Queue<Node>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is IGameEventHandler<T> handler)
                yield return handler;

            foreach (var child in node.GetChildren())
                queue.Enqueue(child);
        }
    }

    private static IReadOnlyDictionary<Type, int> GetHandlerOrdering<T>(IGameEventHandler<T>[] handlers) where T : IGameEvent
    {
        var types = handlers.Select(h => h.GetType()).ToArray();
        var helper = new SortingHelper(types.Length);

        for (var i = 0; i < types.Length; ++i)
        {
            var type = types[i];

            var method = type.GetMethod("OnGameEvent", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(T) }, null);

            if (method == null)
            {
                ConsoleSystem.LogErr($"Can't find IGameEventHandler<{typeof(T).Name}> implementation in {type.Name}!");
                continue;
            }

            foreach (var attrib in method.GetCustomAttributes(true))
                switch (attrib)
                {
                    case EarlyAttribute:
                        helper.AddFirst(i);
                        break;
                    case LateAttribute:
                        helper.AddLast(i);
                        break;
                    case BeforeAttribute before:
                        for (var j = 0; j < types.Length; ++j)
                        {
                            if (i == j) continue;
                            var other = types[j];
                            if (before.Type.IsAssignableFrom(other)) helper.AddConstraint(i, j);
                        }

                        break;
                    case AfterAttribute after:
                        for (var j = 0; j < types.Length; ++j)
                        {
                            if (i == j) continue;
                            var other = types[j];
                            if (after.Type.IsAssignableFrom(other)) helper.AddConstraint(j, i);
                        }

                        break;
                }
        }

        var ordering = new List<int>();

        if (!helper.Sort(ordering, out var invalid))
        {
            ConsoleSystem.LogErr($"Invalid event ordering constraint between {types[invalid.EarlierIndex].Name} and {types[invalid.LaterIndex].Name}!");
            return new Dictionary<Type, int>();
        }

        return ordering.Select((order, index) => (types[index], order))
            .ToDictionary(x => x.Item1, x => x.order);
    }
}

internal class SortingHelper
{
    public readonly struct SortConstraint
    {
        public readonly int EarlierIndex;
        public readonly int LaterIndex;

        public SortConstraint(int earlierIndex, int laterIndex)
        {
            EarlierIndex = earlierIndex;
            LaterIndex = laterIndex;
        }

        public SortConstraint Complement => new(LaterIndex, EarlierIndex);
    }

    private readonly int _itemCount;
    private readonly HashSet<SortConstraint> _initialConstraints = new();
    private readonly HashSet<int> _first = new();
    private readonly HashSet<int> _last = new();

    public SortingHelper(int itemCount)
    {
        _itemCount = itemCount;
    }

    public void AddConstraint(int earlierIndex, int laterIndex)
    {
        _initialConstraints.Add(new SortConstraint(earlierIndex, laterIndex));
    }

    public void AddFirst(int earlierIndex)
    {
        _first.Add(earlierIndex);
    }

    public void AddLast(int laterIndex)
    {
        _last.Add(laterIndex);
    }

    public bool Sort(List<int> result, out SortConstraint invalidConstraint)
    {
        var middle = new HashSet<int>();

        for (var index = 0; index < _itemCount; ++index)
            if (!_first.Contains(index) && !_last.Contains(index))
                middle.Add(index);

        var allConstraints = new HashSet<SortConstraint>();
        var newConstraints = new Queue<SortConstraint>();
        var beforeDict = new Dictionary<int, HashSet<int>>();
        var afterDict = new Dictionary<int, HashSet<int>>();

        bool AddWorkingConstraint(int earlierIndex, int laterIndex, out SortConstraint constraint)
        {
            constraint = new SortConstraint(earlierIndex, laterIndex);

            if (allConstraints.Contains(constraint.Complement))
                return false;

            if (!allConstraints.Add(constraint))
                return true;

            newConstraints.Enqueue(constraint);

            if (!beforeDict.TryGetValue(earlierIndex, out var before))
                beforeDict.Add(earlierIndex, before = new HashSet<int>());

            if (!afterDict.TryGetValue(laterIndex, out var after))
                afterDict.Add(laterIndex, after = new HashSet<int>());

            before.Add(laterIndex);
            after.Add(earlierIndex);

            return true;
        }

        // Add initial constraints
        foreach (var initialConstraint in _initialConstraints)
            if (!AddWorkingConstraint(initialConstraint.EarlierIndex, initialConstraint.LaterIndex, out invalidConstraint))
                return false;

        // Everything in _first should be before everything in _last
        foreach (var earlierIndex in _first)
        foreach (var laterIndex in _last)
            if (!AddWorkingConstraint(earlierIndex, laterIndex, out invalidConstraint))
                return false;

        // Keep propagating constraints until nothing changes
        while (newConstraints.TryDequeue(out var nextConstraint))
        {
            if (beforeDict.TryGetValue(nextConstraint.LaterIndex, out var before))
                foreach (var laterIndex in before)
                    if (!AddWorkingConstraint(nextConstraint.EarlierIndex, laterIndex, out invalidConstraint))
                        return false;

            if (afterDict.TryGetValue(nextConstraint.EarlierIndex, out var after))
                foreach (var earlierIndex in after)
                    if (!AddWorkingConstraint(earlierIndex, nextConstraint.LaterIndex, out invalidConstraint))
                        return false;
        }

        // Handle middle items
        foreach (var middleIndex in middle)
        {
            var isBeforeAnyFirst = beforeDict.TryGetValue(middleIndex, out var before)
                                   && before.Any(x => _first.Contains(x));

            var isAfterAnyLast = afterDict.TryGetValue(middleIndex, out var after)
                                 && after.Any(x => _last.Contains(x));

            if (!isBeforeAnyFirst)
                foreach (var earlierIndex in _first)
                    AddWorkingConstraint(earlierIndex, middleIndex, out invalidConstraint);

            if (!isAfterAnyLast)
                foreach (var laterIndex in _last)
                    AddWorkingConstraint(middleIndex, laterIndex, out invalidConstraint);
        }

        // Generate final ordering
        var earliestRemaining = new Queue<int>();

        for (var index = 0; index < _itemCount; ++index)
            if (!afterDict.ContainsKey(index))
                earliestRemaining.Enqueue(index);

        result.Clear();

        while (earliestRemaining.TryDequeue(out var nextIndex))
        {
            result.Add(nextIndex);

            foreach (var laterIndex in beforeDict.TryGetValue(nextIndex, out var laterIndices)
                         ? laterIndices
                         : Enumerable.Empty<int>())
            {
                var beforeLater = afterDict[laterIndex];
                beforeLater.Remove(nextIndex);

                if (beforeLater.Count == 0)
                    earliestRemaining.Enqueue(laterIndex);
            }
        }

        invalidConstraint = default;
        return result.Count == _itemCount;
    }
}