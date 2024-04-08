using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace TerraformingMod
{
    #region BepInEx
    [BepInEx.BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class TerraformingMod : BepInEx.BaseUnityPlugin
    {
        public const string pluginGuid = "net.elmo.stationeers.Terraforming";
        public const string pluginName = "Terraforming Mod";
        public const string pluginVersion = "0.22";
        public static void Log(string line)
        {
            Debug.Log("[" + pluginName + "]: " + line);
        }
        void Awake()
        {
            try
            {
                var harmony = new Harmony(pluginGuid);
                harmony.PatchAll();
                Log("Patch succeeded");
            }
            catch (Exception e)
            {
                Log("Patch Failed");
                Log(e.ToString());
            }
        }
    }
    #endregion
}
