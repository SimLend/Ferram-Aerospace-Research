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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ferram4;
using FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation;
using KSP.Localization;
using UnityEngine;
using static GameEvents;

namespace FerramAerospaceResearch.FARGUI.FAREditorGUI
{
    internal class DumpAnalysisGUI : IDisposable
    {
        //private ferramGraph _graph = new ferramGraph(400, 350);

        private bool   isAtCOG = false;

        private string minMach  = "0.2";
        private string maxMach  = "1.5";
        private string NbMach   = "10";
                                
        private string minAlpha = "-5.0";
        private string maxAlpha = "25.0";
        private string NbAlpha  = "15";
                                
        private string minBeta  = "-5.0";
        private string maxBeta  = "5.0";
        private string NbBeta   = "10";

        private string minPitch = "-1.0";
        private string maxPitch = "1.0";
        private string NbPitch  = "20";
                                
        private string minYaw   = "-1.0";
        private string maxYaw   = "1.0";
        private string NbYaw    = "20";

        private string minRoll  = "-1.0";
        private string maxRoll  = "1.0";
        private string NbRoll   = "20";

        private double lastMaxBounds, lastMinBounds;
        private bool isMachMode;

        private GUIDropDown<int> flapSettingDropdown;
        private GUIDropDown<CelestialBody> bodySettingDropdown;
        private EditorSimManager simManager;

        private Vector3 upperAoAVec, lowerAoAVec;
        private float pingPongAoAFactor;

        public DumpAnalysisGUI(
            EditorSimManager simManager,
            GUIDropDown<int> flapSettingDropDown,
            GUIDropDown<CelestialBody> bodySettingDropdown
        )
        {
            this.simManager = simManager;
            flapSettingDropdown = flapSettingDropDown;
            this.bodySettingDropdown = bodySettingDropdown;
        }

        public void Dispose()
        {
            flapSettingDropdown = null;
            bodySettingDropdown = null;
            simManager = null;
        }

