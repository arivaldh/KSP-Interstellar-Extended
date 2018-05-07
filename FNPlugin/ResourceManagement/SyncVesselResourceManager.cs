using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin
{
    public class SyncVesselResourceManager
    {
        private class ProductionConsumption
        {
            public double Current { get; set; }
            public double Max { get; set; }

            public ProductionConsumption(double current, double max)
            {
                this.Current = current;
                this.Max = max;
            }
        }

        public const string ELECTRIC_CHARGE_RESOURCE_NAME   = "ElectricCharge";
        public const string MEGAJOULES_RESOURCE_NAME        = "Megajoules";
        public const string CHARGED_PARTICLES_RESOURCE_NAME = "ChargedParticles";
        public const string THERMAL_POWER_RESOURCE_NAME     = "ThermalPower";
        public const string WASTEHEAT_RESOURCE_NAME         = "WasteHeat";

        public readonly static PartResourceDefinition ELECTRIC_CHARGE_DEFINITION   = PartResourceLibrary.Instance.GetDefinition(ELECTRIC_CHARGE_RESOURCE_NAME);
        public readonly static PartResourceDefinition WASTEHEAT_DEFINITION         = PartResourceLibrary.Instance.GetDefinition(WASTEHEAT_RESOURCE_NAME);
        public readonly static PartResourceDefinition THERMAL_POWER_DEFINITION     = PartResourceLibrary.Instance.GetDefinition(THERMAL_POWER_RESOURCE_NAME);
        public readonly static PartResourceDefinition CHARGED_PARTICLES_DEFINITION = PartResourceLibrary.Instance.GetDefinition(CHARGED_PARTICLES_RESOURCE_NAME);
        public readonly static PartResourceDefinition MEGAJOULES_DEFINITION        = PartResourceLibrary.Instance.GetDefinition(MEGAJOULES_RESOURCE_NAME);

        private static Dictionary<Vessel, SyncVesselResourceManager> vesselManagers = new Dictionary<Vessel, SyncVesselResourceManager>();
        
        public static SyncVesselResourceManager GetSyncVesselResourceManager(Vessel vessel)
        {
            SyncVesselResourceManager manager = null;

            if (!vesselManagers.TryGetValue(vessel, out manager))
            {
                manager = new SyncVesselResourceManager(vessel);
                vesselManagers.Add(vessel, manager);
            }

            return manager;
        }

        private Vessel vessel;
        private Dictionary<ISyncResourceModule, List<ConversionProcess>> processes;
        private Dictionary<int, Dictionary<ISyncResourceModule, PtpSnapshot>> ptpSnapshots;
        private Dictionary<int, ResourceSnapshot> snapshots;

        // FixedUpdate Synchronization results
        Dictionary<int, Dictionary<ISyncResourceModule, ProductionConsumption>> productions = new Dictionary<int, Dictionary<ISyncResourceModule, ProductionConsumption>>();
        Dictionary<int, Dictionary<ISyncResourceModule, ProductionConsumption>> consumptions = new Dictionary<int, Dictionary<ISyncResourceModule, ProductionConsumption>>();

        // GUI elements
        private const int labelWidth = 240;
        private const int valueWidth = 55;
        private const int priorityWidth = 30;
        private const int overviewWidth = 65;
        GUIStyle leftBoldLabel;
        GUIStyle rightBoldLabel;
        GUIStyle greenLabel;
        GUIStyle redLabel;
        GUIStyle leftAlignedLabel;
        GUIStyle rightAlignedLabel;
        private Dictionary<int, Boolean> renderWindow = new Dictionary<int, Boolean>();
        private Dictionary<int, Rect> windowPositions = new Dictionary<int, Rect>();
        private Dictionary<int, int> windowIdToResourceId = new Dictionary<int, int>();
        private Dictionary<int, int> resourceIdToWindowId = new Dictionary<int, int>();

        public SyncVesselResourceManager(Vessel vessel)
        {
            this.vessel = vessel;
            this.processes = new Dictionary<ISyncResourceModule, List<ConversionProcess>>();
            this.snapshots = new Dictionary<int, ResourceSnapshot>();
            this.ptpSnapshots = new Dictionary<int, Dictionary<ISyncResourceModule, PtpSnapshot>>();
            this.windowPositions = new Dictionary<int, Rect>();
            this.windowIdToResourceId = new Dictionary<int, int>();
            this.resourceIdToWindowId = new Dictionary<int, int>();
        }

        public static void SynchronizeAll()
        {
            foreach (SyncVesselResourceManager manager in vesselManagers.Values)
            {
                manager.Synchronize();
            }
        }

        public static void CleanAll()
        {
            vesselManagers.Clear();
        }

        public static void RegisterRadiator(FNRadiator radiator)
        {
            SyncVesselResourceManager manager = SyncVesselResourceManager.GetSyncVesselResourceManager(radiator.vessel);
            ResourceSnapshot snapshot = manager.GetResourceSnapshot(WASTEHEAT_DEFINITION.id);
            (snapshot as WasteHeatSnapshot).RegisterRadiator(radiator);
        }

        public ResourceSnapshot GetResourceSnapshot(string resourceName)
        {
            return GetResourceSnapshot(PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public ResourceSnapshot GetResourceSnapshot(int resourceId)
        {
            ResourceSnapshot snapshot = null;

            if (!snapshots.TryGetValue(resourceId, out snapshot))
            {
                snapshot = SnapshotFactory.GetNewSnapshot(vessel, resourceId);
                snapshots.Add(resourceId, snapshot);
                createGUIElements(resourceId);
            }

            return snapshot;
        }

        public ResourceSnapshot GetResourceSnapshot(ISyncResourceModule module, string resourceName)
        {
            return GetResourceSnapshot(module, PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public ResourceSnapshot GetResourceSnapshot(ISyncResourceModule module, int resourceId)
        {
            Dictionary<ISyncResourceModule, PtpSnapshot> resourceSnapshots;
            if (ptpSnapshots.TryGetValue(resourceId, out resourceSnapshots))
            {
                foreach (PtpSnapshot snapshot in resourceSnapshots.Values)
                {
                    if (snapshot.ForModule(module))
                    {
                        return snapshot;
                    }
                }
                return GetResourceSnapshot(resourceId);
            }
            else
            {
                return GetResourceSnapshot(resourceId);
            }
        }

        public void RegisterPtpSnapshot(ISyncResourceModule producer, ISyncResourceModule consumer, string resourceName)
        {
            RegisterPtpSnapshot(producer, consumer, PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public void RegisterPtpSnapshot(ISyncResourceModule producer, ISyncResourceModule consumer, int resourceId)
        {
            Dictionary<ISyncResourceModule, PtpSnapshot> resourceSnapshots;
            if (!ptpSnapshots.TryGetValue(resourceId, out resourceSnapshots))
            {
                resourceSnapshots = new Dictionary<ISyncResourceModule, PtpSnapshot>();
                createGUIElements(resourceId);
                ptpSnapshots.Add(resourceId, resourceSnapshots);
            }

            PtpSnapshot ptpSnapshot;
            if (!resourceSnapshots.TryGetValue(producer, out ptpSnapshot))
            {
                ptpSnapshot = new PtpSnapshot(producer, vessel, resourceId);
                resourceSnapshots.Add(producer, ptpSnapshot);
            }

            // To allow TP/CP snapshot registration for Producer
            if (consumer != null)
                ptpSnapshot.AddConsumer(consumer);
        }

        private void createGUIElements(int resourceId)
        {
            if (ProduceGUI(resourceId) && !windowPositions.ContainsKey(resourceId))
            {
                windowPositions.Add(resourceId, GetWindowPosition(resourceId));
                int windowId = GetWindowId(resourceId);
                windowIdToResourceId.Add(windowId, resourceId);
                resourceIdToWindowId.Add(resourceId, windowId);
                renderWindow.Add(resourceId, false);
            }
        }

        public static ConversionProcess AddProcess(PartModule module, ISyncResourceModule callback, ConversionProcess process)
        {
            SyncVesselResourceManager manager = GetSyncVesselResourceManager(module.vessel);
            manager.InsertConversionProcess(callback, process);
            return process;
        }

        protected void InsertConversionProcess(ISyncResourceModule callback, ConversionProcess process)
        {
            List<ConversionProcess> list = null;
            if (!processes.TryGetValue(callback, out list))
            {
                list = new List<ConversionProcess>();
                processes.Add(callback, list);
            }

            list.Add(process);
        }

        public void Synchronize()
        {
            RunAllProcesses();
            AggregateProductionsConsumptions();
            CommitSnapshots();
            NotifyAllModules();
            processes.Clear();
        }

        private void RunAllProcesses()
        {
            bool ranOK;
            do
            {
                ranOK = false;
                foreach (List<ConversionProcess> processes in processes.Values)
                {
                    foreach (ConversionProcess process in processes)
                    {
                        ranOK |= process.Run(this);
                    }
                }
            } while (ranOK);
        }

        private void AggregateProductionsConsumptions()
        {
            productions.Clear();
            consumptions.Clear();

            HashSet<int> resourceIds = new HashSet<int>(snapshots.Keys);
            resourceIds.UnionWith(ptpSnapshots.Keys);

            foreach (int resourceId in resourceIds)
            {
                if (renderWindow[resourceId])
                {
                    productions.Add(resourceId, GetProductionPerSecond(resourceId));
                    consumptions.Add(resourceId, GetConsumptionPerSecond(resourceId));
                }
            }

            Dictionary<ISyncResourceModule, ProductionConsumption> wasteHeatConsumptions;
            if (!consumptions.TryGetValue(WASTEHEAT_DEFINITION.id, out wasteHeatConsumptions))
            {
                wasteHeatConsumptions = new Dictionary<ISyncResourceModule, ProductionConsumption>();
                consumptions.Add(WASTEHEAT_DEFINITION.id, wasteHeatConsumptions);
            }

            foreach (KeyValuePair<ISyncResourceModule, double> entry in GetWasteHeatConsumptionPerSecond())
            {
                wasteHeatConsumptions.Add(entry.Key, new ProductionConsumption(entry.Value, Double.NaN));
            }
        }

        private Dictionary<string, ProductionConsumption> GetSummary(int resourceId, Dictionary<ISyncResourceModule, ProductionConsumption> input)
        {
            Dictionary<string, ProductionConsumption> summary = new Dictionary<string, ProductionConsumption>();
            var groupedPowerDraws = input.GroupBy(entry => entry.Key.GetResourceManagerDisplayName());

            foreach (var group in groupedPowerDraws)
            {
                double sumOfMax = group.Sum(value => value.Value.Max);
                double sumOfCurrent = group.Sum(value => value.Value.Current);
                double sumRatio = 100.0d * sumOfCurrent / sumOfMax;

                string name = group.Key;
                var count = group.Count();
                Debug.LogError(String.Format("Grouping {0}, count {1}", name, count));
                if (count > 1)
                    name = String.Format("{0} {1}", count, name);
                if (resourceId == MEGAJOULES_DEFINITION.id && sumRatio < 99.5)
                    name = String.Format("{0} {1}%", name, sumRatio.ToString("0"));

                summary.Add(name, new ProductionConsumption(sumOfCurrent, sumOfMax));
            }
            return summary;
        }

        private void CommitSnapshots()
        {
            foreach (ResourceSnapshot snapshot in snapshots.Values)
            {
                snapshot.Commit();
            }

            foreach (Dictionary<ISyncResourceModule, PtpSnapshot> snapshots in ptpSnapshots.Values)
            {
                foreach (PtpSnapshot snapshot in snapshots.Values)
                {
                    snapshot.Commit();
                }
            }
        }

        private void NotifyAllModules()
        {
            foreach (ISyncResourceModule module in processes.Keys)
            {
                module.Notify(processes[module]);
            }
        }

        private Dictionary<ISyncResourceModule, ProductionConsumption> GetProductionPerSecond(int resourceId)
        {
            Dictionary<ISyncResourceModule, ProductionConsumption> result = new Dictionary<ISyncResourceModule, ProductionConsumption>();
            foreach (List<ConversionProcess> processesList in processes.Values)
            {
                foreach (ConversionProcess process in processesList)
                {
                    double current;
                    double max;
                    process.GetProductionPerSecond(resourceId, out current, out max);

                    if (max < Double.Epsilon) continue;

                    ProductionConsumption entry;
                    if (!result.TryGetValue(process.Module, out entry))
                    {
                        entry = new ProductionConsumption(current, max);
                        result.Add(process.Module, entry);
                    }
                    else
                    {
                        Debug.Log("Duplicate Entry in Productions for " + process.Module.GetResourceManagerDisplayName() + " SHOULD NOT HAPPEN");
                        entry.Current += current;
                        entry.Max += max;
                    }
                    
                }
            }
            return result;
        }

        private Dictionary<ISyncResourceModule, ProductionConsumption> GetConsumptionPerSecond(int resourceId)
        {
            Dictionary<ISyncResourceModule, ProductionConsumption> result = new Dictionary<ISyncResourceModule, ProductionConsumption>();
            foreach (List<ConversionProcess> processesList in processes.Values)
            {
                foreach (ConversionProcess process in processesList)
                {
                    double current;
                    double max;
                    process.GetConsumptionPerSecond(resourceId, out current, out max);

                    if (max < Double.Epsilon) continue;

                    result.Add(process.Module, new ProductionConsumption(current, max));
                }
            }
            return result;
        }

        private Dictionary<ISyncResourceModule, double> GetWasteHeatConsumptionPerSecond()
        {
            WasteHeatSnapshot wasteHeatSnapshot = GetResourceSnapshot(WASTEHEAT_DEFINITION.id) as WasteHeatSnapshot;
            if (wasteHeatSnapshot != null)
            {
                return wasteHeatSnapshot.GetRadiatorsOutput();
            }
            return new Dictionary<ISyncResourceModule, double>();
        }

        public void ShowWindow()
        {
            foreach (int resourceId in resourceIdToWindowId.Keys)
            {
                renderWindow[resourceId] = true;
            }
        }

        public void HideWindow()
        {
            foreach (int resourceId in resourceIdToWindowId.Keys)
            {
                renderWindow[resourceId] = false;
            }
        }

        public Boolean ProduceGUI(int resourceId)
        {
            if (resourceId == MEGAJOULES_DEFINITION.id ||
                resourceId == THERMAL_POWER_DEFINITION.id ||
                resourceId == CHARGED_PARTICLES_DEFINITION.id ||
                resourceId == WASTEHEAT_DEFINITION.id)
            {
                return true;
            }
            return false;
        }

        public Rect GetWindowPosition(int resourceId)
        {
            int xPos = 0;
            int yPos = 0;

            if (resourceId == MEGAJOULES_DEFINITION.id)
            {
                xPos = 50;
                yPos = 50;
            }
            else if (resourceId == THERMAL_POWER_DEFINITION.id)
            {
                xPos = 600;
                yPos = 50;
            }
            else if (resourceId == CHARGED_PARTICLES_DEFINITION.id)
            {
                xPos = 50;
                yPos = 600;
            }
            else if (resourceId == WASTEHEAT_DEFINITION.id)
            {
                xPos = 600;
                yPos = 600;
            }

            return new Rect(xPos, yPos, labelWidth + valueWidth + priorityWidth, 50);
        }

        public int GetWindowId(int resourceId)
        {
            return new System.Random(resourceId / 2).Next(int.MinValue, int.MaxValue);
        }

        public void OnGUI()
        {
            if (vessel == FlightGlobals.ActiveVessel)
            {
                HashSet<int> resourceIds = new HashSet<int>(snapshots.Keys);
                resourceIds.UnionWith(ptpSnapshots.Keys);
                foreach (int resourceId in resourceIds)
                {
                    if (renderWindow.ContainsKey(resourceId) && renderWindow[resourceId])
                    {
                        string title = PartResourceLibrary.Instance.GetDefinition(resourceId).name + " Synchronous Management Display";
                        windowPositions[resourceId] = GUILayout.Window(resourceIdToWindowId[resourceId], windowPositions[resourceId], DoWindow, title);
                    }
                }
            }
        }

        public void PrepareGUIElements()
        {
            if (leftBoldLabel == null)
            {
                leftBoldLabel = new GUIStyle(GUI.skin.label);
                leftBoldLabel.fontStyle = FontStyle.Bold;
                leftBoldLabel.font = PluginHelper.MainFont;
            }

            if (rightBoldLabel == null)
            {
                rightBoldLabel = new GUIStyle(GUI.skin.label);
                rightBoldLabel.fontStyle = FontStyle.Bold;
                rightBoldLabel.font = PluginHelper.MainFont;
                rightBoldLabel.alignment = TextAnchor.MiddleRight;
            }

            if (greenLabel == null)
            {
                greenLabel = new GUIStyle(GUI.skin.label);
                greenLabel.normal.textColor = Color.green;
                greenLabel.font = PluginHelper.MainFont;
                greenLabel.alignment = TextAnchor.MiddleRight;
            }

            if (redLabel == null)
            {
                redLabel = new GUIStyle(GUI.skin.label);
                redLabel.normal.textColor = Color.red;
                redLabel.font = PluginHelper.MainFont;
                redLabel.alignment = TextAnchor.MiddleRight;
            }

            if (leftAlignedLabel == null)
            {
                leftAlignedLabel = new GUIStyle(GUI.skin.label);
                leftAlignedLabel.fontStyle = FontStyle.Normal;
                leftAlignedLabel.font = PluginHelper.MainFont;
            }

            if (rightAlignedLabel == null)
            {
                rightAlignedLabel = new GUIStyle(GUI.skin.label);
                rightAlignedLabel.fontStyle = FontStyle.Normal;
                rightAlignedLabel.font = PluginHelper.MainFont;
                rightAlignedLabel.alignment = TextAnchor.MiddleRight;
            }
        }

        public void DoWindow(int windowId)
        {
            PrepareGUIElements();
            int resourceId = windowIdToResourceId[windowId];
            string resourceName = PartResourceLibrary.Instance.GetDefinition(resourceId).name;

            if (renderWindow[resourceId] && GUI.Button(new Rect(windowPositions[resourceId].width - 20, 2, 18, 18), "x"))
                renderWindow[resourceId] = false;

            GUILayout.Space(2);
            GUILayout.BeginVertical();

            double production = 0;
            double maxProduction = 0;
            if (productions.ContainsKey(resourceId))
            {
                foreach (ProductionConsumption entry in productions[resourceId].Values)
                {
                    production += entry.Current;
                    maxProduction += entry.Max;
                }
            }

            double consumption = 0;
            double maxConsumption = 0;
            if (consumptions.ContainsKey(resourceId))
            {
                foreach (ProductionConsumption entry in consumptions[resourceId].Values)
                {
                    consumption += entry.Current;
                    maxConsumption += entry.Max;
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Theoretical Supply", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(resourceId, maxProduction), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Current Supply", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(resourceId, production), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Current Distribution", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label((100 * production / maxProduction).ToString("0.000") + "%", rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Power Demand", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(resourceId, consumption), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Producer Component", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Supply", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.Label("Max", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.EndHorizontal();

            if (productions.ContainsKey(resourceId))
            {
                Dictionary<string, ProductionConsumption> summary = GetSummary(resourceId, productions[resourceId]);

                foreach (var entry in summary.OrderByDescending(entry => entry.Value.Current))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(entry.Key, leftAlignedLabel, GUILayout.ExpandWidth(true));
                    GUILayout.Label(GetUnitFormatString(resourceId, entry.Value.Current), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                    GUILayout.Label(GetUnitFormatString(resourceId, entry.Value.Max), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Consumer Component", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Demand", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.Label("Max", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(priorityWidth));
            //GUILayout.Label("Rank", right_bold_label, GUILayout.ExpandWidth(false), GUILayout.MinWidth(priorityWidth));
            GUILayout.EndHorizontal();

            if (consumptions.ContainsKey(resourceId))
            {
                Dictionary<string, ProductionConsumption> summary = GetSummary(resourceId, consumptions[resourceId]);

                foreach (var entry in summary.OrderByDescending(entry => entry.Value.Current))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(entry.Key, leftAlignedLabel, GUILayout.ExpandWidth(true));
                    GUILayout.Label(GetUnitFormatString(resourceId, entry.Value.Current), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                    GUILayout.Label(GetUnitFormatString(resourceId, entry.Value.Max), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                    GUILayout.EndHorizontal();
                }
            }

            //if (resourceName == ResourceManager.FNRESOURCE_MEGAJOULES)
            //{
            //    GUILayout.BeginHorizontal();
            //    GUILayout.Label("DC Electrical System", leftAlignedLabel, GUILayout.ExpandWidth(true));
            //    GUILayout.Label(GetUnitFormatString(stored_current_charge_demand), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            //    GUILayout.Label("0", rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(priorityWidth));
            //    GUILayout.EndHorizontal();
            //}

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        protected string GetUnit(int resourceId)
        {
            if (resourceId == MEGAJOULES_DEFINITION.id ||
                resourceId == THERMAL_POWER_DEFINITION.id ||
                resourceId == CHARGED_PARTICLES_DEFINITION.id ||
                resourceId == WASTEHEAT_DEFINITION.id)
            {
                return "W";
            }
            return "U";
        }

        protected int GetUnitMultiplier(int resourceId)
        {
            if (resourceId == MEGAJOULES_DEFINITION.id ||
                resourceId == THERMAL_POWER_DEFINITION.id ||
                resourceId == CHARGED_PARTICLES_DEFINITION.id ||
                resourceId == WASTEHEAT_DEFINITION.id)
            {
                return 1_000_000;
            }
            return 1;
        }

        protected string GetPrefix(ref double amount)
        {
            if (Math.Abs(amount) >= 1e+9)
            {
                amount /= 1e+9;
                return "G";
            }
            else if (Math.Abs(amount) >= 1e+6)
            {
                amount /= 1e+6;
                return "M";
            }
            else if (Math.Abs(amount) >= 1e+3)
            {
                amount /= 1e+3;
                return "K";
            }
            else
            {
                return "";
            }
        }

        protected string GetUnitFormatString(int resourceId, double amount)
        {
            string unit = Double.IsNaN(amount) || Double.IsInfinity(amount) ? "" : GetUnit(resourceId);
            int multiplier = GetUnitMultiplier(resourceId);

            amount *= multiplier;

            string prefix = GetPrefix(ref amount);

            if (Math.Abs(amount) > 20)
                return (amount).ToString("0.0") + " " + prefix + unit;
            else
                return (amount).ToString("0.00") + " " + prefix + unit;
        }
    }
}
