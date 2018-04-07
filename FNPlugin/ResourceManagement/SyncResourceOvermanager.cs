using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin.Powermanagement
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, new[] { GameScenes.FLIGHT })]
    class SyncResourceOvermanager : ScenarioModule
    {
        private bool initialized;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (!initialized)
            {
                initialized = true;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            // stub, needs to remember, might be useful someday
            base.OnSave(node);
        }

        public override void OnAwake()
        {
            // stub, needs to remember, might be useful someday
            base.OnAwake();
        }

        public void FixedUpdate()
        {
            SyncVesselResourceManager.SynchronizeAll();
        }

        public void OnGUI()
        {
            // stub, needs to remember, might be useful someday
        }
    }
}
