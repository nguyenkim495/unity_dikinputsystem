using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// Optional base class for gameplay scripts that read input from a <see cref="DikInputReceiver"/>.
    /// It finds the receiver, keeps the binding handles and disposes them on disable, so a subclass only declares
    /// what it binds (<see cref="BindInput"/>) and what it polls (<see cref="ReadValue{T}"/>).
    ///
    /// The receiver is resolved in this order: the serialized field, this object or its parents, then the receiver of
    /// the <see cref="DikInputPlayer"/> with <see cref="PlayerIndex"/>. If that player is not active yet, binding waits
    /// until it registers, so the script can live on any object (camera, UI...) independent of the player object.
    /// </summary>
    public abstract class DikInputBehaviour : MonoBehaviour
    {
        [Tooltip("Receiver to read from. Empty: searched on this object and its parents, then on the player with 'Player Index'.")]
        [SerializeField] DikInputReceiver m_Receiver;

        [Tooltip("Used when no receiver is assigned or found in the parents. Matches PlayerInput.playerIndex.")]
        [SerializeField] int m_PlayerIndex;

        readonly List<InputBindingHandle> m_Bindings = new List<InputBindingHandle>();
        bool m_WaitingForPlayer;

        public DikInputReceiver Receiver => m_Receiver;

        public int PlayerIndex
        {
            get => m_PlayerIndex;
            set => m_PlayerIndex = value;
        }

        protected virtual void OnEnable()
        {
            if (m_Receiver == null)
                m_Receiver = FindReceiver();

            if (m_Receiver != null)
            {
                BindInput();
                return;
            }

            m_WaitingForPlayer = true;
            DikInput.PlayerAdded += OnPlayerAdded;
        }

        protected virtual void OnDisable()
        {
            StopWaiting();

            foreach (var binding in m_Bindings)
                binding.Dispose();
            m_Bindings.Clear();

            OnInputReleased();
        }

        /// <summary>Register event-style handlers with <see cref="Bind"/>. Called once a receiver is known.</summary>
        protected abstract void BindInput();

        /// <summary>Called on disable after every binding is disposed. Reset cached input state here.</summary>
        protected virtual void OnInputReleased() { }

        /// <summary>UE5 BindAction equivalent. The handle is disposed automatically on disable.</summary>
        protected void Bind(InputActionReference action, InputTrigger triggers, Action<InputCommand> callback)
        {
            if (m_Receiver == null || action == null)
                return;

            var binding = m_Receiver.Bind(action, triggers, callback);
            if (binding.IsValid)
                m_Bindings.Add(binding);
        }

        /// <summary>Current value. Default when released, disabled by a context or mode, the device was lost, or there is no receiver yet.</summary>
        protected T ReadValue<T>(InputActionReference action) where T : struct
        {
            return m_Receiver != null && action != null ? m_Receiver.ReadValue<T>(action) : default;
        }

        protected bool IsPressed(InputActionReference action)
        {
            return m_Receiver != null && action != null && m_Receiver.IsPressed(action);
        }

        DikInputReceiver FindReceiver()
        {
            var receiver = GetComponentInParent<DikInputReceiver>();
            if (receiver != null)
                return receiver;

            var player = DikInput.FindPlayer(m_PlayerIndex);
            return player != null ? player.Receiver : null;
        }

        void OnPlayerAdded(DikInputPlayer player)
        {
            if (player.PlayerIndex != m_PlayerIndex || player.Receiver == null)
                return;

            StopWaiting();
            m_Receiver = player.Receiver;
            BindInput();
        }

        void StopWaiting()
        {
            if (!m_WaitingForPlayer)
                return;
            m_WaitingForPlayer = false;
            DikInput.PlayerAdded -= OnPlayerAdded;
        }
    }
}
