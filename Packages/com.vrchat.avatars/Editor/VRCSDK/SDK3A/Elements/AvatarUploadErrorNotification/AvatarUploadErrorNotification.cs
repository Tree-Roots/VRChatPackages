using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDKBase;

namespace VRC.SDK3A.Editor.Elements
{
    public class AvatarUploadErrorNotification: VisualElement
    {
        public AvatarUploadErrorNotification(string error)
        {
            Resources.Load<VisualTreeAsset>("AvatarUploadErrorNotification").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("AvatarUploadErrorNotificationStyles"));

            this.Q<Label>("notification-error-reason").text = error;
            
            var openUnityConsole = this.Q<Button>("open-unity-console");
            openUnityConsole.clicked += VRC_EditorTools.OpenConsoleWindow;
        }
    }
}