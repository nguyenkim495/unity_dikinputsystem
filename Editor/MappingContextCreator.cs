using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input.Editor
{
    static class MappingContextCreator
    {
        const string k_MenuPath = "Assets/Create/Dik/Input/Mapping Contexts From Action Asset";

        /// <summary>
        /// Creates one Input Mapping Context per action map of the selected .inputactions asset.
        /// Maps whose name contains "UI" or "Menu" become UI / LocalOnly contexts; others become Gameplay / Replicated.
        /// </summary>
        [MenuItem(k_MenuPath, false, 1)]
        static void CreateFromSelection()
        {
            var asset = Selection.activeObject as InputActionAsset;
            if (asset == null)
                return;

            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset))?.Replace('\\', '/');
            Object last = null;

            foreach (var map in asset.actionMaps)
            {
                var isUI = Regex.IsMatch(map.name, @"(\bUI\b|UI$|Menu)", RegexOptions.IgnoreCase);

                var context = ScriptableObject.CreateInstance<InputMappingContext>();
                context.Configure(asset, new[] { map },
                    isUI ? InputLayer.UI : InputLayer.Gameplay,
                    isUI ? 100 : 0,
                    isUI ? InputConsumeMode.None : InputConsumeMode.ConsumeBoundControls,
                    isUI ? InputRouting.LocalOnly : InputRouting.Replicated);

                var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/IMC_{Sanitize(map.name)}.asset");
                AssetDatabase.CreateAsset(context, path);
                last = context;
            }

            AssetDatabase.SaveAssets();
            if (last != null)
            {
                Selection.activeObject = last;
                EditorGUIUtility.PingObject(last);
            }
        }

        [MenuItem(k_MenuPath, true)]
        static bool ValidateCreateFromSelection() => Selection.activeObject is InputActionAsset;

        static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
