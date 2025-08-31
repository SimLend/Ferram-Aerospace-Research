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

namespace FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation
{
    internal class DumpSim
    {
        private readonly DumpSimInput iterationInput = new DumpSimInput();
        private List<FARAeroSection> _currentAeroSections;
        private List<FARAeroPartModule> _currentAeroModules;
        private List<FARWingAerodynamicModel> _wingAerodynamicModel;

        public double _maxCrossSectionFromBody;
        public double _bodyLength;

        private double neededCl;
        public InstantConditionSimOutput iterationOutput;

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

        public void computeCoefficients(
            DumpSimInput input,
            ref DumpSimOutput output,
            bool clear,
            bool reset_stall = false
        )
        {
            double area = 0;
            double MAC  = 0;
            double b_2  = 0;

            Vector3d forwardBody = Vector3.forward;
            Vector3d upBody      = Vector3.up;
            Vector3d rightBody   = Vector3.right;

            Vector3d CoM     = Vector3d.zero;

            if (EditorDriver.editorFacility == EditorFacility.VAB)
            {
                forwardBody = Vector3.up;
                upBody      = -Vector3.forward;
            }

            double mass = 0;
            List<Part> partsList = EditorLogic.SortedShipList;
            foreach (Part p in partsList)
            {
                if (FARAeroUtil.IsNonphysical(p))
                    continue;

                double partMass = p.mass;
                if (p.Resources.Count > 0)
                    partMass += p.GetResourceMass();

                // If you want to use GetModuleMass, you need to start from p.partInfo.mass, not p.mass
                CoM += partMass * (Vector3d)p.transform.TransformPoint(p.CoMOffset);
                mass += partMass;
            }

            CoM /= mass;

            // Rodhern: The original reference directions (velocity, liftVector, sideways) did not form an orthonormal
            //  basis. That in turn produced some counterintuitive calculation results, such as coupled yaw and pitch
            //  derivatives. A more thorough discussion of the topic can be found on the KSP forums:
            //  https://forum.kerbalspaceprogram.com/index.php?/topic/19321-131-ferram-aerospace-research-v01591-liepmann-4218/&do=findComment&comment=2781270
            //  The reference directions have been replaced by new ones that are orthonormal by construction.
            //  In dkavolis branch Vector3.Cross() and Vector3d.Normalize() are used explicitly. There is no apparent
            //  benefit to this other than possibly improved readability.
            float cosAlpha = (float)Math.Cos(input.alpha * Math.PI / 180);
            float sinAlpha = (float)Math.Sin(input.alpha * Math.PI / 180);

            float cosBeta = (float)Math.Cos(input.alpha * Math.PI / 180);
            float sinBeta = (float)Math.Sin(input.alpha * Math.PI / 180);

            Matrix4x4 RotationAoA = new Matrix4x4(new Vector4(1.0f, 0.0f,      0.0f,     0.0f),
                                                  new Vector4(0.0f, cosAlpha, -sinAlpha, 0.0f),
                                                  new Vector4(0.0f, sinAlpha,  cosAlpha, 0.0f),
                                                  new Vector4(0.0f, 0.0f,      0.0f,     1.0f));

            Matrix4x4 RotationBeta = new Matrix4x4(new Vector4(cosBeta,  0.0f, sinBeta, 0.0f),
                                                   new Vector4(0.0f,     1.0f, 0.0f,    0.0f),
                                                   new Vector4(-sinBeta, 0.0f, cosBeta, 0.0f),
                                                   new Vector4(0.0f,     0.0f, 0.0f,    1.0f));

            Matrix4x4 RBodyToWind = RotationBeta * RotationAoA;

            Vector3d forwardWind = RBodyToWind.MultiplyVector(forwardBody);
            Vector3d upWind      = RBodyToWind.MultiplyVector(upBody);
            Vector3d rightWind   = RBodyToWind.MultiplyVector(rightBody);

            Vector3d velocityVector  = forwardWind;
            Vector3d angularVelocity = new Vector3d(0.0f, 0.0f, 0.0f); // TODO : implement a non null angular velocity

            foreach (FARWingAerodynamicModel w in _wingAerodynamicModel)
            {
                if (!(w && w.part))
                    continue;

                w.ComputeForceEditor(velocityVector, input.machNumber, 2);

                if (clear)
                    w.EditorClClear(reset_stall);

                Vector3d relPos = w.GetAerodynamicCenter() - CoM;
                Vector3d partVelocity = velocityVector + Vector3d.Cross(angularVelocity, relPos);

                if (w is FARControllableSurface controllableSurface)
                    controllableSurface.SetControlStateEditor(CoM,
                                                              partVelocity,
                                                              (float)input.pitchValue,
                                                              (float)input.yawValue,
                                                              (float)input.rollValue,
                                                              input.flaps,
                                                              input.spoilers);
                else if (w.isShielded)
                    continue;

                Vector3d force = w.ComputeForceEditor(partVelocity.normalized, input.machNumber, 2) * 1000;

                output.Cn += Vector3d.Dot(force, upBody);
                output.Cy += Vector3d.Dot(force, rightBody);
                output.Ca += Vector3d.Dot(force, forwardBody);

                Vector3d moment = -Vector3d.Cross(relPos, force);

                output.Cmy += Vector3d.Dot(moment, rightBody);
                output.Cmz += Vector3d.Dot(moment, upBody);
                output.Cmx += Vector3d.Dot(moment, forwardBody);

                area += w.S;
                MAC  += w.GetMAC() * w.S;
                b_2  += w.Getb_2() * w.S;
            }

            var center = new FARCenterQuery();
            foreach (FARAeroSection aeroSection in _currentAeroSections)
                aeroSection.PredictionCalculateAeroForces(2,
                                                          (float)input.machNumber,
                                                          10000,
                                                          0,
                                                          0.005f,
                                                          velocityVector.normalized,
                                                          center);

            Vector3d centerForce = center.force * 1000;

            output.Cn += -Vector3d.Dot(centerForce, upBody);
            output.Cy +=  Vector3d.Dot(centerForce,  rightBody);
            output.Ca += -Vector3d.Dot(centerForce, forwardBody);

            Vector3d centerMoment = -center.TorqueAt(CoM) * 1000;

            output.Cmy += Vector3d.Dot(centerMoment, rightBody);
            output.Cmz += Vector3d.Dot(centerMoment, upBody);
            output.Cmx += Vector3d.Dot(centerMoment, forwardBody);

            if (area.NearlyEqual(0))
            {
                area = _maxCrossSectionFromBody;
                b_2 = 1;
                MAC = _bodyLength;
            }

            double recipArea = 1 / area;

            MAC        *= recipArea;
            b_2        *= recipArea;
            output.Cn  *= recipArea;
            output.Cy  *= recipArea;
            output.Ca  *= recipArea;
            output.Cmy *= recipArea / MAC; // FIXME : Adim is fucked up
            output.Cmz *= recipArea / MAC;
            output.Cmx *= recipArea / MAC;
        }

        public void SetState(double machNumber, double Cl, Vector3d CoM, double pitch, int flapSetting, bool spoilers)
        {
            iterationInput.machNumber = machNumber;
            neededCl = Cl;
            iterationInput.pitchValue = pitch;
            iterationInput.flaps = flapSetting;
            iterationInput.spoilers = spoilers;
            iterationInput.beta = 0;

            foreach (FARWingAerodynamicModel w in _wingAerodynamicModel)
            {
                if (w.isShielded)
                    continue;

                if (w is FARControllableSurface controllableSurface)
                    controllableSurface.SetControlStateEditor(CoM,
                                                              Vector3.up,
                                                              (float)pitch,
                                                              0,
                                                              0,
                                                              flapSetting,
                                                              spoilers);
            }
        }
    }
}
