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
            return GetNewSnapshot(vessel, PartResourceLibrary.Instance.GetDefinition(resourceId).name, resourceId);
        }

        public static ResourceSnapshot GetNewSnapshot(Vessel vessel, String resourceName)
        {
            return GetNewSnapshot(vessel, resourceName, PartResourceLibrary.Instance.GetDefinition(resourceName).id);
        }

        public static ResourceSnapshot GetNewSnapshot(Vessel vessel, String resourceName, int resourceId)
        {
            // Waste Heat is quite special. It can oscillate near equilibrium. Low oscillations with low timewarp,
            // too high oscillations with high timewarp.
            if (resourceName.Equals(ResourceManager.FNRESOURCE_WASTEHEAT))
            {
                return new WasteHeatSnapshot(vessel, resourceId, resourceName);
            }
            // Thermal Power and Charged Particles are always distributed Point-to-Point
            else if (resourceName.Equals(ResourceManager.FNRESOURCE_THERMALPOWER) ||
                     resourceName.Equals(ResourceManager.FNRESOURCE_CHARGED_PARTICLES))
            {
                return new PTPSnapshot(vessel, resourceId, resourceName);
            }
            else
            {
                return new ResourceSnapshot(vessel, resourceId, resourceName);
            }
        }
    }
}
