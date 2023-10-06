#if !VRC_CLIENT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using VRC.Core;
using VRC.Editor;
using VRC.SDKBase.Editor.Api;
using VRC.SDKBase.Editor;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDKBase.Editor.Validation;
using VRC.SDKBase.Validation;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;
using VRC.SDK3.Avatars;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3A.Editor;
using VRC.SDK3A.Editor.Elements;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Elements;
using VRC.SDKBase.Editor.V3;
using Object = UnityEngine.Object;
using VRCStation = VRC.SDK3.Avatars.Components.VRCStation;

[assembly: VRCSdkControlPanelBuilder(typeof(VRCSdkControlPanelAvatarBuilder))]
namespace VRC.SDK3A.Editor
{
    public class VRCSdkControlPanelAvatarBuilder : IVRCSdkAvatarBuilderApi
    {
        private const int MAX_ACTION_TEXTURE_SIZE = 256;

        private VRCSdkControlPanel _builder;
        private VRC_AvatarDescriptor[] _avatars;
        private static VRC_AvatarDescriptor _selectedAvatar;
        private static VRCSdkControlPanelAvatarBuilder _instance;

        private static bool ShowAvatarPerformanceDetails
        {
            get => EditorPrefs.GetBool("VRC.SDKBase_showAvatarPerformanceDetails", false);
            set => EditorPrefs.SetBool("VRC.SDKBase_showAvatarPerformanceDetails",
                value);
        }

        private static PropertyInfo _legacyBlendShapeNormalsPropertyInfo;

        private static PropertyInfo LegacyBlendShapeNormalsPropertyInfo
        {
            get
            {
                if (_legacyBlendShapeNormalsPropertyInfo != null)
                {
                    return _legacyBlendShapeNormalsPropertyInfo;
                }

                Type modelImporterType = typeof(ModelImporter);
                _legacyBlendShapeNormalsPropertyInfo = modelImporterType.GetProperty(
                    "legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );

                return _legacyBlendShapeNormalsPropertyInfo;
            }
        }

        #region Main Interface Methods
        public void ShowSettingsOptions()
        {
            EditorGUILayout.BeginVertical(VRCSdkControlPanel.boxGuiStyle);
            GUILayout.Label("Avatar Options", EditorStyles.boldLabel);
            bool prevShowPerfDetails = ShowAvatarPerformanceDetails;
            bool showPerfDetails =
                EditorGUILayout.ToggleLeft("Show All Avatar Performance Details", prevShowPerfDetails);
            if (showPerfDetails != prevShowPerfDetails)
            {
                ShowAvatarPerformanceDetails = showPerfDetails;
                _builder.ResetIssues();
            }

            EditorGUILayout.EndVertical();
        }

        public bool IsValidBuilder(out string message)
        {
            FindAvatars();
            message = null;
            if (_avatars != null && _avatars.Length > 0) return true;
            message = "A VRCSceneDescriptor or VRCAvatarDescriptor\nis required to build VRChat SDK Content";
            return false;
        }
        
        private void FindAvatars()
        {
            List<VRC_AvatarDescriptor> allAvatars = Tools.FindSceneObjectsOfTypeAll<VRC_AvatarDescriptor>().ToList();
            // Select only the active avatars
            VRC_AvatarDescriptor[] newAvatars =
                allAvatars.Where(av => null != av && av.gameObject.activeInHierarchy).ToArray();

            if (_avatars != null)
            {
                foreach (VRC_AvatarDescriptor a in newAvatars)
                    if (_avatars.Contains(a) == false)
                        _builder.CheckedForIssues = false;
            }

            _avatars = newAvatars.Reverse().ToArray();
        }

        public void ShowBuilder()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            if (BuildPipeline.isBuildingPlayer)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Building – Please Wait ...",
                    VRCSdkControlPanel.titleGuiStyle,
                    GUILayout.Width(VRCSdkControlPanel.SdkWindowWidth));
                return;
            }
            
            if (!_builder.CheckedForIssues)
            {
                _builder.ResetIssues();
                foreach (VRC_AvatarDescriptor t in _avatars)
                    OnGUIAvatarCheck(t);
                _builder.CheckedForIssues = true;
            }

