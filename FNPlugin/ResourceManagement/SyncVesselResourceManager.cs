using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin
{
    public class SyncVesselResourceManager
    {
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

        public SyncVesselResourceManager(Vessel vessel)
        {
            this.vessel = vessel;
            this.processes = new Dictionary<ISyncResourceModule, List<ConversionProcess>>();
            this.snapshots = new Dictionary<int, ResourceSnapshot>();
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
    }
}
