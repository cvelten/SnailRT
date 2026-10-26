# SnailEclipse — ESAPI tools for SnailRT

[![License: GPL-3.0-only](https://img.shields.io/badge/license-GPL--3.0--only-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET_Framework-512BD4?logo=dotnet&logoColor=fff)](#)
[![C#](https://custom-icon-badges.demolab.com/badge/C%23-%23239120.svg?logo=cshrp&logoColor=white)](#)
[![ESAPI](https://img.shields.io/badge/ESAPI-ec6603?logo=&logoColor=fff)](#)

Eclipse Scripting API (ESAPI) plugins to help plan SnailRT / TMPRT (NRG CC-017) treatments in Varian Eclipse. See the [planning primer](../NRG_CC-017/2511.19329v2.pdf) for the planning technique these tools support.

| Tool | Type | Writes to database | Purpose |
|---|---|---|---|
| [PlanPulseSetup](#planpulsesetup) | Binary plugin (`SnailRT-PlanPulseSetup.esapi.dll`) | Yes | Builds a multi-pulse plan from a single-pulse plan |
| [BeamEquivalenceChecker](#beamequivalencechecker) | Single-file script (`.cs`) or binary plugin (`SnailRT-BeamEquivalenceChecker.esapi.dll`) | No | Checks that all pulses (beams) in a plan are identical |

The usual workflow is: plan and optimize **one pulse** → run **PlanPulseSetup** → calculate dose with **preset MU** → run **BeamEquivalenceChecker** on the new plan.

> [!WARNING]
> These tools are research tools and are not clinically validated. Always review the generated plan and verify it independently before clinical use.

---

## PlanPulseSetup

Takes the open single-pulse plan and creates a new plan in the same course, on the same structure set, with the pulse beam copied *N* times. The original plan is not changed.

### Requirements for the source plan

- External-beam plan with **exactly one** treatment beam (setup and imaging fields are ignored).
- The beam is a **VMAT** or **dynamic conformal arc (DCA)** field.
- **C-arm linacs only.** Halcyon/Ethos (SX2 MLC) plans are rejected, because copying leaf positions through ESAPI does not work fully on those machines.
- The beam has valid MU (calculate the pulse plan first).
- **DCA only:** the copied pulses always get evenly spaced meterset weights, because ESAPI's `AddConformalArcBeam` does that and the weights aren't copied. If the source DCA's weights are not evenly spaced, the summary shows a warning and the pulses will not match the source beam.
- The plan has at least 2 fractions, and the number of pulses divides the number of fractions exactly.

### Usage

1. Open the single-pulse plan (e.g. 20 cGy × 230 fx) and run the plugin from *Tools → Scripts*.
2. In the dialog, enter the **number of pulses per fraction** (default 10) and choose whether to **reverse the arc direction on every other pulse** (on by default, as the primer recommends).
3. The plugin creates the new plan and shows a summary.
4. **Calculate dose with preset (fixed) MU.** The plugin does not calculate dose.
5. Add setup fields by hand. They are not copied.

### What the new plan contains

| Property | Value |
|---|---|
| Plan ID | `<source ID>_P`, cut to 13 characters and given a number suffix if that ID is already taken |
| Fractions | source fractions ÷ *N* |
| Dose per fraction | source dose per fraction × *N* |
| Treatment percentage, photon calculation model | copied from the source plan |
| Beams | `P01` … `P<N>`, each with the source beam's MU, jaws and leaf positions at every control point, collimator angle, couch angle and isocenter |
| Alternating direction | even-numbered pulses run in the opposite gantry direction; their control points are reversed and the cumulative meterset weights are mirrored (`total − w`) |

Example: 20 cGy × 230 fx with *N* = 10 → 23 fx × 200 cGy, 10 beams per fraction.

### Build and approval

- Targets .NET Framework 4.8 (x64), C# 7.3, built against ESAPI **v18.1** by default. For another version, run `msbuild /p:EsapiVersion=v16.1`.
- The build looks for `API\<EsapiVersion>\VMS.TPS.Common.Model.API.dll` (and `…Types.dll`) in any parent folder. The `API/` folder is git-ignored, so copy the DLLs from your Eclipse installation into `SnailEclipse/API/<version>/`.
- The output is `SnailRT-PlanPulseSetup.esapi.dll`. The plugin writes to the database, so an administrator must approve it in **Eclipse Script Approvals**. Increase `AssemblyVersion` in the `.cs` file for every change that needs a new approval.
- `SnailRT-PlanPulseSetup.v001r0.dbg.esapi.dll` in this folder is a prebuilt Debug build of version 0.0.1.

---

## BeamEquivalenceChecker

Read-only script. It compares every treatment beam in the open plan with every other treatment beam and reports any differences. Use it to confirm that all pulses in a plan built with PlanPulseSetup (or by hand) deliver the same thing.

### Usage

Open the plan and run `SnailRT-BeamEquivalenceChecker.cs` as a single-file script, or the built `SnailRT-BeamEquivalenceChecker.esapi.dll`, from *Tools → Scripts*. Both are read-only. The binary builds the same way as PlanPulseSetup (same `.csproj` setup) and, because it doesn't write to the database, does not need approval in Script Approvals. It needs at least 2 treatment beams. It shows either "all beams equivalent" or a list of differences (up to 25, then a count of the rest).

### What is compared

| Check | Tolerance |
|---|---|
| Technique, energy mode, dose rate | exact |
| MU | 0.05 MU |
| Isocenter X/Y/Z | 0.1 mm |
| Average SSD | 0.5 mm |
| Wedges (ID, angle, direction) | exact ID, 0.1° |
| Number of control points | exact (if they differ, the remaining checks for that pair are skipped) |
| Per control point: gantry, collimator and couch angle | 0.1° |
| Per control point: jaws X1/X2/Y1/Y2 | 0.1 mm |
| Per control point: every leaf position | 0.1 mm |
| Per control point: cumulative meterset weight | 0.05 MU ÷ beam MU |

**Arc direction:** when one beam is clockwise and the other is counter-clockwise, the second beam's control points are reversed and its meterset weights are mirrored before the comparison. This means the alternating-direction plans from PlanPulseSetup should pass.

The script avoids C# 6+ syntax (`?.`, `??`, `is null`) so that older Eclipse single-file script compilers can build it.

---

## License

Licensed under the GNU General Public License v3.0 only (`SPDX-License-Identifier: GPL-3.0-only`). See [LICENSE](LICENSE).

> [!CAUTION]
> **Disclaimer.** This software is provided "as is", without warranty of any kind, express or implied, including but not limited to merchantability, fitness for a particular purpose, and non-infringement. The authors and contributors accept no liability for any claim, damages, or other liability, including patient harm, arising from its use. It is research software, **not a medical device**, is not cleared or approved by the FDA or any other regulatory body, and is not intended for clinical decision-making. Independently verify all outputs before any clinical use; responsibility for that use rests solely with the user.
