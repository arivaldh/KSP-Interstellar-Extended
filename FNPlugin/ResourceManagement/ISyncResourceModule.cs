using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FNPlugin
{
    public interface ISyncResourceModule
    {
        string GetResourceManagerDisplayName();
        void Notify(List<ConversionProcess> processes);
    }
}
