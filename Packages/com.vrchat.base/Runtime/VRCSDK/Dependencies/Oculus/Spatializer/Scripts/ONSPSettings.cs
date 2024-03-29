using UnityEngine;
using System.Collections;

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
#endif
public sealed class ONSPSettings : ScriptableObject
{
    [SerializeField]
    public int voiceLimit = 16; // very conservative fallback

    private static ONSPSettings instance;
    public static ONSPSettings Instance
    {
        get
        {
            if (instance == null)
            {
#if UNITY_ANDROID || UNITY_IOS
                instance = Resources.Load<ONSPSettings>("ONSPSettings-Android");
#else
                instance = Resources.Load<ONSPSettings>("ONSPSettings");
#endif

                // This can happen if the developer never input their App Id into the Unity Editor
                // and therefore never created the OculusPlatformSettings.asset file
                // Use a dummy object with defaults for the getters so we don't have a null pointer exception
                if (instance == null)
                {
                    instance = ScriptableObject.CreateInstance<ONSPSettings>();

#if UNITY_EDITOR
                    // Only in the editor should we save it to disk
                    string properPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "Resources");
                    if (!System.IO.Directory.Exists(properPath))
                    {
                        UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");
                    }

                    string fullPath = System.IO.Path.Combine(
                        System.IO.Path.Combine("Assets", "Resources"),
#if UNITY_ANDROID || UNITY_IOS
                        "ONSPSettings-Android.asset");
#else 
                        "ONSPSettings.asset");
#endif
                    UnityEditor.AssetDatabase.CreateAsset(instance, fullPath);
#endif
                }
            }

            return instance;
        }
    }
}
