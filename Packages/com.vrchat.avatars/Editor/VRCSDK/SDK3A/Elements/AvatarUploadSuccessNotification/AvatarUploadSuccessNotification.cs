using UnityEngine;
using UnityEngine.UIElements;

namespace VRC.SDK3A.Editor.Elements
{
    public class AvatarUploadSuccessNotification: VisualElement
    {
        public AvatarUploadSuccessNotification(string id)
        {
            Resources.Load<VisualTreeAsset>("AvatarUploadSuccessNotification").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("AvatarUploadSuccessNotificationStyles"));
            
            var openAvatarPageButton = this.Q<Button>("open-avatar-page-button");
            openAvatarPageButton.clicked += () =>
            {
                Application.OpenURL($"https://vrchat.com/home/avatar/{id}");
            };
        }
    }
}