using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin
{
    public class ResourceSnapshot
    {
        public virtual double CurrentAmount
        {
            get
            {
                return storedAmount + changedAmount;
            }
        }
        public virtual double StorageLeft
        {
            get
            {
                return maxAmount - (storedAmount + changedAmount);
            }
        }

        protected double storedAmount;
        protected double maxAmount;
        protected double changedAmount;

        protected readonly string resourceName;
        protected readonly int resourceId;
        protected readonly Vessel vessel;

        public ResourceSnapshot(Vessel vessel, int resourceId)
            : this(vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name) { }

        public ResourceSnapshot(Vessel vessel, string resourceName)
            : this(vessel, PartResourceLibrary.Instance.GetDefinition(resourceName).id, resourceName) { }

        public ResourceSnapshot(Vessel vessel, int resourceId, string resourceName)
        {
            this.vessel = vessel;
            this.resourceId = resourceId;
            this.resourceName = resourceName;
            Reinitialize();
        }

        public void Produce(double amount)
        {
            changedAmount += amount;
        }

        public void Consume(double amount)
        {
            changedAmount -= amount;
        }

        public virtual double GetStorageRatio()
        {
            return maxAmount > Double.Epsilon ? (CurrentAmount / maxAmount) : 0.0d;
        }

        protected virtual void Reinitialize()
        {
            changedAmount = 0;
            vessel.GetConnectedResourceTotals(resourceId, out this.storedAmount, out this.maxAmount);
        }

        public virtual void Commit()
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

            Reinitialize();
        }

        protected virtual void RequestResource()
        {
            double provided = -vessel.Parts.FirstOrDefault().RequestResource(resourceId, -changedAmount, ResourceFlowMode.ALL_VESSEL_BALANCE);

            if (changedAmount < 0 && Math.Abs(provided - changedAmount) > 0.0001)
            {
                Debug.LogError("Requested more resource than the vessel was able to provide!");
                Debug.LogError(String.Format("resourceName = {0}, provided = {1}, changedAmount = {2}", resourceName, provided, changedAmount));
            }
        }
    }

    public class WasteHeatSnapshot : ResourceSnapshot
    {
        private static readonly double TIME_TICK_RATIO = 0.10d;

        public override double StorageLeft
        {
            get
            {
                return double.MaxValue;
            }
        }

        private readonly List<FNRadiator> radiators;
        private double alreadyGeneratedHeat;
        // Consumed = Radiated + Convected
        private double alreadyConsumedHeat;

        public WasteHeatSnapshot(Vessel vessel, int resourceId)
            : this(vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name) { }

        public WasteHeatSnapshot(Vessel vessel, string resourceName)
            : this(vessel, PartResourceLibrary.Instance.GetDefinition(resourceName).id, resourceName) { }

        public WasteHeatSnapshot(Vessel vessel, int resourceId, string resourceName) : base(vessel, resourceId, resourceName)
        {
            radiators = new List<FNRadiator>();
        }

        public override double GetStorageRatio()
        {
            double ratio = maxAmount > Double.Epsilon ? ((storedAmount + alreadyGeneratedHeat - alreadyConsumedHeat) / maxAmount) : 0.0d;
            return Math.Max(0, Math.Min(1, ratio));
        }

        public void RegisterRadiator(FNRadiator radiator)
        {
            radiators.Add(radiator);
        }

        public void DeRegisterRadiator(FNRadiator radiator)
        {
            radiators.Remove(radiator);
        }

        public override void Commit()
        {
            if (!CheatOptions.IgnoreMaxTemperature)
            {
                int timeTicks = Math.Max(1, (int)Math.Ceiling(Math.Abs(changedAmount) / (maxAmount * TIME_TICK_RATIO)));
                double warpTick = ((double)TimeWarp.fixedDeltaTime) / timeTicks;

                double tickGenerated = changedAmount / timeTicks;
                for (int tick = 0; tick < timeTicks; tick++)
                {
                    double tickConsumed = 0.0d;
                    radiators.ForEach(radiator => tickConsumed += warpTick * radiator.GetConsumedWasteHeatPerSecond());

                    alreadyGeneratedHeat += tickGenerated;
                    alreadyConsumedHeat += tickConsumed;
                }

                changedAmount = alreadyGeneratedHeat - alreadyConsumedHeat;

                base.Commit();

                alreadyGeneratedHeat = 0;
                alreadyConsumedHeat = 0;
                radiators.Clear();
            }
            else
            {
                // nothing
            }
        }
    }

    public class PTPSnapshot : ResourceSnapshot
    {
        public PTPSnapshot(Vessel vessel, int resourceId)
            : this(vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name) { }

        public PTPSnapshot(Vessel vessel, string resourceName)
            : this(vessel, PartResourceLibrary.Instance.GetDefinition(resourceName).id, resourceName) { }

        public PTPSnapshot(Vessel vessel, int resourceId, string resourceName) : base(vessel, resourceId, resourceName)
        {

        }
    }
}
