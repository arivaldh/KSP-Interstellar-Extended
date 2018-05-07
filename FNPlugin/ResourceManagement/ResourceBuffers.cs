using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin.Extensions
{
    class ResourceBuffers
    {
        abstract public class Config
        {
            public String ResourceName { get; private set; }
            protected Part part { get; private set; }

            public Config(String resourceName)
            {
                this.ResourceName = resourceName;
            }

            protected virtual bool UpdateRequired() { return true; }

            protected abstract void UpdateBufferForce();

            public virtual void Init(Part part)
            {
                this.part = part;
            }

            public virtual void UpdateBuffer()
            {
                if (UpdateRequired())
                {
                    UpdateBufferForce();
                }
            }
        }

        public class VariableConfig : Config
        {
            public double VariableMultiplier { get; private set; }
            protected double BaseResourceMax { get; set; }
            private bool VariableChanged { get; set; }  = false;

            public VariableConfig(String resourceName) : base(resourceName)
            {
                VariableMultiplier = 1.0d;
            }

            protected virtual void RecalculateBaseResourceMax()
            {
                BaseResourceMax = VariableMultiplier;
            }

            public void ConfigureVariable(double variableMultiplier)
            {
                if (this.VariableMultiplier != variableMultiplier)
                {
                    VariableChanged = true;
                    this.VariableMultiplier = variableMultiplier;
                    RecalculateBaseResourceMax();
                }
            }

            protected override void UpdateBufferForce()
            {
                var bufferedResource = part.Resources[ResourceName];
                if (bufferedResource != null)
                {
                    var resourceRatio = Math.Max(0, Math.Min(1, bufferedResource.maxAmount > 0 ? bufferedResource.amount / bufferedResource.maxAmount : 0));
                    bufferedResource.maxAmount = Math.Max(0.0001, BaseResourceMax);
                    bufferedResource.amount = Math.Max(0, resourceRatio * bufferedResource.maxAmount);
                }
            }

            protected override bool UpdateRequired()
            {
                bool updateRequired = false;
                if (VariableChanged)
                {
                    updateRequired = true;
                    VariableChanged = false;
                }
                return updateRequired;
            }

        }

        public class TimeBasedConfig : VariableConfig
        {
            public readonly bool ClampInitialMaxAmount;
            public double ResourceMultiplier { get; private set; }
            public double BaseResourceAmount { get; private set; }

            private bool initialized = false;
            private float previousDeltaTime;

            public TimeBasedConfig(String resourceName, double resourceMultiplier = 1.0d, double baseResourceAmount = 1.0d, bool clampInitialMaxAmount = false)
                : base(resourceName)
            {
                this.ClampInitialMaxAmount = clampInitialMaxAmount;
                this.ResourceMultiplier = resourceMultiplier;
                this.BaseResourceAmount = baseResourceAmount;
                RecalculateBaseResourceMax();
            }

            protected override void RecalculateBaseResourceMax()
            {
                // calculate Resource Capacity
                this.BaseResourceMax = ResourceMultiplier * BaseResourceAmount * VariableMultiplier;
            }

            protected virtual float GetTimeMultiplier()
            {
                return HighLogic.LoadedSceneIsFlight ? TimeWarp.fixedDeltaTime: 0.02f;
            }

            protected override void UpdateBufferForce()
            {
                var bufferedResource = part.Resources[ResourceName];
                if (bufferedResource != null)
                {
                    float timeMultiplier = GetTimeMultiplier();
                    double maxWasteHeatRatio = ClampInitialMaxAmount && !initialized ? 0.95d : 1.0d;

                    var resourceRatio = Math.Max(0, Math.Min(maxWasteHeatRatio, bufferedResource.maxAmount > 0 ? bufferedResource.amount / bufferedResource.maxAmount : 0));
                    bufferedResource.maxAmount = Math.Max(0.0001, timeMultiplier * BaseResourceMax);
                    bufferedResource.amount = Math.Max(0, resourceRatio * bufferedResource.maxAmount);
                }
                initialized = true;
            }

            protected override bool UpdateRequired()
            {
                bool updateRequired = false;
                if (Math.Abs(GetTimeMultiplier() - previousDeltaTime) > float.Epsilon || base.UpdateRequired())
                {
                    updateRequired = true;
                    previousDeltaTime = TimeWarp.fixedDeltaTime;
                }
                return updateRequired;
            }
        }

        public class HighTimeWarpConfig : TimeBasedConfig
        {
            // 1_000_000 will essentially NEVER scale (unless such high timewarp is added)!
            public static readonly float SCALED_TIME_WARP = 1_000_000f;
            public static readonly float NORMAL_TIME_WARP = 0.02f;

            public HighTimeWarpConfig(String resourceName, double resourceMultiplier = 1.0d, double baseResourceAmount = 1.0d, bool clampInitialMaxAmount = false)
                : base(resourceName, resourceMultiplier, baseResourceAmount, clampInitialMaxAmount) { }

            protected override float GetTimeMultiplier()
            {
                float currentFixedDeltaTime = base.GetTimeMultiplier();
                if (currentFixedDeltaTime <= SCALED_TIME_WARP * NORMAL_TIME_WARP)
                {
                    return NORMAL_TIME_WARP;
                }
                else
                {
                    return currentFixedDeltaTime / SCALED_TIME_WARP;
                }
            }
        }

        public class MaxAmountConfig : TimeBasedConfig
        {
            public double InitialMaxAmount { get; private set; }
            public double MaxMultiplier { get; private set; }

            public MaxAmountConfig(String resourceName, double maxMultiplier)
                : base(resourceName, 1.0d, 1.0d, false)
            {
                this.MaxMultiplier = maxMultiplier;
            }

            public override void Init(Part part)
            {
                base.Init(part);
                var bufferedResource = part.Resources[ResourceName];
                if (bufferedResource != null)
                {
                    InitialMaxAmount = bufferedResource.maxAmount;
                    RecalculateBaseResourceMax();
                }
            }

            protected override void RecalculateBaseResourceMax()
            {
                // calculate Resource Capacity
                this.BaseResourceMax = InitialMaxAmount * MaxMultiplier;
            }
        }

        protected Dictionary<String, Config> resourceConfigs;
        protected Part part;

        public ResourceBuffers()
        {
            this.resourceConfigs = new Dictionary<String, Config>();
        }

        public void AddConfiguration(Config resourceConfig)
        {
            resourceConfigs.Add(resourceConfig.ResourceName, resourceConfig);
        }

        public void Init(Part part)
        {
            this.part = part;
            foreach (Config resourceConfig in resourceConfigs.Values)
            {
                resourceConfig.Init(part);
            }
            UpdateBuffers();
        }

        public void AddWasteHeatBuffer(double wasteHeatMultiplier, double unitsPerMassUnit, bool preventOverheat = false)
        {
            ResourceBuffers.TimeBasedConfig config =
                new HighTimeWarpConfig(SyncVesselResourceManager.WASTEHEAT_RESOURCE_NAME, wasteHeatMultiplier, unitsPerMassUnit, preventOverheat);
            config.ConfigureVariable(this.part.mass);
            config.Init(this.part);
            this.AddConfiguration(config);
        }

        public void UpdateVariable(String resourceName, double variableMultiplier)
        {
            Config resourceConfig = resourceConfigs[resourceName];
            if (resourceConfig != null && resourceConfig is VariableConfig)
            {
                (resourceConfig as VariableConfig).ConfigureVariable(variableMultiplier);
            }
            else
            {
                Debug.LogError("[KSPI] - Resource = " + resourceName + " doesn't have variable buffer config!");
            }
        }

        public void UpdateBuffers()
        {
            foreach (Config resourceConfig in resourceConfigs.Values)
            {
                resourceConfig.UpdateBuffer();
            }
        }
    }
}
