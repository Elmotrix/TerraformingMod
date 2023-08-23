using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Assets.Scripts.Atmospherics;
using static Assets.Scripts.Atmospherics.Chemistry;
using Assets.Scripts.Objects;
using Assets.Scripts;
using Assets.Scripts.Serialization;

namespace TerraformingMod
{
    [HarmonyPatch(typeof(Atmosphere), "GiveAtmospherePortion")]
    public class AtmosphereGiveAtmospherePortionPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Atmosphere __instance, Atmosphere atmosphere, out GasMixture __state)
        {
            if (atmosphere.Mode == AtmosphereHelper.AtmosphereMode.World)
            {
                if (atmosphere.Room == null || __instance.Room == null)
                {
                    __state = TerraformingFunctions.GasMixCopy(atmosphere.GasMixture);
                    return;
                }
            }
            __state = GasMixtureHelper.Invalid;
        }
        [HarmonyPostfix]
        public static void Postfix(Atmosphere __instance, Atmosphere atmosphere, GasMixture __state)
        {
            if (atmosphere.Mode == AtmosphereHelper.AtmosphereMode.World)
            {
                if ((atmosphere.Room == null || __instance.Room == null) && TerraformingFunctions.ThisGlobalPrecise != null)
                {
                    SimpleGasMixture change = TerraformingFunctions.GasMixCompair(__state, atmosphere.GasMixture, false);
                    if (atmosphere.Room != null)
                    {
                        change.Scale(0.1);
                    }
                    else if (__instance.Room != null)
                    {
                        change.Scale(-1);
                    }
                    TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(change);
                }
            }
        }
    }
    [HarmonyPatch(typeof(WorldManager), "StartWorld")]
    public class WorldManagerStartWorldPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            LightManager.SunPathTraceWorldAtmos = true;
            TerraformingFunctions.ThisGlobalPrecise = new GlobalAtmospherePrecise(WorldManager.CurrentWorldSetting.DifficultySetting.Name, Mathf.Abs(WorldManager.CurrentWorldSetting.Gravity));
            TerraformingFunctions.ThisGlobalPrecise.OnLoadMix = TerraformingFunctions.GasMixCopy(AtmosphericsController.GlobalAtmosphere.GasMixture);
            TerraformingFunctions.ThisGlobalPrecise.solarScale = WorldManager.CurrentWorldSetting.SolarScale;
            TerraformingFunctions.ThisGlobalPrecise.solarScaleSquare = Math.Pow(WorldManager.CurrentWorldSetting.SolarScale, 2);
            ConsoleWindow.Print("GlobalPrecise generated (Terrafmorning mod loaded successfully)");
        }
    }
    [HarmonyPatch(typeof(XmlSaveLoad), "WriteWorld")]
    public class WorldManagerExportWorldSettingDataPatch
    {
        [HarmonyPrefix]
        public static void Prefix(WorldSettingData ____worldSettingData)
        {
            
            if (AtmosphericsController.GlobalAtmosphere != null)
            {
                ____worldSettingData.SpawnContents = TerraformingFunctions.UpdateWorldSetting(AtmosphericsController.GlobalAtmosphere.GasMixture);
                ConsoleWindow.Print("Exported GlobalPrecise World Settings");
            }
            else
            {
                ConsoleWindow.Print("Exported GlobalPrecise failed");
            }
        }
    }
    [HarmonyPatch(typeof(Atmosphere), "UpdateGlobalAtmosphereWorldTemperature")]
    public class AtmosphereUpdateGlobalAtmosphereWorldTemperaturePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Atmosphere __instance)
        {
            if (__instance.Mode == AtmosphereHelper.AtmosphereMode.Global && TerraformingFunctions.ThisGlobalPrecise != null)
            {
                float temp = TerraformingFunctions.GetTemperature(CursorManager._timeOfDay, __instance.GasMixture) + WorldManager.EventModTemperature;
                TerraformingFunctions.ThisGlobalPrecise.UpdateGlobalAtmosphere(temp, AtmosphericsController.GlobalAtmosphere);
            }
            return false;
        }
    }

    public class TerraformingFunctions
    {

        public static GlobalAtmospherePrecise ThisGlobalPrecise;
        public static float GetTemperature(float timeOfDay, GasMixture gasMix)
        {
            float temperatureBase = ThisGlobalPrecise.GetWorldBaseTemperature(gasMix);
            float temperatureDelta = ThisGlobalPrecise.GetWorldDeltaTemperature(temperatureBase, gasMix);
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
        public static GasMixture GasMixCopy(GasMixture original)
        {
            GasMixture result = GasMixtureHelper.Create();
            result.Set(original);
            return result;
        }
        public static SimpleGasMixture GasMixCompair(GasMixture original1, GasMixture original2, bool add)
        {
            double op = -1;
            if (add)
            {
                op = 1;
            }
            SimpleGasMixture result = new SimpleGasMixture();
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                double num = (double)original1.GetMoleValue(type).Quantity + (double)original2.GetMoleValue(type).Quantity*op;
                result.SetType(type, num);
            }

            return result;
        }
    }
    public class SimpleGasMixture
    {
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
        public static double pressureGravityFactor = 180;

        public GlobalAtmospherePrecise(string Difficulty, float gravity)
        {
            switch (Difficulty)
            {
                case "Easy":
                    break;
                case "Stationeer":
                    break;
                case "Normal":
                default:
                    break;
            }
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

        public float solarScale;
        private float gravity;
        public float rootGravity;
        public double solarScaleSquare;
        public double worldScale;

        public void UpdateGlobalAtmosphereChange(SimpleGasMixture change)
        {
            Pollutant += change.Pollutant * worldScale;
            LiquidPollutant += change.LiquidPollutant * worldScale;
            CarbonDioxide += change.CarbonDioxide * worldScale;
            LiquidCarbonDioxide += change.LiquidCarbonDioxide * worldScale;
            Oxygen += change.Oxygen * worldScale;
            LiquidOxygen += change.LiquidOxygen * worldScale;
            Volatiles += change.Volatiles * worldScale;
            LiquidVolatiles += change.LiquidVolatiles * worldScale;
            Nitrogen += change.Nitrogen * worldScale;
            LiquidNitrogen += change.LiquidNitrogen * worldScale;
            NitrousOxide += change.NitrousOxide * worldScale;
            LiquidNitrousOxide += change.LiquidNitrousOxide * worldScale;
            Water += change.Water * worldScale;
            Steam += change.Steam * worldScale;
        }
        public void UpdateGlobalAtmosphere(float temp, Atmosphere GlobalAtmosphere)
        {
            GlobalAtmosphere.GasMixture.SetReadOnly(false);
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
            float num = temp * GlobalAtmosphere.GasMixture.HeatCapacity;
            if (!float.IsNaN(temp))
            {
                GlobalAtmosphere.GasMixture.TotalEnergy = num;
            }
            if (GlobalAtmosphere.PressureGassesAndLiquids > rootGravity * pressureGravityFactor)
            {
                float num1 = (float)(rootGravity * pressureGravityFactor / GlobalAtmosphere.PressureGassesAndLiquids);
                _OnLoadMix.Scale(num1);
                Scale(num1);
            }
            GlobalAtmosphere.GasMixture.SetReadOnly(true);
            GlobalAtmosphere.UpdateCache();
        }

        public float GetWorldBaseTemperature(GasMixture globalMix)
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
            temperature += baseTQ * globalMix.TotalMolesGasses;

            return (float)Math.Max(temperature, 0);
        }
        public float GetWorldDeltaTemperature(float baseTemp, GasMixture globalMix)
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
            temperature += deltaTQ * globalMix.TotalMolesGasses;
            temperature += deltaPa * globalMix.TotalMolesGasses * baseTemp;

            return (float)Math.Max(temperature, 0);
        }
    }
}