            using (new EditorGUILayout.VerticalScope())
            {
                _builder.OnGUIShowIssues();

                if (_selectedAvatar != null)
                {
                    _builder.OnGUIShowIssues(_selectedAvatar);
                }
            }
        }

        public void RegisterBuilder(VRCSdkControlPanel baseBuilder)
        {
            _builder = baseBuilder;
        }

        public void SelectAllComponents()
        {
            List<Object> show = new List<Object>(Selection.objects);
            foreach (VRC_AvatarDescriptor a in _avatars)
                show.Add(a.gameObject);
            Selection.objects = show.ToArray();
        }
        
        #endregion

        public static void SelectAvatar(VRC_AvatarDescriptor avatar)
        {
            if (_instance == null) return;
            _selectedAvatar = avatar;
            _instance._avatarSelector.SetAvatarSelection(avatar);
            _instance.HandleAvatarSwitch(_instance._visualRoot);
        }

        #region Avatar Validations (IMGUI)
        
        private void OnGUIAvatarCheck(VRC_AvatarDescriptor avatar)
        {
            if (avatar == null) return;
            string vrcFilePath = UnityWebRequest.UnEscapeURL(EditorPrefs.GetString("currentBuildingAssetBundlePath"));
            bool isMobilePlatform = ValidationEditorHelpers.IsMobilePlatform();
            if (!string.IsNullOrEmpty(vrcFilePath) &&
                ValidationHelpers.CheckIfAssetBundleFileTooLarge(ContentType.Avatar, vrcFilePath, out int fileSize, isMobilePlatform))
            {
                _builder.OnGUIWarning(avatar,
                    ValidationHelpers.GetAssetBundleOverSizeLimitMessageSDKWarning(ContentType.Avatar, fileSize, isMobilePlatform),
                    delegate { Selection.activeObject = avatar.gameObject; }, null);
            }

            AvatarPerformanceStats perfStats = new AvatarPerformanceStats(ValidationEditorHelpers.IsMobilePlatform());
            AvatarPerformance.CalculatePerformanceStats(avatar.Name, avatar.gameObject, perfStats, isMobilePlatform);

            OnGUIPerformanceInfo(avatar, perfStats, AvatarPerformanceCategory.Overall,
                GetAvatarSubSelectAction(avatar, typeof(VRC_AvatarDescriptor)), null);
            OnGUIPerformanceInfo(avatar, perfStats, AvatarPerformanceCategory.PolyCount,
                GetAvatarSubSelectAction(avatar, new[] {typeof(MeshRenderer), typeof(SkinnedMeshRenderer)}), null);
            OnGUIPerformanceInfo(avatar, perfStats, AvatarPerformanceCategory.AABB,
                GetAvatarSubSelectAction(avatar, typeof(VRC_AvatarDescriptor)), null);

            if (avatar.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                avatar.VisemeSkinnedMesh == null)
                _builder.OnGUIError(avatar, "This avatar uses Visemes but the Face Mesh is not specified.",
                    delegate { Selection.activeObject = avatar.gameObject; }, null);

            if (ShaderKeywordsUtility.DetectCustomShaderKeywords(avatar))
                _builder.OnGUIWarning(avatar,
                    "A Material on this avatar has custom shader keywords. Please consider optimizing it using the Shader Keywords Utility.",
                    () => { Selection.activeObject = avatar.gameObject; },
                    () =>
                    {
                        EditorApplication.ExecuteMenuItem("VRChat SDK/Utilities/Avatar Shader Keywords Utility");
                    });

            VerifyAvatarMipMapStreaming(avatar);

            Animator anim = avatar.GetComponent<Animator>();
            if (anim == null)
            {
                _builder.OnGUIWarning(avatar,
                    "This avatar does not contain an Animator, and will not animate in VRChat.",
                    delegate { Selection.activeObject = avatar.gameObject; }, null);
            }
            else if (anim.isHuman == false)
            {
                _builder.OnGUIWarning(avatar,
                    "This avatar is not imported as a humanoid rig and will not play VRChat's provided animation set.",
                    delegate { Selection.activeObject = avatar.gameObject; }, null);
            }
            else if (avatar.gameObject.activeInHierarchy == false)
            {
                _builder.OnGUIError(avatar, "Your avatar is disabled in the scene hierarchy!",
                    delegate { Selection.activeObject = avatar.gameObject; }, null);
            }
            else
            {
                Transform lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if ((lFoot == null) || (rFoot == null))
                    _builder.OnGUIError(avatar, "Your avatar is humanoid, but its feet aren't specified!",
                        delegate { Selection.activeObject = avatar.gameObject; }, null);
                if (lFoot != null && rFoot != null)
                {
                    Vector3 footPos = lFoot.position - avatar.transform.position;
                    if (footPos.y < 0)
                        _builder.OnGUIWarning(avatar,
                            "Avatar feet are beneath the avatar's origin (the floor). That's probably not what you want.",
                            delegate
                            {
                                List<Object> gos = new List<Object> {rFoot.gameObject, lFoot.gameObject};
                                Selection.objects = gos.ToArray();
                            }, null);
                }

                Transform lShoulder = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform rShoulder = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (lShoulder == null || rShoulder == null)
                    _builder.OnGUIError(avatar, "Your avatar is humanoid, but its upper arms aren't specified!",
                        delegate { Selection.activeObject = avatar.gameObject; }, null);
                if (lShoulder != null && rShoulder != null)
                {
                    Vector3 shoulderPosition = lShoulder.position - avatar.transform.position;
                    if (shoulderPosition.y < 0.2f)
                        _builder.OnGUIError(avatar, "This avatar is too short. The minimum is 20cm shoulder height.",
                            delegate { Selection.activeObject = avatar.gameObject; }, null);
                    else if (shoulderPosition.y < 1.0f)
                        _builder.OnGUIWarning(avatar, "This avatar is shorter than average.",
                            delegate { Selection.activeObject = avatar.gameObject; }, null);
                    else if (shoulderPosition.y > 5.0f)
                        _builder.OnGUIWarning(avatar, "This avatar is too tall. The maximum is 5m shoulder height.",
                            delegate { Selection.activeObject = avatar.gameObject; }, null);
                    else if (shoulderPosition.y > 2.5f)
                        _builder.OnGUIWarning(avatar, "This avatar is taller than average.",
                            delegate { Selection.activeObject = avatar.gameObject; }, null);
                }

                if (AnalyzeIK(avatar, anim) == false)
                    _builder.OnGUILink(avatar, "See Avatar Rig Requirements for more information.",
                        VRCSdkControlPanelHelp.AVATAR_RIG_REQUIREMENTS_URL);
            }

            ValidateFeatures(avatar, anim, perfStats);

            PipelineManager pm = avatar.GetComponent<PipelineManager>();

            PerformanceRating rating = perfStats.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall);
            if (_builder.NoGuiErrors())
            {
                if (!anim.isHuman)
                {
                    if (pm != null) pm.fallbackStatus = PipelineManager.FallbackStatus.InvalidRig;
                    _builder.OnGUIInformation(avatar, "This avatar does not have a humanoid rig, so it can not be used as a custom fallback.");
                }
                else if (rating > PerformanceRating.Good)
                {
                    if (pm != null) pm.fallbackStatus = PipelineManager.FallbackStatus.InvalidPerformance;
                    _builder.OnGUIInformation(avatar, "This avatar does not have an overall rating of Good or better, so it can not be used as a custom fallback. See the link below for details on Avatar Optimization.");
                }
                else
                {
                    if (pm != null) pm.fallbackStatus = PipelineManager.FallbackStatus.Valid;
                    _builder.OnGUIInformation(avatar, "This avatar can be used as a custom fallback. This avatar must be uploaded for every supported platform to be valid for fallback selection.");
                    if (perfStats.animatorCount.HasValue && perfStats.animatorCount.Value > 1)
                        _builder.OnGUIInformation(avatar, "This avatar uses additional animators, they will be disabled when used as a fallback.");
                }

                // additional messages for Poor and Very Poor Avatars
#if UNITY_ANDROID || UNITY_IOS
                if (rating > PerformanceRating.Poor)
                    _builder.OnGUIInformation(avatar, "This avatar will be blocked by default due to performance. Your fallback will be shown instead.");
                else if (rating > PerformanceRating.Medium)
                    _builder.OnGUIInformation(avatar, "Other users may choose to block this avatar due to performance. Your fallback will be shown instead.");
#else
                if (rating > PerformanceRating.Medium)
                    _builder.OnGUIInformation(avatar, "Other users may choose to block this avatar due to performance. Your fallback will be shown instead.");
#endif
            }
            else
            {
                // shouldn't matter because we can't hit upload button
                if (pm != null) pm.fallbackStatus = PipelineManager.FallbackStatus.InvalidPlatform;
            }
        }

        private void GenerateDebugHashset(VRCAvatarDescriptor avatar)
        {
            avatar.animationHashSet.Clear();

            foreach (VRCAvatarDescriptor.CustomAnimLayer animLayer in avatar.baseAnimationLayers)
            {
                AnimatorController controller = animLayer.animatorController as AnimatorController;
                if (controller != null)
                {
                    foreach (AnimatorControllerLayer layer in controller.layers)
                    {
                        ProcessStateMachine(layer.stateMachine, "");
                        void ProcessStateMachine(AnimatorStateMachine stateMachine, string prefix)
                        {
                            //Update prefix
                            prefix = prefix + stateMachine.name + ".";

                            //States
                            foreach (var state in stateMachine.states)
                            {
                                VRCAvatarDescriptor.DebugHash hash = new VRCAvatarDescriptor.DebugHash();
                                string fullName = prefix + state.state.name;
                                hash.hash = Animator.StringToHash(fullName);
                                hash.name = fullName.Remove(0, layer.stateMachine.name.Length + 1);
                                avatar.animationHashSet.Add(hash);
                            }

                            //Sub State Machines
                            foreach (var subMachine in stateMachine.stateMachines)
                                ProcessStateMachine(subMachine.stateMachine, prefix);
                        }
                    }
                }
            }
        }

        private void ValidateFeatures(VRC_AvatarDescriptor avatar, Animator anim, AvatarPerformanceStats perfStats)
        {
            //Create avatar debug hashset
            VRCAvatarDescriptor avatarSDK3 = avatar as VRCAvatarDescriptor;
            if (avatarSDK3 != null)
            {
                GenerateDebugHashset(avatarSDK3);
            }

            //Validate Playable Layers
            if (avatarSDK3 != null && avatarSDK3.customizeAnimationLayers)
            {
                VRCAvatarDescriptor.CustomAnimLayer gestureLayer = avatarSDK3.baseAnimationLayers[2];
                if (anim != null
                    && anim.isHuman
                    && gestureLayer.animatorController != null
                    && gestureLayer.type == VRCAvatarDescriptor.AnimLayerType.Gesture
                    && !gestureLayer.isDefault)
                {
                    AnimatorController controller = gestureLayer.animatorController as AnimatorController;
                    if (controller != null && controller.layers[0].avatarMask == null)
                        _builder.OnGUIError(avatar, "Gesture Layer needs valid mask on first animator layer",
                            delegate { OpenAnimatorControllerWindow(controller); }, null);
                }
            }

            //Expression menu images
            if (avatarSDK3 != null)
            {
                bool ValidateTexture(Texture2D texture)
                {
                    string path = AssetDatabase.GetAssetPath(texture);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        return true;
                    TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();

                    //Max texture size
                    if ((texture.width > MAX_ACTION_TEXTURE_SIZE || texture.height > MAX_ACTION_TEXTURE_SIZE) &&
                        settings.maxTextureSize > MAX_ACTION_TEXTURE_SIZE)
                        return false;

                    //Compression
                    if (settings.textureCompression == TextureImporterCompression.Uncompressed)
                        return false;

                    //Success
                    return true;
                }

                void FixTexture(Texture2D texture)
                {
                    string path = AssetDatabase.GetAssetPath(texture);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        return;
                    TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();

                    //Max texture size
                    if (texture.width > MAX_ACTION_TEXTURE_SIZE || texture.height > MAX_ACTION_TEXTURE_SIZE)
                        settings.maxTextureSize = Math.Min(settings.maxTextureSize, MAX_ACTION_TEXTURE_SIZE);

                    //Compression
                    if (settings.textureCompression == TextureImporterCompression.Uncompressed)
                        settings.textureCompression = TextureImporterCompression.Compressed;

                    //Set & Reimport
                    importer.SetPlatformTextureSettings(settings);
                    AssetDatabase.ImportAsset(path);
                }

                //Find all textures
                List<Texture2D> textures = new List<Texture2D>();
                List<VRCExpressionsMenu> menuStack = new List<VRCExpressionsMenu>();
                FindTextures(avatarSDK3.expressionsMenu);

                void FindTextures(VRCExpressionsMenu menu)
                {
                    if (menu == null || menuStack.Contains(menu)) //Prevent recursive menu searching
                        return;
                    menuStack.Add(menu);

                    //Check controls
                    foreach (VRCExpressionsMenu.Control control in menu.controls)
                    {
                        AddTexture(control.icon);
                        if (control.labels != null)
                        {
                            foreach (VRCExpressionsMenu.Control.Label label in control.labels)
                                AddTexture(label.icon);
                        }

                        if (control.subMenu != null)
                            FindTextures(control.subMenu);
                    }

                    void AddTexture(Texture2D texture)
                    {
                        if (texture != null)
                            textures.Add(texture);
                    }
                }

                //Validate
                bool isValid = true;
                foreach (Texture2D texture in textures)
                {
                    if (!ValidateTexture(texture))
                        isValid = false;
                }

                if (!isValid)
                    _builder.OnGUIError(avatar, "Images used for Actions & Moods are too large.",
                        delegate { Selection.activeObject = avatar.gameObject; }, FixTextures);

                //Fix
                void FixTextures()
                {
                    foreach (Texture2D texture in textures)
                        FixTexture(texture);
                }
            }

            //Expression menu parameters
            if (avatarSDK3 != null)
            {
                //Check for expression menu/parameters object
                if (avatarSDK3.expressionsMenu != null || avatarSDK3.expressionParameters != null)
                {
                    //Menu
                    if (avatarSDK3.expressionsMenu == null)
                        _builder.OnGUIError(avatar, "VRCExpressionsMenu object reference is missing.",
                            delegate { Selection.activeObject = avatarSDK3; }, null);

                    //Parameters
                    if (avatarSDK3.expressionParameters == null)
                        _builder.OnGUIError(avatar, "VRCExpressionParameters object reference is missing.",
                            delegate { Selection.activeObject = avatarSDK3; }, null);
                }

                //Check if parameters is valid
                if (avatarSDK3.expressionParameters != null && avatarSDK3.expressionParameters.CalcTotalCost() > VRCExpressionParameters.MAX_PARAMETER_COST)
                {
                    _builder.OnGUIError(avatar, "VRCExpressionParameters has too many parameters defined.",
                        delegate { Selection.activeObject = avatarSDK3.expressionParameters; }, null);
                }

                //Find all existing parameters
                if (avatarSDK3.expressionsMenu != null && avatarSDK3.expressionParameters != null)
                {
                    List<VRCExpressionsMenu> menuStack = new List<VRCExpressionsMenu>();
                    List<string> parameters = new List<string>();
                    List<VRCExpressionsMenu> selects = new List<VRCExpressionsMenu>();
                    FindParameters(avatarSDK3.expressionsMenu);

                    void FindParameters(VRCExpressionsMenu menu)
                    {
                        if (menu == null || menuStack.Contains(menu)) //Prevent recursive menu searching
                            return;
                        menuStack.Add(menu);

                        //Check controls
                        foreach (VRCExpressionsMenu.Control control in menu.controls)
                        {
                            AddParameter(control.parameter);
                            if (control.subParameters != null)
                            {
                                foreach (VRCExpressionsMenu.Control.Parameter subParameter in control.subParameters)
                                {
                                    AddParameter(subParameter);
                                }
                            }

                            if (control.subMenu != null)
                                FindParameters(control.subMenu);
                        }

                        void AddParameter(VRCExpressionsMenu.Control.Parameter parameter)
                        {
                            if (parameter != null)
                            {
                                parameters.Add(parameter.name);
                                selects.Add(menu);
                            }
                        }
                    }

                    //Validate parameters
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        string parameter = parameters[i];
                        VRCExpressionsMenu select = selects[i];

                        //Find
                        bool exists = string.IsNullOrEmpty(parameter) || avatarSDK3.expressionParameters.FindParameter(parameter) != null;
                        if (!exists)
                        {
                            _builder.OnGUIError(avatar,
                                "VRCExpressionsMenu uses a parameter that is not defined.\nParameter: " + parameter,
                                delegate { Selection.activeObject = select; }, null);
                        }
                    }

                    //Validate param choices
                    foreach (var menu in menuStack)
                    {
                        foreach (var control in menu.controls)
                        {
                            bool isValid = true;
                            if (control.type == VRCExpressionsMenu.Control.ControlType.FourAxisPuppet)
                            {
                                isValid &= ValidateNonBoolParam(control.subParameters[0].name);
                                isValid &= ValidateNonBoolParam(control.subParameters[1].name);
                                isValid &= ValidateNonBoolParam(control.subParameters[2].name);
                                isValid &= ValidateNonBoolParam(control.subParameters[3].name);
                            }
                            else if (control.type == VRCExpressionsMenu.Control.ControlType.RadialPuppet)
                            {
                                isValid &= ValidateNonBoolParam(control.subParameters[0].name);
                            }
                            else if (control.type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet)
                            {
                                isValid &= ValidateNonBoolParam(control.subParameters[0].name);
                                isValid &= ValidateNonBoolParam(control.subParameters[1].name);
                            }
                            if (!isValid)
                            {
                                _builder.OnGUIError(avatar,
                                "VRCExpressionsMenu uses an invalid parameter for a control.\nControl: " + control.name,
                                delegate { Selection.activeObject = menu; }, null);
                            }
                        }

                        bool ValidateNonBoolParam(string name)
                        {
                            VRCExpressionParameters.Parameter param = string.IsNullOrEmpty(name) ? null : avatarSDK3.expressionParameters.FindParameter(name);
                            if (param != null && param.valueType == VRCExpressionParameters.ValueType.Bool)
                                return false;
                            return true;
                        }
                    }
                }

                //Dynamic Bones
                if (perfStats.dynamicBone != null && (perfStats.dynamicBone.Value.colliderCount > 0 || perfStats.dynamicBone.Value.componentCount > 0))
                {
                    _builder.OnGUIWarning(avatar, "This avatar uses depreciated DynamicBone components. Upgrade to PhysBones to guarantee future compatibility.",
                        null,
                        () => { AvatarDynamicsSetup.ConvertDynamicBonesToPhysBones( new GameObject[]{ avatarSDK3.gameObject} ); });
                }
            }

            List<Component> componentsToRemove = SDK3.Validation.AvatarValidation.FindIllegalComponents(avatar.gameObject).ToList();

            // create a list of the PipelineSaver component(s)
            List<Component> toRemoveSilently = new List<Component>();
            foreach (Component c in componentsToRemove)
            {
                if (c.GetType().Name == "PipelineSaver")
                {
                    toRemoveSilently.Add(c);
                }
            }

            // delete PipelineSaver(s) from the list of the Components we will destroy now
            foreach (Component c in toRemoveSilently)
            {
                    componentsToRemove.Remove(c);
            }

            HashSet<string> componentsToRemoveNames = new HashSet<string>();
            List<Component> toRemove = componentsToRemove ?? componentsToRemove;
            foreach (Component c in toRemove)
            {
                if (componentsToRemoveNames.Contains(c.GetType().Name) == false)
                    componentsToRemoveNames.Add(c.GetType().Name);
            }

            if (componentsToRemoveNames.Count > 0)
                _builder.OnGUIError(avatar,
                    "The following component types are found on the Avatar and will be removed by the client: " +
                    string.Join(", ", componentsToRemoveNames.ToArray()),
                    delegate { ShowRestrictedComponents(toRemove); },
                    delegate { FixRestrictedComponents(toRemove); });

            List<AudioSource> audioSources =
                avatar.gameObject.GetComponentsInChildrenExcludingEditorOnly<AudioSource>(true).ToList();
            if (audioSources.Count > 0)
                _builder.OnGUIWarning(avatar,
                    "Audio sources found on Avatar, they will be adjusted to safe limits, if necessary.",
                    GetAvatarSubSelectAction(avatar, typeof(AudioSource)), null);

            foreach (var audioSource in audioSources)
            {
                if (audioSource.clip && audioSource.clip.loadType == AudioClipLoadType.DecompressOnLoad && !audioSource.clip.loadInBackground)
                    _builder.OnGUIError(avatar,
                        "Found an audio clip with load type `Decompress On Load` which doesn't have `Load In Background` enabled.\nPlease enable `Load In Background` on the audio clip.", 
                         GetAvatarAudioSourcesWithDecompressOnLoadWithoutBackgroundLoad(avatar), null);
            }
            
            List<VRCStation> stations =
                avatar.gameObject.GetComponentsInChildrenExcludingEditorOnly<VRCStation>(true).ToList();
            if (stations.Count > 0)
                _builder.OnGUIWarning(avatar, "Stations found on Avatar, they will be adjusted to safe limits, if necessary.",
                    GetAvatarSubSelectAction(avatar, typeof(VRCStation)), null);

            if (VRCSdkControlPanel.HasSubstances(avatar.gameObject))
            {
                _builder.OnGUIWarning(avatar,
                    "This avatar has one or more Substance materials, which is not supported and may break in-game. Please bake your Substances to regular materials.",
                    () => { Selection.objects = VRCSdkControlPanel.GetSubstanceObjects(avatar.gameObject); },
                    null);
            }

            CheckAvatarMeshesForLegacyBlendShapesSetting(avatar);
            CheckAvatarMeshesForMeshReadWriteSetting(avatar);

#if UNITY_ANDROID
            IEnumerable<Shader> illegalShaders = VRC.SDK3.Validation.AvatarValidation.FindIllegalShaders(avatar.gameObject);
            foreach (Shader s in illegalShaders)
            {
                _builder.OnGUIError(avatar, "Avatar uses unsupported shader '" + s.name + "'. You can only use the shaders provided in 'VRChat/Mobile' for Quest avatars.", delegate () { Selection.activeObject
     = avatar.gameObject; }, null);
            }
