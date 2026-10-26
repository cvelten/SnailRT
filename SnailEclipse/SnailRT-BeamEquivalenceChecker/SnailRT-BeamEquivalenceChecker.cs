// Single-file ESAPI plugin (v16.1+). Paste directly into Eclipse's script editor or
// compile standalone against the ESAPI API assemblies only - no project references.
//
// Compares every treatment beam in the in-scope ExternalPlanSetup against every other
// treatment beam to verify they are dosimetrically/functionally equivalent: MU, leaf
// pattern (arc-direction aware), gantry/collimator/couch angles, field size (jaws),
// energy/dose rate/technique, wedge settings, isocenter position, and average SSD.
//
// Deliberately avoids C# 6+ null-conditional (?.) and null-coalescing (??) operators,
// and "is null" pattern matching (C# 7), for compatibility with older single-file
// script compilation. All null checks use plain "== null" / "!= null".

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: ESAPIScript(IsWriteable = false)]

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

            string report = BeamEquivalenceChecker.Run(planSetup);
            bool hasMismatches = report.Contains("mismatch(es) found");
            MessageBox.Show(report, "Beam Equivalence Check",
                MessageBoxButton.OK, hasMismatches ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
    }

    /// <summary>Snapshot of a single beam's dosimetrically-relevant properties.</summary>
    internal class BeamSnapshot
    {
        public string Id;
        public string TechniqueId;
        public string EnergyModeDisplayName;
        public int DoseRate;
        public double MetersetValue;
        public VVector IsocenterPosition;
        public double AverageSSD;
        public GantryDirection GantryDirection;
        public List<WedgeInfo> Wedges;
        public List<ControlPointInfo> ControlPoints;
    }

    internal class WedgeInfo
    {
        public string Id;
        public double WedgeAngle;
        public double Direction;
    }

    internal class ControlPointInfo
    {
        public double GantryAngle;
        public double CollimatorAngle;
        public double PatientSupportAngle;
        public double JawX1;
        public double JawX2;
        public double JawY1;
        public double JawY2;
        public float[,] LeafPositions;
        public double MetersetWeight;
    }

    internal static class BeamEquivalenceChecker
    {
        private const double EPSILON_MU = 0.05;
        private const double EPSILON_ANGLE_DEG = 0.1;
        private const double EPSILON_FIELD_MM = 0.1;
        private const double EPSILON_SSD_MM = 0.5;
        private const int MAX_REPORTED_MISMATCHES = 25;

        public static string Run(ExternalPlanSetup planSetup)
        {
            var treatmentBeams = new List<Beam>();
            foreach (Beam b in planSetup.Beams)
            {
                if (b != null && !b.IsSetupField && !b.IsImagingTreatmentField)
                    treatmentBeams.Add(b);
            }

            if (treatmentBeams.Count < 2)
            {
                return string.Format(
                    "Plan '{0}' has only {1} treatment beam(s). Need at least 2 to compare.",
                    planSetup.Id, treatmentBeams.Count);
            }

            var snapshots = new List<BeamSnapshot>();
            foreach (Beam b in treatmentBeams)
                snapshots.Add(Snapshot(b));

            var mismatches = new List<string>();
            for (int i = 0; i < snapshots.Count; i++)
            {
                for (int j = i + 1; j < snapshots.Count; j++)
                    CompareBeams(snapshots[i], snapshots[j], mismatches);
            }

            var sb = new StringBuilder();
            sb.AppendFormat("Patient: {0}   Course: {1}   Plan: {2}",
                planSetup.Course.Patient.Id, planSetup.Course.Id, planSetup.Id);
            sb.AppendLine();
            sb.AppendFormat("Compared {0} treatment beam(s): {1}",
                treatmentBeams.Count, string.Join(", ", treatmentBeams.Select(b => b.Id).ToArray()));
            sb.AppendLine();
            sb.AppendLine();

            if (mismatches.Count == 0)
            {
                sb.AppendFormat("All {0} beams are dosimetrically equivalent within tolerance.", treatmentBeams.Count);
            }
            else
            {
                sb.AppendFormat("{0} mismatch(es) found:", mismatches.Count);
                sb.AppendLine();
                int shown = 0;
                foreach (string m in mismatches)
                {
                    if (shown >= MAX_REPORTED_MISMATCHES)
                    {
                        sb.AppendFormat("... and {0} more.", mismatches.Count - shown);
                        break;
                    }
                    sb.AppendLine(m);
                    shown++;
                }
            }

            return sb.ToString();
        }

        private static BeamSnapshot Snapshot(Beam b)
        {
            var s = new BeamSnapshot();
            s.Id = b.Id;
            s.TechniqueId = b.Technique != null ? b.Technique.Id : string.Empty;
            s.EnergyModeDisplayName = b.EnergyModeDisplayName;
            s.DoseRate = b.DoseRate;
            s.MetersetValue = b.Meterset.Value;
            s.IsocenterPosition = b.IsocenterPosition;
            s.AverageSSD = b.AverageSSD;
            s.GantryDirection = b.GantryDirection;

            s.Wedges = new List<WedgeInfo>();
            if (b.Wedges != null)
            {
                foreach (Wedge w in b.Wedges)
                {
                    WedgeInfo wi = new WedgeInfo();
                    wi.Id = w.Id;
                    wi.WedgeAngle = w.WedgeAngle;
                    wi.Direction = w.Direction;
                    s.Wedges.Add(wi);
                }
            }
            s.Wedges = s.Wedges.OrderBy(w => w.Id).ToList();

            s.ControlPoints = new List<ControlPointInfo>();
            foreach (ControlPoint cp in b.ControlPoints)
            {
                var ci = new ControlPointInfo();
                ci.GantryAngle = cp.GantryAngle;
                ci.CollimatorAngle = cp.CollimatorAngle;
                ci.PatientSupportAngle = cp.PatientSupportAngle;
                ci.JawX1 = cp.JawPositions.X1;
                ci.JawX2 = cp.JawPositions.X2;
                ci.JawY1 = cp.JawPositions.Y1;
                ci.JawY2 = cp.JawPositions.Y2;
                ci.LeafPositions = cp.LeafPositions;
                ci.MetersetWeight = cp.MetersetWeight;
                s.ControlPoints.Add(ci);
            }

            return s;
        }

        /// <summary>
        /// Reverses control-point order and recomputes cumulative MetersetWeight, so a
        /// clockwise arc's control points can be compared index-for-index against the
        /// mirrored control points of a counter-clockwise arc of the same field.
        /// </summary>
        private static List<ControlPointInfo> ReverseControlPoints(List<ControlPointInfo> original)
        {
            var reversed = new List<ControlPointInfo>(original);
            reversed.Reverse();

            double totalMeterset = original[original.Count - 1].MetersetWeight;
            for (int i = 0; i < reversed.Count; i++)
            {
                ControlPointInfo cp = reversed[i];
                ControlPointInfo copy = new ControlPointInfo();
                copy.GantryAngle = cp.GantryAngle;
                copy.CollimatorAngle = cp.CollimatorAngle;
                copy.PatientSupportAngle = cp.PatientSupportAngle;
                copy.JawX1 = cp.JawX1;
                copy.JawX2 = cp.JawX2;
                copy.JawY1 = cp.JawY1;
                copy.JawY2 = cp.JawY2;
                copy.LeafPositions = cp.LeafPositions;
                copy.MetersetWeight = totalMeterset - cp.MetersetWeight;
                reversed[i] = copy;
            }

            return reversed;
        }

        private static void CompareBeams(BeamSnapshot a, BeamSnapshot b, List<string> mismatches)
        {
            string pair = string.Format("{0} vs {1}", a.Id, b.Id);

            CompareString(pair, "Technique", a.TechniqueId, b.TechniqueId, mismatches);
            CompareString(pair, "Energy mode", a.EnergyModeDisplayName, b.EnergyModeDisplayName, mismatches);
            CompareDouble(pair, "Dose rate", a.DoseRate, b.DoseRate, 0, mismatches);
            CompareDouble(pair, "MU", a.MetersetValue, b.MetersetValue, EPSILON_MU, mismatches);
            CompareDouble(pair, "Isocenter X", a.IsocenterPosition.x, b.IsocenterPosition.x, EPSILON_FIELD_MM, mismatches);
            CompareDouble(pair, "Isocenter Y", a.IsocenterPosition.y, b.IsocenterPosition.y, EPSILON_FIELD_MM, mismatches);
            CompareDouble(pair, "Isocenter Z", a.IsocenterPosition.z, b.IsocenterPosition.z, EPSILON_FIELD_MM, mismatches);
            CompareDouble(pair, "Average SSD", a.AverageSSD, b.AverageSSD, EPSILON_SSD_MM, mismatches);

            CompareWedges(pair, a.Wedges, b.Wedges, mismatches);

            if (a.ControlPoints.Count != b.ControlPoints.Count)
            {
                mismatches.Add(string.Format("{0}: Control point count differs ({1} vs {2}) - cannot compare further.",
                    pair, a.ControlPoints.Count, b.ControlPoints.Count));
                return;
            }

            List<ControlPointInfo> bControlPoints = b.ControlPoints;
            bool mirroredArc = IsMirroredArcPair(a.GantryDirection, b.GantryDirection);
            if (mirroredArc)
                bControlPoints = ReverseControlPoints(b.ControlPoints);

            for (int i = 0; i < a.ControlPoints.Count; i++)
            {
                ControlPointInfo cpA = a.ControlPoints[i];
                ControlPointInfo cpB = bControlPoints[i];
                string cpLabel = string.Format("{0} [CP {1}{2}]", pair, i, mirroredArc ? ", mirrored" : "");

                CompareDouble(cpLabel, "Gantry angle", cpA.GantryAngle, cpB.GantryAngle, EPSILON_ANGLE_DEG, mismatches);
                CompareDouble(cpLabel, "Collimator angle", cpA.CollimatorAngle, cpB.CollimatorAngle, EPSILON_ANGLE_DEG, mismatches);
                CompareDouble(cpLabel, "Patient support (couch) angle", cpA.PatientSupportAngle, cpB.PatientSupportAngle, EPSILON_ANGLE_DEG, mismatches);
                CompareDouble(cpLabel, "Jaw X1", cpA.JawX1, cpB.JawX1, EPSILON_FIELD_MM, mismatches);
                CompareDouble(cpLabel, "Jaw X2", cpA.JawX2, cpB.JawX2, EPSILON_FIELD_MM, mismatches);
                CompareDouble(cpLabel, "Jaw Y1", cpA.JawY1, cpB.JawY1, EPSILON_FIELD_MM, mismatches);
                CompareDouble(cpLabel, "Jaw Y2", cpA.JawY2, cpB.JawY2, EPSILON_FIELD_MM, mismatches);
                CompareDouble(cpLabel, "Meterset weight", cpA.MetersetWeight, cpB.MetersetWeight, EPSILON_MU / Math.Max(a.MetersetValue, 1.0), mismatches);

                CompareLeafPositions(cpLabel, cpA.LeafPositions, cpB.LeafPositions, mismatches);
            }
        }

        private static bool IsMirroredArcPair(GantryDirection a, GantryDirection b)
        {
            return (a == GantryDirection.Clockwise && b == GantryDirection.CounterClockwise)
                || (a == GantryDirection.CounterClockwise && b == GantryDirection.Clockwise);
        }

        private static void CompareWedges(string pair, List<WedgeInfo> a, List<WedgeInfo> b, List<string> mismatches)
        {
            if (a.Count != b.Count)
            {
                mismatches.Add(string.Format("{0}: Wedge count differs ({1} vs {2}).", pair, a.Count, b.Count));
                return;
            }

            for (int i = 0; i < a.Count; i++)
            {
                CompareString(pair, "Wedge Id", a[i].Id, b[i].Id, mismatches);
                CompareDouble(pair, "Wedge angle", a[i].WedgeAngle, b[i].WedgeAngle, EPSILON_ANGLE_DEG, mismatches);
                CompareDouble(pair, "Wedge direction", a[i].Direction, b[i].Direction, EPSILON_ANGLE_DEG, mismatches);
            }
        }

        private static void CompareLeafPositions(string cpLabel, float[,] a, float[,] b, List<string> mismatches)
        {
            if (a == null || b == null)
            {
                if (a != b)
                    mismatches.Add(string.Format("{0}: Leaf positions - one beam has none, the other does.", cpLabel));
                return;
            }

            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
            {
                mismatches.Add(string.Format("{0}: Leaf position array size differs ({1}x{2} vs {3}x{4}).",
                    cpLabel, a.GetLength(0), a.GetLength(1), b.GetLength(0), b.GetLength(1)));
                return;
            }

            int mismatchCount = 0;
            for (int bank = 0; bank < a.GetLength(0); bank++)
            {
                for (int leaf = 0; leaf < a.GetLength(1); leaf++)
                {
                    if (Math.Abs(a[bank, leaf] - b[bank, leaf]) > EPSILON_FIELD_MM)
                        mismatchCount++;
                }
            }

            if (mismatchCount > 0)
            {
                mismatches.Add(string.Format("{0}: Leaf pattern differs in {1} leaf position(s) (tolerance {2}mm).",
                    cpLabel, mismatchCount, EPSILON_FIELD_MM));
            }
        }

        private static void CompareDouble(string label, string property, double a, double b, double epsilon, List<string> mismatches)
        {
            double delta = Math.Abs(a - b);
            if (delta > epsilon)
            {
                mismatches.Add(string.Format("{0}: {1} differs ({2:0.###} vs {3:0.###}, Δ{4:0.###} > {5:0.###})",
                    label, property, a, b, delta, epsilon));
            }
        }

        private static void CompareString(string label, string property, string a, string b, List<string> mismatches)
        {
            string sa = a == null ? string.Empty : a;
            string sb = b == null ? string.Empty : b;
            if (!string.Equals(sa, sb, StringComparison.Ordinal))
            {
                mismatches.Add(string.Format("{0}: {1} differs ('{2}' vs '{3}')", label, property, sa, sb));
            }
        }
    }
}
