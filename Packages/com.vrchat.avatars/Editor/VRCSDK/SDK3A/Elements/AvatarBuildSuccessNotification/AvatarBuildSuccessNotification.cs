using UnityEngine;
using UnityEngine.UIElements;

namespace VRC.SDK3A.Editor.Elements
{
    public class AvatarBuildSuccessNotification: VisualElement
    {
        public AvatarBuildSuccessNotification(bool testBuild = false)
        {
            Resources.Load<VisualTreeAsset>("AvatarBuildSuccessNotification").CloneTree(this);
            styleSheets.Add(Resources.Load<StyleSheet>("AvatarBuildSuccessNotificationStyles"));

            if (testBuild)
            {
                var openAvatarPageButton = this.Q<Button>("open-avatar-page-button");
                openAvatarPageButton.RemoveFromClassList("d-none");

                openAvatarPageButton.clicked += () =>
                {
                    Application.OpenURL($"https://creators.vrchat.com/avatars/#local-avatar-testing");
                };
            }
        }
    }
}