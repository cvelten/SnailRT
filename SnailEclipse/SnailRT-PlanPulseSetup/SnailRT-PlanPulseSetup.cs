// Lightweight binary ESAPI plugin (v16.1+), built as SnailRT-PlanPulseSetup.esapi.dll against the
// ESAPI API assemblies only - no project references. Write-enabled, so the DLL must be approved
// in Eclipse Script Approvals; bump AssemblyVersion on every change that needs re-approval.
//
// SnailRT / TMPRT (NRG CC-017) pulse plan setup. Takes the in-scope single-pulse plan
// (exactly one VMAT or DCA treatment beam, e.g. 20 cGy x 230 fx) and creates a new plan in
// the same course on the same structure set with the pulse beam duplicated nb_pulses times:
//   new_nb_fx          = orig_nb_fx / nb_pulses
//   new_dose_per_fx    = orig_dose_per_fx * nb_pulses
// Every pulse keeps the original beam's MU (fixed-MU dose calculation). Optionally, every
// other pulse is delivered in the reversed arc direction. The original plan is not modified.
// Setup fields are not copied.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: ESAPIScript(IsWriteable = true)]
[assembly: AssemblyVersion("0.0.1.1")]
[assembly: AssemblyFileVersion("0.0.1.1")]

namespace VMS.TPS
{
    public class Script
    {
        public void Execute(ScriptContext context)
        {
            if (context == null || context.Patient == null)
            {
                MessageBox.Show("No patient is set in ESAPI!\nWill stop execution.",
                    "Invalid patient object", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var planSetup = context.PlanSetup as ExternalPlanSetup;
            if (planSetup == null)
            {
                MessageBox.Show("No external plan is set in ESAPI (or it is not an external beam plan)!\nWill stop execution.",
                    "Invalid plan object", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string report = PlanPulseSetup.Run(context.Patient, planSetup);
                if (report != null)
                    MessageBox.Show(report, "SnailRT Plan Pulse Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "SnailRT Plan Pulse Setup - Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    internal static class PlanPulseSetup
    {
        /// <summary>Default for "reverse arc direction on every other pulse" (primer recommendation).</summary>
        private const bool ALTERNATE_DIRECTION = true;
        private const int MAX_PLAN_ID_LENGTH = 13;

        /// <summary>Returns the report text, or null if the user cancelled.</summary>
        public static string Run(Patient patient, ExternalPlanSetup orig)
        {
            var treatmentBeams = orig.Beams.Where(b => !b.IsSetupField && !b.IsImagingTreatmentField).ToList();
            if (treatmentBeams.Count != 1)
                throw new InvalidOperationException(string.Format(
                    "Plan '{0}' has {1} treatment beam(s). Exactly one (the pulse) is required.", orig.Id, treatmentBeams.Count));

            Beam pulse = treatmentBeams[0];
            if (pulse.MLCPlanType != MLCPlanType.VMAT && pulse.MLCPlanType != MLCPlanType.ArcDynamic)
                throw new InvalidOperationException(string.Format("Beam '{0}' is {1}. Only VMAT and dynamic conformal arcs are supported.", pulse.Id, pulse.MLCPlanType));
            if (pulse.MLC != null && pulse.MLC.Model == "SX2")
                throw new NotSupportedException(string.Format("Beam '{0}' uses the SX2 (Halcyon) MLC, which is not supported.", pulse.Id));
            if (double.IsNaN(pulse.Meterset.Value) || pulse.Meterset.Value <= 0)
                throw new InvalidOperationException(string.Format("Beam '{0}' has no valid MU. Calculate the pulse plan first.", pulse.Id));
            if (!orig.NumberOfFractions.HasValue || orig.NumberOfFractions.Value < 2)
                throw new InvalidOperationException("Plan has no (or a single) fraction set.");

            int origFx = orig.NumberOfFractions.Value;
            int nPulses;
            if (!Prompt(orig, out nPulses))
                return null;
            if (nPulses < 2 || origFx % nPulses != 0)
                throw new InvalidOperationException(string.Format("Number of pulses ({0}) must be >= 2 and divide the number of fractions ({1}).", nPulses, origFx));

            int newFx = origFx / nPulses;
            var newDosePerFx = new DoseValue(orig.DosePerFraction.Dose * nPulses, orig.DosePerFraction.Unit);

            patient.BeginModifications();
            Course course = orig.Course;
            if (!course.CanAddPlanSetup(orig.StructureSet))
                throw new InvalidOperationException(string.Format("Cannot add a plan to course '{0}'.", course.Id));

            ExternalPlanSetup plan = course.AddExternalPlanSetup(orig.StructureSet);
            plan.Id = UniquePlanId(course, orig.Id + "_P");
            string calcModel = orig.GetCalculationModel(CalculationType.PhotonVolumeDose);
            if (!string.IsNullOrEmpty(calcModel))
                plan.SetCalculationModel(CalculationType.PhotonVolumeDose, calcModel);
            plan.SetPrescription(newFx, newDosePerFx, orig.TreatmentPercentage);

            for (int k = 1; k <= nPulses; ++k)
            {
                Beam b = DuplicateArc(plan, pulse, reverse: false);
                b.Id = string.Format("P{0:00}", k);
            }

            var sb = new StringBuilder();
            sb.AppendFormat("Patient:     {0}   Course: {1}", patient.Id, course.Id).AppendLine();
            sb.AppendFormat("Source plan: {0}   {1} fx x {2}", orig.Id, origFx, orig.DosePerFraction).AppendLine();
            sb.AppendFormat("New plan:    {0}   {1} fx x {2}", plan.Id, newFx, newDosePerFx).AppendLine();
            sb.AppendFormat("Pulses: {0} x {1:0.0} MU ({2})", nPulses, pulse.Meterset.Value, "same direction").AppendLine();
            sb.AppendLine();
            sb.AppendLine("WARNING: Calculate manually with preset MU!");
            if (pulse.MLCPlanType == MLCPlanType.ArcDynamic && !HasUniformWeights(pulse))
                sb.AppendLine("WARNING: Source DCA has non-uniform meterset weights; the pulses use evenly spaced weights and differ from the source beam!");
            sb.AppendLine();
            sb.AppendLine("Setup fields were not copied.");
            return sb.ToString();
        }

        /// <summary>
        /// Adds a copy of an arc beam (VMAT or conformal arc) to the plan, copying per-control-point
        /// jaws and leaves. With reverse, the arc runs in the opposite direction: gantry start/stop
        /// swapped, control points reversed and cumulative meterset weights mirrored (total - w).
        /// </summary>
        private static Beam DuplicateArc(ExternalPlanSetup plan, Beam src, bool reverse)
        {
            var cps = src.ControlPoints.ToList();
            double totalWeight = cps[cps.Count - 1].MetersetWeight;
            if (reverse)
                cps.Reverse();

            ControlPoint first = cps[0];
            ControlPoint last = cps[cps.Count - 1];
            GantryDirection direction = reverse ? Flip(src.GantryDirection) : src.GantryDirection;
            ExternalBeamMachineParameters mp = MachineParameters(src);

            Beam beam;
            if (src.MLCPlanType == MLCPlanType.VMAT)
            {
                var weights = cps.Select(cp => reverse ? totalWeight - cp.MetersetWeight : cp.MetersetWeight).ToList();
                beam = plan.AddVMATBeam(mp, weights, first.CollimatorAngle, first.GantryAngle, last.GantryAngle,
                    direction, first.PatientSupportAngle, src.IsocenterPosition);
            }
            else
            {
                beam = plan.AddConformalArcBeam(mp, first.CollimatorAngle, cps.Count, first.GantryAngle, last.GantryAngle,
                    direction, first.PatientSupportAngle, src.IsocenterPosition);
            }

            BeamParameters parameters = beam.GetEditableParameters();
            foreach (ControlPointParameters cp in parameters.ControlPoints)
            {
                cp.JawPositions = cps[cp.Index].JawPositions;
                cp.LeafPositions = cps[cp.Index].LeafPositions;
            }
            beam.ApplyParameters(parameters);
            return beam;
        }

        /// <summary>
        /// AddConformalArcBeam spaces meterset weights evenly and DuplicateArc does not copy them,
        /// so a DCA pulse is only reproduced exactly if the source weights are evenly spaced too.
        /// </summary>
        private static bool HasUniformWeights(Beam b)
        {
            var cps = b.ControlPoints.ToList();
            double total = cps[cps.Count - 1].MetersetWeight;
            for (int i = 0; i < cps.Count; i++)
            {
                if (Math.Abs(cps[i].MetersetWeight - total * i / (cps.Count - 1)) > 1e-4)
                    return false;
            }
            return true;
        }

        private static GantryDirection Flip(GantryDirection d)
        {
            if (d == GantryDirection.Clockwise) return GantryDirection.CounterClockwise;
            if (d == GantryDirection.CounterClockwise) return GantryDirection.Clockwise;
            return d;
        }

        /// <summary>Same as Beams.GetExternalBeamMachineParameters in Nucular.Common.ESAPI ("6X-FFF" -> "6X", "FFF").</summary>
        private static ExternalBeamMachineParameters MachineParameters(Beam b)
        {
            Match m = Regex.Match(b.EnergyModeDisplayName, @"(\d*[EX])[\s\W]?(.*)?");
            string energy = m.Success ? m.Groups[1].Value : string.Empty;
            string fluence = m.Success ? m.Groups[2].Value : string.Empty;
            return new ExternalBeamMachineParameters(b.TreatmentUnit.Id, energy, b.DoseRate, b.Technique.Id, fluence);
        }

        private static string UniquePlanId(Course course, string proposed)
        {
            string baseId = proposed.Length > MAX_PLAN_ID_LENGTH ? proposed.Substring(0, MAX_PLAN_ID_LENGTH) : proposed;
            var taken = new HashSet<string>(course.PlanSetups.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(baseId))
                return baseId;
            for (int i = 2; i < 100; i++)
            {
                string suffix = i.ToString();
                string id = baseId.Substring(0, Math.Min(baseId.Length, MAX_PLAN_ID_LENGTH - suffix.Length)) + suffix;
                if (!taken.Contains(id))
                    return id;
            }
            throw new InvalidOperationException("Could not find a unique plan Id.");
        }

        /// <summary>Code-built dialog: number of pulses + alternate-direction checkbox.</summary>
        private static bool Prompt(ExternalPlanSetup orig, out int nPulses)
        {
            var info = new TextBlock
            {
                Text = string.Format("Plan '{0}': {1} fx x {2}\nNumber of pulses per fraction:",
                    orig.Id, orig.NumberOfFractions.Value, orig.DosePerFraction),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var input = new TextBox { Text = "10", Margin = new Thickness(0, 0, 0, 6) };
            //var check = new CheckBox { Content = "Reverse arc direction on every other pulse", IsChecked = ALTERNATE_DIRECTION, Margin = new Thickness(0, 0, 0, 10) };
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(info);
            panel.Children.Add(input);
            //panel.Children.Add(check);
            panel.Children.Add(buttons);

            var window = new Window
            {
                Title = "SnailRT Plan Pulse Setup",
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            ok.Click += (s, e) => { window.DialogResult = true; };
            input.Loaded += (s, e) => { input.Focus(); input.SelectAll(); };

            bool accepted = window.ShowDialog() == true;
            //alternate = check.IsChecked == true;
            nPulses = 0;
            if (!accepted)
                return false;
            if (!int.TryParse(input.Text.Trim(), out nPulses))
                throw new InvalidOperationException(string.Format("'{0}' is not a whole number.", input.Text));
            return true;
        }
    }
}
