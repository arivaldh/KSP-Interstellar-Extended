using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FNPlugin
{
    public class ConversionProcess
    {
        public class Entry
        {
            public string ResourceName { get; protected set; }
            public int ResourceId { get; protected set; }
            public double Amount { get; protected set; }
            public bool DumpExcess { get; protected set; }
            public bool IsVirtual { get; protected set; }

            public Entry(string resourceName, double amount, bool dumpExcess = false, bool isVirtual = false)
                : this(resourceName, PartResourceLibrary.Instance.GetDefinition(resourceName).id, amount, dumpExcess, isVirtual) { }

            public Entry(int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
                : this(PartResourceLibrary.Instance.GetDefinition(resourceId).name, resourceId, amount, dumpExcess, isVirtual) { }

            public Entry(string resourceName, int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
            {
                this.ResourceName = resourceName;
                this.ResourceId = resourceId;
                this.Amount = amount;
                this.DumpExcess = dumpExcess;
                this.IsVirtual = isVirtual;
            }

            public override string ToString()
            {
                return String.Format("Entry[{0}, {1}, {2}, {3}]", ResourceName, Amount, DumpExcess, IsVirtual);
            }
        }

        public class PerSecondEntry : Entry
        {
            public PerSecondEntry(string resourceName, double amount, bool dumpExcess = false, bool isVirtual = false)
                : this(resourceName, PartResourceLibrary.Instance.GetDefinition(resourceName).id, amount, dumpExcess, isVirtual) { }

            public PerSecondEntry(int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
                : this(PartResourceLibrary.Instance.GetDefinition(resourceId).name, resourceId, amount, dumpExcess, isVirtual) { }

            public PerSecondEntry(string resourceName, int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
                : base(resourceName, resourceId, amount * TimeWarp.fixedDeltaTime, dumpExcess, isVirtual) { }
        }

        public class ProcessBuilder
        {
            private List<Entry> inputs;
            private List<Entry> outputs;
            private ISyncResourceModule module;

            public ProcessBuilder()
            {
                inputs = new List<Entry>();
                outputs = new List<Entry>();
            }

            public ConversionProcess Build()
            {
                return new ConversionProcess(module, inputs, outputs);
            }

            public ProcessBuilder Module(ISyncResourceModule module)
            {
                this.module = module;
                return this;
            }

            public ProcessBuilder AddInputPerSecond(string resourceName, double amount)
            {
                return AddInput(new PerSecondEntry(resourceName, amount, false, false));
            }

            public ProcessBuilder AddInputPerSecond(int resourceId, double amount)
            {
                return AddInput(new PerSecondEntry(resourceId, amount, false, false));
            }

            public ProcessBuilder AddInputPerSecond(string resourceName, int resourceId, double amount)
            {
                return AddInput(new PerSecondEntry(resourceName, resourceId, amount, false, false));
            }

            public ProcessBuilder AddInput(Entry entry)
            {
                inputs.Add(entry);
                return this;
            }

            public ProcessBuilder AddOutputPerSecond(string resourceName, double amount, bool dumpExcess = false, bool isVirtual = false)
            {
                return AddOutput(new PerSecondEntry(resourceName, amount, dumpExcess, isVirtual));
            }

            public ProcessBuilder AddOutputPerSecond(int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
            {
                return AddOutput(new PerSecondEntry(resourceId, amount, dumpExcess, isVirtual));
            }

            public ProcessBuilder AddOutputPerSecond(string resourceName, int resourceId, double amount, bool dumpExcess = false, bool isVirtual = false)
            {
                return AddOutput(new PerSecondEntry(resourceName, resourceId, amount, dumpExcess, isVirtual));
            }

            public ProcessBuilder AddOutput(Entry entry)
            {
                outputs.Add(entry);
                return this;
            }
        }

        public static ProcessBuilder Builder()
        {
            return new ProcessBuilder();
        }

        public double FractionToProcess { get; private set;  }
        public ISyncResourceModule Module { get; private set; }

        private readonly List<Entry> inputs;
        private readonly List<Entry> outputs;

        private readonly bool anyOutputVirtual;

        protected ConversionProcess(ISyncResourceModule module, List<Entry> inputs, List<Entry> outputs)
        {
            this.FractionToProcess = 1.0d;
            this.Module = module;
            this.inputs = inputs;
            this.outputs = outputs;

            this.anyOutputVirtual = IsAnyVirtual(outputs);
        }

        public void GetProductionPerSecond(int resourceId, out double current, out double max)
        {
            current = 0;
            max = 0;
            foreach (Entry entry in outputs)
            {
                if (entry.ResourceId == resourceId)
                {
                    current += entry.Amount * (1 - FractionToProcess);
                    max += entry.Amount;
                }
            }
            current /= TimeWarp.fixedDeltaTime;
            max /= TimeWarp.fixedDeltaTime;
        }

        public void GetConsumptionPerSecond(int resourceId, out double current, out double max)
        {
            current = 0;
            max = 0;
            foreach (Entry entry in inputs)
            {
                if (entry.ResourceId == resourceId)
                {
                    current += entry.Amount * (1 - FractionToProcess);
                    max += entry.Amount;
                }
            }
            current /= TimeWarp.fixedDeltaTime;
            max /= TimeWarp.fixedDeltaTime;
        }

        public double GetProduction(string resourceName)
        {
            return GetProduction(PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public double GetProduction(int resourceId)
        {
            double production = 0;
            foreach (Entry entry in outputs)
            {
                if (entry.ResourceId == resourceId)
                {
                    production += entry.Amount * (1 - FractionToProcess);
                }
            }
            return production;
        }

        public double GetConsumption(string resourceName)
        {
            return GetConsumption(PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public double GetConsumption(int resourceId)
        {
            double consumption = 0;
            foreach (Entry entry in inputs)
            {
                if (entry.ResourceId == resourceId)
                {
                    consumption += entry.Amount * (1 - FractionToProcess);
                }
            }
            return consumption;
        }

        public bool Run(SyncVesselResourceManager manager)
        {
            if (FractionToProcess < Double.Epsilon) return false;

            // lowest ratio of requested resource to it's stored value
            double minInputRatio = GetMinInputRatio(manager);
            // lowest ratio of produced resource to it's available storage
            double minOutputRatio = GetMinOutputRatio(manager);

            // how much can we proceed with the convertion
            double ratio = Math.Min(FractionToProcess, Math.Min(minOutputRatio, minInputRatio));

            inputs.ForEach(entry => manager.GetResourceSnapshot(this.Module, entry.ResourceId).Consume(entry.Amount * ratio));
            outputs.ForEach(entry => manager.GetResourceSnapshot(this.Module, entry.ResourceId).Produce(entry.Amount * ratio));

            FractionToProcess -= ratio;

            return ratio >= Double.Epsilon;
        }

        private double GetMinInputRatio(SyncVesselResourceManager manager)
        {
            double minInputRatio = 1.0d;
            foreach (Entry entry in inputs)
            {
                double entryRatio = manager.GetResourceSnapshot(this.Module, entry.ResourceId).CurrentAmount / entry.Amount;
                if (entryRatio < minInputRatio)
                    minInputRatio = entryRatio;
            }
            return minInputRatio;
        }

        private double GetMinOutputRatio(SyncVesselResourceManager manager)
        {
            double virtualMinOutputRatio = GetMinVirtualRatio(manager);
            double normalMinOutputRatio = 1.0d;
            foreach (Entry entry in outputs)
            {
                double entryRatio = manager.GetResourceSnapshot(this.Module, entry.ResourceId).StorageLeft / entry.Amount;
                if (!entry.IsVirtual && entryRatio >= 0 && entryRatio < normalMinOutputRatio && !entry.DumpExcess)
                {
                    normalMinOutputRatio = entryRatio;
                }
            }
            return Math.Min(virtualMinOutputRatio, normalMinOutputRatio);
        }

        private double GetMinVirtualRatio(SyncVesselResourceManager manager)
        {
            if (anyOutputVirtual)
            {
                double virtualMinOutputRatio = 0.0d;
                foreach (Entry entry in outputs)
                {
                    double entryRatio = manager.GetResourceSnapshot(this.Module, entry.ResourceId).StorageLeft / entry.Amount;
                    if (entry.IsVirtual && entryRatio > virtualMinOutputRatio)
                    {
                        virtualMinOutputRatio = entryRatio;
                    }
                }
                return virtualMinOutputRatio;
            }
            else
            {
                return 1.0d;
            }
        }

        private static bool IsAnyVirtual(List<Entry> entries)
        {
            foreach (Entry entry in entries)
            {
                if (entry.IsVirtual)
                    return true;
            }
            return false;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendFormat("Process for Module {0} fractionToProcess={1}\n", this.Module.GetResourceManagerDisplayName(), this.FractionToProcess);
            builder.Append(InputsToString());
            builder.Append(OutputsToString());
            return builder.ToString();
        }

        public string InputsToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("inputs: \n");
            inputs.ForEach(entry => builder.Append(entry.ToString() + "\n"));
            return builder.ToString();
        }

        public string OutputsToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("outputs: \n");
            outputs.ForEach(entry => builder.Append(entry.ToString() + "\n"));
            return builder.ToString();
        }
    }
}
