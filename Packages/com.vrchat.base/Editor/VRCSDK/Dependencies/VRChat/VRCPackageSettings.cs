﻿using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRC.SDKBase.Editor
{
    public class VRCPackageSettings : ISaveable
    {
        private static string PackageSettingsPath = Path.Combine(new DirectoryInfo(Application.dataPath).Parent?.FullName, "ProjectSettings", "Packages");
        public bool samplesImported = false;
        public bool allowVRCPackageChanges = false;
        public bool samplesHintCreated = false;

        protected internal static VRCPackageSettings Create()
        {
            var result = new VRCPackageSettings();
            result.Load();
            return result;
        }

        private static string GetPathFromType(Type t)
        {
            // Get package info for this assembly
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(t.Assembly);

            // Exit early for non-VPM SDK
            if (packageInfo == null)
            {
                return null;
            }

            return Path.Combine(PackageSettingsPath, packageInfo.name, "settings.json");
        }

        private string GetPath()
        {
            return GetPathFromType(GetType());
        }

        private void EnsurePathExists()
        {
            var dir = Path.GetDirectoryName(GetPath());
            Directory.CreateDirectory(dir);
        }

        protected void Load()
        {
            EnsurePathExists();
            var path = GetPath();
            if (!File.Exists(path))
            {
                var settings = (ISaveable)Activator.CreateInstance(GetType());
                settings.Save();
            }
            else
            {
                EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(path), this);
            }
        }

        public void Save()
        {
            var json = JsonUtility.ToJson(this, true);
            File.WriteAllText(GetPath(), json);
        }
    }

    public interface ISaveable
    {
        void Save();
    }
}