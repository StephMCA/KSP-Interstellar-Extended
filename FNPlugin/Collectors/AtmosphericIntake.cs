﻿using OpenResourceSystem;
using System;
using UnityEngine;

namespace FNPlugin  
{
    class AtmosphericIntake : PartModule
    {
        [KSPField(guiName = "Intake Speed", isPersistant = false, guiActive = false, guiFormat = "F3")]
        protected float _intake_speed;
        [KSPField(guiName = "Atmosphere Flow", guiUnits = "U", guiFormat = "F3", isPersistant = false, guiActive = false)]
        public double airFlow;
        [KSPField(guiName = "Atmosphere Speed", guiUnits = "M/s", guiFormat = "F3", isPersistant = false, guiActive = false)]
        public double airSpeed;
        [KSPField(guiName = "Air This Update", isPersistant = false, guiActive = false, guiFormat ="F6")]
        public double airThisUpdate;
        [KSPField(guiName = "Intake Angle", isPersistant = false, guiActive = false, guiFormat = "F3")]
        public float intakeAngle = 0;
        [KSPField(guiName = "aoaThreshold", isPersistant = false, guiActive = false, guiActiveEditor = false)]
        public float aoaThreshold = 0.1f;
        [KSPField(isPersistant = false, guiName = "Area", guiActiveEditor = false, guiActive = false)]
        public double area = 0.01f;
        [KSPField(isPersistant = false)]
        public string intakeTransformName;
        [KSPField(isPersistant = false, guiName = "maxIntakeSpeed", guiActive = false, guiActiveEditor = false)]
        public float maxIntakeSpeed = 100;
        [KSPField(isPersistant = false, guiName = "unitScalar", guiActive = false, guiActiveEditor = false)]
        public double unitScalar = 0.2f;
        [KSPField(isPersistant = false, guiName = "storesResource", guiActiveEditor = true)]
        public bool storesResource = false;
        [KSPField(isPersistant = false, guiName = "Intake Exposure", guiActiveEditor = false, guiActive = false)]
        public double intakeExposure = 0;
        [KSPField(isPersistant = false, guiName = "Trace atmo. density", guiFormat = "F3", guiActiveEditor = false, guiActive = false)]
        public double upperAtmoDensity;
        [KSPField(guiName = "Air Density", isPersistant = false, guiActive = false, guiFormat = "F3")]
        public double airDensity;
        [KSPField(guiName = "Tech Bonus", isPersistant = false, guiActive = false, guiFormat = "F3")]
        public float jetTechBonusPercentage;
        [KSPField(guiName = "Upper Atmo Fraction", isPersistant = false, guiActive = false, guiFormat = "F3")]
        public double upperAtmoFraction;

        // persistents
        [KSPField(isPersistant = true, guiName = "Air / sec", guiActiveEditor = false, guiActive = true, guiFormat = "F5" )]
        public double finalAir;

        public double startupCount;
        private float previousDeltaTime;
        private double atmosphereBuffer;

        Vector3 _intake_direction;

        PartResource intake_air_resource;
        PartResource intake_atmosphere_resource;

        private PartResourceDefinition _resourceAtmosphere;
        private ModuleResourceIntake _moduleResourceIntake;

        // this property will be accessed by the atmospheric extractor
        public double FinalAir
        {
            get { return finalAir; }
        }

        public override void OnStart(PartModule.StartState state)
        {
            if (state == StartState.Editor) return; // don't do any of this stuff in editor

            bool hasJetUpgradeTech0 = PluginHelper.HasTechRequirementOrEmpty(PluginHelper.JetUpgradeTech0);
            bool hasJetUpgradeTech1 = PluginHelper.HasTechRequirementOrEmpty(PluginHelper.JetUpgradeTech1);
            bool hasJetUpgradeTech2 = PluginHelper.HasTechRequirementOrEmpty(PluginHelper.JetUpgradeTech2);
            bool hasJetUpgradeTech3 = PluginHelper.HasTechRequirementOrEmpty(PluginHelper.JetUpgradeTech3);

            var jetTechBonus = Convert.ToInt32(hasJetUpgradeTech0) + 1.2f * Convert.ToInt32(hasJetUpgradeTech1) + 1.44f * Convert.ToInt32(hasJetUpgradeTech2) + 1.728f * Convert.ToInt32(hasJetUpgradeTech3);
            jetTechBonusPercentage = 1 + (jetTechBonus / 10.736f);

            _moduleResourceIntake = this.part.FindModuleImplementing<ModuleResourceIntake>();

            // add atmosphere buffer if needed
            intake_air_resource = part.Resources[InterstellarResourcesConfiguration.Instance.IntakeAir];

            //atmosphereBuffer = intake_air_resource.maxAmount * 50;
            atmosphereBuffer = area * unitScalar * jetTechBonusPercentage * maxIntakeSpeed * 300 ;

            if (!part.Resources.Contains(InterstellarResourcesConfiguration.Instance.IntakeAtmosphere))
            {
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("name", InterstellarResourcesConfiguration.Instance.IntakeAtmosphere);
                node.AddValue("maxAmount", atmosphereBuffer);
                part.AddResource(node);
            }
            intake_atmosphere_resource = part.Resources[InterstellarResourcesConfiguration.Instance.IntakeAtmosphere];
            _resourceAtmosphere = PartResourceLibrary.Instance.GetDefinition(InterstellarResourcesConfiguration.Instance.IntakeAtmosphere);
            _intake_speed = maxIntakeSpeed;
            _intake_direction = part.transform.up.normalized;
        }