#endif

            foreach (AvatarPerformanceCategory perfCategory in Enum.GetValues(typeof(AvatarPerformanceCategory)))
            {
                if (perfCategory == AvatarPerformanceCategory.Overall ||
                    perfCategory == AvatarPerformanceCategory.PolyCount ||
                    perfCategory == AvatarPerformanceCategory.AABB ||
                    perfCategory == AvatarPerformanceCategory.AvatarPerformanceCategoryCount)
                {
                    continue;
                }

                Action show = null;

                switch (perfCategory)
                {
                    case AvatarPerformanceCategory.AnimatorCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(Animator));
                        break;
                    case AvatarPerformanceCategory.AudioSourceCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(AudioSource));
                        break;
                    case AvatarPerformanceCategory.BoneCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(SkinnedMeshRenderer));
                        break;
                    case AvatarPerformanceCategory.ClothCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(Cloth));
                        break;
                    case AvatarPerformanceCategory.ClothMaxVertices:
                        show = GetAvatarSubSelectAction(avatar, typeof(Cloth));
                        break;
                    case AvatarPerformanceCategory.LightCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(Light));
                        break;
                    case AvatarPerformanceCategory.LineRendererCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(LineRenderer));
                        break;
                    case AvatarPerformanceCategory.MaterialCount:
                        show = GetAvatarSubSelectAction(avatar,
                            new[] {typeof(MeshRenderer), typeof(SkinnedMeshRenderer)});
                        break;
                    case AvatarPerformanceCategory.MeshCount:
                        show = GetAvatarSubSelectAction(avatar,
                            new[] {typeof(MeshRenderer), typeof(SkinnedMeshRenderer)});
                        break;
                    case AvatarPerformanceCategory.ParticleCollisionEnabled:
                        show = GetAvatarSubSelectAction(avatar, typeof(ParticleSystem));
                        break;
                    case AvatarPerformanceCategory.ParticleMaxMeshPolyCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(ParticleSystem));
                        break;
                    case AvatarPerformanceCategory.ParticleSystemCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(ParticleSystem));
                        break;
                    case AvatarPerformanceCategory.ParticleTotalCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(ParticleSystem));
                        break;
                    case AvatarPerformanceCategory.ParticleTrailsEnabled:
                        show = GetAvatarSubSelectAction(avatar, typeof(ParticleSystem));
                        break;
                    case AvatarPerformanceCategory.PhysicsColliderCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(Collider));
                        break;
                    case AvatarPerformanceCategory.PhysicsRigidbodyCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(Rigidbody));
                        break;
                    case AvatarPerformanceCategory.PolyCount:
                        show = GetAvatarSubSelectAction(avatar,
                            new[] {typeof(MeshRenderer), typeof(SkinnedMeshRenderer)});
                        break;
                    case AvatarPerformanceCategory.SkinnedMeshCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(SkinnedMeshRenderer));
                        break;
                    case AvatarPerformanceCategory.TrailRendererCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(TrailRenderer));
                        break;
                    case AvatarPerformanceCategory.PhysBoneComponentCount:
                    case AvatarPerformanceCategory.PhysBoneTransformCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone));
                        break;
                    case AvatarPerformanceCategory.PhysBoneColliderCount:
                    case AvatarPerformanceCategory.PhysBoneCollisionCheckCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider));
                        break;
                    case AvatarPerformanceCategory.ContactCount:
                        show = GetAvatarSubSelectAction(avatar, typeof(VRC.Dynamics.ContactBase));
                        break;
                }

                // we can only show these buttons if DynamicBone is installed

                Type dynamicBoneType = typeof(VRC.SDK3.Validation.AvatarValidation).Assembly.GetType("DynamicBone");
                Type dynamicBoneColliderType = typeof(VRC.SDK3.Validation.AvatarValidation).Assembly.GetType("DynamicBoneCollider");
                if ((dynamicBoneType != null) && (dynamicBoneColliderType != null))
                {
                    switch (perfCategory)
                    {
                        case AvatarPerformanceCategory.DynamicBoneColliderCount:
                            show = GetAvatarSubSelectAction(avatar, dynamicBoneColliderType);
                            break;
                        case AvatarPerformanceCategory.DynamicBoneCollisionCheckCount:
                            show = GetAvatarSubSelectAction(avatar, dynamicBoneColliderType);
                            break;
                        case AvatarPerformanceCategory.DynamicBoneComponentCount:
                            show = GetAvatarSubSelectAction(avatar, dynamicBoneType);
                            break;
                        case AvatarPerformanceCategory.DynamicBoneSimulatedBoneCount:
                            show = GetAvatarSubSelectAction(avatar, dynamicBoneType);
                            break;
                    }
                }

                OnGUIPerformanceInfo(avatar, perfStats, perfCategory, show, null);
            }

            _builder.OnGUILink(avatar, "Avatar Optimization Tips", VRCSdkControlPanelHelp.AVATAR_OPTIMIZATION_TIPS_URL);
        }

        private void OnGUIPerformanceInfo(VRC_AvatarDescriptor avatar, AvatarPerformanceStats perfStats,
            AvatarPerformanceCategory perfCategory, Action show, Action fix)
        {
            PerformanceRating rating = perfStats.GetPerformanceRatingForCategory(perfCategory);
            SDKPerformanceDisplay.GetSDKPerformanceInfoText(perfStats, perfCategory, out string text,
                out PerformanceInfoDisplayLevel displayLevel);

            switch (displayLevel)
            {
                case PerformanceInfoDisplayLevel.None:
                {
                    break;
                }
                case PerformanceInfoDisplayLevel.Verbose:
                {
                    if (ShowAvatarPerformanceDetails)
                    {
                        _builder.OnGUIStat(avatar, text, rating, show, fix);
                    }

                    break;
                }
                case PerformanceInfoDisplayLevel.Info:
                {
                    _builder.OnGUIStat(avatar, text, rating, show, fix);
                    break;
                }
                case PerformanceInfoDisplayLevel.Warning:
                {
                    _builder.OnGUIStat(avatar, text, rating, show, fix);
                    break;
                }
                case PerformanceInfoDisplayLevel.Error:
                {
                    _builder.OnGUIStat(avatar, text, rating, show, fix);
                    _builder.OnGUIError(avatar, text, delegate { Selection.activeObject = avatar.gameObject; }, null);
                    break;
                }
                default:
                {
                    _builder.OnGUIError(avatar, "Unknown performance display level.",
                        delegate { Selection.activeObject = avatar.gameObject; }, null);
                    break;
                }
            }
        }
        
        #endregion

        #region Avatar Builder UI (UIToolkit)
        
        public void CreateBuilderErrorGUI(VisualElement root)
        {
            var errorContainer = new VisualElement();
            errorContainer.AddToClassList("builder-error-container");
            root.Add(errorContainer);
            var errorLabel = new Label("A VRCAvatarDescriptor is required to build a VRChat Avatar");
            errorLabel.AddToClassList("mb-2");
            errorLabel.AddToClassList("text-center");
            errorLabel.AddToClassList("white-space-normal");
            errorLabel.style.maxWidth = 450;
            errorContainer.Add(errorLabel);
            var addButton = new Button
            {
                text = "Add a VRCAvatarDescriptor",
                tooltip = "Adds a VRCAvatarDescriptor to the selected GameObject"
            };
            addButton.clickable.clicked += () =>
            {
                Undo.AddComponent<VRCAvatarDescriptor>(Selection.activeGameObject);
                _builder.ResetIssues();
            };
            errorContainer.Add(addButton);
            
            if (Selection.activeGameObject == null)
            {
                addButton.SetEnabled(false);
            }

            errorContainer.schedule.Execute(() =>
            {
                var hasSelection = Selection.activeGameObject != null;
                addButton.SetEnabled(hasSelection);
                errorLabel.text = "A VRCAvatarDescriptor is required to build a VRChat Avatar" + (hasSelection ? "" : ".\nSelect a GameObject to add a VRCAvatarDescriptor to it.");
            }).Every(500);
        }
        
        private VRCAvatar _avatarData;
        private VRCAvatar _originalAvatarData;

        private VisualElement _saveChangesBlock;
        private VisualElement _visualRoot;

        private string _newThumbnailImagePath;
        
        private VRCTextField _nameField;
        private VRCTextField _descriptionField;
        private Label _lastUpdatedLabel;
        private Label _versionLabel;
        private Button _saveChangesButton;
        private Button _discardChangesButton;
        private VisualElement _progressBlock;
        private VisualElement _progressBar;
        private Label _progressText;
        private Foldout _infoFoldout;
        private Thumbnail _thumbnail;
        private ThumbnailFoldout _thumbnailFoldout;
        private Button _buildAndTestButton;
        private Button _buildAndUploadButton;
        private VisualElement _newAvatarBlock;
        private Checklist _creationChecklist;
        private AvatarSelector _avatarSelector;
        private VisualElement _uploadDisabledBlock;
        private Label _uploadDisabledText;
        private VisualElement _localTestDisabledBlock;
        private Label _localTestDisabledText;
        private VisualElement _fallbackInfo;
        private Button _updateCancelButton;
        private ContentWarningsField _contentWarningsField;
        private VisualElement _v3Block;
        private VisualElement _platformSwitcher;
        private VisualElement _visibilityPopupBlock;
        private Toggle _acceptTermsToggle;
        private PopupField<string> _visibilityPopup;
        private Dictionary<string, Foldout> _foldouts = new Dictionary<string, Foldout>();

        private string _lastBlueprintId;

        private bool _isContentInfoDirty;
        private bool IsContentInfoDirty
        {
            get => _isContentInfoDirty;
            set
            {
                _isContentInfoDirty = value;
                var isDirty = CheckDirty();
                var alreadyDirty = !_saveChangesBlock.ClassListContains("d-none");
                _saveChangesBlock.EnableInClassList("d-none", !isDirty);
                if (isDirty && !alreadyDirty)
                {
                    _saveChangesBlock.experimental.animation.Start(new Vector2(_visualRoot.layout.width, 0), new Vector2(_visualRoot.layout.width, 20), 250, (element, vector2) =>
                    {
                        element.style.height = vector2.y;
                    });
                }
            }
        }

        private static CancellationTokenSource _avatarSwitchCancellationToken = new CancellationTokenSource();
        private struct ProgressBarStateData
        {
            public bool Visible { get; set; }
            public string Text { get; set; }
            public float Progress { get; set; }
            
            public static implicit operator ProgressBarStateData(bool visible)
            {
                return new ProgressBarStateData {Visible = visible};
            }
        }

        private ProgressBarStateData _progressBarState;
        private ProgressBarStateData ProgressBarState
        {
            get => _progressBarState;
            set
            {
                if (_progressBarState.Visible != value.Visible)
                {
                    _progressBlock.EnableInClassList("d-none", !value.Visible);
                    if (value.Visible)
                    {
                        _progressBar.style.width = 0;
                    }
                }

                _progressText.text = value.Text;
                if (Mathf.Abs(_progressBarState.Progress - value.Progress) > float.Epsilon)
                {
                    // execute on next frame to allow for layout to calculate
                    _visualRoot.schedule.Execute(() =>
                    {
                        _progressBar.experimental.animation.Start(
                            new StyleValues {width = _progressBar.layout.width, height = 28f}, 
                            new StyleValues {width = _progressBlock.layout.width * value.Progress, height = 28f}, 
                            500
                        );
                    }).StartingIn(50);
                }
                _progressBarState = value;
            }
        }

        private bool UpdateCancelEnabled
        {
            get => !_updateCancelButton.ClassListContains("d-none");
            set
            {
                bool wasEnabled = UpdateCancelEnabled;
                if (wasEnabled != value) 
                {
                    _updateCancelButton.EnableInClassList("d-none", wasEnabled);
                }
            }
        }

        private bool _uiEnabled;
        private bool UiEnabled
        {
            get => _uiEnabled;
            set
            {
                _uiEnabled = value;
                _infoFoldout.SetEnabled(value);
                _saveChangesButton.SetEnabled(value);
                _discardChangesButton.SetEnabled(value);
                _buildAndTestButton?.SetEnabled(value);
                _buildAndUploadButton?.SetEnabled(value);
                _thumbnailFoldout.SetEnabled(value);
                _avatarSelector.PopupEnabled = value;
                _platformSwitcher.SetEnabled(value);
                _acceptTermsToggle?.SetEnabled(value);
            }
        }

        private bool _isNewAvatar;
        private bool IsNewAvatar
        {
            get => _isNewAvatar;
            set
            {
                _isNewAvatar = value;
                _newAvatarBlock.EnableInClassList("d-none", !value);
            }
        }

        private enum FallbackStatus
        {
            Incompatible,
            Compatible,
            Selectable,
            Selected
        }

        private FallbackStatus _currentFallbackStatus;

        private FallbackStatus CurrentFallbackStatus
        {
            get => _currentFallbackStatus;
            set
            {
                _currentFallbackStatus = value;
                switch (_currentFallbackStatus)
                {
                    case FallbackStatus.Incompatible:
                        _fallbackInfo.Clear();
                        var label = new Label(
                            "This avatar cannot be used as a fallback. Check Validations below for more info");
                        label.AddToClassList("white-space-normal");
                        _fallbackInfo.Add(label);
                        break;
                    case FallbackStatus.Compatible:
                        _fallbackInfo.Clear();
                        label = new Label();
                        if (Tools.Platform == "android")
                        {
                            label.text = "This avatar can be used as a fallback. Check Validations below for more info.";
                        }
                        else
                        {
                            label.text =
                                "This avatar can be used as a fallback. Switch to the Android platform to select it.";
                        }
                        label.AddToClassList("white-space-normal");
                        _fallbackInfo.Add(label);
                        break;
                    case FallbackStatus.Selectable:
                    {
                        async void SetAvatarFallback()
                        {
                            try
                            {
                                _avatarData = await VRCApi.SetAvatarAsFallback(_avatarData.ID, _avatarData);
                                CurrentFallbackStatus = FallbackStatus.Selected;
                            }
                            catch (ApiErrorException apiError)
                            {
                                await _builder.ShowBuilderNotification("Failed to set fallback",
                                    new AvatarFallbackSelectionErrorNotification(apiError.ErrorMessage),
                                    "red");
                            }
                            catch (RequestFailedException requestError)
                            {
                                await _builder.ShowBuilderNotification("Failed to set fallback",
                                    new AvatarFallbackSelectionErrorNotification(requestError.Message),
                                    "red");
                            }
                        }
                        _fallbackInfo.Clear();
                        var button = new Button(SetAvatarFallback)
                        {
                            text = "Set this avatar as fallback"
                        };
                        button.AddToClassList("flex-grow-1");
                        _fallbackInfo.Add(button);
                        break;
                    }
                    case FallbackStatus.Selected:
                        _fallbackInfo.Clear();
                        label = new Label(
                            "This avatar is currently set as your fallback");
                        label.AddToClassList("white-space-normal");
                        _fallbackInfo.Add(label);
                        break;
                }
            }
        }

        public void CreateContentInfoGUI(VisualElement root)
        {
            root.Clear();
            root.UnregisterCallback<DetachFromPanelEvent>(HandlePanelDetach);
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
            VRCSdkControlPanel.OnSdkPanelDisable -= HandleSdkPanelDisable;
            
            var tree = Resources.Load<VisualTreeAsset>("VRCSdkAvatarBuilderContentInfo");
            tree.CloneTree(root);
            var styles = Resources.Load<StyleSheet>("VRCSdkAvatarBuilderContentInfoStyles");
            if (!root.styleSheets.Contains(styles))
            {
                root.styleSheets.Add(styles);
            }

            root.RegisterCallback<DetachFromPanelEvent>(HandlePanelDetach);
            EditorSceneManager.sceneClosed += HandleSceneClosed;
            VRCSdkControlPanel.OnSdkPanelDisable += HandleSdkPanelDisable;
            
            _avatarSelector = root.Q<AvatarSelector>("avatar-selector");
            _nameField = root.Q<VRCTextField>("content-name");
            _descriptionField = root.Q<VRCTextField>("content-description");
            _lastUpdatedLabel = root.Q<Label>("last-updated-label");
            _versionLabel = root.Q<Label>("version-label");
            _infoFoldout = root.Q<Foldout>("info-foldout");
            _thumbnailFoldout = root.Q<ThumbnailFoldout>();
            _thumbnail = _thumbnailFoldout.Thumbnail;
            _saveChangesBlock = root.Q("save-changes-block");
            _saveChangesButton = root.Q<Button>("save-changes-button");
            _discardChangesButton = root.Q<Button>("discard-changes-button");
            _newAvatarBlock = root.Q("new-avatar-block");
            _creationChecklist = root.Q<Checklist>("new-avatar-checklist");
            _fallbackInfo = root.Q<VisualElement>("fallback-avatar-info");
            _contentWarningsField = root.Q<ContentWarningsField>("content-warnings");

            _platformSwitcher = _builder.rootVisualElement.Q("platform-switcher");
            _progressBlock = _builder.rootVisualElement.Q("progress-section");
            _progressBar = _builder.rootVisualElement.Q("update-progress-bar");
            _progressText = _builder.rootVisualElement.Q<Label>("update-progress-text");
            _progressBarState = false;
            _updateCancelButton = _builder.rootVisualElement.Q<Button>("update-cancel-button");

            _visibilityPopupBlock = root.Q("visibility-block");
            _visibilityPopup = new PopupField<string>(
                "Visibility", 
                new List<string> {"private", "public"},
                "private",
                selected => selected.Substring(0,1).ToUpper() + selected.Substring(1), 
                item => item.Substring(0,1).ToUpper() + item.Substring(1)
            );
            _visibilityPopupBlock.Add(_visibilityPopup);

            var foldouts = root.Query<Foldout>().ToList();
            _foldouts.Clear();
            foreach (var foldout in foldouts)
            {
                _foldouts[foldout.name] = foldout;
                foldout.RegisterValueChangedCallback(HandleFoldoutToggle);
                foldout.SetValueWithoutNotify(SessionState.GetBool($"{AvatarBuilderSessionState.SESSION_STATE_PREFIX}.Foldout.{foldout.name}", true));
            }

            var currentAvatars = _avatars.ToList();
            
            {
                _avatarSelector.RegisterValueChangedCallback(evt =>
                {
                    _selectedAvatar = evt.newValue;
                    HandleAvatarSwitch(root);
                });

                var selectedIndex = currentAvatars.IndexOf(_selectedAvatar);
                if (selectedIndex < 0) selectedIndex = currentAvatars.Count - 1;
                _avatarSelector.SetAvatars(currentAvatars, selectedIndex);
            }
            
            // avatars can be added or removed at any time, so we need to check for changes periodically
            root.schedule.Execute(() =>
            {
                // this handles a case where the avatars didn't exist when the builder was opened
                if (_selectedAvatar == null && _avatars.Length > 0)
                {
                    // special case when the avatar gets removed during upload
                    if (_uploadState == SdkUploadState.Uploading) return;
                    
                    _selectedAvatar = _avatars[_avatars.Length - 1];
                    HandleAvatarSwitch(root);
                }
                
                // ignore any changes while the UI is disabled
                if (!UiEnabled) return;

                // this handles addition and removal of new avatars
                if (_avatars.SequenceEqual(currentAvatars)) return;
                
                currentAvatars = _avatars.ToList();
                if (currentAvatars.Count == 0) return;
                var selectedIndex = currentAvatars.IndexOf(_selectedAvatar);
                // if the selected avatar was removed - redraw the whole panel with new data
                if (selectedIndex == -1)
                {
                    selectedIndex = currentAvatars.Count - 1;
                    _selectedAvatar = _avatars[selectedIndex];
                    HandleAvatarSwitch(root);
                }
                // update the popup with the new list on any sequence change
                _avatarSelector.SetAvatars(currentAvatars, selectedIndex);
            }).Every(1000);

            if (_selectedAvatar != null)
            {
                HandleAvatarSwitch(root);
            }
        }

        private async void HandleAvatarSwitch(VisualElement root)
        {
            _visualRoot = root;
            // Cancel all ongoing ops
            _avatarSwitchCancellationToken.Cancel();
            _avatarSwitchCancellationToken = new CancellationTokenSource();
            
            var platformsBlock = root.Q<Label>("content-platform-info");
            
            // Unregister all the callbacks to avoid multiple calls
            _nameField.UnregisterCallback<ChangeEvent<string>>(HandleNameChange);
            _descriptionField.UnregisterCallback<ChangeEvent<string>>(HandleDescriptionChange);
            _visibilityPopup.UnregisterCallback<ChangeEvent<string>>(HandleVisibilityChange);
            _thumbnailFoldout.OnNewThumbnailSelected -= HandleThumbnailChanged;
            _discardChangesButton.clicked -= HandleDiscardChangesClick;
            _saveChangesButton.clicked -= HandleSaveChangesClick;
            _contentWarningsField.OnToggleTag -= HandleToggleTag;
            root.schedule.Execute(CheckBlueprintChanges).Pause();

            // Load the avatar data
            _nameField.Loading = true;
            _descriptionField.Loading = true;
            _thumbnail.Loading = true;
            _nameField.Reset();
            _descriptionField.Reset();
            _thumbnail.ClearImage();
            IsNewAvatar = false;
            _fallbackInfo.Clear();
            _fallbackInfo.Add(new Label("Loading..."));
            UiEnabled = false;

            // we're in the middle of scene changes, so we exit early
            if (_selectedAvatar == null) return;
            
            var hasPm = _selectedAvatar.TryGetComponent<PipelineManager>(out var pm);
            if (!hasPm)
            {
                Debug.LogWarning("No PipelineManager found on the avatar, make sure you added an Avatar Descriptor");
                return;
            }

            var avatarId = pm.blueprintId;
            _lastBlueprintId = avatarId;
            _avatarData = new VRCAvatar();
            if (string.IsNullOrWhiteSpace(avatarId))
            {
                IsNewAvatar = true;
            }
            else
            {
                try
                {
                    _avatarData = await VRCApi.GetAvatar(avatarId, true, cancellationToken: _avatarSwitchCancellationToken.Token);
                    if (APIUser.CurrentUser != null && _avatarData.AuthorId != APIUser.CurrentUser?.id)
                    {
                        Core.Logger.LogError("Loaded data for the avatar we do not own, clearing blueprint ID");
                        Undo.RecordObject(pm, "Cleared the blueprint ID we do not own");
                        pm.blueprintId = "";
                        avatarId = "";
                        _lastBlueprintId = "";
                        _avatarData = new VRCAvatar();
                        IsNewAvatar = true;
                    }
                }
                catch (TaskCanceledException)
                {
                    // avatar selection changed
                    return;
                }
                catch (ApiErrorException ex)
                {
                    if (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        _avatarData = new VRCAvatar();
                        IsNewAvatar = true;
                    }
                    else
                    {
                        Debug.LogError(ex.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            if (IsNewAvatar)
            {
                RestoreSessionState();
                
                _avatarData.CreatedAt = DateTime.Now;
                _avatarData.UpdatedAt = DateTime.MinValue;
                _lastUpdatedLabel.parent.AddToClassList("d-none");
                _versionLabel.parent.AddToClassList("d-none");
                _fallbackInfo.parent.AddToClassList("d-none");

                platformsBlock.parent.AddToClassList("d-none");
                
                switch (pm.fallbackStatus)
                {
                    case PipelineManager.FallbackStatus.Valid:
                        CurrentFallbackStatus = FallbackStatus.Compatible;
                        break;
                    default:
                        CurrentFallbackStatus = FallbackStatus.Incompatible;
                        break;
                }
                
                _creationChecklist.RemoveFromClassList("d-none");
                _creationChecklist.Items = new List<Checklist.ChecklistItem>
                {
                    new Checklist.ChecklistItem
                    {
                        Value = "name",
                        Label = "Give your avatar a name",
                        Checked = false
                    },
                    new Checklist.ChecklistItem
                    {
                        Value = "thumbnail",
                        Label = "Select a thumbnail image",
                        Checked = false
                    },
                    new Checklist.ChecklistItem
                    {
                        Value = "build",
                        Label = "Click \"Build & Publish\"",
                        Checked = false
                    },
                };

                ValidateChecklist();
            }
            else
            {
                AvatarBuilderSessionState.Clear();
                
                platformsBlock.parent.RemoveFromClassList("d-none");
                _creationChecklist.AddToClassList("d-none");
            
                _nameField.value = _avatarData.Name;
                _descriptionField.value = _avatarData.Description;
                _visibilityPopup.value = _avatarData.ReleaseStatus;
                
                _lastUpdatedLabel.text = (_avatarData.UpdatedAt != DateTime.MinValue) ? _avatarData.UpdatedAt.ToString() : _avatarData.CreatedAt.ToString();
                _lastUpdatedLabel.parent.RemoveFromClassList("d-none");
                
                _versionLabel.text = _avatarData.Version.ToString();
                _versionLabel.parent.RemoveFromClassList("d-none");

                _fallbackInfo.parent.RemoveFromClassList("d-none");

                var platforms = new HashSet<string>();
                foreach (var p in _avatarData.UnityPackages.Select(p => VRCSdkControlPanel.CONTENT_PLATFORMS_MAP[p.Platform]))
                {
                    platforms.Add(p);
                }
                platformsBlock.text = string.Join(", ", platforms);
                
                await _thumbnail.SetImageUrl(_avatarData.ThumbnailImageUrl, _avatarSwitchCancellationToken.Token);
                
                if (APIUser.CurrentUser.fallbackId == _avatarData.ID)
                {
                    CurrentFallbackStatus = FallbackStatus.Selected;
                }
                else
                {
                    switch (pm.fallbackStatus)
                    {
                        case PipelineManager.FallbackStatus.Valid:
                            if (platforms.Contains("Windows") && platforms.Contains("Android") && Tools.Platform == "android")
                            {
                                CurrentFallbackStatus = FallbackStatus.Selectable;
                            }
                            else
                            {
                                CurrentFallbackStatus = FallbackStatus.Compatible;
                            }
                            break;
                        default:
                            CurrentFallbackStatus = FallbackStatus.Incompatible;
                            break;
                    }
                }
            }
            
            _nameField.Loading = false;
            _descriptionField.Loading = false;
            _thumbnail.Loading = false;
            UiEnabled = true;

            var avatarTags = _avatarData.Tags ?? new List<string>();
            _originalAvatarData = _avatarData;
            // lists get passed by reference, so we instantiate a new list to avoid modifying the original
            _originalAvatarData.Tags = new List<string>(avatarTags);

            _contentWarningsField.originalTags = _originalAvatarData.Tags;
            _contentWarningsField.tags = avatarTags;
            _contentWarningsField.OnToggleTag += HandleToggleTag;

            _nameField.RegisterValueChangedCallback(HandleNameChange);
            _descriptionField.RegisterValueChangedCallback(HandleDescriptionChange);
            _visibilityPopup.RegisterValueChangedCallback(HandleVisibilityChange);
            _thumbnailFoldout.OnNewThumbnailSelected += HandleThumbnailChanged;
            
            _discardChangesButton.clicked += HandleDiscardChangesClick;
            _saveChangesButton.clicked += HandleSaveChangesClick;

            root.schedule.Execute(CheckBlueprintChanges).Every(1000);
        }

        private void RestoreSessionState()
        {
            _avatarData.Name = AvatarBuilderSessionState.AvatarName;
            _nameField.SetValueWithoutNotify(_avatarData.Name);

            _avatarData.Description = AvatarBuilderSessionState.AvatarDesc;
            _descriptionField.SetValueWithoutNotify(_avatarData.Description);
            
            _avatarData.ReleaseStatus = AvatarBuilderSessionState.AvatarReleaseStatus;
            _visibilityPopup.SetValueWithoutNotify(_avatarData.ReleaseStatus);

            _contentWarningsField.tags = _avatarData.Tags = new List<string>(AvatarBuilderSessionState.AvatarTags.Split('|'));
            
            _newThumbnailImagePath = AvatarBuilderSessionState.AvatarThumbPath;
            if (!string.IsNullOrWhiteSpace(_newThumbnailImagePath))
                _thumbnail.SetImage(_newThumbnailImagePath);
        }

        #region Event Handlers

        private void HandlePanelDetach(DetachFromPanelEvent evt)
        {
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
        }

        // This auto-cancels uploads when the user changes scenes
        private void HandleSceneClosed(Scene scene)
        {
            _avatarSwitchCancellationToken.Cancel();
            _avatarSwitchCancellationToken = new CancellationTokenSource();
            
            if (_avatarUploadCancellationTokenSource == null) return;
            _avatarUploadCancellationTokenSource.Cancel();
            _avatarUploadCancellationTokenSource = null;
        }

        private void HandleSdkPanelDisable(object sender, EventArgs evt)
        {
            _avatarSwitchCancellationToken.Cancel();
            _avatarSwitchCancellationToken = new CancellationTokenSource();
            
            if (_avatarUploadCancellationTokenSource == null) return;
            _avatarUploadCancellationTokenSource.Cancel();
            _avatarUploadCancellationTokenSource = null;
        }

        
        private void HandleFoldoutToggle(ChangeEvent<bool> evt)
        {
            SessionState.SetBool($"{AvatarBuilderSessionState.SESSION_STATE_PREFIX}.Foldout.{((VisualElement) evt.currentTarget).name}", evt.newValue);
        }

        private void HandleNameChange(ChangeEvent<string> evt)
        {
            _avatarData.Name = evt.newValue;
            if (IsNewAvatar)
                AvatarBuilderSessionState.AvatarName = _avatarData.Name;

            // do not allow empty names
            _saveChangesButton.SetEnabled(!string.IsNullOrWhiteSpace(evt.newValue));
            IsContentInfoDirty = CheckDirty();

            ValidateChecklist();
        }

        private void HandleDescriptionChange(ChangeEvent<string> evt)
        {
            _avatarData.Description = evt.newValue;
            if (IsNewAvatar)
                AvatarBuilderSessionState.AvatarDesc = _avatarData.Description;

            IsContentInfoDirty = CheckDirty();
        }
        
        private void HandleToggleTag(object sender, string tag)
        {
            if (_avatarData.Tags == null)
                _avatarData.Tags = new List<string>();

            if (_avatarData.Tags.Contains(tag))
                _avatarData.Tags.Remove(tag);
            else
                _avatarData.Tags.Add(tag);

            _contentWarningsField.tags = _avatarData.Tags;
            if (IsNewAvatar)
                AvatarBuilderSessionState.AvatarTags = string.Join("|", _avatarData.Tags);

            IsContentInfoDirty = CheckDirty();
        }

        private void HandleVisibilityChange(ChangeEvent<string> evt)
        {
            _avatarData.ReleaseStatus = evt.newValue;

            if (IsNewAvatar)
                AvatarBuilderSessionState.AvatarReleaseStatus = evt.newValue;
            
            IsContentInfoDirty = CheckDirty();
        }

        private void HandleThumbnailChanged(object sender, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            
            _newThumbnailImagePath = imagePath;
            if (IsNewAvatar)
                AvatarBuilderSessionState.AvatarThumbPath = _newThumbnailImagePath;
            
            _thumbnail.SetImage(_newThumbnailImagePath);
            IsContentInfoDirty = CheckDirty();

            ValidateChecklist();
        }

        private async void HandleDiscardChangesClick()
        {
            _avatarData = _originalAvatarData;
            _contentWarningsField.tags = _avatarData.Tags = new List<string>(_originalAvatarData.Tags);
            _nameField.value = _avatarData.Name;
            _descriptionField.value = _avatarData.Description;
            _visibilityPopup.value = _avatarData.ReleaseStatus;
            _lastUpdatedLabel.text = _avatarData.UpdatedAt != DateTime.MinValue ? _avatarData.UpdatedAt.ToString() : _avatarData.CreatedAt.ToString();
            _versionLabel.text = _avatarData.Version.ToString();

            _nameField.Reset();
            _descriptionField.Reset();
            _newThumbnailImagePath = null;
            await _thumbnail.SetImageUrl(_avatarData.ThumbnailImageUrl, _avatarSwitchCancellationToken.Token);
            IsContentInfoDirty = false;
        }

        private async void HandleSaveChangesClick()
        {
            UiEnabled = false;

            if (_nameField.IsPlaceholder() || string.IsNullOrWhiteSpace(_nameField.text))
            {
                Debug.LogError("Name cannot be empty");
                return;
            }

            if (_descriptionField.IsPlaceholder())
            {
                _avatarData.Description = "";
            }

            _avatarUploadCancellationTokenSource = new CancellationTokenSource();
            _avatarUploadCancellationToken = _avatarUploadCancellationTokenSource.Token;
            
            if (!string.IsNullOrWhiteSpace(_newThumbnailImagePath))
            {
                _progressBar.style.width = 0f;

                // to avoid loss of exceptions, we hoist it into a local function
                async void Progress(string status, float percentage)
                {
                    // these callbacks can be dispatched off-thread, so we ensure we're main thread pinned
                    await UniTask.SwitchToMainThread();
                    ProgressBarState = new ProgressBarStateData
                    {
                        Visible = true,
                        Progress = percentage * 0.8f,
                        Text = status
                    };
                }
                
                _newThumbnailImagePath = VRC_EditorTools.CropImage(_newThumbnailImagePath, 800, 600, true);
                var updatedAvatar = await VRCApi.UpdateAvatarImage(
                    _avatarData.ID,
                    _avatarData,
                    _newThumbnailImagePath,
                    Progress, _avatarUploadCancellationToken);
                
                // also need to update the base avatar data
                if (!AvatarDataEqual())
                {
                    ProgressBarState = new ProgressBarStateData
                    {
                        Visible = true,
                        Text = "Saving Avatar Changes...",
                        Progress = 1f
                    };
                    updatedAvatar = await VRCApi.UpdateAvatarInfo(_avatarData.ID, _avatarData, _avatarUploadCancellationToken);
                }
                _avatarData = updatedAvatar;
                _originalAvatarData = updatedAvatar;
                await _thumbnail.SetImageUrl(_avatarData.ThumbnailImageUrl, _avatarSwitchCancellationToken.Token);
                _contentWarningsField.originalTags = _originalAvatarData.Tags = new List<string>(_avatarData.Tags ?? new List<string>());
                _contentWarningsField.tags = _avatarData.Tags ?? new List<string>();
                _newThumbnailImagePath = null;
            }
            else
            {
                ProgressBarState = new ProgressBarStateData
                {
                    Visible = true,
                    Text = "Saving Avatar Changes...",
                    Progress = 1f
                };
                Core.Logger.Log("Updating avatar");
                var updatedAvatar = await VRCApi.UpdateAvatarInfo(_avatarData.ID, _avatarData, _avatarUploadCancellationToken);
                Core.Logger.Log("Updated avatar");
                _avatarData = updatedAvatar;
                _originalAvatarData = updatedAvatar;
                _contentWarningsField.originalTags = _originalAvatarData.Tags = new List<string>(_avatarData.Tags ?? new List<string>());
                _contentWarningsField.tags = _avatarData.Tags ?? new List<string>();
            }

            UpdateCancelEnabled = false;
            ProgressBarState = false;


            UiEnabled = true;
            _nameField.value = _avatarData.Name;
            _descriptionField.value = _avatarData.Description;
            _visibilityPopup.value = _avatarData.ReleaseStatus;
            _lastUpdatedLabel.text = _avatarData.UpdatedAt != DateTime.MinValue ? _avatarData.UpdatedAt.ToString(): _avatarData.CreatedAt.ToString();
            _versionLabel.text = _avatarData.Version.ToString();

            _nameField.Reset();
            _descriptionField.Reset();
            IsContentInfoDirty = false;
        }
        #endregion

        private void ValidateChecklist()
        {
            _creationChecklist.MarkItem("name", !string.IsNullOrWhiteSpace(_avatarData.Name));
            _creationChecklist.MarkItem("thumbnail", !string.IsNullOrWhiteSpace(_newThumbnailImagePath));
        }

        private bool AvatarDataEqual()
        {
            return _avatarData.Name.Equals(_originalAvatarData.Name) &&
                   _avatarData.Description.Equals(_originalAvatarData.Description) &&
                   _avatarData.Tags.SequenceEqual(_originalAvatarData.Tags) &&
                   _avatarData.ReleaseStatus.Equals(_originalAvatarData.ReleaseStatus);
        }
        
        private bool CheckDirty()
        {
            // we ignore the diffs for new avatars, since they're not published yet
            if (IsNewAvatar) return false;
            if (string.IsNullOrWhiteSpace(_avatarData.ID) || string.IsNullOrWhiteSpace(_originalAvatarData.ID))
                return false;
            return !AvatarDataEqual()|| !string.IsNullOrWhiteSpace(_newThumbnailImagePath);
        }
        
        private void CheckBlueprintChanges()
        {
            if (!UiEnabled) return;
            if (_selectedAvatar == null) return;
            if (!_selectedAvatar.TryGetComponent<PipelineManager>(out var pm)) return;
            if (_lastBlueprintId == pm.blueprintId) return;
            HandleAvatarSwitch(_visualRoot);
            _lastBlueprintId = pm.blueprintId;
        }

        private bool _acceptedTerms;
        public void CreateBuildGUI(VisualElement root)
        {
            var tree = Resources.Load<VisualTreeAsset>("VRCSdkAvatarBuilderBuildLayout");
            tree.CloneTree(root);
            
            root.Q<Button>("show-local-test-help-button").clicked += () =>
            {
                root.Q("local-test-help-text").ToggleInClassList("d-none");
            };
            root.Q<Button>("show-online-publishing-help-button").clicked += () =>
            {
                root.Q("online-publishing-help-text").ToggleInClassList("d-none");
            };
            
            _buildAndTestButton = root.Q<Button>("build-and-test-button");
            _localTestDisabledBlock = root.Q("local-test-disabled-block");
            _localTestDisabledText = root.Q<Label>("local-test-disabled-text");
            _acceptTermsToggle = root.Q<Toggle>("accept-terms-toggle");
            _v3Block = root.Q("v3-block");

            _acceptTermsToggle.RegisterValueChangedCallback(evt =>
            {
                _acceptedTerms = evt.newValue;
            });

#if UNITY_ANDROID || UNITY_IOS
            _buildAndTestButton.SetEnabled(false);
            _localTestDisabledBlock.RemoveFromClassList("d-none");
            _localTestDisabledText.text = "Building and testing on this platform is not supported.";
#endif

            _buildAndTestButton.clicked += async () =>
            {
                UiEnabled = false;

                async void BuildSuccess(object sender, string path)
                {
                    ProgressBarState = new ProgressBarStateData
                    {
                        Visible = true,
                        Text = "Avatar Built",
                        Progress = 1f
                    };

                    await Task.Delay(500);
                    ProgressBarState = false;
                    UiEnabled = true;

                    ShowBuildSuccessNotification(true);
                    
                    _thumbnail.Loading = false;
                    RevertThumbnail();
                }

                OnSdkBuildStart += BuildStart;
                OnSdkBuildError += BuildError;
                OnSdkBuildSuccess += BuildSuccess;

                try
                {
                    await BuildAndTest(_selectedAvatar.gameObject);
                }
                finally
                {
                    OnSdkBuildStart -= BuildStart;
                    OnSdkBuildError -= BuildError;
                    OnSdkBuildSuccess -= BuildSuccess;
                }
            };
            
            _buildAndUploadButton = root.Q<Button>("build-and-upload-button");
            _uploadDisabledBlock = root.Q<VisualElement>("build-and-upload-disabled-block");
            _uploadDisabledText = _uploadDisabledBlock.Q<Label>("build-and-upload-disabled-text");
            
            _buildAndUploadButton.clicked += async () =>
            {
                UiEnabled = false;

                void BuildSuccess(object sender, string path)
                {
                    ProgressBarState = new ProgressBarStateData
                    {
                        Visible = true,
                        Text = "Avatar Built",
                        Progress = 0.1f,
                    };
                }

                OnSdkBuildStart += BuildStart;
                OnSdkBuildError += BuildError;
                OnSdkBuildSuccess += BuildSuccess;
                
                OnSdkUploadStart += UploadStart;
                OnSdkUploadProgress += UploadProgress;
                OnSdkUploadError += UploadError;
                OnSdkUploadSuccess += UploadSuccess;
                OnSdkUploadFinish += UploadFinish;

                _avatarUploadCancellationTokenSource = new CancellationTokenSource();

                try
                {
                    await BuildAndUpload(_selectedAvatar.gameObject, _avatarData, _newThumbnailImagePath,
                        _avatarUploadCancellationTokenSource.Token);
                }
                finally
                {
                    
                    OnSdkBuildStart -= BuildStart;
                    OnSdkBuildError -= BuildError;
                    OnSdkBuildSuccess -= BuildSuccess;

                    OnSdkUploadStart -= UploadStart;
                    OnSdkUploadProgress -= UploadProgress;
                    OnSdkUploadError -= UploadError;
                    OnSdkUploadSuccess -= UploadSuccess;
                    OnSdkUploadFinish -= UploadFinish;
                }
            };
            
            _v3Block.Add(new IMGUIContainer(() =>
            {
                V3SdkUI.DrawV3UI(
                    () => _builder.NoGuiErrorsOrIssues(),
                    () =>
                    {
                        bool buildBlocked = !VRCBuildPipelineCallbacks.OnVRCSDKBuildRequested(VRCSDKRequestedBuildType.Avatar);
                        if (!buildBlocked)
                        {
                            if (Core.APIUser.CurrentUser.canPublishAvatars)
                            {
                                EnvConfig.FogSettings originalFogSettings = EnvConfig.GetFogSettings();
                                EnvConfig.SetFogSettings(
                                    new EnvConfig.FogSettings(EnvConfig.FogSettings.FogStrippingMode.Custom, true, true, true));

#if UNITY_ANDROID
                        EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", true);
#else
                                EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", false);
#endif

                                VRC_SdkBuilder.shouldBuildUnityPackage = false;
                                VRC_SdkBuilder.ExportAvatarToV3(_selectedAvatar.gameObject);

                                EnvConfig.SetFogSettings(originalFogSettings);
                            }
                            else
                            {
                                VRCSdkControlPanel.ShowContentPublishPermissionsDialog();
                            }
                        }
                    }, 
                    VRCSdkControlPanel.boxGuiStyle, 
                    VRCSdkControlPanel.infoGuiStyle, 
                    VRCSdkControlPanel.SdkWindowWidth - 16
                );
            }));

            root.schedule.Execute(() =>
            {
                if (_selectedAvatar == null) return;
                
                var localBuildsAllowed = (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows ||
                                          EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) &&
                                         ((_builder.NoGuiErrorsOrIssuesForItem(_selectedAvatar) && _builder.NoGuiErrorsOrIssuesForItem(_builder)) || APIUser.CurrentUser.developerType ==
                                             APIUser.DeveloperType.Internal);
                
                _localTestDisabledBlock.EnableInClassList("d-none", localBuildsAllowed);
                if (localBuildsAllowed)
                {
                    _localTestDisabledText.text =
                        "You must fix the issues listed above before you can do an Offline Test";
                }

                if (!_acceptedTerms)
                {
                    _uploadDisabledText.text = "You must accept the terms above to upload content to VRChat";
                    _uploadDisabledBlock.RemoveFromClassList("d-none");
                    return;
                }
                else
                {
                    _uploadDisabledBlock.AddToClassList("d-none");
                }
                
                if (IsNewAvatar && (string.IsNullOrWhiteSpace(_avatarData.Name) || string.IsNullOrWhiteSpace(_newThumbnailImagePath)))
                {
                    _uploadDisabledText.text = "Please set a name and thumbnail before uploading";
                    _uploadDisabledBlock.RemoveFromClassList("d-none");
                    return;
                }
                else
                {
                    _uploadDisabledText.text = "You must fix the issues listed above before you can Upload a Build";
                }
                
                var uploadsAllowed = (_builder.NoGuiErrorsOrIssuesForItem(_selectedAvatar) && _builder.NoGuiErrorsOrIssuesForItem(_builder)) ||
                           APIUser.CurrentUser.developerType == APIUser.DeveloperType.Internal;
                _uploadDisabledBlock.EnableInClassList("d-none", uploadsAllowed);
                
            }).Every(1000);
        }

        private async Task<string> Build(GameObject target, bool testAvatar)
        {
            if (target == null) return null;
            
            var buildBlocked =
                    !VRCBuildPipelineCallbacks.OnVRCSDKBuildRequested(VRCSDKRequestedBuildType.Avatar);
            if (buildBlocked)
            {
                throw await HandleBuildError(new BuildBlockedException("Build was blocked by the SDK callback"));
            }
            
            if (!APIUser.CurrentUser.canPublishAvatars)
            {
                VRCSdkControlPanel.ShowContentPublishPermissionsDialog();
                throw await HandleBuildError(new BuildBlockedException("Current User does not have permissions to build avatars"));
            }

            if (_builder == null)
            {
                throw await HandleBuildError(new BuilderException("Open the SDK panel to build and upload avatars"));
            }

            var originalFogSettings = EnvConfig.GetFogSettings();
            EnvConfig.SetFogSettings(
                new EnvConfig.FogSettings(EnvConfig.FogSettings.FogStrippingMode.Custom, true, true, true));

#if UNITY_ANDROID
            EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", true);
#else
            EditorPrefs.SetBool("VRC.SDKBase_StripAllShaders", false);
#endif
            
            if (!target.TryGetComponent<PipelineManager>(out _))
            {
                throw await HandleBuildError(new BuilderException("This avatar does not have a PipelineManager"));
            }

            VRC_SdkBuilder.shouldBuildUnityPackage = false;
            VRC_SdkBuilder.ClearCallbacks();

            var successTask = new TaskCompletionSource<string>();
            var errorTask = new TaskCompletionSource<string>();
            var validationTask = new TaskCompletionSource<object>();
            VRC_SdkBuilder.RegisterBuildProgressCallback((sender, status) =>
            {
                OnSdkBuildProgress?.Invoke(sender, status);
            });
            VRC_SdkBuilder.RegisterBuildContentProcessedCallback((sender, processedAvatar) =>
            {
                var avatarObject = (GameObject) processedAvatar;
                if (avatarObject == null) return;
                if (!avatarObject.TryGetComponent<VRCAvatarDescriptor>(out var descriptor)) return;
                GenerateDebugHashset(descriptor);
                
                if (!descriptor.TryGetComponent<Animator>(out var animator)) return;
                if (!animator.isHuman) return;
                    
                // re-save the layer masks for base layers after potential modifications
                var sO = new SerializedObject(descriptor);
                var baseLayers = sO.FindProperty("baseAnimationLayers");
                for (int i = 0; i < baseLayers.arraySize; i++)
                {
                    var layer = baseLayers.GetArrayElementAtIndex(i);
                    var type = (VRCAvatarDescriptor.AnimLayerType)layer.FindPropertyRelative("type").enumValueIndex;
                    switch (type)
                    {
                        case VRCAvatarDescriptor.AnimLayerType.FX:
                            SetLayerMaskFromControllerInternal(layer);
                            break;
                        case VRCAvatarDescriptor.AnimLayerType.Gesture:
                            SetLayerMaskFromControllerInternal(layer);
                            break;
                    }
                }
            });
            VRC_SdkBuilder.RegisterBuildContentProcessedCallback(async (sender, processedAvatar) =>
            {
                var avatarObject = (GameObject) processedAvatar;
                if (avatarObject == null)
                {
                    validationTask.SetResult(null);
                    return;
                }

                if (!avatarObject.TryGetComponent<VRCAvatarDescriptor>(out var descriptor))
                {
                    validationTask.SetResult(null);
                    return;
                }
                
                try
                {
                    await CheckAvatarForValidationIssues(descriptor);
                }
                catch (Exception e)
                {
                    if (e is ValidationException validationException)
                    {
                        Debug.LogError("Encountered the following validation issues during build:");
                        foreach (var error in validationException.Errors)
                        {
                            Debug.LogError(error);
                        }
                    }
                    errorTask.TrySetResult(e.Message);
                    return;
                }

                validationTask.SetResult(null);
            });
            VRC_SdkBuilder.RegisterBuildErrorCallback((sender, error) =>
            {
                errorTask.TrySetResult(error);
            });
            VRC_SdkBuilder.RegisterBuildSuccessCallback((sender, path) =>
            {
                successTask.TrySetResult(path);
            });
            
            VRC_EditorTools.GetSetPanelBuildingMethod().Invoke(_builder, null);
            OnSdkBuildStart?.Invoke(this, target);
            _buildState = SdkBuildState.Building;
            OnSdkBuildStateChange?.Invoke(this, _buildState);

            await Task.Delay(100);

            if (testAvatar && Tools.Platform != "standalonewindows")
            {
                throw new BuilderException("Avatar testing is only supported on Windows");
            }

            if (testAvatar)
            {
                try
                {
                    VRC_SdkBuilder.RunExportAndTestAvatarBlueprint(target);
                }
                catch
                {
                    // Errors are handled by the error callback
                }
            }
            else
            {
                try
                {
                    VRC_SdkBuilder.RunExportAvatarBlueprint(target);
                }
                catch
                {
                    // Errors are handled by the error callback
                }
            }

            // wait for avatar validations to finish first
            var avatarProcessedTask = Task.WhenAll(successTask.Task, validationTask.Task);
            var result = await Task.WhenAny(avatarProcessedTask, errorTask.Task);

            string bundlePath = null;
            bundlePath = result == avatarProcessedTask ? successTask.Task.Result : null;
            
            VRC_SdkBuilder.ClearCallbacks();
            EnvConfig.SetFogSettings(originalFogSettings);

            if (bundlePath == null)
            {
                throw await HandleBuildError(new BuilderException(errorTask.Task.Status == TaskStatus.RanToCompletion ? errorTask.Task.Result : "Unexpected Error Occurred"));
            }
            else
            {
                _buildState = SdkBuildState.Success;
                OnSdkBuildSuccess?.Invoke(this, bundlePath);
                OnSdkBuildStateChange?.Invoke(this, _buildState);
            }

            await FinishBuild();

            return bundlePath;
        }
        
        private async Task FinishBuild()
        {
            await Task.Delay(100);
            _buildState = SdkBuildState.Idle;
            OnSdkBuildFinish?.Invoke(this, "Avatar build finished");
            OnSdkBuildStateChange?.Invoke(this, _buildState);
            VRC_EditorTools.GetSetPanelIdleMethod().Invoke(_builder, null);
        }

        private async Task<Exception> HandleBuildError(Exception exception)
        {
            OnSdkBuildError?.Invoke(this, exception.Message);
            _buildState = SdkBuildState.Failure;
            OnSdkBuildStateChange?.Invoke(this, _buildState);

            await FinishBuild();
            return exception;
        }

        #region Build Callbacks

        private async void BuildStart(object sender, object target)
        {
            UiEnabled = false;
            _thumbnail.Loading = true;
            _thumbnail.ClearImage();
            if (IsNewAvatar)
            {
                _creationChecklist.MarkItem("build", true);
                await Task.Delay(100);
            }
            
            ProgressBarState = new ProgressBarStateData
            {
                Visible = true,
                Text = "Building Avatar",
                Progress = 0.0f
            };
        }
        private async void BuildError(object sender, string error)
        {
            Core.Logger.Log("Failed to build avatar!");
            Core.Logger.LogError(error);

            await Task.Delay(100);
            ProgressBarState = false;
            UiEnabled = true;
            
            _thumbnail.Loading = false;
            RevertThumbnail();
            
            await _builder.ShowBuilderNotification(
                "Build Failed",
                new AvatarUploadErrorNotification(error),
                "red"
            );
        }

        private async void RevertThumbnail()
        {
            if (IsNewAvatar)
            {
                _thumbnail.SetImage(_newThumbnailImagePath);
            }
            else
            {
                await _thumbnail.SetImageUrl(_avatarData.ThumbnailImageUrl, _avatarSwitchCancellationToken.Token);
            }
        }

        private void UploadStart(object sender, EventArgs e)
        {
            _thumbnail.Loading = true;
            _thumbnail.ClearImage();
            UpdateCancelEnabled = true;
            _updateCancelButton.clicked += CancelUpload;
            VRC_EditorTools.ToggleSdkTabsEnabled(_builder, false);
        }

        private async void UploadProgress(object sender, (string status, float percentage) progress)
        {
            await UniTask.SwitchToMainThread();
            ProgressBarState = new ProgressBarStateData
            {
                Visible = true,
                Text = progress.status,
                Progress = 0.2f + progress.percentage * 0.8f
            };
            _progressBlock.MarkDirtyRepaint();
        }
        private async void UploadSuccess(object sender, string avatarId)
        {
            await Task.Delay(100);
            UpdateCancelEnabled = false;
            ProgressBarState = false;
            UiEnabled = true;

            _originalAvatarData = _avatarData;
            _originalAvatarData.Tags = new List<string>(_avatarData.Tags ?? new List<string>());
            _newThumbnailImagePath = null;

            await _builder.ShowBuilderNotification(
                "Upload Succeeded!",
                new AvatarUploadSuccessNotification(avatarId),
                "green"
            );
            
            HandleAvatarSwitch(_visualRoot);
        }
        private async void UploadError(object sender, string error)
        {
            Core.Logger.Log("Failed to upload avatar!");
            Core.Logger.LogError(error);
            
            await Task.Delay(100);
            UpdateCancelEnabled = false;
            ProgressBarState = false;
            UiEnabled = true;
            _thumbnail.Loading = false;
            RevertThumbnail();
            
            await _builder.ShowBuilderNotification(
                "Upload Failed",
                new AvatarUploadErrorNotification(error),
                "red"
            );
        }

        private void UploadFinish(object sender, string message)
        {
            _updateCancelButton.clicked -= CancelUpload;
        }

        private async void ShowBuildSuccessNotification(bool testBuild = false)
        {
            await _builder.ShowBuilderNotification(
                "Build Succeeded!",
                new AvatarBuildSuccessNotification(testBuild),
                "green"
            );
        }

        #endregion

        #endregion

        #region Public API Backing

        private SdkBuildState _buildState;
        private SdkUploadState _uploadState;
        
        private static CancellationTokenSource _avatarUploadCancellationTokenSource;
        private CancellationToken _avatarUploadCancellationToken;

        #endregion

        #region Public API
        
        public event EventHandler<object> OnSdkBuildStart;
        public event EventHandler<string> OnSdkBuildProgress;
        public event EventHandler<string> OnSdkBuildFinish;
        public event EventHandler<string> OnSdkBuildSuccess;
        public event EventHandler<string> OnSdkBuildError;
        public event EventHandler<SdkBuildState> OnSdkBuildStateChange;
        public SdkBuildState BuildState => _buildState;
        
        public event EventHandler OnSdkUploadStart;
        public event EventHandler<(string status, float percentage)> OnSdkUploadProgress;
        public event EventHandler<string> OnSdkUploadFinish;
        public event EventHandler<string> OnSdkUploadSuccess;
        public event EventHandler<string> OnSdkUploadError;
        public event EventHandler<SdkUploadState> OnSdkUploadStateChange;
        public SdkUploadState UploadState => _uploadState;

        public async Task<string> Build(GameObject target)
        {
            return await Build(target, false);
        }

        public async Task BuildAndUpload(GameObject target, VRCAvatar avatar, string thumbnailPath = null, CancellationToken cancellationToken = default)
        {
            if (cancellationToken == default)
            {
                _avatarUploadCancellationTokenSource = new CancellationTokenSource();
                _avatarUploadCancellationToken = _avatarUploadCancellationTokenSource.Token;
            }
            else
            {
                _avatarUploadCancellationToken = cancellationToken;
            }
            
            var bundlePath = await Build(target);

            if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            {
                throw await HandleUploadError(new UploadException("Failed to find the built avatar bundle, the build likely failed"));
            }

            if (ValidationHelpers.CheckIfAssetBundleFileTooLarge(ContentType.Avatar, bundlePath, out int fileSize,
                    VRC.Tools.Platform != "standalonewindows"))
            {
                var limit = ValidationHelpers.GetAssetBundleSizeLimit(ContentType.Avatar,
                    Tools.Platform != "standalonewindows");
                throw await HandleUploadError(new UploadException(
                    $"Avatar is too large for the target platform. {(fileSize / 1024 / 1024):F2} MB > {(limit / 1024 / 1024):F2} MB"));
                    
            }

            VRC_EditorTools.GetSetPanelUploadingMethod().Invoke(_builder, null);
            _uploadState = SdkUploadState.Uploading;
            OnSdkUploadStateChange?.Invoke(this, _uploadState);
            OnSdkUploadStart?.Invoke(this, EventArgs.Empty);

            await Task.Delay(100, _avatarUploadCancellationToken);
            
            if (!target.TryGetComponent<PipelineManager>(out var pM))
            {
                throw await HandleUploadError(new UploadException("Target avatar does not have a PipelineManager, make sure a PipelineManager component is present before uploading"));
            }
            
            var creatingNewAvatar = string.IsNullOrWhiteSpace(pM.blueprintId) || string.IsNullOrWhiteSpace(avatar.ID);

            if (creatingNewAvatar && (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)))
            {
                throw await HandleUploadError(new UploadException("You must provide a path to the thumbnail image when creating a new avatar"));
            }

            if (!creatingNewAvatar)
            {
                var remoteData = await VRCApi.GetAvatar(avatar.ID, cancellationToken: _avatarUploadCancellationToken);
                if (APIUser.CurrentUser == null || remoteData.AuthorId != APIUser.CurrentUser?.id)
                {
                    throw await HandleUploadError(new OwnershipException("Avatar's current ID belongs to a different user, assign a different ID"));
                }
            }

            if (string.IsNullOrWhiteSpace(pM.blueprintId))
            {
                Undo.RecordObject(pM, "Assigning a new ID");
                pM.AssignId();
            }

            try
            {
                if (creatingNewAvatar)
                {
                    thumbnailPath = VRC_EditorTools.CropImage(thumbnailPath, 800, 600);
                    _avatarData = await VRCApi.CreateNewAvatar(pM.blueprintId, avatar, bundlePath,
                        thumbnailPath,
                        (status, percentage) => { OnSdkUploadProgress?.Invoke(this, (status, percentage)); },
                        _avatarUploadCancellationToken);
                }
                else
                {
                    if (avatar.Tags?.Contains(VRCApi.AVATAR_FALLBACK_TAG) ?? false)
                    {
                        if (pM.fallbackStatus == PipelineManager.FallbackStatus.InvalidPerformance ||
                            pM.fallbackStatus == PipelineManager.FallbackStatus.InvalidRig)
                        {
                            avatar.Tags = avatar.Tags.Where(t => t != VRCApi.AVATAR_FALLBACK_TAG).ToList();
                        }
                    }
                    _avatarData = await VRCApi.UpdateAvatarBundle(pM.blueprintId, avatar, bundlePath,
                        (status, percentage) => { OnSdkUploadProgress?.Invoke(this, (status, percentage)); },
                        _avatarUploadCancellationToken);
                }
                
                _uploadState = SdkUploadState.Success;
                OnSdkUploadSuccess?.Invoke(this, _avatarData.ID);

                await FinishUpload();
            }
            catch (TaskCanceledException e)
            {
                AnalyticsSDK.AvatarUploadFailed(pM.blueprintId, !creatingNewAvatar);
                if (cancellationToken.IsCancellationRequested)
                {
                    Core.Logger.LogError("Request cancelled", DebugLevel.API);
                    throw await HandleUploadError(new UploadException("Request Cancelled", e));
                }
            }
            catch (ApiErrorException e)
            {
                AnalyticsSDK.AvatarUploadFailed(pM.blueprintId, !creatingNewAvatar);
                throw await HandleUploadError(new UploadException(e.ErrorMessage, e));
            }
            catch (Exception e)
            {
                AnalyticsSDK.AvatarUploadFailed(pM.blueprintId, !creatingNewAvatar);
                throw await HandleUploadError(new UploadException(e.Message, e));
            }
        }
        
        private async Task FinishUpload()
        {
            await Task.Delay(100);

            _uploadState = SdkUploadState.Idle;
            OnSdkUploadFinish?.Invoke(this, "Avatar upload finished");
            OnSdkUploadStateChange?.Invoke(this, _uploadState);
            VRC_EditorTools.GetSetPanelIdleMethod().Invoke(_builder, null);
            _avatarUploadCancellationToken = default;
            VRC_EditorTools.ToggleSdkTabsEnabled(_builder, true);
        }

        private async Task<Exception> HandleUploadError(Exception exception)
        {
            OnSdkUploadError?.Invoke(this, exception.Message);
            _uploadState = SdkUploadState.Failure;
            OnSdkUploadStateChange?.Invoke(this, _uploadState);

            await FinishUpload();
            return exception;
        }

        public async Task BuildAndTest(GameObject target)
        {
            await Build(target, true);
        }

        public void CancelUpload()
        {
            VRC_EditorTools.GetSetPanelIdleMethod().Invoke(_builder, null);
            if (_avatarUploadCancellationToken != default)
            {
                _avatarUploadCancellationTokenSource?.Cancel();
                Core.Logger.Log("Avatar upload canceled");
                return;
            }
            
            Core.Logger.LogError("Custom cancellation token passed, you should cancel via its token source instead");
        }
        
        #endregion
        
        #region Validation Helpers
        
        private async Task CheckAvatarForValidationIssues(VRC_AvatarDescriptor targetDescriptor)
        {
            _builder.CheckedForIssues = false;
            _builder.ResetIssues();
            OnGUIAvatarCheck(targetDescriptor);
            _builder.CheckedForIssues = true;
            if (!_builder.NoGuiErrorsOrIssuesForItem(targetDescriptor) || !_builder.NoGuiErrorsOrIssuesForItem(_builder))
            {
                var errorsList = new List<string>();
                errorsList.AddRange(_builder.GetGuiErrorsOrIssuesForItem(targetDescriptor).Select(i => i.issueText));
                errorsList.AddRange(_builder.GetGuiErrorsOrIssuesForItem(_builder).Select(i => i.issueText));
                throw await HandleBuildError(new ValidationException("Avatar validation failed", errorsList));
            }
        }

        
        private static Action GetAvatarSubSelectAction(Component avatar, Type[] types)
        {
            return () =>
            {
                List<Object> gos = new List<Object>();
                foreach (Type t in types)
                {
                    List<Component> components = avatar.gameObject.GetComponentsInChildrenExcludingEditorOnly(t, true);
                    foreach (Component c in components)
                        gos.Add(c.gameObject);
                }

                Selection.objects = gos.Count > 0 ? gos.ToArray() : new Object[] {avatar.gameObject};
            };
        }

        private static Action GetAvatarSubSelectAction(Component avatar, Type type)
        {
            List<Type> t = new List<Type> {type};
            return GetAvatarSubSelectAction(avatar, t.ToArray());
        }

        private static Action GetAvatarAudioSourcesWithDecompressOnLoadWithoutBackgroundLoad(Component avatar)
        {
            return () =>
            {
                List<Object> gos = new List<Object>();
                AudioSource[] audioSources = avatar.GetComponentsInChildren<AudioSource>(true);

                foreach (var audioSource in audioSources)
                {
                    if (audioSource.clip && audioSource.clip.loadType == AudioClipLoadType.DecompressOnLoad && !audioSource.clip.loadInBackground)
                    {
                        gos.Add(audioSource.gameObject);
                    }
                }

                Selection.objects = gos.Count > 0 ? gos.ToArray() : new Object[] { avatar.gameObject };
            };
        }

        private void VerifyAvatarMipMapStreaming(Component avatar)
        {
            List<TextureImporter> badTextureImporters = new List<TextureImporter>();
            List<Object> badTextures = new List<Object>();
            foreach (Renderer r in avatar.gameObject.GetComponentsInChildrenExcludingEditorOnly<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (!m)
                        continue;
                    int[] texIDs = m.GetTexturePropertyNameIDs();
                    if (texIDs == null)
                        continue;
                    foreach (int i in texIDs)
                    {
                        Texture t = m.GetTexture(i);
                        if (!t)
                            continue;
                        string path = AssetDatabase.GetAssetPath(t);
                        if (string.IsNullOrEmpty(path))
                            continue;
                        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (importer != null && importer.mipmapEnabled && !importer.streamingMipmaps)
                        {
                            badTextureImporters.Add(importer);
                            badTextures.Add(t);
                        }
                    }
                }
            }

            if (badTextureImporters.Count == 0)
                return;

            _builder.OnGUIError(avatar, "This avatar has mipmapped textures without 'Streaming Mip Maps' enabled.",
                () => { Selection.objects = badTextures.ToArray(); },
                () =>
                {
                    List<string> paths = new List<string>();
                    foreach (TextureImporter t in badTextureImporters)
                    {
                        Undo.RecordObject(t, "Set Mip Map Streaming");
                        t.streamingMipmaps = true;
                        t.streamingMipmapsPriority = 0;
                        EditorUtility.SetDirty(t);
                        paths.Add(t.assetPath);
                    }

                    AssetDatabase.ForceReserializeAssets(paths);
                    AssetDatabase.Refresh();
                });
        }

        private bool AnalyzeIK(Object ad, Animator anim)
        {
            bool hasHead;
            bool hasFeet;
            bool hasHands;
#if VRC_SDK_VRCSDK2
            bool hasThreeFingers;
#endif
            bool correctSpineHierarchy;
            bool correctLeftArmHierarchy;
            bool correctRightArmHierarchy;
            bool correctLeftLegHierarchy;
            bool correctRightLegHierarchy;

            bool status = true;

            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            Transform lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);

            hasHead = null != head;
            hasFeet = (null != lFoot && null != rFoot);
            hasHands = (null != lHand && null != rHand);

            if (!hasHead || !hasFeet || !hasHands)
            {
                _builder.OnGUIError(ad, "Humanoid avatar must have head, hands and feet bones mapped.",
                    delegate { Selection.activeObject = anim.gameObject; }, null);
                return false;
            }

            Transform lThumb = anim.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            Transform lIndex = anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            Transform lMiddle = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            Transform rThumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            Transform rIndex = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            Transform rMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);

            Transform pelvis = anim.GetBoneTransform(HumanBodyBones.Hips);
            Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            Transform upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            Transform torso = anim.GetBoneTransform(HumanBodyBones.Spine);

            Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            Transform lClav = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
            Transform rClav = anim.GetBoneTransform(HumanBodyBones.RightShoulder);


            if (null == neck || null == lClav || null == rClav || null == pelvis || null == torso || null == chest)
            {
                string missingElements =
                    ((null == neck) ? "Neck, " : "") +
                    (((null == lClav) || (null == rClav)) ? "Shoulders, " : "") +
                    ((null == pelvis) ? "Pelvis, " : "") +
                    ((null == torso) ? "Spine, " : "") +
                    ((null == chest) ? "Chest, " : "");
                missingElements = missingElements.Remove(missingElements.LastIndexOf(',')) + ".";
                _builder.OnGUIError(ad, "Spine hierarchy missing elements, please map: " + missingElements,
                    delegate { Selection.activeObject = anim.gameObject; }, null);
                return false;
            }

            if (null != upperChest)
                correctSpineHierarchy =
                    lClav.parent == upperChest && rClav.parent == upperChest && neck.parent == upperChest;
            else
                correctSpineHierarchy = lClav.parent == chest && rClav.parent == chest && neck.parent == chest;

            if (!correctSpineHierarchy)
            {
                _builder.OnGUIError(ad,
                    "Spine hierarchy incorrect. Make sure that the parent of both Shoulders and the Neck is the Chest (or UpperChest if set).",
                    delegate
                    {
                        List<Object> gos = new List<Object>
                        {
                            lClav.gameObject,
                            rClav.gameObject,
                            neck.gameObject,
                            null != upperChest ? upperChest.gameObject : chest.gameObject
                        };
                        Selection.objects = gos.ToArray();
                    }, null);
                return false;
            }

            Transform lShoulder = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform lElbow = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform rShoulder = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform rElbow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

            correctLeftArmHierarchy = lShoulder && lElbow && lShoulder.GetChild(0) == lElbow && lHand &&
                                      lElbow.GetChild(0) == lHand;
            correctRightArmHierarchy = rShoulder && rElbow && rShoulder.GetChild(0) == rElbow && rHand &&
                                       rElbow.GetChild(0) == rHand;

            if (!(correctLeftArmHierarchy && correctRightArmHierarchy))
            {
                _builder.OnGUIWarning(ad,
                    "LowerArm is not first child of UpperArm or Hand is not first child of LowerArm: you may have problems with Forearm rotations.",
                    delegate
                    {
                        List<Object> gos = new List<Object>();
                        if (!correctLeftArmHierarchy && lShoulder)
                            gos.Add(lShoulder.gameObject);
                        if (!correctRightArmHierarchy && rShoulder)
                            gos.Add(rShoulder.gameObject);
                        if (gos.Count > 0)
                            Selection.objects = gos.ToArray();
                        else
                            Selection.activeObject = anim.gameObject;
                    }, null);
                status = false;
            }

            Transform lHip = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform lKnee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform rHip = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform rKnee = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);

            correctLeftLegHierarchy = lHip && lKnee && lHip.GetChild(0) == lKnee && lKnee.GetChild(0) == lFoot;
            correctRightLegHierarchy = rHip && rKnee && rHip.GetChild(0) == rKnee && rKnee.GetChild(0) == rFoot;

            if (!(correctLeftLegHierarchy && correctRightLegHierarchy))
            {
                _builder.OnGUIWarning(ad,
                    "LowerLeg is not first child of UpperLeg or Foot is not first child of LowerLeg: you may have problems with Shin rotations.",
                    delegate
                    {
                        List<Object> gos = new List<Object>();
                        if (!correctLeftLegHierarchy && lHip)
                            gos.Add(lHip.gameObject);
                        if (!correctRightLegHierarchy && rHip)
                            gos.Add(rHip.gameObject);
                        if (gos.Count > 0)
                            Selection.objects = gos.ToArray();
                        else
                            Selection.activeObject = anim.gameObject;
                    }, null);
                status = false;
            }

            if (!(IsAncestor(pelvis, rFoot) && IsAncestor(pelvis, lFoot) && IsAncestor(pelvis, lHand) &&
                  IsAncestor(pelvis, rHand)))
            {
                _builder.OnGUIWarning(ad,
                    "This avatar has a split hierarchy (Hips bone is not the ancestor of all humanoid bones). IK may not work correctly.",
                    delegate
                    {
                        List<Object> gos = new List<Object> {pelvis.gameObject};
                        if (!IsAncestor(pelvis, rFoot))
                            gos.Add(rFoot.gameObject);
                        if (!IsAncestor(pelvis, lFoot))
                            gos.Add(lFoot.gameObject);
                        if (!IsAncestor(pelvis, lHand))
                            gos.Add(lHand.gameObject);
                        if (!IsAncestor(pelvis, rHand))
                            gos.Add(rHand.gameObject);
                        Selection.objects = gos.ToArray();
                    }, null);
                status = false;
            }

            // if thigh bone rotations diverge from 180 from hip bone rotations, full-body tracking/ik does not work well
            if (!lHip || !rHip) return status;
            {
                Vector3 hipLocalUp = pelvis.InverseTransformVector(Vector3.up);
                Vector3 legLDir = lHip.TransformVector(hipLocalUp);
                Vector3 legRDir = rHip.TransformVector(hipLocalUp);
                float angL = Vector3.Angle(Vector3.up, legLDir);
                float angR = Vector3.Angle(Vector3.up, legRDir);
                if (!(angL < 175f) && !(angR < 175f)) return status;
                string angle = $"{Mathf.Min(angL, angR):F1}";
                _builder.OnGUIWarning(ad,
                    $"The angle between pelvis and thigh bones should be close to 180 degrees (this avatar's angle is {angle}). Your avatar may not work well with full-body IK and Tracking.",
                    delegate
                    {
                        List<Object> gos = new List<Object>();
                        if (angL < 175f)
                            gos.Add(rFoot.gameObject);
                        if (angR < 175f)
                            gos.Add(lFoot.gameObject);
                        Selection.objects = gos.ToArray();
                    }, null);
                status = false;
            }

            return status;
        }

        private static bool IsAncestor(Object ancestor, Transform child)
        {
            bool found = false;
            Transform thisParent = child.parent;
            while (thisParent != null)
            {
                if (thisParent == ancestor)
                {
                    found = true;
                    break;
                }

                thisParent = thisParent.parent;
            }

            return found;
        }

        private void CheckAvatarMeshesForLegacyBlendShapesSetting(Component avatar)
        {
            if (LegacyBlendShapeNormalsPropertyInfo == null)
            {
                Debug.LogError(
                    "Could not check for legacy blend shape normals because 'legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes' was not found.");
                return;
            }

            // Get all of the meshes used by skinned mesh renderers.
            HashSet<Mesh> avatarMeshes = GetAllMeshesInGameObjectHierarchy(avatar.gameObject);
            HashSet<Mesh> incorrectlyConfiguredMeshes =
                ScanMeshesForIncorrectBlendShapeNormalsSetting(avatarMeshes);
            if (incorrectlyConfiguredMeshes.Count > 0)
            {
                _builder.OnGUIError(
                    avatar,
                    "This avatar contains skinned meshes that were imported with Blendshape Normals set to 'Calculate' but aren't using 'Legacy Blendshape Normals'. This will significantly increase the size of the uploaded avatar. This must be fixed in the mesh import settings before uploading.",
                    null,
                    () => { EnableLegacyBlendShapeNormals(incorrectlyConfiguredMeshes); });
            }
        }

        private static HashSet<Mesh> ScanMeshesForIncorrectBlendShapeNormalsSetting(IEnumerable<Mesh> avatarMeshes)
        {
            HashSet<Mesh> incorrectlyConfiguredMeshes = new HashSet<Mesh>();
            foreach (Mesh avatarMesh in avatarMeshes)
            {
                // Can't get ModelImporter if the model isn't an asset.
                if (!AssetDatabase.Contains(avatarMesh))
                {
                    continue;
                }

                string meshAssetPath = AssetDatabase.GetAssetPath(avatarMesh);
                if (string.IsNullOrEmpty(meshAssetPath))
                {
                    continue;
                }

                ModelImporter avatarImporter = AssetImporter.GetAtPath(meshAssetPath) as ModelImporter;
                if (avatarImporter == null)
                {
                    continue;
                }

                if (avatarImporter.importBlendShapeNormals != ModelImporterNormals.Calculate)
                {
                    continue;
                }

                bool useLegacyBlendShapeNormals = (bool) LegacyBlendShapeNormalsPropertyInfo.GetValue(avatarImporter);
                if (useLegacyBlendShapeNormals)
                {
                    continue;
                }

                incorrectlyConfiguredMeshes.Add(avatarMesh);
            }

            return incorrectlyConfiguredMeshes;
        }

        private static void EnableLegacyBlendShapeNormals(IEnumerable<Mesh> meshesToFix)
        {
            HashSet<string> meshAssetPaths = new HashSet<string>();
            foreach (Mesh meshToFix in meshesToFix)
            {
                // Can't get ModelImporter if the model isn't an asset.
                if (!AssetDatabase.Contains(meshToFix))
                {
                    continue;
                }

                string meshAssetPath = AssetDatabase.GetAssetPath(meshToFix);
                if (string.IsNullOrEmpty(meshAssetPath))
                {
                    continue;
                }

                if (meshAssetPaths.Contains(meshAssetPath))
                {
                    continue;
                }

                meshAssetPaths.Add(meshAssetPath);
            }

            foreach (string meshAssetPath in meshAssetPaths)
            {
                ModelImporter avatarImporter = AssetImporter.GetAtPath(meshAssetPath) as ModelImporter;
                if (avatarImporter == null)
                {
                    continue;
                }

                if (avatarImporter.importBlendShapeNormals != ModelImporterNormals.Calculate)
                {
                    continue;
                }

                LegacyBlendShapeNormalsPropertyInfo.SetValue(avatarImporter, true);
                avatarImporter.SaveAndReimport();
            }
        }

        private void CheckAvatarMeshesForMeshReadWriteSetting(Component avatar)
        {
            // Get all of the meshes used by skinned mesh renderers.
            HashSet<Mesh> avatarMeshes = GetAllMeshesInGameObjectHierarchy(avatar.gameObject);
            HashSet<Mesh> incorrectlyConfiguredMeshes =
                ScanMeshesForDisabledMeshReadWriteSetting(avatarMeshes);
            if (incorrectlyConfiguredMeshes.Count > 0)
            {
                _builder.OnGUIError(
                    avatar,
                    "This avatar contains meshes that were imported with Read/Write disabled. This must be fixed in the mesh import settings before uploading.",
                    null,
                    () => { EnableMeshReadWrite(incorrectlyConfiguredMeshes); });
            }
        }

        private static HashSet<Mesh> ScanMeshesForDisabledMeshReadWriteSetting(IEnumerable<Mesh> avatarMeshes)
        {
            HashSet<Mesh> incorrectlyConfiguredMeshes = new HashSet<Mesh>();
            foreach (Mesh avatarMesh in avatarMeshes)
            {
                // Can't get ModelImporter if the model isn't an asset.
                if (!AssetDatabase.Contains(avatarMesh))
                {
                    continue;
                }

                string meshAssetPath = AssetDatabase.GetAssetPath(avatarMesh);
                if (string.IsNullOrEmpty(meshAssetPath))
                {
                    continue;
                }

                ModelImporter avatarImporter = AssetImporter.GetAtPath(meshAssetPath) as ModelImporter;
                if (avatarImporter == null)
                {
                    continue;
                }

                if (avatarImporter.isReadable)
                {
                    continue;
                }

                incorrectlyConfiguredMeshes.Add(avatarMesh);
            }

            return incorrectlyConfiguredMeshes;
        }

        private static void EnableMeshReadWrite(IEnumerable<Mesh> meshesToFix)
        {
            HashSet<string> meshAssetPaths = new HashSet<string>();
            foreach (Mesh meshToFix in meshesToFix)
            {
                // Can't get ModelImporter if the model isn't an asset.
                if (!AssetDatabase.Contains(meshToFix))
                {
                    continue;
                }

                string meshAssetPath = AssetDatabase.GetAssetPath(meshToFix);
                if (string.IsNullOrEmpty(meshAssetPath))
                {
                    continue;
                }

                if (meshAssetPaths.Contains(meshAssetPath))
                {
                    continue;
                }

                meshAssetPaths.Add(meshAssetPath);
            }

            foreach (string meshAssetPath in meshAssetPaths)
            {
                ModelImporter avatarImporter = AssetImporter.GetAtPath(meshAssetPath) as ModelImporter;
                if (avatarImporter == null)
                {
                    continue;
                }

                if (avatarImporter.isReadable)
                {
                    continue;
                }

                avatarImporter.isReadable = true;
                avatarImporter.SaveAndReimport();
            }
        }

        private static HashSet<Mesh> GetAllMeshesInGameObjectHierarchy(GameObject avatar)
        {
            HashSet<Mesh> avatarMeshes = new HashSet<Mesh>();
            foreach (SkinnedMeshRenderer avatarSkinnedMeshRenderer in avatar
                .GetComponentsInChildrenExcludingEditorOnly<SkinnedMeshRenderer>(true))
            {
                if (avatarSkinnedMeshRenderer == null)
                {
                    continue;
                }

                Mesh skinnedMesh = avatarSkinnedMeshRenderer.sharedMesh;
                if (skinnedMesh == null)
                {
                    continue;
                }

                if (avatarMeshes.Contains(skinnedMesh))
                {
                    continue;
                }

                avatarMeshes.Add(skinnedMesh);
            }

            foreach (MeshFilter avatarMeshFilter in avatar.GetComponentsInChildrenExcludingEditorOnly<MeshFilter>(true))
            {
                if (avatarMeshFilter == null)
                {
                    continue;
                }

                Mesh skinnedMesh = avatarMeshFilter.sharedMesh;
                if (skinnedMesh == null)
                {
                    continue;
                }

                if (avatarMeshes.Contains(skinnedMesh))
                {
                    continue;
                }

                avatarMeshes.Add(skinnedMesh);
            }

            foreach (ParticleSystemRenderer avatarParticleSystemRenderer in avatar
                .GetComponentsInChildrenExcludingEditorOnly<ParticleSystemRenderer>(true))
            {
                if (avatarParticleSystemRenderer == null)
                {
                    continue;
                }

                Mesh[] avatarParticleSystemRendererMeshes = new Mesh[avatarParticleSystemRenderer.meshCount];
                avatarParticleSystemRenderer.GetMeshes(avatarParticleSystemRendererMeshes);
                foreach (Mesh avatarParticleSystemRendererMesh in avatarParticleSystemRendererMeshes)
                {
                    if (avatarParticleSystemRendererMesh == null)
                    {
                        continue;
                    }

                    if (avatarMeshes.Contains(avatarParticleSystemRendererMesh))
                    {
                        continue;
                    }

                    avatarMeshes.Add(avatarParticleSystemRendererMesh);
                }
            }

            return avatarMeshes;
        }

        private void OpenAnimatorControllerWindow(object animatorController)
        {
            Assembly asm = Assembly.Load("UnityEditor.Graphs");
            Module editorGraphModule = asm.GetModule("UnityEditor.Graphs.dll");
            Type animatorWindowType = editorGraphModule.GetType("UnityEditor.Graphs.AnimatorControllerTool");
            EditorWindow animatorWindow = EditorWindow.GetWindow(animatorWindowType, false, "Animator", false);
            PropertyInfo propInfo = animatorWindowType.GetProperty("animatorController");
            if (propInfo != null) propInfo.SetValue(animatorWindow, animatorController, null);
        }

        private static void ShowRestrictedComponents(IEnumerable<Component> componentsToRemove)
        {
            List<Object> gos = new List<Object>();
            foreach (Component c in componentsToRemove)
                gos.Add(c.gameObject);
            Selection.objects = gos.ToArray();
        }

        private static void FixRestrictedComponents(IEnumerable<Component> componentsToRemove)
        {
            if (!(componentsToRemove is List<Component> list)) return;
            for (int v = list.Count - 1; v > -1; v--)
            {
                Object.DestroyImmediate(list[v]);
            }
        }

        private static void SetLayerMaskFromControllerInternal(SerializedProperty layer)
        {
            var method = typeof(AvatarDescriptorEditor3).GetMethod("SetLayerMaskFromController",
                BindingFlags.Static | BindingFlags.NonPublic);
            method?.Invoke(null, new object[] { layer });
            layer.serializedObject.ApplyModifiedProperties();
        }

        #endregion
    }
}
#endif
