using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin
{
    public class ResourceSnapshot
    {
        public double CurrentAmount
        {
            get
            {
                return storedAmount + changedAmount;
            }
        }
        public double StorageLeft
        {
            get
            {
                return maxAmount - (storedAmount + changedAmount);
            }
        }

        private double storedAmount;
        private double maxAmount;
        private double changedAmount;

        private readonly string resourceName;
        private readonly int resourceId;
        private readonly Vessel vessel;

        public ResourceSnapshot(Vessel vessel, int resourceId)
            : this(vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name) { }

        public ResourceSnapshot(Vessel vessel, string resourceName)
            : this(vessel, PartResourceLibrary.Instance.GetDefinition(resourceName).id, resourceName) { }

        public ResourceSnapshot(Vessel vessel, int resourceId, string resourceName)
        {
            this.vessel = vessel;
            this.resourceId = resourceId;
            this.resourceName = resourceName;
            vessel.GetConnectedResourceTotals(resourceId, out this.storedAmount, out this.maxAmount);
        }

        public void Produce(double amount)
        {
            changedAmount += amount;
        }

        public void Consume(double amount)
        {
            changedAmount -= amount;
        }

        public void Commit()
        {
            vessel.GetConnectedResourceTotals(resourceId, out this.storedAmount, out this.maxAmount);

            if (changedAmount < 0 && Math.Abs(changedAmount) - storedAmount > Double.Epsilon)
            {
                Debug.LogError("Used more resource than there is in the tanks. No idea why!");
                Debug.LogError(String.Format("Vessel = {0}, resourceName = {1}, changedAmount = {2}, storedAmount = {3}",
                    resourceName, vessel.name, changedAmount, storedAmount));
            }

            if (Math.Abs(changedAmount) > Double.Epsilon)
                RequestResource();
        }

        private void RequestResource()
        {
            double provided = -vessel.Parts.FirstOrDefault().RequestResource(resourceId, -changedAmount, ResourceFlowMode.ALL_VESSEL_BALANCE);
            Debug.LogError(String.Format("resourceName = {0}, storedAmount = {1}, requested = {2}, provided = {3}", resourceName, storedAmount, -changedAmount, -provided));

            if (changedAmount < 0 && Math.Abs(provided - changedAmount) > 0.0001)
            {
                Debug.LogError("Requested more resource than the vessel was able to provide!");
                Debug.LogError(String.Format("resourceName = {0}, provided = {1}, changedAmount = {2}", resourceName, provided, changedAmount));
            }
        }
    }
}
