using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input.Editor
{
    [CustomEditor(typeof(DikInputPlayer))]
    sealed class DikInputPlayerEditor : UnityEditor.Editor
    {
        readonly List<InputContextStack.ActiveContext> m_Contexts = new List<InputContextStack.ActiveContext>();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var player = (DikInputPlayer)target;
            var playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null && playerInput.actions == null)
                EditorGUILayout.HelpBox("PlayerInput has no Input Action Asset.", MessageType.Error);
            if (player.GetComponent<DikInputReceiver>() == null && player.Receiver == null)
                EditorGUILayout.HelpBox("No DikInputReceiver assigned: gameplay will not receive commands.", MessageType.Warning);

            if (!Application.isPlaying)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Player Index", player.PlayerIndex.ToString());
                EditorGUILayout.TextField("Input Enabled", player.InputEnabled.ToString());
                EditorGUILayout.TextField("Mode", player.Mode.ToString());
                EditorGUILayout.TextField("Network Mode", DikInput.NetworkMode.ToString());
                EditorGUILayout.TextField("Control Scheme", player.CurrentControlScheme ?? "-");
                EditorGUILayout.TextField("Device", $"{player.CurrentDeviceKind} / {player.CurrentGamepadFamily}");
                EditorGUILayout.TextField("Needs Device", player.NeedsDevice.ToString());
            }

            EditorGUILayout.LabelField("Contexts (highest priority first)", EditorStyles.miniBoldLabel);
            player.Contexts.GetContexts(m_Contexts);
            if (m_Contexts.Count == 0)
                EditorGUILayout.LabelField("  (none)");
            foreach (var context in m_Contexts)
            {
                var state = context.IsAllowedByLayer ? "active" : "blocked by mode";
                EditorGUILayout.LabelField($"  [{context.Priority}] {context.Context.name}", $"{context.Context.Layer} - {state}");
            }

            if (player.Actions != null)
            {
                EditorGUILayout.LabelField("Enabled Actions", EditorStyles.miniBoldLabel);
                foreach (var action in player.Actions)
                {
                    if (action.enabled)
                        EditorGUILayout.LabelField($"  {action.actionMap.name}/{action.name}", action.phase.ToString());
                }
            }

            Repaint();
        }
    }
}
