using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Atmospherics;
using Assets.Scripts.Networks;
using System.Reflection;
using Assets.Scripts.Objects.Items;
using static Assets.Scripts.Atmospherics.Chemistry;
using Objects.Pipes;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects;
using JetBrains.Annotations;
using Assets.Scripts;
using static Assets.Scripts.Atmospherics.Atmosphere;

namespace TerraformingMod
{
    [HarmonyPatch(typeof(Atmosphere), "MixInWorld")]
    public class AtmosphereLerpAtmospherePAtch
    {
        public static void Prefix(Atmosphere __instance, ref GasMixture __State)
        {
            if (__instance.Mode == Atmosphere.AtmosphereMode.World)
            {
                if (__instance.Room == null)
                {
                    __State.Set(__instance.GasMixture);
                }
            }
        }
        public static void Postfix(Atmosphere __instance, ref GasMixture __State)
        {
            if (__instance.Mode == Atmosphere.AtmosphereMode.World)
            {
                if (__instance.Room == null)
                {
                    SimpleGasMixture change = TerraformingBackend.GasMixCompair(__State, __instance.GasMixture, false);
                    TerraformingBackend.ThisGlobalPrecise.UpdateGlobalAtmosphereChange(change);
                }
            }
        }
    }
    [HarmonyPatch(typeof(AtmosphericsController), "CreateGlobalAtmosphere")]
    public class AtmosphericsControllerCreateGlobalAtmospherePatch
    {
        public static void Prefix()
        {

        }
        public static void Postfix()
        {
            TerraformingBackend.ThisGlobalPrecise = new GlobalAtmospherePrecise();
            TerraformingBackend.ThisGlobalPrecise.OnLoadMix = TerraformingBackend.GasMixCopy(AtmosphericsController.GlobalAtmosphere.GasMixture);
            TerraformingBackend.ThisGlobalPrecise.solarScale = WorldManager.CurrentWorldSetting.SolarScale;
            TerraformingBackend.ThisGlobalPrecise.solarScaleSquare = Math.Pow(WorldManager.CurrentWorldSetting.SolarScale, 2);
            TerraformingBackend.ThisGlobalPrecise.gravity = Mathf.Abs(WorldManager.CurrentWorldSetting.Gravity);
        }
    }
    [HarmonyPatch(typeof(WorldManager), "ExportWorldSettingData", new Type[] { typeof(WorldSetting) })]
    public class WorldManagerExportWorldSettingDataPatch
    {
        public static void Prefix()
        {
            TerraformingBackend.UpdateWorldSetting(AtmosphericsController.GlobalAtmosphere.GasMixture);
        }
    }
    [HarmonyPatch(typeof(Atmosphere), "UpdateGlobalAtmosphereWorldTemperature")]
    public class AtmosphereUpdateGlobalAtmosphereWorldTemperaturePatch
    {
        public static bool Prefix(Atmosphere __instance)
        {
            if (__instance.Mode == Atmosphere.AtmosphereMode.Global)
            {
                float temperatureBase = TerraformingBackend.ThisGlobalPrecise.GetWorldBaseTemperature(__instance.GasMixture);
                float temperatureDelta = TerraformingBackend.ThisGlobalPrecise.GetWorldDeltaTemperature(temperatureBase, __instance.GasMixture);
                float temp = temperatureBase + Mathf.Sin(CursorManager._timeOfDay*2f*Mathf.PI + Mathf.PI/3) * temperatureDelta/2 + WorldManager.EventModTemperature;
                TerraformingBackend.ThisGlobalPrecise.UpdateGlobalAtmosphere(temp, AtmosphericsController.GlobalAtmosphere);
                __instance.GasMixture.SetReadOnly(false);
                __instance.GasMixture.TotalEnergy = temp * __instance.GasMixture.HeatCapacity;
                __instance.GasMixture.SetReadOnly(true);
                __instance.UpdateCache();
            }
            return false;
        }
    }

    public class TerraformingBackend
    {

        public static GlobalAtmospherePrecise ThisGlobalPrecise;

