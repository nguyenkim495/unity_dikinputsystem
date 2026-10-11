using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input.Editor
{
    static class InputPlayerCreator
    {
        /// <summary>
        /// Creates a GameObject with PlayerInput + DikInputReceiver + DikInputPlayer, wired together.
        /// If an Input Action Asset is selected in the Project window, it is assigned to PlayerInput.
        /// </summary>
        [MenuItem("GameObject/Dik/Input Player", false, 10)]
        static void Create(MenuCommand command)
        {
            var go = new GameObject("InputPlayer");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Dik Input Player");

            var playerInput = go.AddComponent<PlayerInput>();
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            playerInput.defaultActionMap = null;
            if (Selection.activeObject is InputActionAsset actions)
                playerInput.actions = actions;

            var receiver = go.AddComponent<DikInputReceiver>();
            var player = go.AddComponent<DikInputPlayer>();

            var serialized = new SerializedObject(player);
            serialized.FindProperty("m_PlayerInput").objectReferenceValue = playerInput;
            serialized.FindProperty("m_Receiver").objectReferenceValue = receiver;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeObject = go;
        }
    }
}
