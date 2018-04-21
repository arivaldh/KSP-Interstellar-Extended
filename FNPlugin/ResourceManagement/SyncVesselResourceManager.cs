﻿using System;
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
        private Dictionary<int, ResourceSnapshot> snapshots;

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

        public static void RegisterRadiator(FNRadiator radiator)
        {
            ResourceSnapshot snapshot = SyncVesselResourceManager.GetSyncVesselResourceManager(radiator.vessel).GetResourceSnapshot(ResourceManager.FNRESOURCE_WASTEHEAT);
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
                if (ProduceGUI(resourceId))
                {
                    windowPositions.Add(resourceId, GetWindowPosition(resourceId));
                    int windowId = GetWindowId(resourceId);
                    windowIdToResourceId.Add(windowId, resourceId);
                    resourceIdToWindowId.Add(resourceId, windowId);
                    renderWindow.Add(resourceId, false);
                }
            }

            return snapshot;
        }

        public static void AddProcess(PartModule module, ISyncResourceModule callback, ConversionProcess process)
        {
            SyncVesselResourceManager manager = GetSyncVesselResourceManager(module.vessel);
            manager.InsertConversionProcess(callback, process);
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

            foreach (ResourceSnapshot snapshot in snapshots.Values)
            {
                snapshot.Commit();
            }

            foreach (ISyncResourceModule module in processes.Keys)
            {
                module.Notify(processes[module]);
            }

            processes.Clear();
        }

        private Dictionary<string, ProductionConsumption> GetProduction(int resourceId)
        {
            Dictionary<string, ProductionConsumption> result = new Dictionary<string, ProductionConsumption>();
            foreach (List<ConversionProcess> processesList in processes.Values)
            {
                foreach (ConversionProcess process in processesList)
                {
                    string name = process.module.GetResourceManagerDisplayName();
                    double current;
                    double max;
                    process.GetProduction(resourceId, out current, out max);

                    if (max < Double.Epsilon) continue;

                    if (result.ContainsKey(name))
                    {
                        result[name].Current += current;
                        result[name].Max += max;
                    }
                    else
                    {
                        result.Add(name, new ProductionConsumption(current, max));
                    }
                }
            }
            return result;
        }

        private Dictionary<string, ProductionConsumption> GetConsumption(int resourceId)
        {
            Dictionary<string, ProductionConsumption> result = new Dictionary<string, ProductionConsumption>();
            foreach (List<ConversionProcess> processesList in processes.Values)
            {
                foreach (ConversionProcess process in processesList)
                {
                    string name = process.module.GetResourceManagerDisplayName();
                    double current;
                    double max;
                    process.GetConsumption(resourceId, out current, out max);

                    if (max < Double.Epsilon) continue;

                    if (result.ContainsKey(name))
                    {
                        result[name].Current += current;
                        result[name].Max += max;
                    }
                    else
                    {
                        result.Add(name, new ProductionConsumption(current, max));
                    }
                }
            }
            return result;
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
            string resourceName = PartResourceLibrary.Instance.GetDefinition(resourceId).name;

            if (resourceName == ResourceManager.FNRESOURCE_MEGAJOULES ||
                resourceName == ResourceManager.FNRESOURCE_THERMALPOWER ||
                resourceName == ResourceManager.FNRESOURCE_CHARGED_PARTICLES || 
                resourceName == ResourceManager.FNRESOURCE_WASTEHEAT)
            {
                return true;
            }
            return false;
        }

        public Rect GetWindowPosition(int resourceId)
        {
            int xPos = 0;
            int yPos = 0;

            string resourceName = PartResourceLibrary.Instance.GetDefinition(resourceId).name;

            if (resourceName == ResourceManager.FNRESOURCE_MEGAJOULES)
            {
                xPos = 50;
                yPos = 50;
            }
            else if (resourceName == ResourceManager.FNRESOURCE_THERMALPOWER)
            {
                xPos = 600;
                yPos = 50;
            }
            else if (resourceName == ResourceManager.FNRESOURCE_CHARGED_PARTICLES)
            {
                xPos = 50;
                yPos = 600;
            }
            else if (resourceName == ResourceManager.FNRESOURCE_WASTEHEAT)
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
                foreach (int resourceId in snapshots.Keys)
                {
                    if (ProduceGUI(resourceId) && renderWindow[resourceId])
                    {
                        string title = PartResourceLibrary.Instance.GetDefinition(resourceId).name + "Synchronous Management Display";
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

            Dictionary<string, ProductionConsumption> productions = this.GetProduction(resourceId);
            double production = 0;
            double maxProduction = 0;
            foreach (ProductionConsumption entry in productions.Values)
            {
                production += entry.Current;
                maxProduction += entry.Max;
            }

            Dictionary<string, ProductionConsumption> consumptions = this.GetConsumption(resourceId);
            double consumption = 0;
            double maxConsumption = 0;
            foreach (ProductionConsumption entry in consumptions.Values)
            {
                consumption += entry.Current;
                maxConsumption += entry.Max;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Theoretical Supply", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(maxProduction), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Current Supply", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(production), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Current Distribution", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label((production / maxProduction).ToString("0.000") + "%", rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Power Demand", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(GetUnitFormatString(consumption), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            GUILayout.EndHorizontal();

            //double new_power_supply = getOverproduction();
            //double net_utilisation_supply = getDemandStableSupply();

            //GUIStyle net_poer_style = new_power_supply < -0.001 ? redLabel : greenLabel;
            //GUIStyle utilisation_style = net_utilisation_supply > 1.001 ? redLabel : greenLabel;

            //GUILayout.BeginHorizontal();
            //var new_power_label = (resourceName == ResourceManager.FNRESOURCE_WASTEHEAT) ? "Net Change" : "Net Power";
            //GUILayout.Label(new_power_label, leftBoldLabel, GUILayout.ExpandWidth(true));
            //GUILayout.Label(GetUnitFormatString(new_power_supply), net_poer_style, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            //GUILayout.EndHorizontal();

            //if (!double.IsNaN(net_utilisation_supply) && !double.IsInfinity(net_utilisation_supply))
            //{
            //    GUILayout.BeginHorizontal();
            //    GUILayout.Label("Utilisation", leftBoldLabel, GUILayout.ExpandWidth(true));
            //    GUILayout.Label((net_utilisation_supply).ToString("P3"), utilisation_style, GUILayout.ExpandWidth(false), GUILayout.MinWidth(overviewWidth));
            //    GUILayout.EndHorizontal();
            //}

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Producer Component", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Supply", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.Label("Max", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.EndHorizontal();

            foreach (var entry in productions.OrderByDescending(entry => entry.Value.Current))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Key, leftAlignedLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label(GetUnitFormatString(entry.Value.Current), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                GUILayout.Label(GetUnitFormatString(entry.Value.Max), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Consumer Component", leftBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Demand", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
            GUILayout.Label("max", rightBoldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(priorityWidth));
            //GUILayout.Label("Rank", right_bold_label, GUILayout.ExpandWidth(false), GUILayout.MinWidth(priorityWidth));
            GUILayout.EndHorizontal();

            foreach (var entry in consumptions.OrderByDescending(entry => entry.Value.Current))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Key, leftAlignedLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label(GetUnitFormatString(entry.Value.Current), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                GUILayout.Label(GetUnitFormatString(entry.Value.Max), rightAlignedLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(valueWidth));
                GUILayout.EndHorizontal();
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

        protected string GetUnitFormatString(double amount)
        {
            if (Math.Abs(amount) >= 1000)
            {
                if (Math.Abs(amount) > 20000)
                    return (amount / 1000).ToString("0.0") + " GU";
                else
                    return (amount / 1000).ToString("0.00") + " GU";
            }
            else
            {
                if (Math.Abs(amount) > 20)
                    return amount.ToString("0.0") + " MU";
                else
                {
                    if (Math.Abs(amount) >= 1)
                        return amount.ToString("0.00") + " MU";

                    else
                        return (amount * 1000).ToString("0.0") + " KU";
                }
            }
        }
    }
}