        public void Display()
        {
            GUILayout.Label(Localizer.Format("FAREditorStabDerivFlightCond"), GUILayout.Width(300.0F),
                                                                              GUILayout.Height(25.0F));
            GUILayout.BeginVertical();

            // Mach panel
            GUILayout.Label(Localizer.Format("FARAbbrevMach"), GUILayout.Width(300.0F),
                                                               GUILayout.Height(25.0F));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphLowLim"), GUILayout.Width(50.0F),
                                                                            GUILayout.Height(25.0F));
            minMach = GUILayout.TextField(minMach, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphUpLim"), GUILayout.Width(50.0F),
                                                                           GUILayout.Height(25.0F));
            maxMach = GUILayout.TextField(maxMach, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphPtCount"), GUILayout.Width(70.0F),
                                                                             GUILayout.Height(25.0F));
            NbMach = GUILayout.TextField(NbMach, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();


            // AOA panel
            GUILayout.Label(Localizer.Format("FARAbbrevAoA"), GUILayout.Width(300.0F),
                                                              GUILayout.Height(25.0F));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphLowLim"), GUILayout.Width(50.0F),
                                                                            GUILayout.Height(25.0F));
            minAlpha = GUILayout.TextField(minAlpha, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphUpLim"), GUILayout.Width(50.0F),
                                                                           GUILayout.Height(25.0F));
            maxAlpha = GUILayout.TextField(maxAlpha, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphPtCount"), GUILayout.Width(70.0F),
                                                                             GUILayout.Height(25.0F));
            NbAlpha = GUILayout.TextField(NbAlpha, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();


            // Sideslip panel
            GUILayout.Label(Localizer.Format("FARAbbrevSideslip"), GUILayout.Width(300.0F),
                                                                   GUILayout.Height(25.0F));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphLowLim"), GUILayout.Width(50.0F),
                                                                            GUILayout.Height(25.0F));
            minBeta = GUILayout.TextField(minBeta, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphUpLim"), GUILayout.Width(50.0F),
                                                                           GUILayout.Height(25.0F));
            maxBeta = GUILayout.TextField(maxBeta, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphPtCount"), GUILayout.Width(70.0F),
                                                                             GUILayout.Height(25.0F));
            NbBeta = GUILayout.TextField(NbBeta, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // Pitch panel
            GUILayout.Label(Localizer.Format("FAREditorStaticPitchSetting"), GUILayout.Width(300.0F),
                                                                             GUILayout.Height(25.0F));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphLowLim"), GUILayout.Width(50.0F),
                                                                            GUILayout.Height(25.0F));
            minPitch = GUILayout.TextField(minPitch, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphUpLim"), GUILayout.Width(50.0F),
                                                                           GUILayout.Height(25.0F));
            maxPitch = GUILayout.TextField(maxPitch, GUILayout.ExpandWidth(true));

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphPtCount"), GUILayout.Width(70.0F),
                                                                             GUILayout.Height(25.0F));
            NbPitch = GUILayout.TextField(NbPitch, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // Whether the moment coefficients should be written at the COG
            isAtCOG = GUILayout.Toggle(isAtCOG, Localizer.Format("FAREditorDumpDataAtCOG"));

            if (GUILayout.Button(Localizer.Format("FAREditorDumpData"),
                                 GUILayout.Width(300.0F),
                                 GUILayout.Height(25.0F)))
            {
                // TODO : deduplicate
                double fminMach = sanitizeFloatInput(ref minMach, 0.0,      100);
                double fmaxMach = sanitizeFloatInput(ref maxMach, fminMach, 100);
                int    inbMach  = sanitizeIntInput(ref NbMach);

                double fminAlpha = sanitizeFloatInput(ref minAlpha, -90, 90);
                double fmaxAlpha = sanitizeFloatInput(ref maxAlpha, fminAlpha, 90);
                int    inbAlpha  = sanitizeIntInput(ref NbAlpha);

                double fminBeta = sanitizeFloatInput(ref minBeta, -90,      90);
                double fmaxBeta = sanitizeFloatInput(ref maxBeta, fminBeta, 90);
                int    inbBeta = sanitizeIntInput(ref NbBeta);

                double fminPitch = sanitizeFloatInput(ref minPitch, -1,        1);
                double fmaxPitch = sanitizeFloatInput(ref maxPitch, fminPitch, 1);
                int    inbPitch  = sanitizeIntInput(ref NbPitch);

                double fminYaw = sanitizeFloatInput(ref minYaw, -1,      1);
                double fmaxYaw = sanitizeFloatInput(ref maxYaw, fminYaw, 1);
                int    inbYaw  = sanitizeIntInput(ref NbYaw);

                double fminRoll = sanitizeFloatInput(ref minRoll, -1,      1);
                double fmaxRoll = sanitizeFloatInput(ref maxYaw, fminRoll, 1);
                int    inbRoll  = sanitizeIntInput(ref NbRoll);

                DumpSim sim = new DumpSim();
                DumpSimInput simInput = new DumpSimInput(fminAlpha, fminBeta, 0.0, 0.0, 0.0, fminMach, fminPitch, fminYaw, fminRoll, 0, false);
                DumpSimOutput simOutput = new DumpSimOutput();
                sim.computeCoefficients(simInput, ref simOutput, false, false);
                UnityEngine.Debug.LogWarning("AERODYNAMIC COEFFICIENTS : CA : " + simOutput.Ca.ToString() +
                                                              "\n CN : " + simOutput.Cn.ToString() +
                                                              "\n CY : " + simOutput.Cy.ToString() +
                                                              "\n CMX : " + simOutput.Cmx.ToString() +
                                                              "\n CMY : " + simOutput.Cmy.ToString() +
                                                              "\n CMZ : " + simOutput.Cmz.ToString());
            }
            GUILayout.EndVertical();
        }
        private double sanitizeFloatInput(ref string input, double min, double max)
        {
            input         = Regex.Replace(input, @"[^-?[0-9]*(\.[0-9]*)?]", "");
            double fValue = double.Parse(input);
            fValue        = fValue.Clamp(min, max);
            input         = fValue.ToString(CultureInfo.InvariantCulture);
            return fValue;
        }

