/*
Ferram Aerospace Research v0.16.1.2 "Marangoni"
=========================
Aerodynamics model for Kerbal Space Program

Copyright 2022, Michael Ferrara, aka Ferram4

   This file is part of Ferram Aerospace Research.

   Ferram Aerospace Research is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.

   Ferram Aerospace Research is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU General Public License for more details.

   You should have received a copy of the GNU General Public License
   along with Ferram Aerospace Research.  If not, see <http://www.gnu.org/licenses/>.

   Serious thanks:		a.g., for tons of bugfixes and code-refactorings
				stupid_chris, for the RealChuteLite implementation
            			Taverius, for correcting a ton of incorrect values
				Tetryds, for finding lots of bugs and issues and not letting me get away with them, and work on example crafts
            			sarbian, for refactoring code for working with MechJeb, and the Module Manager updates
            			ialdabaoth (who is awesome), who originally created Module Manager
                        	Regex, for adding RPM support
				DaMichel, for some ferramGraph updates and some control surface-related features
            			Duxwing, for copy editing the readme

   CompatibilityChecker by Majiir, BSD 2-clause http://opensource.org/licenses/BSD-2-Clause

   Part.cfg changes powered by sarbian & ialdabaoth's ModuleManager plugin; used with permission
	http://forum.kerbalspaceprogram.com/threads/55219

   ModularFLightIntegrator by Sarbian, Starwaster and Ferram4, MIT: http://opensource.org/licenses/MIT
	http://forum.kerbalspaceprogram.com/threads/118088

   Toolbar integration powered by blizzy78's Toolbar plugin; used with permission
	http://forum.kerbalspaceprogram.com/threads/60863
 */

using System;
using System.Collections.Generic;
using ferram4;
using FerramAerospaceResearch.FARAeroComponents;
using UnityEngine;
using static GameEvents;

namespace FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation
{
    internal class DumpSim
    {
        private readonly InstantConditionSim _instantCondition;

        public DumpSim(InstantConditionSim instantConditionSim)
        {
            _instantCondition = instantConditionSim;
        }

        private readonly DumpSimInput iterationInput = new DumpSimInput();
        private List<FARAeroSection> _currentAeroSections;
        private List<FARAeroPartModule> _currentAeroModules;
        private List<FARWingAerodynamicModel> _wingAerodynamicModel;

        public double _maxCrossSectionFromBody;
        public double _bodyLength;

        private double neededCl;
        public InstantConditionSimOutput iterationOutput;

        private double alpha;
        private double beta;


        public bool Ready
        {
            get { return _currentAeroSections != null && _currentAeroModules != null && _wingAerodynamicModel != null; }
        }

        public void UpdateAeroData(
            List<FARAeroPartModule> aeroModules,
            List<FARAeroSection> aeroSections,
            VehicleAerodynamics vehicleAero,
            List<FARWingAerodynamicModel> wingAerodynamicModel
        )
        {
            _currentAeroModules = aeroModules;
            _currentAeroSections = aeroSections;
            _wingAerodynamicModel = wingAerodynamicModel;
            _maxCrossSectionFromBody = vehicleAero.MaxCrossSectionArea;
            _bodyLength = vehicleAero.Length;
        }

        public static double CalculateAccelerationDueToGravity(CelestialBody body, double alt)
        {
            double radius = body.Radius + alt;
            double mu = body.gravParameter;

            double accel = radius * radius;
            accel = mu / accel;
            return accel;
        }

        public void dumpCoefficients(double machMin,  double machMax,  int nbMach,
                                     double alphaMin, double alphaMax, int nbAlpha,
                                     double betaMin,  double betaMax,  int nbBeta,
                                     double pitchMin, double pitchMax, int nbPitch,
                                     double yawMin,   double yawMax,   int nbYaw,
                                     double rollMin,  double rollMax,  int nbRoll)
        {
            alpha = alphaMin;
            beta = betaMin;


            DumpSimInput  simInput  = new DumpSimInput(alphaMin, betaMin, 0.0, 0.0, 0.0, machMin, pitchMin, yawMin, rollMin, 0, false);
            DumpSimOutput simOutput = new DumpSimOutput();
            _instantCondition.computeBodyCoefficients(simInput, ref simOutput, true, false);

            UnityEngine.Debug.LogWarning("AERODYNAMIC COEFFICIENTS : CA : "  + simOutput.Ca.ToString() +
                                                                 "\n CN : "  + simOutput.Cn.ToString() +
                                                                 "\n CY : "  + simOutput.Cy.ToString() +
                                                                 "\n CMX : " + simOutput.Cmx.ToString() +
                                                                 "\n CMY : " + simOutput.Cmy.ToString() +
                                                                 "\n CMZ : " + simOutput.Cmz.ToString());
        }

        internal void arrowPlot(ArrowPointer velocityArrow)
        {
            float cosAlpha = (float)Math.Cos(alpha * Math.PI / 180);
            float sinAlpha = (float)Math.Sin(alpha * Math.PI / 180);

            float cosBeta = (float)Math.Cos(beta * Math.PI / 180);
            float sinBeta = (float)Math.Sin(beta * Math.PI / 180);

            Matrix4x4 RotationAoA = new Matrix4x4(new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                                                  new Vector4(0.0f, cosAlpha, sinAlpha, 0.0f),
                                                  new Vector4(0.0f, -sinAlpha, cosAlpha, 0.0f),
                                                  new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            Matrix4x4 RotationBeta = new Matrix4x4(new Vector4(cosBeta,  0.0f, sinBeta, 0.0f),
                                                   new Vector4(0.0f,     1.0f, 0.0f,    0.0f),
                                                   new Vector4(-sinBeta, 0.0f, cosBeta, 0.0f),
                                                   new Vector4(0.0f,    0.0f,  0.0f,    1.0f));

            Matrix4x4 RBodyToWind = RotationBeta * RotationAoA;

            Vector3d forwardWind = RBodyToWind.MultiplyVector(Vector3d.forward);

            velocityArrow.Direction = forwardWind;
        }
    }
}
