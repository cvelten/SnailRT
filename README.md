# SnailRT

> Tools and documentation supporting the NRG CC-017 clinical trial of temporally modulated pulsed radiotherapy (TMPRT).

## Background

### SnailRT

Temporally modulated pulsed radiotherapy (TMPRT) - a.k.a. SnailRT - delivers conventional fraction doses of radiation as temporally separated pulses of low dose (<30 cGy each), keeping the instantaneous field dose below the threshold at which the ATM pathway activates for DNA repair. This yields a fraction-effective dose rate of approximately 6.7 cGy min⁻¹ — exploiting the well-documented hypersensitivity of tumor cells, including glioblastoma, to low-dose-rate irradiation. The technique has been validated in preclinical models and prospective single-arm clinical trials and was recently endorsed as an appropriate radiotherapy technique by ASTRO.

**[NRG CC-017](https://clinicaltrials.gov/study/NCT07452458)** (activated April 1st, 2026) is a randomized phase III trial conducted through NRG Oncology comparing TMPRT + temozolomide versus standard radiotherapy + temozolomide in adults with newly diagnosed MGMT-unmethylated glioblastoma. The principal investigator is Jiayi Huang, MD (Washington University School of Medicine); the medical physics chair is Wolfgang Tomé, PhD (Montefiore–Einstein Comprehensive Cancer Center / Albert Einstein College of Medicine).

To facilitate treatment planning we have published a primer on planning techniques using Eclipse and RayStation. The pre-print can be found on [arXiv:2511.19329](https://arxiv.org/abs/2511.19329) with the final publication in _Practical Radiation Oncology_:

> Christian Velten, Adam Bayliss, Jiayi Huang, Wolfgang A. Tomé. \
A primer on treatment planning aspects for temporally modulated pulsed radiation therapy. \
_Practical Radiation Oncology_. 2026 \
[doi:10.1016/j.prro.2026.04.009](https://doi.org/10.1016/j.prro.2026.04.009)

---

## Sub-projects

| Sub-project | Description |
|---|---|
| **[SnailEclipse](SnailEclipse/README.md)** | Eclipse (ESAPI) plugins for SnailRT planning: **PlanPulseSetup** creates a multi-pulse plan from a single-pulse plan (C-arm VMAT/DCA only; dose must then be calculated with preset MU), and **BeamEquivalenceChecker** checks that all pulses in a plan are identical within tolerance. |

## References

- Planning primer: [doi:10.1016/j.prro.2026.04.009](https://doi.org/10.1016/j.prro.2026.04.009)
- SnailEclipse (ESAPI tools): [`SnailEclipse/README.md`](SnailEclipse/README.md)

## License

[SnailEclipse](SnailEclipse/LICENSE) are licensed under the [GNU General Public License v3.0 only](https://www.gnu.org/licenses/gpl-3.0.html) (`SPDX-License-Identifier: GPL-3.0-only`).

> [!CAUTION]
> **Disclaimer.** This software is provided "as is", without warranty of any kind, express or implied, including but not limited to merchantability, fitness for a particular purpose, and non-infringement. The authors and contributors accept no liability for any claim, damages, or other liability, including patient harm, arising from its use. It is research software, **not a medical device**, is not cleared or approved by the FDA or any other regulatory body, and is not intended for clinical decision-making. Independently verify all outputs before any clinical use; responsibility for that use rests solely with the user.
