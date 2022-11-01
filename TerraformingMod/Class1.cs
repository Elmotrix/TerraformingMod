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
    [HarmonyPatch(typeof(Atmosphere), "LerpAtmosphere")]
    public class AtmosphereLerpAtmospherePAtch
    {
        public static void Prefix(Atmosphere targetAtmos, Atmosphere __instance, ref GasMixture __State)
        {
            if (targetAtmos.Mode == Atmosphere.AtmosphereMode.Global)
            {
                __State = TerraformingBackend.GasMixCopy(__instance.GasMixture);
            }
        }
        public static void Postfix(Atmosphere targetAtmos, Atmosphere __instance, ref GasMixture __State)
        {
            if (targetAtmos.Mode == Atmosphere.AtmosphereMode.Global)
            {
                GasMixture change = TerraformingBackend.GasMixCompair(__State, __instance.GasMixture, false);
                TerraformingBackend.ThisGlobalPrecise.UpdateGlobalAtmosphere(change, targetAtmos);
                targetAtmos.UpdateCache();
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
                float solarScale = WorldManager.CurrentWorldSetting.SolarScale;
                float temperatureBase = TerraformingBackend.GetWorldBaseTemperature(solarScale, __instance.GasMixture);
                float temperatureDelta = TerraformingBackend.GetWorldDeltaTemperature(solarScale, temperatureBase, __instance.GasMixture);
                float num = temperatureBase + Mathf.Sin(CursorManager._timeOfDay*2f*Mathf.PI + Mathf.PI/3) * temperatureDelta + WorldManager.EventModTemperature;
                __instance.GasMixture.SetReadOnly(false);
                __instance.GasMixture.TotalEnergy = num * __instance.GasMixture.HeatCapacity;
                __instance.GasMixture.SetReadOnly(true);
                __instance.UpdateCache();
            }
            return false;
        }
    }

    public class TerraformingBackend
    {
        public static GasType[] gasTypes = new GasType[]
        { GasType.CarbonDioxide, GasType.Nitrogen, GasType.NitrousOxide, GasType.Oxygen, GasType.Pollutant, GasType.Volatiles, GasType.Water };

        public static double worldSize =  1000000000;
        public static GlobalAtmospherePrecise ThisGlobalPrecise;
        public static void UpdateWorldSetting(GasMixture globalGasMixture)
        {
            List<SpawnGas> currentSpawnGas = new List<SpawnGas>();
            foreach (GasType type in gasTypes)
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
        public static GasMixture GasMixCompair(GasMixture original1, GasMixture original2, bool add)
        {
            float op = -1;
            if (add)
            {
                op = 1;
            }
            GasMixture result = GasMixture.Create();
            result.Nitrogen.Quantity = original1.Nitrogen.Quantity + original2.Nitrogen.Quantity * op;
            result.Nitrogen.Energy = original1.Nitrogen.Energy + original2.Nitrogen.Energy * op;

            result.Oxygen.Quantity = original1.Oxygen.Quantity + original2.Oxygen.Quantity * op;
            result.Oxygen.Energy = original1.Oxygen.Energy + original2.Oxygen.Energy * op;

            result.NitrousOxide.Quantity = original1.NitrousOxide.Quantity + original2.NitrousOxide.Quantity * op;
            result.NitrousOxide.Energy = original1.NitrousOxide.Energy + original2.NitrousOxide.Energy * op;

            result.CarbonDioxide.Quantity = original1.CarbonDioxide.Quantity + original2.CarbonDioxide.Quantity * op;
            result.CarbonDioxide.Energy = original1.CarbonDioxide.Energy + original2.CarbonDioxide.Energy * op;

            result.Pollutant.Quantity = original1.Pollutant.Quantity + original2.Pollutant.Quantity * op;
            result.Pollutant.Energy = original1.Pollutant.Energy + original2.Pollutant.Energy * op;

            result.Volatiles.Quantity = original1.Volatiles.Quantity + original2.Volatiles.Quantity * op;
            result.Volatiles.Energy = original1.Volatiles.Energy + original2.Volatiles.Energy * op;

            result.Water.Quantity = original1.Water.Quantity + original2.Water.Quantity * op;
            result.Water.Energy = original1.Water.Energy + original2.Water.Energy * op;

            return result;
        }

        public static float GetWorldBaseTemperature(float solarScale, GasMixture globalMix)
        {
            double temperature = 0;
            double solarScaleSquare = Math.Pow(solarScale, 2);
            temperature += 269.8904689 * solarScaleSquare;
            temperature += 4.958051666 * globalMix.GetGasTypeRatio(GasType.Pollutant) * globalMix.Pollutant.Quantity;
            temperature += 1.732343399 * globalMix.GetGasTypeRatio(GasType.CarbonDioxide) * globalMix.CarbonDioxide.Quantity;
            temperature += 0.233233832 * globalMix.GetGasTypeRatio(GasType.Oxygen) * globalMix.Oxygen.Quantity;
            temperature += 2.790796816 * globalMix.GetGasTypeRatio(GasType.Volatiles) * globalMix.Volatiles.Quantity;
            temperature += -0.01702955 * globalMix.GetGasTypeRatio(GasType.Nitrogen) * globalMix.Nitrogen.Quantity;
            temperature += -1.288345881 * globalMix.GetGasTypeRatio(GasType.NitrousOxide) * globalMix.NitrousOxide.Quantity;
            temperature += -0.004675393 * globalMix.TotalMolesGasses;

            return (float)temperature;
        }
        public static float GetWorldDeltaTemperature(float solarScale, float baseTemp, GasMixture globalMix)
        {
            double temperature = 0;
            double solarScaleSquare = Math.Pow(solarScale, 2);
            temperature += 98.82809337 * solarScaleSquare;
            temperature += 1.105542252 * globalMix.GetGasTypeRatio(GasType.Pollutant) * globalMix.Pollutant.Quantity;
            temperature += -0.005683588 * globalMix.GetGasTypeRatio(GasType.CarbonDioxide) * globalMix.CarbonDioxide.Quantity;
            temperature += 0.016181241 * globalMix.GetGasTypeRatio(GasType.Oxygen) * globalMix.Oxygen.Quantity;
            temperature += 21.50039752 * globalMix.GetGasTypeRatio(GasType.Volatiles) * globalMix.Volatiles.Quantity;
            temperature += -0.057927256 * globalMix.GetGasTypeRatio(GasType.Nitrogen) * globalMix.Nitrogen.Quantity;
            temperature += -0.987064019 * globalMix.GetGasTypeRatio(GasType.NitrousOxide) * globalMix.NitrousOxide.Quantity;
            temperature += 0.019918323 * globalMix.TotalMolesGasses;
            temperature += -0.00045 * globalMix.TotalMolesGasses * baseTemp;

            return (float)Math.Max(temperature,0);
        }
    }
    public class GlobalAtmospherePrecise
    {
        public double Pollutant { get; set; }
        public double CarbonDioxide { get; set; }
        public double Oxygen { get; set; }
        public double Volatiles { get; set; }
        public double Nitrogen { get; set; }
        public double NitrousOxide { get; set; }
        public double Water { get; set; }
        public GasMixture OnLoadMix { get; set; }
        public void UpdateGlobalAtmosphere(GasMixture change, Atmosphere GlobalAtmosphere)
        {
            double scale = 1 / TerraformingBackend.worldSize;
            Pollutant += (double)change.Pollutant.Quantity * scale;
            CarbonDioxide += (double)change.CarbonDioxide.Quantity * scale;
            Oxygen += (double)change.Oxygen.Quantity * scale;
            Volatiles += (double)change.Volatiles.Quantity * scale;
            Nitrogen += (double)change.Nitrogen.Quantity * scale;
            NitrousOxide += (double)change.NitrousOxide.Quantity * scale;
            Water += (double)change.Water.Quantity * scale;
            GlobalAtmosphere.GasMixture.SetReadOnly(false);
            GlobalAtmosphere.GasMixture.Set(OnLoadMix);
            GlobalAtmosphere.GasMixture.Pollutant.Quantity += (float)Pollutant;
            GlobalAtmosphere.GasMixture.CarbonDioxide.Quantity += (float)CarbonDioxide;
            GlobalAtmosphere.GasMixture.Oxygen.Quantity += (float)Oxygen;
            GlobalAtmosphere.GasMixture.Volatiles.Quantity += (float)Volatiles;
            GlobalAtmosphere.GasMixture.Nitrogen.Quantity += (float)Nitrogen;
            GlobalAtmosphere.GasMixture.NitrousOxide.Quantity += (float)NitrousOxide;
            GlobalAtmosphere.GasMixture.Water.Quantity += (float)Water;
            GlobalAtmosphere.GasMixture.SetReadOnly(true);
            GlobalAtmosphere.UpdateCache();
        }
    }
}
