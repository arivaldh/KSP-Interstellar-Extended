﻿using System;
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

            public Entry(string resourceName, double amount, bool dumpExcess = false)
                : this(resourceName, PartResourceLibrary.Instance.GetDefinition(resourceName).id, amount, dumpExcess) { }

            public Entry(int resourceId, double amount, bool dumpExcess = false)
                : this(PartResourceLibrary.Instance.GetDefinition(resourceId).name, resourceId, amount, dumpExcess) { }

            public Entry(string resourceName, int resourceId, double amount, bool dumpExcess = false)
            {
                this.ResourceName = resourceName;
                this.ResourceId = resourceId;
                this.Amount = amount;
                this.DumpExcess = dumpExcess;
            }

            public override string ToString()
            {
                return String.Format("Entry[{0}, {1}, {2}, {3}]", ResourceName, ResourceId, Amount, DumpExcess);
            }
        }

        public class PerSecondEntry : Entry
        {
            public PerSecondEntry(string resourceName, double amount, bool dumpExcess = false)
                : this(resourceName, PartResourceLibrary.Instance.GetDefinition(resourceName).id, amount, dumpExcess) { }

            public PerSecondEntry(int resourceId, double amount, bool dumpExcess = false)
                : this(PartResourceLibrary.Instance.GetDefinition(resourceId).name, resourceId, amount, dumpExcess) { }

            public PerSecondEntry(string resourceName, int resourceId, double amount, bool dumpExcess = false)
                : base(resourceName, resourceId, amount * TimeWarp.fixedDeltaTime, dumpExcess) { }
        }

        public class ProcessBuilder : Object
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

            public ProcessBuilder AddInputPerSecond(string resourceName, double amount, bool dumpExcess = false)
            {
                return AddInput(new PerSecondEntry(resourceName, amount, dumpExcess));
            }

            public ProcessBuilder AddInputPerSecond(int resourceId, double amount, bool dumpExcess = false)
            {
                return AddInput(new PerSecondEntry(resourceId, amount, dumpExcess));
            }

            public ProcessBuilder AddInputPerSecond(string resourceName, int resourceId, double amount, bool dumpExcess = false)
            {
                return AddInput(new PerSecondEntry(resourceName, resourceId, amount, dumpExcess));
            }

            public ProcessBuilder AddInput(Entry entry)
            {
                inputs.Add(entry);
                return this;
            }

            public ProcessBuilder AddOutputPerSecond(string resourceName, double amount, bool dumpExcess = false)
            {
                return AddOutput(new PerSecondEntry(resourceName, amount, dumpExcess));
            }

            public ProcessBuilder AddOutputPerSecond(int resourceId, double amount, bool dumpExcess = false)
            {
                return AddOutput(new PerSecondEntry(resourceId, amount, dumpExcess));
            }

            public ProcessBuilder AddOutputPerSecond(string resourceName, int resourceId, double amount, bool dumpExcess = false)
            {
                return AddOutput(new PerSecondEntry(resourceName, resourceId, amount, dumpExcess));
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
        private readonly List<Entry> inputs;
        private readonly List<Entry> outputs;
        private readonly ISyncResourceModule module;

        protected ConversionProcess(ISyncResourceModule module, List<Entry> inputs, List<Entry> outputs)
        {
            this.FractionToProcess = 1.0d;
            this.module = module;
            this.inputs = inputs;
            this.outputs = outputs;
        }

        public bool Run(SyncVesselResourceManager manager)
        {
            if (FractionToProcess < Double.Epsilon) return false;

            // lowest ratio of requested resource to it's stored resource
            double minInputRatio = 1.0d;
            foreach (Entry entry in inputs)
            {
                double entryRatio = manager.GetResourceSnapshot(entry.ResourceId).CurrentAmount / entry.Amount;
                if (entryRatio < minInputRatio)
                    minInputRatio = entryRatio;
            }

            double minOutputRatio = 1.0d;
            foreach (PerSecondEntry entry in outputs)
            {
                double entryRatio = manager.GetResourceSnapshot(entry.ResourceId).StorageLeft / entry.Amount;
                if (entryRatio < minOutputRatio)
                    minOutputRatio = entryRatio;
            }

            // how much can we proceed with the convertion
            double ratio = Math.Min(FractionToProcess, Math.Min(minOutputRatio, minInputRatio));

            inputs.ForEach(entry => manager.GetResourceSnapshot(entry.ResourceId).Consume(entry.Amount * ratio));
            outputs.ForEach(entry => manager.GetResourceSnapshot(entry.ResourceId).Produce(entry.Amount * ratio));

            FractionToProcess -= ratio;

            return ratio >= Double.Epsilon;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            //builder.AppendFormat("Process for Module {0} fractionToProcess={1}", this.module.GetResourceManagerDisplayName(), this.FractionToProcess);
            builder.Append(toStringInputs());
            builder.Append(toStringOutputs());
            return builder.ToString();
        }

        public string toStringInputs()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("inputs: \n");
            inputs.ForEach(entry => builder.Append(entry.ToString() + "\n"));
            return builder.ToString();
        }

        public string toStringOutputs()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("outputs: \n");
            outputs.ForEach(entry => builder.Append(entry.ToString() + "\n"));
            return builder.ToString();
        }
    }
}
