using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Xml.Serialization;
using System.IO;
using HarmonyLib;
using UnityEngine;
using Assets.Scripts;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Objects;
using Assets.Scripts.Networking;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Serialization;
using static Assets.Scripts.Atmospherics.Chemistry;
using TerraformingMod.Tools;
using System.Reflection;

namespace TerraformingMod
{
    [HarmonyPatch(typeof(Atmosphere), "LerpAtmosphere")]
    public class AtmosphereLerpAtmospherePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Atmosphere __instance, Atmosphere targetAtmos, ref GasMixture __state)
        {
            if (!NetworkManager.IsClient && targetAtmos.IsGlobalAtmosphere)
            {
                __state = new GasMixture(__instance.GasMixture);
                return;
            }

            __state = GasMixtureHelper.Invalid;
        }

        [HarmonyPostfix]
        public static void Postfix(Atmosphere __instance, Atmosphere targetAtmos, GasMixture __state)
        {
            if (TerraformingFunctions.ThisGlobalPrecise == null)
                return;

            if (!NetworkManager.IsClient && targetAtmos.IsGlobalAtmosphere)
            {
                var change = TerraformingFunctions.GasMixCompair(__instance.GasMixture, __state);
                TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(change);
            }
        }
    }

    [HarmonyPatch(typeof(Atmosphere), "TakeAtmospherePortion")]
    public class AtmospherTakeAtmospherePortionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Atmosphere __instance, Atmosphere atmosphere, ref GasMixture __state, GasMixture ____totalMixInWorldGasMix)
        {
            if (!NetworkManager.IsClient && atmosphere.IsGlobalAtmosphere)
            {
                __state = new GasMixture(____totalMixInWorldGasMix);
                return;
            }

            __state = GasMixtureHelper.Invalid;
        }

        [HarmonyPostfix]
        public static void Postfix(Atmosphere __instance, Atmosphere atmosphere, GasMixture __state, GasMixture ____totalMixInWorldGasMix)
        {
            if (TerraformingFunctions.ThisGlobalPrecise == null)
                return;

            if (!NetworkManager.IsClient && atmosphere.IsGlobalAtmosphere)
            {
                var change = TerraformingFunctions.GasMixCompair(____totalMixInWorldGasMix, __state);
                TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(change);
            }
        }
    }

    [HarmonyPatch(typeof(Atmosphere), "GiveAtmospheresMixInWorld")]
    public class AtmospherGiveAtmospheresMixInWorldPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Atmosphere __instance, object ____mixingAtmos, float ____totalMixInWorldGiveWeight, GasMixture ____totalMixInWorldGasMix)
        {
            if (NetworkManager.IsClient || TerraformingFunctions.ThisGlobalPrecise == null)
                return;

            var atmoList = ____mixingAtmos as System.Collections.IEnumerable;
            if (atmoList == null)
                return;

            foreach (var entry in atmoList)
            {
                var traverse = Traverse.Create(entry);
                var atmo = traverse.Field("atmos").GetValue() as Atmosphere;
                float giveWeight = traverse.Field("GiveWeight").GetValue<float>();

                if (atmo != null && atmo.IsGlobalAtmosphere)
                {
                    float ratio = giveWeight / ____totalMixInWorldGiveWeight;
                    var gasMixture = new SimpleGasMixture(____totalMixInWorldGasMix);
                    gasMixture.Scale(ratio);

                    TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(gasMixture);
                }
            }
        }
    }

    [HarmonyPatch(typeof(AtmosphericsManager), "Deregister", new Type[] { typeof(Atmosphere) })]
    public class AtmosphericsControllerDeregisterPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Atmosphere atmosphere)
        {
            if (!NetworkManager.IsClient && atmosphere != null && atmosphere.Mode == AtmosphereHelper.AtmosphereMode.World && atmosphere.Room == null && atmosphere.IsCloseToGlobal(AtmosphereHelper.GlobalAtmosphereNeighbourThreshold / 6f * AtmosphereHelper.NewAtmosSupressionMultiplier()))
            {
                // scale the volume up to the size of the global atmosphere, or the values will be off
                var mixture = GasMixtureHelper.Create();
                mixture.Add(atmosphere.GasMixture);
                mixture.Scale(TerraformingFunctions.GlobalAtmosphere.Volume / atmosphere.Volume);

                // check the difference to global and compensate for it
                var change = TerraformingFunctions.GasMixCompair(TerraformingFunctions.GlobalAtmosphere.GasMixture, mixture);
                TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(change);
            }
        }
    }

    [HarmonyPatch(typeof(WorldManager), "StartWorld")]
    public class WorldManagerStartWorldPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (NetworkManager.IsClient) // on clients, bail out here, settings are not loaded yet.
                return;

            LightManager.SunPathTraceWorldAtmos = true;
            TerraformingFunctions.ThisGlobalPrecise = new GlobalAtmospherePrecise(Mathf.Abs(WorldSetting.Current.Gravity));
            TerraformingFunctions.ThisGlobalPrecise.OnLoadMix = WorldSetting.Current.GlobalGasMixture;

            // load saved atmosphere
            if (XmlSaveLoad.Instance.CurrentWorldSave != null)
            {
                var fileName = StationSaveUtils.GetWorldSaveDirectory(XmlSaveLoad.Instance.CurrentWorldSave.Name) + "/" + TerraformingFunctions.TerraformingFilename;
                object obj = XmlSerialization.Deserialize(TerraformingFunctions.AtmoSerializer, fileName);
                if (!(obj is TerraformingAtmosphere terraformingAtmosphere))
                {
                    ConsoleWindow.Print("Terraforming: Failed to load the terraforming_atmosphere.xml: " + fileName, ConsoleColor.Red);
                }
                else
                {
                    if (terraformingAtmosphere.GasMix != null)
                        TerraformingFunctions.ThisGlobalPrecise.OnLoadMix = terraformingAtmosphere.GasMix.Apply();
                    else
                        ConsoleWindow.Print("Terraforming: No stored GasMix found");
                }
            }

            // ensure the global atmosphere is being synced as needed
            var globalAtmo = TerraformingFunctions.GlobalAtmosphere;
            if (globalAtmo != null)
                AtmosphericsManager.AllAtmospheres.Add(globalAtmo);
            else
                ConsoleWindow.Print("Terraforming: Global Atmosphere is not valid");

            // update Solar Irradiance now for an accurate value right from here on out
            var value = typeof(OrbitalSimulation).GetMethod("CalculateSolarIrradiance", BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[0], null).Invoke(OrbitalSimulation.System, null);
            Traverse.Create(typeof(OrbitalSimulation)).Property("SolarIrradiance").SetValue(value);

            ConsoleWindow.Print($"Terraforming: GlobalPrecise generated (Terraforming mod loaded on server), Solar Scale {TerraformingFunctions.GetSolarScale()}");
        }
    }

    [HarmonyPatch(typeof(NetworkClient), "ProcessJoinData")]
    public class NetworkClientProcessJoinDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!NetworkManager.IsClient) // on server, this should not fire
                return;

            LightManager.SunPathTraceWorldAtmos = true;
            TerraformingFunctions.ThisGlobalPrecise = new GlobalAtmospherePrecise(Mathf.Abs(WorldSetting.Current.Gravity));
            TerraformingFunctions.ThisGlobalPrecise.OnLoadMix = WorldSetting.Current.GlobalGasMixture;
            ConsoleWindow.Print("GlobalPrecise generated (Terraforming mod loaded on client)");
        }
    }

    [HarmonyPatch(typeof(AtmosphereHelper), "IsValidForNetworkSend")]
    public class AtmosphereHelperIsValidForNetworkSendPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Atmosphere atmos, ref bool __result)
        {
            if (atmos != null && !atmos.BeingDestroyed && !atmos.IsNaN() && atmos == TerraformingFunctions.GlobalAtmosphere)
            {
                // only send the global atmosphere if the gas quantities changed
                __result = (atmos.GasMixture.GasQuantitiesDirtied() != 0);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(AtmosphereHelper), "ReadStatic")]
    public class AtmosphereHelperReadStaticPatch
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var codes = instructions.ToList();

            // hash to ensure the function didnt change and we might do bad things
            if (codes.HashInstructionsHex() != "cbeeb1b5")
            {
                ConsoleWindow.Print($"TerraformingMod: Code change detected ({codes.HashInstructionsHex()}), AtmosphereHelper ReadStatic patch disabled", ConsoleColor.Red);
                return codes.AsEnumerable();
            }

            System.Reflection.Emit.Label skipGlobalLabel = il.DefineLabel();

            // the end of the function should look like this, which we're adapting:
            /*
	            IL_0145: ldloc.3
	            IL_0146: ldarg.0
	            IL_0147: ldloc.1
	            IL_0148: callvirt instance void Assets.Scripts.Atmospherics.Atmosphere::Read(class Assets.Scripts.Networking.RocketBinaryReader, uint8)
	            IL_014d: ret

                Local index 1 = Network Flags ("b")
                Local index 2 = Atmosphere Mode
                Local index 3 = Atmosphere
            */

            // insert a branch before the call to the Read function
            int Index = codes.Count - 5;
            codes[Index].labels.Add(skipGlobalLabel); // store a skip label at the old instruction

            // Add a branch before the reading of the atmosphere, which will set the current GasMix to the atmosphere
            // this ensures we can detect any changes to the atmosphere properly
            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldloc, 2)); // load atmosphereMode
            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldc_I4, 3)); // load Mode=Global
            codes.Insert(Index++, new CodeInstruction(OpCodes.Bne_Un_S, skipGlobalLabel)); // skip branch if not equal

            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldloc, 3)); // load Atmosphere
            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldloc, 1)); // load network flags ("b")
            codes.Insert(Index++, CodeInstruction.Call(typeof(AtmosphereHelperReadStaticPatch), "PrepareReadingGlobalAtmosphere"));

            // jump to last instruction
            // at the end of the function, take the now updated Atmosphere, and update the GlobalAtmosphere with its values
            Index = codes.Count - 1;
            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldloc, 3)); // load Atmosphere
            codes.Insert(Index++, new CodeInstruction(OpCodes.Ldloc, 1)); // load network flags ("b")
            codes.Insert(Index++, CodeInstruction.Call(typeof(AtmosphereHelperReadStaticPatch), "ProcessGlobalAtmosphere"));

            return codes.AsEnumerable();
        }

        public static void PrepareReadingGlobalAtmosphere(Atmosphere atmosphere, byte networkUpdateFlags)
        {
            if (atmosphere != null && atmosphere.Mode == AtmosphereHelper.AtmosphereMode.Global)
            {
                if (AtmosphereHelper.IsNetworkUpdateRequired(64, networkUpdateFlags))
                {
                    atmosphere.GasMixture.Set(TerraformingFunctions.GlobalAtmosphere.GasMixture);
                }
            }
        }

        public static void ProcessGlobalAtmosphere(Atmosphere atmosphere, byte networkUpdateFlags)
        {
            if (atmosphere != null && atmosphere.Mode == AtmosphereHelper.AtmosphereMode.Global)
            {
                if (AtmosphereHelper.IsNetworkUpdateRequired(64, networkUpdateFlags))
                {
                    // ConsoleWindow.Print("Updating global atmosphere GasMixture");
                    var globalAtmosphere = TerraformingFunctions.GlobalAtmosphere;
                    globalAtmosphere.GasMixture.SetReadOnly(false);
                    globalAtmosphere.GasMixture.Set(atmosphere.GasMixture);
                    globalAtmosphere.GasMixture.SetReadOnly(true);
                    globalAtmosphere.GasMixture.UpdateCache();
                }
            }
        }
    }
    [HarmonyPatch(typeof(XmlSaveLoad), "WriteWorld")]
    public class WorldManagerExportWorldSettingDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string worldDirectory)
        {
            
            if (TerraformingFunctions.ThisGlobalPrecise != null)
            {
                var fileName = worldDirectory + "/temp_" + TerraformingFunctions.TerraformingFilename;
                fileName = fileName.Replace("\\", "/");

                // write out the global atmosphere
                var saveAtmosphere = new TerraformingAtmosphere();
                saveAtmosphere.GasMix = new GasMixSaveData(TerraformingFunctions.GlobalAtmosphere.GasMixture);
                if (!XmlSerialization.Serialization(TerraformingFunctions.AtmoSerializer, saveAtmosphere, fileName))
                {
                    ConsoleWindow.Print("Error Saving Terraforming Atmosphere: " + fileName, ConsoleColor.Red);
                    return;
                }
                // move file into the right place
                try
                {
                    if (File.Exists(worldDirectory + "/" + TerraformingFunctions.TerraformingFilename))
                    {
                        File.Delete(worldDirectory + "/" + TerraformingFunctions.TerraformingFilename);
                    }
                    File.Move(worldDirectory + "/temp_" + TerraformingFunctions.TerraformingFilename, worldDirectory + "/" + TerraformingFunctions.TerraformingFilename);
                }
                catch (Exception ex)
                {
                    ConsoleWindow.Print("Error Renaming Temporary Save Files: " + ex.Message, ConsoleColor.Red);
                    return;
                }
                ConsoleWindow.Print("Exported Terraforming Atmosphere");
            }
        }
    }

    [HarmonyPatch(typeof(AtmosphericsController), "UpdateGlobalAtmosphereWorldTemperature")]
    public class AtmosphereUpdateGlobalAtmosphereWorldTemperaturePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Atmosphere ____globalAtmosphere)
        {
            if (TerraformingFunctions.ThisGlobalPrecise != null)
            {
                float temp = TerraformingFunctions.GetTemperature(OrbitalSimulation.TimeOfDay, ____globalAtmosphere.GasMixture) + WorldManager.EventModTemperature;
                TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphere(temp, ____globalAtmosphere);
            }
            return false;
        }
    }

    public class TerraformingFunctions
    {

        public static GlobalAtmospherePrecise ThisGlobalPrecise;
        private static Atmosphere _global = null;
        public const string TerraformingFilename = "terraforming_atmosphere.xml";

        public static Atmosphere GlobalAtmosphere
        {
            get
            {
                return _global ?? (_global = AtmosphericsController.GlobalAtmosphere(new Grid3(0)));
            }
        }

        public static double GetSolarScale()
        {
            // calculate solar scale from the earth ratio
            double solarRatio = OrbitalSimulation.EarthSolarRatio;
            // the curve to fit these values has been created with a solver to retain similar values for the vanilla planets as the legacy solarScale values
            // but with the advantage of actually fluctuating with the seasons
            double fittedSolarScale = 0.3728728 + 1.746147 * solarRatio - 1.711329 * Math.Pow(solarRatio, 2) + 0.650517 * Math.Pow(solarRatio, 3);
            return fittedSolarScale;
        }

        public static float GetTemperature(float timeOfDay, GasMixture gasMix)
        {
            double squaredSolarScale = Math.Pow(GetSolarScale(), 2);

            float temperatureBase = ThisGlobalPrecise.GetWorldBaseTemperature(squaredSolarScale, gasMix);
            float temperatureDelta = ThisGlobalPrecise.GetWorldDeltaTemperature(temperatureBase, squaredSolarScale, gasMix);
            float temp = temperatureBase + Mathf.Sin(timeOfDay * 2f * Mathf.PI - Mathf.PI / 4) * temperatureDelta / 2;
            return temp;
        }
        public static List<SpawnGas> UpdateWorldSetting(GasMixture globalGasMixture)
        {
            List<SpawnGas> currentSpawnGas = new List<SpawnGas>();
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                currentSpawnGas.Add(new SpawnGas(type, globalGasMixture.GetMoleValue(type).Quantity));
            }
            return currentSpawnGas;
        }
        public static SimpleGasMixture GasMixCompair(GasMixture original1, GasMixture original2)
        {
            SimpleGasMixture result = new SimpleGasMixture();
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                double num = (double)original2.GetMoleValue(type).Quantity - (double)original1.GetMoleValue(type).Quantity;
                result.SetType(type, num);
            }

            return result;
        }
        private static XmlSerializer _atmoSerializer = null;
        public static XmlSerializer AtmoSerializer
        {
            get
            {
                if (_atmoSerializer != null)
                {
                    return _atmoSerializer;
                }

                _atmoSerializer = new XmlSerializer(typeof(TerraformingAtmosphere), XmlSaveLoad.ExtraTypes);
                return _atmoSerializer;
            }
        }
    }
    public class SimpleGasMixture
    {
        public SimpleGasMixture() {}
        public SimpleGasMixture(GasMixture gasMixture)
        {
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                SetType(type, gasMixture.GetMoleValue(type).Quantity);
            }
        }

        public double Pollutant { get; set; }
        public double LiquidPollutant { get; set; }
        public double CarbonDioxide { get; set; }
        public double LiquidCarbonDioxide { get; set; }
        public double Oxygen { get; set; }
        public double LiquidOxygen { get; set; }
        public double Volatiles { get; set; }
        public double LiquidVolatiles { get; set; }
        public double Nitrogen { get; set; }
        public double LiquidNitrogen { get; set; }
        public double NitrousOxide { get; set; }
        public double LiquidNitrousOxide { get; set; }
        public double Water { get; set; }
        public double Steam { get; set; }

        public void Reset()
        {
            Pollutant = 0;
            LiquidPollutant = 0;
            CarbonDioxide = 0;
            LiquidCarbonDioxide = 0;
            Oxygen = 0;
            LiquidOxygen = 0;
            Volatiles = 0;
            LiquidVolatiles = 0;
            Nitrogen = 0;
            LiquidNitrogen = 0;
            NitrousOxide = 0;
            LiquidNitrousOxide = 0;
            Water = 0;
            Steam = 0;
        }

        public void Scale(double scale)
        {
            Pollutant *= scale;
            LiquidPollutant *= scale;
            CarbonDioxide *= scale;
            LiquidCarbonDioxide *= scale;
            Oxygen *= scale;
            LiquidOxygen *= scale;
            Volatiles *= scale;
            LiquidVolatiles *= scale;
            Nitrogen *= scale;
            LiquidNitrogen *= scale;
            NitrousOxide *= scale;
            LiquidNitrousOxide *= scale;
            Water *= scale;
            Steam *= scale;
        }

        public double Add(SimpleGasMixture gasMix)
        {
            double AddedMoles = 0;
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                var add = gasMix.GetType(type);
                SetType(type, GetType(type) + add);
                AddedMoles += add;
            }

            return AddedMoles;
        }

        public void SetType(GasType gasType, double quantity)
        {
            switch (gasType)
            {
                case GasType.Undefined:
                    break;
                case GasType.Oxygen:
                    Oxygen = quantity;
                    break;
                case GasType.Nitrogen:
                    Nitrogen = quantity;
                    break;
                case GasType.CarbonDioxide:
                    CarbonDioxide = quantity;
                    break;
                case GasType.Volatiles:
                    Volatiles = quantity;
                    break;
                case GasType.Pollutant:
                    Pollutant = quantity;
                    break;
                case GasType.Water:
                    Water = quantity;
                    break;
                case GasType.NitrousOxide:
                    NitrousOxide = quantity;
                    break;
                case GasType.LiquidOxygen:
                    LiquidOxygen = quantity;
                    break;
                case GasType.LiquidNitrogen:
                    LiquidNitrogen = quantity;
                    break;
                case GasType.LiquidCarbonDioxide:
                    LiquidCarbonDioxide = quantity;
                    break;
                case GasType.LiquidVolatiles:
                    LiquidVolatiles = quantity;
                    break;
                case GasType.LiquidPollutant:
                    LiquidPollutant = quantity;
                    break;
                case GasType.Steam:
                    Steam = quantity;
                    break;
                case GasType.LiquidNitrousOxide:
                    LiquidNitrousOxide = quantity;
                    break;
                default:
                    break;
            }
        }

        public double GetType(GasType gasType)
        {
            switch (gasType)
            {
                case GasType.Undefined:
                    break;
                case GasType.Oxygen:
                    return Oxygen;
                case GasType.Nitrogen:
                    return Nitrogen;
                case GasType.CarbonDioxide:
                    return CarbonDioxide;
                case GasType.Volatiles:
                    return Volatiles;
                case GasType.Pollutant:
                    return Pollutant;
                case GasType.Water:
                    return Water;
                case GasType.NitrousOxide:
                    return NitrousOxide;
                case GasType.LiquidOxygen:
                    return LiquidOxygen;
                case GasType.LiquidNitrogen:
                    return LiquidNitrogen;
                case GasType.LiquidCarbonDioxide:
                    return LiquidCarbonDioxide;
                case GasType.LiquidVolatiles:
                    return LiquidVolatiles;
                case GasType.LiquidPollutant:
                    return LiquidPollutant;
                case GasType.Steam:
                    return Steam;
                case GasType.LiquidNitrousOxide:
                    return LiquidNitrousOxide;
                default:
                    break;
            }

            return 0.0;
        }
    }
    public class GlobalAtmospherePrecise: SimpleGasMixture
    {
        public static GasType[] gasTypes = new GasType[]
   {GasType.Pollutant, GasType.CarbonDioxide,GasType.Oxygen,GasType.Volatiles, GasType.Nitrogen, GasType.NitrousOxide, GasType.Water, GasType.LiquidPollutant, GasType.LiquidCarbonDioxide,GasType.LiquidOxygen,GasType.LiquidVolatiles, GasType.LiquidNitrogen, GasType.LiquidNitrousOxide, GasType.Steam };
        public static double worldSize;
        public static double[] baseFactors = new double[] { 3.21255958929106, 1.70512498586279, 0.260992760476665, 1.65544673748613, -0.447676800266691, -1.288345881, 0, 3.21255958929106, 1.70512498586279, 0.260992760476665, 1.65544673748613, -0.447676800266691, -1.288345881, 0 };
        public static double[] deltaFactors = new double[] { 1.03068489808625, -0.00586528497786273, 0.0151066403234939, 15.4334358506862, -0.044571485135339, -0.987064019, 0, 1.03068489808625, -0.00586528497786273, 0.0151066403234939, 15.4334358506862, -0.044571485135339, -0.987064019, 0 };
        public static double baseSolarScale = 269.391273688767;
        public static double deltaSolarScale = 98.8204375153876;
        public static double baseTQ = -0.0222557717480231;
        public static double deltaTQ = 0.0210397597758406;
        public static double deltaPa = -0.000450687147663802;
        public static double pressureGravityFactorInPa = 180 * 1000f;

        public GlobalAtmospherePrecise(float gravity)
        {
            worldSize = 7 * Math.Pow(10, 6);
            worldScale = 1 / worldSize;
            this.gravity = Mathf.Abs(gravity);
            rootGravity = Mathf.Sqrt(this.gravity);
        }

        private GasMixture _OnLoadMix;
        public GasMixture OnLoadMix
        {
            get { return _OnLoadMix; }
            set { _OnLoadMix = value; }
        }

        private float gravity;
        public float rootGravity;
        public double worldScale;

        private SimpleGasMixture GasMixAccumulater = new SimpleGasMixture();
        private double GasMixAccumulatorMoles = 0;

        public void UpdateGlobalAtmosphereChange(SimpleGasMixture change)
        {
            lock (this)
            {
                // add to accumulator, and only update the global atmosphere if there is a significant change
                GasMixAccumulatorMoles += GasMixAccumulater.Add(change);
                if (GasMixAccumulatorMoles <= 1)
                    return;

                // re-scale for world scale
                GasMixAccumulater.Scale(worldScale);

                // add to this mix
                Add(GasMixAccumulater);

                // reset
                GasMixAccumulater.Reset();
                GasMixAccumulatorMoles = 0;
            }
        }
        public void UpdateGlobalAtmosphere(float temp, Atmosphere GlobalAtmosphere)
        {
            GlobalAtmosphere.GasMixture.SetReadOnly(false);
            if (!NetworkManager.IsClient) // clients only update temperature, servers controls atmosphere
            {
                GlobalAtmosphere.GasMixture.Set(OnLoadMix);
                GlobalAtmosphere.GasMixture.Pollutant.Quantity += (float)Pollutant;
                GlobalAtmosphere.GasMixture.LiquidPollutant.Quantity += (float)LiquidPollutant;
                GlobalAtmosphere.GasMixture.CarbonDioxide.Quantity += (float)CarbonDioxide;
                GlobalAtmosphere.GasMixture.LiquidCarbonDioxide.Quantity += (float)LiquidCarbonDioxide;
                GlobalAtmosphere.GasMixture.Oxygen.Quantity += (float)Oxygen;
                GlobalAtmosphere.GasMixture.LiquidOxygen.Quantity += (float)LiquidOxygen;
                GlobalAtmosphere.GasMixture.Volatiles.Quantity += (float)Volatiles;
                GlobalAtmosphere.GasMixture.LiquidVolatiles.Quantity += (float)LiquidVolatiles;
                GlobalAtmosphere.GasMixture.Nitrogen.Quantity += (float)Nitrogen;
                GlobalAtmosphere.GasMixture.LiquidNitrogen.Quantity += (float)LiquidNitrogen;
                GlobalAtmosphere.GasMixture.NitrousOxide.Quantity += (float)NitrousOxide;
                GlobalAtmosphere.GasMixture.LiquidNitrousOxide.Quantity += (float)LiquidNitrousOxide;
                GlobalAtmosphere.GasMixture.Water.Quantity += (float)Water;
                GlobalAtmosphere.GasMixture.Steam.Quantity += (float)Steam;
            }
            float num = temp * GlobalAtmosphere.GasMixture.HeatCapacity;
            if (!float.IsNaN(temp))
            {
                GlobalAtmosphere.GasMixture.TotalEnergy = num;
            }
            if (!NetworkManager.IsClient && GlobalAtmosphere.PressureGassesAndLiquidsInPa > rootGravity * pressureGravityFactorInPa)
            {
                float num1 = (float)(rootGravity * pressureGravityFactorInPa / GlobalAtmosphere.PressureGassesAndLiquidsInPa);
                _OnLoadMix.Scale(num1);
                Scale(num1);
            }
            GlobalAtmosphere.GasMixture.SetReadOnly(true);
            GlobalAtmosphere.UpdateCache();
        }

        public float GetWorldBaseTemperature(double solarScaleSquare, GasMixture globalMix)
        {
            double temperature = 0;
            temperature += baseSolarScale * solarScaleSquare;
            for (int i = 0; i < gasTypes.Length; i++)
            {
                if (baseFactors[i] != 0)
                {
                    temperature += baseFactors[i] * Math.Sqrt(globalMix.GetGasTypeRatio(gasTypes[i])) * globalMix.GetMoleValue(gasTypes[i]).Quantity;
                }
            }
            temperature += baseTQ * globalMix.TotalMolesGassesAndLiquids;

            return (float)Math.Max(temperature, 0);
        }
        public float GetWorldDeltaTemperature(float baseTemp, double solarScaleSquare, GasMixture globalMix)
        {
            double temperature = 0;
            temperature += deltaSolarScale * solarScaleSquare;
            for (int i = 0; i < gasTypes.Length; i++)
            {
                if (deltaFactors[i] != 0)
                {
                    temperature += deltaFactors[i] * Math.Sqrt(globalMix.GetGasTypeRatio(gasTypes[i])) * globalMix.GetMoleValue(gasTypes[i]).Quantity;
                }
            }
            temperature += deltaTQ * globalMix.TotalMolesGassesAndLiquids;
            temperature += deltaPa * globalMix.TotalMolesGassesAndLiquids * baseTemp;

            return (float)Math.Max(temperature, 0);
        }
    }
}
