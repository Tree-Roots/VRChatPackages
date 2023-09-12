using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRC.SDK3A.Editor.Elements
{
    public class AvatarFallbackSelectionErrorNotification: VisualElement
    {
        public AvatarFallbackSelectionErrorNotification(string error)
        {
            Resources.Load<VisualTreeAsset>("AvatarFallbackSelectionErrorNotification").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("AvatarFallbackSelectionErrorNotificationStyles"));

            this.Q<Label>("notification-error-reason").text = error;
            
            var openUnityConsole = this.Q<Button>("open-unity-console");
            openUnityConsole.clicked += () =>
            {
                Assembly.GetAssembly(typeof(EditorWindow)).GetType("UnityEditor.ConsoleWindow")
                    .GetMethod("ShowConsoleWindow", BindingFlags.Static | BindingFlags.Public)?.Invoke(this, new object[] { false });
            };
        }
    }
}