        private int sanitizeIntInput(ref string input)
        {
            input = Regex.Replace(input, @"[^-?[0-9]*(\.[0-9]*)?]", "");
            double fValue = double.Parse(input);
            fValue = Math.Ceiling(fValue);
            input  = fValue.ToString(CultureInfo.InvariantCulture);
            return (int)fValue;
        }


        /*
        private void BelowGraphInputsGUI(GraphInputs input)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(Localizer.Format("FAREditorStaticGraphLowLim"),
                            GUILayout.Width(50.0F),
                            GUILayout.Height(25.0F));
            input.lowerBound = GUILayout.TextField(input.lowerBound, GUILayout.ExpandWidth(true));
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphUpLim"),
                            GUILayout.Width(50.0F),
                            GUILayout.Height(25.0F));
            input.upperBound = GUILayout.TextField(input.upperBound, GUILayout.ExpandWidth(true));
            GUILayout.Label(Localizer.Format("FAREditorStaticGraphPtCount"),
                            GUILayout.Width(70.0F),
                            GUILayout.Height(25.0F));
            input.numPts = GUILayout.TextField(input.numPts, GUILayout.ExpandWidth(true));
            GUILayout.Label(isMachMode ? Localizer.Format("FARAbbrevAoA") : Localizer.Format("FARAbbrevMach"),
                            GUILayout.Width(50.0F),
                            GUILayout.Height(25.0F));
            input.otherInput = GUILayout.TextField(input.otherInput, GUILayout.ExpandWidth(true));

            GUI.enabled = !EditorGUI.Instance.VoxelizationUpdateQueued;
            if (GUILayout.Button(isMachMode
                                     ? Localizer.Format("FAREditorStaticSweepMach")
                                     : Localizer.Format("FAREditorStaticSweepAoA"),
                                 GUILayout.Width(100.0F),
                                 GUILayout.Height(25.0F)))
            {
                input.lowerBound = Regex.Replace(input.lowerBound, @"[^-?[0-9]*(\.[0-9]*)?]", "");
                input.upperBound = Regex.Replace(input.upperBound, @"[^-?[0-9]*(\.[0-9]*)?]", "");
                input.numPts = Regex.Replace(input.numPts, @"[^-?[0-9]*(\.[0-9]*)?]", "");
                input.pitchSetting = Regex.Replace(input.pitchSetting, @"[^-?[0-9]*(\.[0-9]*)?]", "");
                input.otherInput = Regex.Replace(input.otherInput, @"[^-?[0-9]*(\.[0-9]*)?]", "");

                double lowerBound = double.Parse(input.lowerBound);
                lowerBound = lowerBound.Clamp(-90, 90);
                input.lowerBound = lowerBound.ToString(CultureInfo.InvariantCulture);

                double upperBound = double.Parse(input.upperBound);
                upperBound = upperBound.Clamp(lowerBound, 90);
                input.upperBound = upperBound.ToString(CultureInfo.InvariantCulture);

                double numPts = double.Parse(input.numPts);
                numPts = Math.Ceiling(numPts);
                input.numPts = numPts.ToString(CultureInfo.InvariantCulture);

                double pitchSetting = double.Parse(input.pitchSetting);
                pitchSetting = pitchSetting.Clamp(-1, 1);
                input.pitchSetting = pitchSetting.ToString(CultureInfo.InvariantCulture);

                double otherInput = double.Parse(input.otherInput);

                SweepSim sim = simManager.SweepSim;
                if (sim.IsReady())
                {
                    GraphData data;
                    if (isMachMode)
                    {
                        data = sim.MachNumberSweep(otherInput,
                                                   pitchSetting,
                                                   lowerBound,
                                                   upperBound,
                                                   (int)numPts,
                                                   input.flapSetting,
                                                   input.spoilers,
                                                   bodySettingDropdown.ActiveSelection);
                        SetAngleVectors(pitchSetting, pitchSetting);
                    }
                    else
                    {
                        data = sim.AngleOfAttackSweep(otherInput,
                                                      pitchSetting,
                                                      lowerBound,
                                                      upperBound,
                                                      (int)numPts,
                                                      input.flapSetting,
                                                      input.spoilers,
                                                      bodySettingDropdown.ActiveSelection);
                        SetAngleVectors(lowerBound, upperBound);
                    }
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }*/
    }
}
