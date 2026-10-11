using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// The prioritised set of mapping contexts applied to one player's actions
    /// (equivalent to UE5 EnhancedInputLocalPlayerSubsystem Add/RemoveMappingContext).
    /// Each call to <see cref="Apply"/> works out which actions should be enabled and enables or disables them.
    /// Disabling an action that is in progress raises its Canceled callback, so held values are released cleanly.
    /// </summary>
    public sealed class InputContextStack
    {
        public readonly struct ActiveContext
        {
            public readonly InputMappingContext Context;
            public readonly int Priority;
            public readonly bool IsAllowedByLayer;

            public ActiveContext(InputMappingContext context, int priority, bool isAllowedByLayer)
            {
                Context = context;
                Priority = priority;
                IsAllowedByLayer = isAllowedByLayer;
            }
        }

        struct Entry
        {
            public InputMappingContext Context;
            public int Priority;
            public int Order;
        }

        static readonly Comparison<Entry> s_ByPriority = (a, b) =>
        {
            var byPriority = b.Priority.CompareTo(a.Priority);
            return byPriority != 0 ? byPriority : b.Order.CompareTo(a.Order);
        };

        readonly List<Entry> m_Entries = new List<Entry>();
        readonly List<Entry> m_Sorted = new List<Entry>();
        readonly HashSet<InputAction> m_Desired = new HashSet<InputAction>();
        readonly HashSet<InputControl> m_Consumed = new HashSet<InputControl>();
        readonly Dictionary<InputAction, InputRouting> m_Routing = new Dictionary<InputAction, InputRouting>();
        readonly List<InputActionMap> m_MapBuffer = new List<InputActionMap>();
        readonly List<InputAction> m_ContextActions = new List<InputAction>();

        InputActionAsset m_Actions;
        InputLayer m_AllowedLayers = InputLayer.All;
        bool m_Enabled = true;
        bool m_Suspended;
        int m_NextOrder;
        bool m_Applying;
        bool m_ApplyPending;

        /// <summary>Raised after every Apply.</summary>
        public event Action Changed;

        /// <summary>The player's (possibly copied) action asset. Nothing is applied while null.</summary>
        public InputActionAsset Actions
        {
            get => m_Actions;
            set
            {
                if (m_Actions == value)
                    return;
                m_Actions = value;
                m_Routing.Clear();
                Apply();
            }
        }

        /// <summary>Layers allowed to receive input. Usually set through DikInputPlayer.SetMode.</summary>
        public InputLayer AllowedLayers
        {
            get => m_AllowedLayers;
            set
            {
                if (m_AllowedLayers == value)
                    return;
                m_AllowedLayers = value;
                Apply();
            }
        }

        /// <summary>Master switch. False disables every action, including System contexts.</summary>
        public bool Enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value)
                    return;
                m_Enabled = value;
                Apply();
            }
        }

        /// <summary>Set by the owning component when it is disabled.</summary>
        internal bool Suspended
        {
            get => m_Suspended;
            set
            {
                if (m_Suspended == value)
                    return;
                m_Suspended = value;
                Apply();
            }
        }

        public int Count => m_Entries.Count;

        /// <summary>
        /// Adds a context, or updates its priority if it was already added.
        /// </summary>
        /// <param name="priorityOverride">Null uses the context asset's priority.</param>
        public void Add(InputMappingContext context, int? priorityOverride = null)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var entry = new Entry
            {
                Context = context,
                Priority = priorityOverride ?? context.Priority,
                Order = m_NextOrder++
            };

            var index = IndexOf(context);
            if (index >= 0)
                m_Entries[index] = entry;
            else
                m_Entries.Add(entry);

            Apply();
        }

        public bool Remove(InputMappingContext context)
        {
            var index = IndexOf(context);
            if (index < 0)
                return false;
            m_Entries.RemoveAt(index);
            Apply();
            return true;
        }

        public bool Contains(InputMappingContext context) => IndexOf(context) >= 0;

        public void Clear()
        {
            if (m_Entries.Count == 0)
                return;
            m_Entries.Clear();
            Apply();
        }

        /// <summary>Fills <paramref name="results"/> with all added contexts, highest priority first.</summary>
        public void GetContexts(List<ActiveContext> results)
        {
            results.Clear();
            m_Sorted.Clear();
            m_Sorted.AddRange(m_Entries);
            m_Sorted.Sort(s_ByPriority);
            foreach (var entry in m_Sorted)
            {
                if (entry.Context != null)
                    results.Add(new ActiveContext(entry.Context, entry.Priority, IsAllowed(entry.Context)));
            }
        }

        /// <summary>True if the current contexts want this action enabled.</summary>
        public bool IsActionActive(InputAction action) => action != null && m_Desired.Contains(action);

        /// <summary>Routing of the context that last enabled this action. Defaults to Replicated.</summary>
        public InputRouting GetRouting(InputAction action)
        {
            return action != null && m_Routing.TryGetValue(action, out var routing) ? routing : InputRouting.Replicated;
        }

        /// <summary>
        /// Recomputes the enabled actions. Called automatically on every change; call it yourself only if something
        /// outside this class enabled or disabled actions on the asset.
        /// </summary>
        public void Apply()
        {
            // Disabling actions raises Canceled callbacks that may call back into Add/Remove.
            if (m_Applying)
            {
                m_ApplyPending = true;
                return;
            }

            m_Applying = true;
            try
            {
                do
                {
                    m_ApplyPending = false;
                    ApplyInternal();
                }
                while (m_ApplyPending);
            }
            finally
            {
                m_Applying = false;
            }

            Changed?.Invoke();
        }

        void ApplyInternal()
        {
            if (m_Actions == null)
                return;

            m_Desired.Clear();
            m_Consumed.Clear();

            if (m_Enabled && !m_Suspended)
                CollectDesiredActions();

            foreach (var map in m_Actions.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    var wanted = m_Desired.Contains(action);
                    if (wanted && !action.enabled)
                        action.Enable();
                    else if (!wanted && action.enabled)
                        action.Disable();
                }
            }
        }

        void CollectDesiredActions()
        {
            m_Sorted.Clear();
            m_Sorted.AddRange(m_Entries);
            m_Sorted.Sort(s_ByPriority);

            foreach (var entry in m_Sorted)
            {
                var context = entry.Context;
                if (context == null || !IsAllowed(context))
                    continue;

                m_MapBuffer.Clear();
                context.ResolveMaps(m_Actions, m_MapBuffer);

                // Collect first, then consume, so actions inside the same context never block each other.
                m_ContextActions.Clear();
                foreach (var map in m_MapBuffer)
                {
                    foreach (var action in map.actions)
                    {
                        if (m_Desired.Contains(action) || IsConsumed(action))
                            continue;
                        m_ContextActions.Add(action);
                    }
                }

                foreach (var action in m_ContextActions)
                {
                    m_Desired.Add(action);
                    m_Routing[action] = context.Routing;
                }

                if (context.ConsumeMode == InputConsumeMode.ConsumeBoundControls)
                {
                    foreach (var action in m_ContextActions)
                    {
                        var controls = action.controls;
                        for (var i = 0; i < controls.Count; i++)
                            m_Consumed.Add(controls[i]);
                    }
                }
                else if (context.ConsumeMode == InputConsumeMode.BlockLowerPriority)
                {
                    break;
                }
            }
        }

        bool IsConsumed(InputAction action)
        {
            if (m_Consumed.Count == 0)
                return false;
            var controls = action.controls;
            for (var i = 0; i < controls.Count; i++)
            {
                if (m_Consumed.Contains(controls[i]))
                    return true;
            }
            return false;
        }

        bool IsAllowed(InputMappingContext context) => (context.Layer & m_AllowedLayers) != 0;

        int IndexOf(InputMappingContext context)
        {
            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].Context == context)
                    return i;
            }
            return -1;
        }
    }
}