        public static void UpdateWorldSetting(GasMixture globalGasMixture)
        {
            List<SpawnGas> currentSpawnGas = new List<SpawnGas>();
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                currentSpawnGas.Add(new SpawnGas(type, globalGasMixture.GetMoleValue(type).Quantity));
            }
            WorldManager.CurrentWorldSetting.SpawnContents = currentSpawnGas;
        }
        public static GasMixture GasMixCopy(GasMixture original)
        {
            GasMixture result = GasMixture.Create();
            result.Set(original);
            return result;
        }
        public static SimpleGasMixture GasMixCompair(GasMixture original1, GasMixture original2, bool add)
        {
            float op = -1;
            if (add)
            {
                op = 1;
            }
            SimpleGasMixture result = new SimpleGasMixture();
            foreach (GasType type in GlobalAtmospherePrecise.gasTypes)
            {
                float num = original1.GetMoleValue(type).Quantity + original2.GetMoleValue(type).Quantity*op;
                result.SetType(type, num);
            }

            return result;
        }
    }
    public class SimpleGasMixture
    {
        public double Pollutant { get; set; }
        public double CarbonDioxide { get; set; }
        public double Oxygen { get; set; }
        public double Volatiles { get; set; }
        public double Nitrogen { get; set; }
        public double NitrousOxide { get; set; }
        public double Water { get; set; }

        public void SetType(GasType gasType, float quantity)
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
                default:
                    break;
            }
        }
    }
    public class GlobalAtmospherePrecise: SimpleGasMixture
    {
        public static GasType[] gasTypes = new GasType[]
   {GasType.Pollutant, GasType.CarbonDioxide,GasType.Oxygen,GasType.Volatiles, GasType.Nitrogen, GasType.NitrousOxide, GasType.Water };
        public static double worldSize = 1000000000;
        public static double[] baseFactors = new double[] { 3.21255958929106, 1.70512498586279, 0.260992760476665, 1.65544673748613, -0.447676800266691, -1.288345881, 0 };
        public static double[] deltaFactors = new double[] { 1.03068489808625, -0.00586528497786273, 0.0151066403234939, 15.4334358506862, -0.044571485135339, -0.987064019, 0 };
        public static double baseSolarScale = 269.391273688767;
        public static double deltaSolarScale = 98.8204375153876;
        public static double baseTQ = -0.0222557717480231;
        public static double deltaTQ = 0.0210397597758406;
        public static double deltaPa = -0.000450687147663802;
        public static double pressureGravityFactor = 180;

        public GlobalAtmospherePrecise()
        {
            worldScale = 1 / worldSize;
        }

        private GasMixture _OnLoadMix;
        public GasMixture OnLoadMix
        {
            get { return _OnLoadMix; }
            set { _OnLoadMix = value; }
        }

        public float solarScale;
        public float gravity;
        public double solarScaleSquare;
        public double worldScale;

        public void UpdateGlobalAtmosphereChange(SimpleGasMixture change)
        {
            Pollutant += change.Pollutant * worldScale;
            CarbonDioxide += change.CarbonDioxide * worldScale;
            Oxygen += change.Oxygen * worldScale;
            Volatiles += change.Volatiles * worldScale;
            Nitrogen += change.Nitrogen * worldScale;
            NitrousOxide += change.NitrousOxide * worldScale;
            Water += change.Water * worldScale;
        }
        public void UpdateGlobalAtmosphere(float temp, Atmosphere GlobalAtmosphere)
        {
            GlobalAtmosphere.GasMixture.SetReadOnly(false);
            GlobalAtmosphere.GasMixture.Set(OnLoadMix);
            GlobalAtmosphere.GasMixture.Pollutant.Quantity += (float)Pollutant;
            GlobalAtmosphere.GasMixture.CarbonDioxide.Quantity += (float)CarbonDioxide;
            GlobalAtmosphere.GasMixture.Oxygen.Quantity += (float)Oxygen;
            GlobalAtmosphere.GasMixture.Volatiles.Quantity += (float)Volatiles;
            GlobalAtmosphere.GasMixture.Nitrogen.Quantity += (float)Nitrogen;
            GlobalAtmosphere.GasMixture.NitrousOxide.Quantity += (float)NitrousOxide;
            GlobalAtmosphere.GasMixture.Water.Quantity += (float)Water;
            float num = temp * GlobalAtmosphere.GasMixture.HeatCapacity;
            _OnLoadMix.TotalEnergy = num;
            GlobalAtmosphere.GasMixture.TotalEnergy = num;
            if (GlobalAtmosphere.PressureGassesAndLiquids > gravity* pressureGravityFactor)
            {
                float num1 = (float)(gravity * pressureGravityFactor / GlobalAtmosphere.PressureGassesAndLiquids);
                GlobalAtmosphere.GasMixture.Scale(num1);
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
