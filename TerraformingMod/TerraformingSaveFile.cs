using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Assets.Scripts.Atmospherics;
using Assets.Scripts;
using Assets.Scripts.Serialization;
using HarmonyLib;
using System.Diagnostics;

namespace TerraformingMod
{
    static class TerraformingSaveFile
    {
        //XmlSaveLoad.DeleteEachFilesOldAutoSaves(worldDirectory, XmlSaveLoad.WorldFileName);
        private static MethodInfo getBackupIndex =
            AccessTools.PropertyGetter(typeof(XmlSaveLoad), "BackupWorldIndex");

        private static string filenamePattern = "terraforming_atmosphere{0}{1}.xml";

        public static uint GetBackupIndex()
        {
            return (uint)getBackupIndex.Invoke(XmlSaveLoad.Instance, Array.Empty<object>());
        }

        public static string BuildFileName(bool isBackup = false, uint backupIndex = 0, bool isAutosave = false)
        {
            return String.Format
            (
                filenamePattern,
                isBackup ? "(" + backupIndex + ")" : string.Empty,
                isBackup && isAutosave ? XmlSaveLoad.AutoSave : string.Empty
            );
        }

        public static string GetFullBackupFileName(string worldDirectory, uint backupIndex, bool isAutosave)
        {
            return Path.Combine(worldDirectory, "Backup", BuildFileName(true, backupIndex, isAutosave));
        }

        public static string GetFullSaveFileName(string worldDirectory)
        {
            return Path.Combine(worldDirectory, BuildFileName());
        }

        public static string GetCurrentSaveFileName()
        {
            var worldDirectory = XmlSaveLoad.Instance.CurrentWorldSave.World.Directory.FullName;
            var save = XmlSaveLoad.Instance.CurrentWorldSave;
            var worldFileName = save.World.Name;
            var isAutoSave = worldFileName.EndsWith(XmlSaveLoad.AutoSave + ".xml");
            var isBackup = save.IsBackup;

            return Path.Combine(worldDirectory, BuildFileName(isBackup, (uint)save.Index, isAutoSave));
        }

        public static string GetFullTempFileName(string worldDirectory)
        {
            return Path.Combine(worldDirectory, "temp_" + BuildFileName());
        }

        public static void DeleteOldBackupFiles(string worldDirectory)
        {
            var backupDirectory = new DirectoryInfo(Path.Combine(worldDirectory, "Backup"));
            var files = backupDirectory.GetFiles
            (
                String.Format(filenamePattern, "(*)", "*"),
                SearchOption.TopDirectoryOnly
            );

            foreach (var file in files)
            {
                var worldFileName = file.Name.Replace("terraforming_atmosphere", "world");
                //delete if the according world does not exists
                if (!File.Exists(Path.Combine(file.Directory.FullName, worldFileName)))
                {
                    ConsoleWindow.Print("Delete old backup file: " + file.FullName, ConsoleColor.White);
                    file.Delete();
                }
            }
        }
    }
}
