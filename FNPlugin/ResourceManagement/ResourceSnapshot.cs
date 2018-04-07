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

        private readonly int resourceId;
        private readonly Vessel vessel;

        public ResourceSnapshot(Vessel vessel, int resourceId)
        {
            this.vessel = vessel;
            this.resourceId = resourceId;
            RecalculateResourceStorage(vessel, resourceId, out this.storedAmount, out this.maxAmount);
        }

        private static void RecalculateResourceStorage(Vessel vessel, int resourceId, out double amount, out double maxAmount)
        {
            amount = 0.0d;
            maxAmount = 0.0d;
            foreach (Part part in vessel.Parts)
            {
                foreach (PartResource resource in part.Resources)
                {
                    if (resource.flowState && resource.info.id == resourceId)
                    {
                        amount += resource.amount;
                        maxAmount += resource.maxAmount;
                    }
                }
            }
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
            RecalculateResourceStorage(vessel, resourceId, out this.storedAmount, out this.maxAmount);

            if (changedAmount < 0 && Math.Abs(changedAmount) - storedAmount > Double.Epsilon)
            {
                Debug.LogError("Used more resource than there is in the tanks. No idea why!");
                Debug.LogError("changedAmount = " + changedAmount + ", storedAmount = " + storedAmount);
            }

            if (changedAmount > Double.Epsilon)
                RequestResource();
        }

        private void RequestResource()
        {
            // no idea if it will work ok
            double provided = vessel.RequestResource(vessel.Parts.FirstOrDefault(), resourceId, changedAmount, true);

            if (changedAmount < 0 && Math.Abs(provided - changedAmount) > Double.Epsilon)
            {
                Debug.LogError("Requested more resource than the vessel was able to provide!");
                Debug.LogError("provided = " + provided + ", changedAmount = " + changedAmount);
            }
        }
    }
}
