using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// Maps every action of an asset to a compact index used in <see cref="InputCommand"/>.
    /// The order follows the asset (maps, then actions), so a per-player copy, or the same asset on the server,
    /// produces the same indices. Compare <see cref="Signature"/> to detect mismatched builds.
    /// </summary>
    public sealed class InputActionTable
    {
        readonly InputAction[] m_Actions;
        readonly Dictionary<Guid, ushort> m_ById = new Dictionary<Guid, ushort>();
        readonly Dictionary<string, ushort> m_ByName = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        public InputActionAsset Asset { get; }
        public int Count => m_Actions.Length;

        /// <summary>Hash of all action ids in order. Must match between client and server.</summary>
        public uint Signature { get; }

        public InputActionTable(InputActionAsset asset)
        {
            Asset = asset ? asset : throw new ArgumentNullException(nameof(asset));

            var actions = new List<InputAction>();
            var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var signature = 2166136261u;

            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    if (actions.Count > ushort.MaxValue)
                        throw new InvalidOperationException("Too many actions for InputActionTable.");

                    var index = (ushort)actions.Count;
                    actions.Add(action);
                    m_ById[action.id] = index;
                    m_ByName[$"{map.name}/{action.name}"] = index;

                    // A bare action name works only if it is unique across maps.
                    if (m_ByName.ContainsKey(action.name) || ambiguousNames.Contains(action.name))
                    {
                        m_ByName.Remove(action.name);
                        ambiguousNames.Add(action.name);
                    }
                    else
                    {
                        m_ByName[action.name] = index;
                    }

                    foreach (var b in action.id.ToByteArray())
                        signature = (signature ^ b) * 16777619u;
                }
            }

            m_Actions = actions.ToArray();
            Signature = signature;
        }

        public InputAction GetAction(ushort index) => index < m_Actions.Length ? m_Actions[index] : null;

        public bool IsValid(ushort index) => index < m_Actions.Length;

        public bool TryGetIndex(Guid actionId, out ushort index) => m_ById.TryGetValue(actionId, out index);

        public bool TryGetIndex(InputAction action, out ushort index)
        {
            index = 0;
            return action != null && m_ById.TryGetValue(action.id, out index);
        }

        public bool TryGetIndex(InputActionReference reference, out ushort index)
        {
            index = 0;
            return reference != null && TryGetIndex(reference.action, out index);
        }

        /// <summary>Accepts "Map/Action", a unique "Action" name, or an action id string.</summary>
        public bool TryGetIndex(string nameOrId, out ushort index)
        {
            index = 0;
            if (string.IsNullOrEmpty(nameOrId))
                return false;
            if (m_ByName.TryGetValue(nameOrId, out index))
                return true;
            return Guid.TryParse(nameOrId, out var id) && m_ById.TryGetValue(id, out index);
        }
    }
}
