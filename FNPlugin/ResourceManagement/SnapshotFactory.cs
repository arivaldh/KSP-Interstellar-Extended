using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FNPlugin
{
    class SnapshotFactory
    {
        public static ResourceSnapshot GetNewSnapshot(Vessel vessel, int resourceId)
        {
            return GetNewSnapshot(vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name);
        }

        public static ResourceSnapshot GetNewSnapshot(Vessel vessel, int resourceId, String resourceName)
        {
            // Waste Heat is quite special. It can oscillate near equilibrium. Low oscillations with low timewarp,
            // too high oscillations with high timewarp.
            if (resourceName.Equals(ResourceManager.FNRESOURCE_WASTEHEAT))
            {
                return new WasteHeatSnapshot(vessel, resourceId, resourceName);
            }
            else
            {
                return new ResourceSnapshot(vessel, resourceId, resourceName);
            }
        }

        public static PtpSnapshot GetNewPtpSnapshot(ISyncResourceModule producer, Vessel vessel, int resourceId)
        {
            return GetNewPtpSnapshot(producer, vessel, resourceId, PartResourceLibrary.Instance.GetDefinition(resourceId).name);
        }


        public static PtpSnapshot GetNewPtpSnapshot(ISyncResourceModule producer, Vessel vessel, int resourceId, string resourceName)
        {
            return new PtpSnapshot(producer, vessel, resourceId, resourceName);
        }
    }
}
