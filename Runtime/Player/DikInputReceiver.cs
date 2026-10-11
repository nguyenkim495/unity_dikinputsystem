using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// Disposable token returned by DikInputReceiver.Bind. Dispose it (e.g. in OnDisable) to unbind.
    /// </summary>
    public readonly struct InputBindingHandle : IDisposable
    {
        readonly DikInputReceiver m_Receiver;
        readonly int m_Id;

        internal InputBindingHandle(DikInputReceiver receiver, int id)
        {
            m_Receiver = receiver;
            m_Id = id;
        }

        public bool IsValid => m_Receiver != null && m_Id != 0;

        public void Dispose()
        {
            if (m_Receiver != null)
                m_Receiver.Unbind(m_Id);
        }
    }

    /// <summary>
    /// The only input API gameplay code needs. It receives <see cref="InputCommand"/>s, either directly from a local
    /// DikInputPlayer (offline / host) or from the network bridge (dedicated server), so gameplay code is identical
    /// in both cases. Equivalent to UE5 EnhancedInputComponent::BindAction.
    ///
    /// Supports event-style handlers (Bind) and polling (ReadValue / IsPressed / WasPerformedThisFrame).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Dik/Input/Dik Input Receiver")]
    public sealed class DikInputReceiver : MonoBehaviour
    {
        [Tooltip("Same Input Action Asset used by PlayerInput. Optional when a PlayerInput is on this GameObject; " +
                 "required on server-side objects without PlayerInput.")]
        [SerializeField] InputActionAsset m_Actions;

        struct Handler
        {
            public int Id;
            public InputTrigger Mask;
            public Action<InputCommand> Callback;
        }

        struct ActionState
        {
            public InputCommand Last;
            public bool InProgress;
            public int StartedFrame;
            public int PerformedFrame;
            public int CanceledFrame;
        }

        InputActionTable m_Table;
        // Copy-on-write so handlers can bind or unbind while commands are being dispatched.
        List<Handler>[] m_Handlers;
        ActionState[] m_States;
        readonly Dictionary<int, ushort> m_HandlerOwners = new Dictionary<int, ushort>();
        int m_NextHandlerId = 1;

        /// <summary>
        /// Server-side validation (anti-cheat): return false to drop a command, e.g. an impossible stick magnitude.
        /// </summary>
        public Func<InputCommand, bool> Validator { get; set; }

        /// <summary>Raised for every accepted command, before per-action handlers. Useful for recording or replay.</summary>
        public event Action<InputCommand> CommandReceived;

        public InputActionTable ActionTable => EnsureTable();

        // ------------------------------------------------------------------ Setup

        /// <summary>Called by DikInputPlayer. Can also be called manually for server-side receivers.</summary>
        public void Initialize(InputActionTable table)
        {
            if (table == null || m_Table != null)
                return;
            SetTable(table);
        }

        InputActionTable EnsureTable()
        {
            if (m_Table != null)
                return m_Table;

            var asset = m_Actions;
            if (asset == null && TryGetComponent<PlayerInput>(out var playerInput))
                asset = playerInput.actions;
            if (asset == null)
                return null;

            SetTable(new InputActionTable(asset));
            return m_Table;
        }

        void SetTable(InputActionTable table)
        {
            m_Table = table;
            m_Handlers = new List<Handler>[table.Count];
            m_States = new ActionState[table.Count];
            for (var i = 0; i < m_States.Length; i++)
                m_States[i] = new ActionState { StartedFrame = -1, PerformedFrame = -1, CanceledFrame = -1 };
        }

        // ------------------------------------------------------------------ Binding

        public InputBindingHandle Bind(InputActionReference action, InputTrigger triggers, Action<InputCommand> callback)
        {
            var table = EnsureTable();
            if (table == null || !table.TryGetIndex(action, out var index))
            {
                Debug.LogError($"[DikInput] Cannot bind '{(action != null ? action.name : "null")}': action not found in receiver's asset.", this);
                return default;
            }
            return Bind(index, triggers, callback);
        }

        /// <param name="actionNameOrPath">"Map/Action", a unique "Action" name, or an action id.</param>
        public InputBindingHandle Bind(string actionNameOrPath, InputTrigger triggers, Action<InputCommand> callback)
        {
            var table = EnsureTable();
            if (table == null || !table.TryGetIndex(actionNameOrPath, out var index))
            {
                Debug.LogError($"[DikInput] Cannot bind '{actionNameOrPath}': action not found in receiver's asset.", this);
                return default;
            }
            return Bind(index, triggers, callback);
        }

        public InputBindingHandle Bind(ushort actionIndex, InputTrigger triggers, Action<InputCommand> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (EnsureTable() == null || !m_Table.IsValid(actionIndex))
                return default;

            var id = m_NextHandlerId++;
            var list = m_Handlers[actionIndex] != null ? new List<Handler>(m_Handlers[actionIndex]) : new List<Handler>(1);
            list.Add(new Handler { Id = id, Mask = triggers, Callback = callback });
            m_Handlers[actionIndex] = list;
            m_HandlerOwners[id] = actionIndex;
            return new InputBindingHandle(this, id);
        }

        internal void Unbind(int handlerId)
        {
            if (!m_HandlerOwners.TryGetValue(handlerId, out var index))
                return;
            m_HandlerOwners.Remove(handlerId);

            var current = m_Handlers[index];
            if (current == null)
                return;
            var list = new List<Handler>(current);
            list.RemoveAll(h => h.Id == handlerId);
            m_Handlers[index] = list.Count > 0 ? list : null;
        }

        public void UnbindAll()
        {
            if (m_Handlers != null)
                Array.Clear(m_Handlers, 0, m_Handlers.Length);
            m_HandlerOwners.Clear();
        }

        // ------------------------------------------------------------------ Receiving

        /// <summary>
        /// Entry point for commands. Called by the local DikInputPlayer, or by your netcode on the server.
        /// </summary>
        public void Receive(in InputCommand command)
        {
            if (EnsureTable() == null || !m_Table.IsValid(command.ActionIndex) || command.Trigger == InputTrigger.None)
                return;
            if (Validator != null && !Validator(command))
                return;

            var frame = Time.frameCount;
            ref var state = ref m_States[command.ActionIndex];
            state.Last = command;
            switch (command.Trigger)
            {
                case InputTrigger.Started:
                    state.InProgress = true;
                    state.StartedFrame = frame;
                    break;
                case InputTrigger.Performed:
                    state.InProgress = true;
                    state.PerformedFrame = frame;
                    break;
                case InputTrigger.Canceled:
                    state.InProgress = false;
                    state.CanceledFrame = frame;
                    break;
            }

            CommandReceived?.Invoke(command);

            var handlers = m_Handlers[command.ActionIndex];
            if (handlers == null)
                return;

            for (var i = 0; i < handlers.Count; i++)
            {
                if ((handlers[i].Mask & command.Trigger) == 0)
                    continue;
                try
                {
                    handlers[i].Callback(command);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        /// <summary>
        /// Sends a zero-value Canceled to every in-progress action, e.g. when a remote client disconnects
        /// or gameplay is reset.
        /// </summary>
        public void ReleaseAll()
        {
            if (m_States == null)
                return;
            var tick = DikInput.CurrentTick();
            for (var i = 0; i < m_States.Length; i++)
            {
                if (m_States[i].InProgress)
                    Receive(InputCommand.CreateRelease((ushort)i, m_States[i].Last.ValueKind, tick));
            }
        }

        // ------------------------------------------------------------------ Polling

        public T ReadValue<T>(InputActionReference action) where T : struct => TryResolve(action, out var i) ? ReadValue<T>(i) : default;
        public T ReadValue<T>(string action) where T : struct => TryResolve(action, out var i) ? ReadValue<T>(i) : default;

        public T ReadValue<T>(ushort actionIndex) where T : struct
        {
            if (m_States == null || actionIndex >= m_States.Length || !m_States[actionIndex].InProgress)
                return default;
            return m_States[actionIndex].Last.ReadValue<T>();
        }

        /// <summary>True between Started/Performed and Canceled.</summary>
        public bool IsPressed(InputActionReference action) => TryResolve(action, out var i) && IsPressed(i);
        public bool IsPressed(string action) => TryResolve(action, out var i) && IsPressed(i);
        public bool IsPressed(ushort actionIndex) => m_States != null && actionIndex < m_States.Length && m_States[actionIndex].InProgress;

        public bool WasStartedThisFrame(InputActionReference action) => TryResolve(action, out var i) && FrameIs(i, s => s.StartedFrame);
        public bool WasPerformedThisFrame(InputActionReference action) => TryResolve(action, out var i) && FrameIs(i, s => s.PerformedFrame);
        public bool WasCanceledThisFrame(InputActionReference action) => TryResolve(action, out var i) && FrameIs(i, s => s.CanceledFrame);
        public bool WasStartedThisFrame(string action) => TryResolve(action, out var i) && FrameIs(i, s => s.StartedFrame);
        public bool WasPerformedThisFrame(string action) => TryResolve(action, out var i) && FrameIs(i, s => s.PerformedFrame);
        public bool WasCanceledThisFrame(string action) => TryResolve(action, out var i) && FrameIs(i, s => s.CanceledFrame);

        bool FrameIs(ushort index, Func<ActionState, int> frame) => m_States != null && frame(m_States[index]) == Time.frameCount;

        bool TryResolve(InputActionReference action, out ushort index)
        {
            index = 0;
            var table = EnsureTable();
            return table != null && table.TryGetIndex(action, out index);
        }

        bool TryResolve(string action, out ushort index)
        {
            index = 0;
            var table = EnsureTable();
            return table != null && table.TryGetIndex(action, out index);
        }
    }
}
