using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace SuperchargedPatch.Extensions
{
    public static class PlateReturnControllerExt
    {
        private static readonly Type type = typeof(PlateReturnController);
        private static readonly FieldInfo f_m_platesToReturn = AccessTools.Field(type, "m_platesToReturn");
        private static readonly MethodInfo m_FindBestReturnStation = AccessTools.Method(type, "FindBestReturnStation");
        private static readonly MethodInfo m_IsInLevelBounds = AccessTools.Method(type, "IsInLevelBounds");
        
        public static PlateReturnController_PlatesPendingReturnFastListExt m_platesToReturn(this PlateReturnController instance)
        {
            return new PlateReturnController_PlatesPendingReturnFastListExt(f_m_platesToReturn.GetValue(instance));
        }

        public static ServerPlateReturnStation FindBestReturnStation(this PlateReturnController instance, PlatingStepData _plateType)
        {
            return (ServerPlateReturnStation)m_FindBestReturnStation.Invoke(instance, new object[] { _plateType });
        }

        public static bool IsInLevelBounds(this PlateReturnController instance, ServerPlateReturnStation _station)
        {
            return (bool)m_IsInLevelBounds.Invoke(instance, new object[] { _station });
        }
    }

    public class PlateReturnController_PlatesPendingReturnFastListExt
    {
        private static readonly Type eleType = AccessTools.Inner(typeof(PlateReturnController), "PlatesPendingReturn");
        private static readonly Type listType = typeof(FastList<>).MakeGenericType(eleType);
        private static readonly MethodInfo m_Clear = AccessTools.Method(listType, "Clear");
        private static readonly MethodInfo m_Add = AccessTools.Method(listType, "Add");
        private static readonly PropertyInfo p_Count = AccessTools.Property(listType, "Count");
        private static readonly FieldInfo f_items = AccessTools.Field(listType, "_items");
        private static readonly MethodInfo m_RemoveAt = AccessTools.Method(listType, "RemoveAt");

        public readonly object instance;

        public PlateReturnController_PlatesPendingReturnFastListExt(object instance)
        {
            this.instance = instance;
        }

        public void Clear()
        {
            m_Clear.Invoke(instance, null);
        }

        public void Add(PlateReturnController_PlatesPendingReturnExt item)
        {
            m_Add.Invoke(instance, new object[] { item.instance });
        }

        public int Count
        {
            get
            {
                return (int)p_Count.GetValue(instance, null);
            }
        }

        public PlateReturnController_PlatesPendingReturnExt this[int index]
        {
            get
            {
                return new PlateReturnController_PlatesPendingReturnExt(((Array)f_items.GetValue(instance)).GetValue(index));
            }
        }

        public void RemoveAt(int index)
        {
            m_RemoveAt.Invoke(instance, new object[] { index });
        }
    }

    public class PlateReturnController_PlatesPendingReturnExt
    {
        private static readonly Type type = AccessTools.Inner(typeof(PlateReturnController), "PlatesPendingReturn");
        private static readonly FieldInfo f_m_station = AccessTools.Field(type, "m_station");
        private static readonly FieldInfo f_m_timer = AccessTools.Field(type, "m_timer");
        private static readonly FieldInfo f_m_platingStepData = AccessTools.Field(type, "m_platingStepData");

        public readonly object instance;


        public PlateReturnController_PlatesPendingReturnExt(object instance)
        {
            this.instance = instance;
        }

        public static PlateReturnController_PlatesPendingReturnExt Create(ServerPlateReturnStation _returnStation, float _delay, PlatingStepData platingStepData)
        {
            return new PlateReturnController_PlatesPendingReturnExt(Activator.CreateInstance(type, _returnStation, _delay, platingStepData));
        }

        public ServerPlateReturnStation m_station
        {
            get
            {
                return (ServerPlateReturnStation)f_m_station.GetValue(instance);
            }
            set
            {
                f_m_station.SetValue(instance, value);
            }
        }

        public float m_timer
        {
            get
            {
                return (float)f_m_timer.GetValue(instance);
            }
            set
            {
                f_m_timer.SetValue(instance, value);
            }
        }

        public PlatingStepData m_platingStepData
        {
            get
            {
                return (PlatingStepData)f_m_platingStepData.GetValue(instance);
            }
        }
    }
}