        public void FixedUpdate()
        {
            if (vessel == null) // No vessel? No collecting
                return;

            if (!vessel.mainBody.atmosphere) // No atmosphere? No collecting
                return;

            UpdateAtmosphereBuffer();

            IntakeThatAir(TimeWarp.fixedDeltaTime); // collect intake atmosphere for the timeframe
        }

        private void UpdateAtmosphereBuffer()
        {
            if (intake_atmosphere_resource != null && atmosphereBuffer > 0 && TimeWarp.fixedDeltaTime != previousDeltaTime)
            {
                double requiredAtmosphereCapacity = atmosphereBuffer * TimeWarp.fixedDeltaTime;
                double previousAtmosphereCapacity = atmosphereBuffer * previousDeltaTime;
                double atmosphereRatio = (intake_atmosphere_resource.amount / intake_atmosphere_resource.maxAmount);

                intake_atmosphere_resource.maxAmount = requiredAtmosphereCapacity;

                intake_atmosphere_resource.amount = TimeWarp.fixedDeltaTime > previousDeltaTime
                    ? Math.Max(0, Math.Min(requiredAtmosphereCapacity, intake_atmosphere_resource.amount + requiredAtmosphereCapacity - previousAtmosphereCapacity))
                    : Math.Max(0, Math.Min(requiredAtmosphereCapacity, atmosphereRatio * requiredAtmosphereCapacity));
            }

            previousDeltaTime = TimeWarp.fixedDeltaTime;
        }


        public void IntakeThatAir(double deltaTimeInSecs)
        {
            // do not return anything when intakes are closed
            if (_moduleResourceIntake != null && !_moduleResourceIntake.intakeEnabled)
            {
                airSpeed = 0;
                airThisUpdate = 0;
                finalAir = 0;
                intakeExposure = 0;
                airFlow = 0;
                return;
            }

            airSpeed = vessel.speed + _intake_speed;
            intakeExposure = (airSpeed * unitScalar) + _intake_speed;
            intakeExposure *= area * unitScalar * jetTechBonusPercentage;
            airFlow = vessel.atmDensity * intakeExposure / _resourceAtmosphere.density;
            airThisUpdate = airFlow * TimeWarp.fixedDeltaTime;

            if (part.vessel.atmDensity < PluginHelper.MinAtmosphericAirDensity && vessel.altitude < part.vessel.mainBody.scienceValues.spaceAltitudeThreshold) // only collect when it is possible and relevant
            {
                upperAtmoFraction = Math.Max(0, (vessel.altitude / (part.vessel.mainBody.scienceValues.spaceAltitudeThreshold))); // calculate the fraction of the atmosphere
                var spaceAirDensity = PluginHelper.MinAtmosphericAirDensity * (1 - upperAtmoFraction);             // calculate the space atmospheric density
                airDensity = Math.Max(part.vessel.atmDensity, spaceAirDensity);                             // display amount of density
                upperAtmoDensity = Math.Max(0, spaceAirDensity - part.vessel.atmDensity);                   // calculate effective addition upper atmosphere density
                intakeAngle = Mathf.Clamp(Vector3.Dot(vessel.orbit.GetRelativeVel().normalized, part.transform.up.normalized), 0, 1);   // get intake angle
                var space_airFlow = intakeAngle * upperAtmoDensity * intakeExposure / _resourceAtmosphere.density; // how much of that air is our intake catching
                airThisUpdate = airThisUpdate + (space_airFlow * TimeWarp.fixedDeltaTime);                  // increase how much  air do we get per update 
            }

            if (startupCount > 10)
            {
                // take the final airThisUpdate value and assign it to the finalAir property (this will in turn get used by atmo extractor)
                finalAir = airThisUpdate / TimeWarp.fixedDeltaTime;
            }
            else
                startupCount++;

            if (!storesResource)
            {
                foreach (PartResource resource in part.Resources)
                {
                    if (resource.resourceName != _resourceAtmosphere.name)
                        continue;

                    airThisUpdate = airThisUpdate >= 0
                        ? (airThisUpdate <= resource.maxAmount
                            ? airThisUpdate
                            : resource.maxAmount)
                        : 0;
                    resource.amount = airThisUpdate;
                    break;
                }
            }
            else
            {
                part.RequestResource(_resourceAtmosphere.name, -airThisUpdate); // create the resource, finally
            }


        }
    }
}
