using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// What a context does to contexts with a lower priority.
    /// </summary>
    public enum InputConsumeMode
    {
        /// <summary>Lower contexts keep receiving input, even on the same controls.</summary>
        None,
        /// <summary>
        /// Lower-priority actions that share a control with this context are disabled
        /// (UE5 behaviour: the higher context "eats" the key).
        /// </summary>
        ConsumeBoundControls,
        /// <summary>Every lower-priority context is disabled while this one is active (modal).</summary>
        BlockLowerPriority
    }

    /// <summary>
    /// Where commands produced by this context's actions are processed.
    /// </summary>
    public enum InputRouting
    {
        /// <summary>Gameplay input. Sent to the server when running as a client, processed locally otherwise.</summary>
        Replicated,
        /// <summary>Never leaves this machine (UI, pause, camera-only, debug...).</summary>
        LocalOnly
    }

    /// <summary>
    /// UE5-style Input Mapping Context: a group of Input System action maps that can be
    /// added to or removed from a player at runtime with a priority.
    /// Bindings, interactions (UE "Triggers") and processors (UE "Modifiers") stay in the
    /// .inputactions asset; this asset only decides when they are active and who wins on conflicts.
    /// </summary>
    [CreateAssetMenu(fileName = "IMC_New", menuName = "Dik/Input/Input Mapping Context", order = 0)]
    public sealed class InputMappingContext : ScriptableObject
    {
        [Serializable]
        public struct ActionMapReference
        {
            public string id;
            public string name;
        }

        [Tooltip("The Input Action Asset that owns the action maps. Must be the same asset assigned to PlayerInput.")]
        [SerializeField] InputActionAsset m_Actions;

        [SerializeField] List<ActionMapReference> m_ActionMaps = new List<ActionMapReference>();

        [Tooltip("Layer(s) this context belongs to. The context is active only if the player's mode allows one of them.")]
        [SerializeField] InputLayer m_Layer = InputLayer.Gameplay;

        [Tooltip("Higher priority wins. Equal priorities: the context added last wins.")]
        [SerializeField] int m_Priority;

        [SerializeField] InputConsumeMode m_ConsumeMode = InputConsumeMode.ConsumeBoundControls;

        [SerializeField] InputRouting m_Routing = InputRouting.Replicated;

        public InputActionAsset Actions => m_Actions;
        public IReadOnlyList<ActionMapReference> ActionMaps => m_ActionMaps;
        public InputLayer Layer => m_Layer;
        public int Priority => m_Priority;
        public InputConsumeMode ConsumeMode => m_ConsumeMode;
        public InputRouting Routing => m_Routing;

        /// <summary>
        /// Sets up a context from code, e.g. on an instance from ScriptableObject.CreateInstance.
        /// </summary>
        public void Configure(InputActionAsset actions, IEnumerable<InputActionMap> maps, InputLayer layer,
            int priority = 0, InputConsumeMode consumeMode = InputConsumeMode.ConsumeBoundControls,
            InputRouting routing = InputRouting.Replicated)
        {
            m_Actions = actions;
            m_ActionMaps.Clear();
            if (maps != null)
            {
                foreach (var map in maps)
                {
                    if (map != null)
                        m_ActionMaps.Add(new ActionMapReference { id = map.id.ToString(), name = map.name });
                }
            }
            m_Layer = layer;
            m_Priority = priority;
            m_ConsumeMode = consumeMode;
            m_Routing = routing;
        }

        /// <summary>
        /// Finds this context's maps inside <paramref name="playerActions"/>, which may be a per-player copy
        /// of <see cref="Actions"/>. Matches by id first, then by name.
        /// </summary>
        public void ResolveMaps(InputActionAsset playerActions, List<InputActionMap> results)
        {
            if (playerActions == null)
                return;

            for (var i = 0; i < m_ActionMaps.Count; i++)
            {
                var reference = m_ActionMaps[i];
                InputActionMap map = null;

                if (!string.IsNullOrEmpty(reference.id) && Guid.TryParse(reference.id, out var guid))
                    map = playerActions.FindActionMap(guid);
                if (map == null && !string.IsNullOrEmpty(reference.name))
                    map = playerActions.FindActionMap(reference.name);

                if (map != null && !results.Contains(map))
                    results.Add(map);
            }
        }
    }
}
