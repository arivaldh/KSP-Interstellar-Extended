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
            // Waste Heat is quite special. It can oscillate near equilibrium. Low oscillations with low timewarp,
            // too high oscillations with high timewarp.
            if (SyncVesselResourceManager.WASTEHEAT_DEFINITION.id == resourceId)
            {
                return new WasteHeatSnapshot(vessel, resourceId);
            }
            else
            {
                return new ResourceSnapshot(vessel, resourceId);
            }
        }

        public static PtpSnapshot GetNewPtpSnapshot(ISyncResourceModule producer, Vessel vessel, int resourceId)
        {
            return new PtpSnapshot(producer, vessel, resourceId);
        }
    }
}
