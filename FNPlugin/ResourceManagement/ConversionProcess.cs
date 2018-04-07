using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FNPlugin
{
    public class ConversionProcess
    {
        public class Entry : Object
        {
            public int ResourceId { get; private set; }
            public double Amount { get; private set; }
            public bool DumpExcess { get; private set; }

            public Entry(string resourceName, double amount, bool dumpExcess = false)
                : this(PartResourceLibrary.Instance.GetDefinition(resourceName).id, amount, dumpExcess) { }

            public Entry(int resourceId, double amount, bool dumpExcess = false)
            {
                this.ResourceId = resourceId;
                this.Amount = amount;
                this.DumpExcess = dumpExcess;
            }

            public override string ToString()
            {
                return String.Format("Entry[{0}, {1}, {2}]", ResourceId, Amount, DumpExcess);
            }
        }

        public double FractionToProcess { get; private set;  }
        private List<Entry> inputs;
        private List<Entry> outputs;
        private ISyncResourceModule module;

        public ConversionProcess(ISyncResourceModule module)
        {
            this.FractionToProcess = 1.0d;
            this.module = module;
            this.inputs = new List<Entry>();
            this.outputs = new List<Entry>();
        }

        public void AddInput(Entry entry)
        {
            inputs.Add(entry);
        }

        public void AddOutput(Entry entry)
        {
            outputs.Add(entry);
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
            foreach (Entry entry in outputs)
            {
                double entryRatio = manager.GetResourceSnapshot(entry.ResourceId).StorageLeft / entry.Amount;
                if (entryRatio < minOutputRatio)
                    minOutputRatio = entryRatio;
            }

            // how much can we proceed with the convertion
            double ratio = Math.Min(minOutputRatio, minInputRatio);
            ratio = Math.Min(FractionToProcess, ratio);

            inputs.ForEach(entry => manager.GetResourceSnapshot(entry.ResourceId).Consume(entry.Amount * ratio));
            outputs.ForEach(entry => manager.GetResourceSnapshot(entry.ResourceId).Consume(entry.Amount * ratio));

            FractionToProcess -= ratio;

            return ratio >= Double.Epsilon;
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
