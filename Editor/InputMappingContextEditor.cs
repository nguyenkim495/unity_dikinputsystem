using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input.Editor
{
    [CustomEditor(typeof(InputMappingContext))]
    sealed class InputMappingContextEditor : UnityEditor.Editor
    {
        SerializedProperty m_Actions;
        SerializedProperty m_ActionMaps;
        SerializedProperty m_Layer;
        SerializedProperty m_Priority;
        SerializedProperty m_ConsumeMode;
        SerializedProperty m_Routing;
        bool m_ShowPreview = true;

        void OnEnable()
        {
            m_Actions = serializedObject.FindProperty("m_Actions");
            m_ActionMaps = serializedObject.FindProperty("m_ActionMaps");
            m_Layer = serializedObject.FindProperty("m_Layer");
            m_Priority = serializedObject.FindProperty("m_Priority");
            m_ConsumeMode = serializedObject.FindProperty("m_ConsumeMode");
            m_Routing = serializedObject.FindProperty("m_Routing");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_Actions);
            var asset = m_Actions.objectReferenceValue as InputActionAsset;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Action Maps", EditorStyles.boldLabel);

            if (asset == null)
            {
                EditorGUILayout.HelpBox("Assign the Input Action Asset used by your PlayerInput.", MessageType.Info);
            }
            else
            {
                DrawMapToggles(asset);
                DrawMissingMaps(asset);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Layer);
            EditorGUILayout.PropertyField(m_Priority);
            EditorGUILayout.PropertyField(m_ConsumeMode);
            EditorGUILayout.HelpBox(GetConsumeHelp((InputConsumeMode)m_ConsumeMode.enumValueIndex), MessageType.None);
            EditorGUILayout.PropertyField(m_Routing);
            EditorGUILayout.HelpBox(GetRoutingHelp((InputRouting)m_Routing.enumValueIndex), MessageType.None);

            if (asset != null)
                DrawPreview(asset);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawMapToggles(InputActionAsset asset)
        {
            if (asset.actionMaps.Count == 0)
            {
                EditorGUILayout.HelpBox("The asset has no action maps.", MessageType.Warning);
                return;
            }

            foreach (var map in asset.actionMaps)
            {
                var index = FindMapIndex(map);
                var included = index >= 0;

                // Keep the stored name in sync after a rename in the actions editor.
                if (included)
                {
                    var nameProperty = m_ActionMaps.GetArrayElementAtIndex(index).FindPropertyRelative("name");
                    if (nameProperty.stringValue != map.name)
                        nameProperty.stringValue = map.name;
                }

                var toggled = EditorGUILayout.ToggleLeft($"{map.name}  ({map.actions.Count} actions)", included);
                if (toggled == included)
                    continue;

                if (toggled)
                {
                    var newIndex = m_ActionMaps.arraySize;
                    m_ActionMaps.InsertArrayElementAtIndex(newIndex);
                    var element = m_ActionMaps.GetArrayElementAtIndex(newIndex);
                    element.FindPropertyRelative("id").stringValue = map.id.ToString();
                    element.FindPropertyRelative("name").stringValue = map.name;
                }
                else
                {
                    m_ActionMaps.DeleteArrayElementAtIndex(index);
                }
            }
        }

        void DrawMissingMaps(InputActionAsset asset)
        {
            for (var i = m_ActionMaps.arraySize - 1; i >= 0; i--)
            {
                var element = m_ActionMaps.GetArrayElementAtIndex(i);
                var id = element.FindPropertyRelative("id").stringValue;
                var name = element.FindPropertyRelative("name").stringValue;
                var exists = (Guid.TryParse(id, out var guid) && asset.FindActionMap(guid) != null) || asset.FindActionMap(name) != null;
                if (exists)
                    continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"Map '{name}' no longer exists in the asset.", MessageType.Warning);
                    if (GUILayout.Button("Remove", GUILayout.Width(70), GUILayout.Height(38)))
                        m_ActionMaps.DeleteArrayElementAtIndex(i);
                }
            }
        }

        void DrawPreview(InputActionAsset asset)
        {
            EditorGUILayout.Space();
            m_ShowPreview = EditorGUILayout.Foldout(m_ShowPreview, "Actions & Bindings", true);
            if (!m_ShowPreview)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < m_ActionMaps.arraySize; i++)
                {
                    var element = m_ActionMaps.GetArrayElementAtIndex(i);
                    var id = element.FindPropertyRelative("id").stringValue;
                    var map = Guid.TryParse(id, out var guid) ? asset.FindActionMap(guid) : null;
                    if (map == null)
                        map = asset.FindActionMap(element.FindPropertyRelative("name").stringValue);
                    if (map == null)
                        continue;

                    EditorGUILayout.LabelField(map.name, EditorStyles.miniBoldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        foreach (var action in map.actions)
                        {
                            var bindings = action.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions);
                            EditorGUILayout.LabelField($"{action.name}  [{action.type}]", string.IsNullOrEmpty(bindings) ? "(no bindings)" : bindings);
                        }
                    }
                }
            }
        }

        int FindMapIndex(InputActionMap map)
        {
            var id = map.id.ToString();
            for (var i = 0; i < m_ActionMaps.arraySize; i++)
            {
                var element = m_ActionMaps.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("id").stringValue == id)
                    return i;
            }
            for (var i = 0; i < m_ActionMaps.arraySize; i++)
            {
                var element = m_ActionMaps.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("name").stringValue == map.name)
                    return i;
            }
            return -1;
        }

        static string GetConsumeHelp(InputConsumeMode mode)
        {
            switch (mode)
            {
                case InputConsumeMode.ConsumeBoundControls:
                    return "Lower-priority actions using the same controls are disabled while this context is active.";
                case InputConsumeMode.BlockLowerPriority:
                    return "All lower-priority contexts are disabled while this context is active (modal).";
                default:
                    return "Lower-priority contexts are not affected.";
            }
        }

        static string GetRoutingHelp(InputRouting routing)
        {
            return routing == InputRouting.Replicated
                ? "Gameplay input: sent to the server when running as a client, processed locally otherwise."
                : "Local only: never sent over the network (UI, pause, camera, debug).";
        }
    }
}
