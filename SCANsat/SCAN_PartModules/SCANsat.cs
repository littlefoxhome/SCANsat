#region license
/*  [Scientific Committee on Advanced Navigation]
 * 			S.C.A.N. Satellite
 *
 * SCANsat - SCAN RADAR Altimetry Sensor part (& More)
 * 
 * Copyright (c)2013 damny;
 * Copyright (c)2014 technogeeky <technogeeky@gmail.com>;
 * Copyright (c)2014 (Your Name Here) <your email here>; see LICENSE.txt for licensing details.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using SCANsat.SCAN_Data;
using SCANsat.SCAN_Toolbar;
using SCANsat.SCAN_UI;
using KSP.UI.Screens.Flight.Dialogs;

using UnityEngine;
using palette = SCANsat.SCAN_UI.UI_Framework.SCANpalette;

namespace SCANsat.SCAN_PartModules
{
	public class SCANsat : PartModule, IScienceDataContainer
	{
		private bool powerIsProblem;
		private int powerTimer;
		private Animation anim = null;
		private List<ScienceData> storedData = new List<ScienceData>();
		private ExperimentsResultDialog expDialog = null;

		/* SAT: KSP entry points */
		public override void OnStart(StartState state)
		{
			if (state == StartState.Editor)
			{
				print("[SCANsat] start: in editor");
				Events["editorExtend"].active = !string.IsNullOrEmpty(animationName);
			}
			else
			{
				print("[SCANsat] start: live");
			}
			if (!string.IsNullOrEmpty(animationName))
			{
				Animation[] a = part.FindModelAnimators(animationName);
				if (a.Length == 0)
				{
					print("[SCANsat] animation '" + animationName + "' not found");
				}
				else
				{
					print("[SCANsat] using animation #1 out of " + a.Length.ToString() + " animations named '" + animationName + "'");
					anim = a[0];
				}
			}
			if (scanName != null)
			{ // Use bitwise operators to check if the part has valid science collection scanners
				if ((sensorType & (Int32)SCANtype.AltimetryLoRes) == 0 && (sensorType & (Int32)SCANtype.AltimetryHiRes) == 0 && (sensorType & (Int32)SCANtype.Biome) == 0)
				{
					Events["startScan"].guiName = "Start " + scanName;
					Events["stopScan"].guiName = "Stop " + scanName;
					Actions["startScanAction"].guiName = "Start " + scanName;
					Actions["stopScanAction"].guiName = "Stop " + scanName;
					Actions["toggleScanAction"].guiName = "Toggle " + scanName;
				}
				else
				{
					Events["startScan"].guiName = "Start " + scanName;
					Events["stopScan"].guiName = "Stop " + scanName;
					Actions["startScanAction"].guiName = "Start " + scanName;
					Actions["stopScanAction"].guiName = "Stop " + scanName;
					Actions["toggleScanAction"].guiName = "Toggle " + scanName;
				}

				if ((sensorType & (int)SCANtype.Science) == 0)
				{
					Events["analyze"].active = false;
					Actions["analyzeData"].active = false;
				}
				else
				{
					Events["analyze"].active = true;
					Actions["analyzeData"].active = false;
				}
			}

			if (sensorType == 0)
			{
				// here, we override all event and action labels
				// and we also disable the analyze button (it does nothing)
				Events["startScan"].active = false;
				Events["stopScan"].active = false;
				Events["analyze"].active = false;
				Events["editorExtend"].active = false;
				Actions["startScanAction"].active = false;
				Actions["stopScanAction"].active = false;
				Actions["toggleScanAction"].active = false;
				Actions["analyzeData"].active = false;
			}

			if (scanning) animate(1, 1);
			powerIsProblem = false;
			print("[SCANsat] sensorType: " + sensorType.ToString() + " fov: " + fov.ToString() + " min_alt: " + min_alt.ToString() + " max_alt: " + max_alt.ToString() + " best_alt: " + best_alt.ToString() + " power: " + power.ToString());
		}

		protected virtual void Update()
		{
			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (!FlightGlobals.ready)
				return;

			if (sensorType == 0)
				return;

			Events["reviewEvent"].active = storedData.Count > 0;
			Events["EVACollect"].active = storedData.Count > 0;
			Events["startScan"].active = !scanning && !powerIsProblem;
			Events["stopScan"].active = scanning || powerIsProblem;
			if (sensorType != 32)
				Fields["alt_indicator"].guiActive = scanning;

			if (scanning)
				alt_indicator = scanAlt();
		}

		protected virtual void FixedUpdate()
		{
			if (!HighLogic.LoadedSceneIsFlight)
				return;

			if (!FlightGlobals.ready)
				return;

			if (SCANcontroller.controller == null)
				return;

			if (powerIsProblem)
			{
				if (powerTimer < 30)
				{
					powerTimer++;
					return;
				}

				addStatic();
				registerScanner();
			}

			if (scanning)
			{
				if (sensorType != 0 || SCANcontroller.controller.isVesselKnown(vessel.id, (SCANtype)sensorType))
				{
					if (TimeWarp.CurrentRate < 15000)
					{
						float p = power * TimeWarp.fixedDeltaTime;
						float e = part.RequestResource("ElectricCharge", p);
						if (e < p)
						{
							unregisterScanner();
							powerIsProblem = true;
							powerTimer = 0;
						}
						else
						{
							powerIsProblem = false;
						}
					}
					else if (powerIsProblem)
					{
						registerScanner();
						powerIsProblem = false;
					}
				}
				else
					unregisterScanner();
			}
		}

		public override void OnLoad(ConfigNode node)
		{
			if (node.HasNode("ScienceData"))
			{
				foreach (ConfigNode storedDataNode in node.GetNodes("ScienceData"))
				{
					ScienceData data = new ScienceData(storedDataNode);
					storedData.Add(data);
				}
			}
		}

		public override void OnSave(ConfigNode node)
		{
			node.RemoveNodes("ScienceData"); //** Prevent duplicates
			foreach (ScienceData SCANData in storedData)
			{
				ConfigNode storedDataNode = node.AddNode("ScienceData");
				SCANData.Save(storedDataNode);
			}
		}

		public override string GetInfo()
		{
			if (sensorType == 0)
				return "";

			string str = base.GetInfo();
			if (min_alt != 0)
			{
				str += "Altitude ( min): " + (min_alt / 1000).ToString("F0") + " km\n";
			}
			if (best_alt != min_alt)
			{
				str += "Altitude (best): " + (best_alt / 1000).ToString("F0") + " km\n";
			}
			if (max_alt != 0)
			{
				str += "Altitude ( max): " + (max_alt / 1000).ToString("F0") + " km\n";
			}
			if (fov != 0)
			{
				str += "FOV: " + fov.ToString("F0") + " °\n";
			}
			str += "Power usage: " + power.ToString("F1") + " charge/s\n";
			return str;
		}

		/* SAT: KSP fields */
		[KSPField]
		public int sensorType;
		[KSPField]
		public float fov;
		[KSPField]
		public float min_alt;
		[KSPField]
		public float max_alt;
		[KSPField]
		public float best_alt;
		[KSPField]
		public float power;
		[KSPField]
		public string scanName;
		[KSPField]
		public string animationName;
		[KSPField(guiName = "SCANsat Altitude", guiActive = false)]
		public string alt_indicator;

		/* SCAN: all of these fields and only scanning is persistant */
		[KSPField(isPersistant = true)]
		protected bool scanning = false;
		public bool scanningNow() { return scanning; }

		/* SCAN: context (right click) buttons in FLIGHT */
		[KSPEvent(guiActive = true, guiName = "Start RADAR Scan", active = true)]
		public void startScan()
		{
			if (!ToolbarManager.ToolbarAvailable && SCANcontroller.controller != null)
			{
				if (!SCANcontroller.controller.useStockAppLauncher)
					SCANcontroller.controller.mainMap.Visible = true;
			}
			registerScanner();
			animate(1, 0);
		}

		[KSPEvent(guiActive = true, guiName = "Stop RADAR Scan", active = true)]
		public void stopScan()
		{
			unregisterScanner();
			powerIsProblem = false;
			animate(-1, 1);
		}

		[KSPEvent(guiActive = true, guiName = "Analyze Data", active = true)]
		public void analyze()
		{
			makeScienceData(true);
			ReviewData();
		}

		[KSPEvent(guiActive = true, guiName = "Review Data", active = false)]
		public void reviewEvent()
		{
			ReviewData();
		}

		[KSPEvent(guiActiveUnfocused = true, guiName = "Collect Stored Data", externalToEVAOnly = true, unfocusedRange = 1.5f, active = false)]
		public void EVACollect()
		{
			List<ModuleScienceContainer> EVACont = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceContainer>();
			if (storedData.Count > 0)
			{
				if (EVACont.First().StoreData(new List<IScienceDataContainer>() { this }, false))
				{
					foreach (ScienceData data in storedData)
						DumpData(data);
				}
			}
		}

		/* SCAN: context (right click) buttons in EDTIOR */
		[KSPEvent(guiActiveEditor = true, guiName = "Extend", active = true)]
		public void editorExtend()
		{
			Events["editorExtend"].active = false;
			Events["editorRetract"].active = true;
			animate(1, 0);
		}

		[KSPEvent(guiActiveEditor = true, guiName = "Retract", active = false)]
		public void editorRetract()
		{
			Events["editorExtend"].active = true;
			Events["editorRetract"].active = false;
			animate(-1, 1);
		}

		/* SCAN: trivial function to do animation */
		private void animate(float speed, float time)
		{
			if (anim != null && anim[animationName] != null)
			{
				anim[animationName].speed = speed;
				if (!anim.IsPlaying(animationName))
				{
					anim[animationName].wrapMode = WrapMode.Clamp;
					anim[animationName].normalizedTime = time;
					anim.Play(animationName);
				}
			}
		}

		/* SCAN: actions for ... something ... */
		[KSPAction("Start Scan")]
		public void startScanAction(KSPActionParam param)
		{
			startScan();
		}

		[KSPAction("Stop Scan")]
		public void stopScanAction(KSPActionParam param)
		{
			stopScan();
		}

		[KSPAction("Toggle Scan")]
		public void toggleScanAction(KSPActionParam param)
		{
			if (scanning)
				stopScan();
			else
				startScan();
		}

		[KSPAction("Analyze Data")]
		public void analyzeData(KSPActionParam param)
		{
			//if (scanning) ** Always available
			analyze();
		}

		/* SCAN: add static (a warning that we're low on electric charge) */
		private void addStatic()
		{
			if (SCANcontroller.controller == null)
				return;

			if (SCANcontroller.controller.mainMap == null)
				return;

			if (SCANcontroller.controller.mainMap.Map == null)
				return;

			for (int i = 0; i < 1000; i++)
			{
				SCANcontroller.controller.mainMap.Map.SetPixel(UnityEngine.Random.Range(0, 360), UnityEngine.Random.Range(0, 180), palette.lerp(palette.black, palette.white, UnityEngine.Random.value));
			}
		}

		/* SCAN: register scanners without going through animation */
		private void registerScanner()
		{
			scanning = true;
			powerTimer = 0;
			if (sensorType > 0 && SCANcontroller.controller != null)
				SCANcontroller.controller.registerSensor(vessel, (SCANtype)sensorType, fov, min_alt, max_alt, best_alt);
		}

		protected void unregisterScanner()
		{
			scanning = false;
			if (sensorType > 0 && SCANcontroller.controller != null)
				SCANcontroller.controller.unregisterSensor(vessel, (SCANtype)sensorType);
		}

		private string scanAlt()
		{
			string altitude = "Unknown";
			if (vessel.altitude < min_alt)
				altitude = "Too low";
			else if (vessel.altitude < best_alt)
				altitude = "Sub-optimal";
			else if (vessel.altitude >= best_alt && vessel.altitude <= max_alt)
				altitude = "Ideal";
			else if (vessel.altitude > max_alt)
				altitude = "Too high";
			return altitude;
		}

		/* SCAN: SCIENCE! make, store, transmit, keep
		 * 	discard, review, count DATA */
		private void makeScienceData(bool notZero)
		{
			if (expDialog != null)
				DestroyImmediate(expDialog);
			storedData.Clear();
			ScienceData sd = getAvailableScience((SCANtype)sensorType, notZero);
			if (sd == null)
				return;
			storedData.Add(sd);
		}

		private ScienceData getAvailableScience(SCANtype sensor, bool notZero)
		{
			SCANdata data = SCANUtil.getData(vessel.mainBody);
			if (data == null)
				return null;
			ScienceData sd = null;
			ScienceExperiment se = null;
			ScienceSubject su = null;
			bool found = false;
			string id = null;
			double coverage = 0f;
			float multiplier = 1f;

			if (!found && (sensor & SCANtype.AltimetryLoRes) != SCANtype.Nothing)
			{
				found = true;
				if (vessel.mainBody.pqsController == null)
					multiplier = 0.5f;
				id = "SCANsatAltimetryLoRes";
				coverage = SCANUtil.getCoveragePercentage(data, SCANtype.AltimetryLoRes);
			}
			else if (!found && (sensor & SCANtype.AltimetryHiRes) != SCANtype.Nothing)
			{
				found = true;
				if (vessel.mainBody.pqsController == null)
					multiplier = 0.5f;
				id = "SCANsatAltimetryHiRes";
				coverage = SCANUtil.getCoveragePercentage(data, SCANtype.AltimetryHiRes);
			}
			else if (!found && (sensor & SCANtype.Biome) != SCANtype.Nothing)
			{
				found = true;
				if (vessel.mainBody.BiomeMap == null)
					multiplier = 0.5f;
				id = "SCANsatBiomeAnomaly";
				coverage = SCANUtil.getCoveragePercentage(data, SCANtype.Biome);
			}
			else if (!found && (sensor & SCANtype.FuzzyResources) != SCANtype.Nothing)
			{
				found = true;
				if (vessel.mainBody.pqsController == null)
					multiplier = 0.5f;
				id = "SCANsatResources";
				coverage = SCANUtil.getCoveragePercentage(data, SCANtype.FuzzyResources);
			}
			if (!found) return null;
			se = ResearchAndDevelopment.GetExperiment(id);
			if (se == null) return null;

			su = ResearchAndDevelopment.GetExperimentSubject(se, ExperimentSituations.InSpaceHigh, vessel.mainBody, "surface");
			if (su == null) return null;

			su.scienceCap *= multiplier;

			SCANUtil.SCANlog("Coverage: {0}, Science cap: {1}, Subject value: {2}, Scientific value: {3}, Science: {4}", new object[5] { coverage.ToString("F1"), su.scienceCap.ToString("F1"), su.subjectValue.ToString("F2"), su.scientificValue.ToString("F2"), su.science.ToString("F2") });

			su.scientificValue = 1;

			float science = (float)coverage;
			if (science > 95) science = 100;
			if (science < 30) science = 0;
			science = science / 100f;
			science = Mathf.Max(0, (science * su.scienceCap) - su.science);

			SCANUtil.SCANlog("Remaining science: {0}, Base value: {1}", new object[2] { science.ToString("F1"), se.baseValue.ToString("F1") });

			science /= Mathf.Max(0.1f, su.scientificValue); //look 10 lines up; this is always 1...
			science /= su.subjectValue;

			SCANUtil.SCANlog("Resulting science value: {0}", new object[1] { science.ToString("F2") });

			if (notZero && science <= 0) science = 0.00001f;

			sd = new ScienceData(science * su.dataScale, 1f, 0f, su.id, se.experimentTitle + " of " + vessel.mainBody.theName, false, part.flightID);
			su.title = sd.title;
			return sd;
		}

		public ScienceData[] GetData()
		{
			return storedData.ToArray();
		}

		public void ReturnData(ScienceData data)
		{
			if (data == null)
				return;

			storedData.Clear();

			storedData.Add(data);
		}

		private void KeepData(ScienceData data)
		{
			expDialog = null;
		}

		private void TransmitData(ScienceData data)
		{
			expDialog = null;
			List<IScienceDataTransmitter> tranList = vessel.FindPartModulesImplementing<IScienceDataTransmitter>();
			if (tranList.Count > 0 && storedData.Count > 0)
			{
				tranList.OrderBy(ScienceUtil.GetTransmitterScore).First().TransmitData(storedData);
				DumpData(storedData[0]);
			}
			else ScreenMessages.PostScreenMessage("No transmitters available on this vessel.", 4f, ScreenMessageStyle.UPPER_LEFT);
		}

		public void DumpData(ScienceData data)
		{
			expDialog = null;
			while (storedData.Contains(data))
			{
				storedData.Remove(data);
			}
		}

		public void ReviewDataItem(ScienceData sd)
		{
			ReviewData();
		}

		public void ReviewData()
		{
			if (storedData.Count < 1)
				return;
			if (expDialog != null)
				DestroyImmediate(expDialog);
			ScienceData sd = storedData[0];
			expDialog = ExperimentsResultDialog.DisplayResult(new ExperimentResultDialogPage(part, sd, 1f, 0f, false, "", true, new ScienceLabSearch(vessel, sd), DumpData, KeepData, TransmitData, null));
		}

		public bool IsRerunnable()
		{
			return true;
		}

		public int GetScienceCount()
		{
			return storedData.Count;
		}
	}
}

