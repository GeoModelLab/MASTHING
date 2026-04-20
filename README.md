<p align="center">
  <img src="assets/logo.png" alt="MASTHING logo" width="420">
</p>

# MASTHING

**MAS**ting **TH**eory modell**ING** — a process-based model of mast seeding in European beech (*Fagus sylvatica* L.)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/)
[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.XXXXXXX.svg)](https://doi.org/10.5281/zenodo.XXXXXXX)

> After you archive the repository on Zenodo, replace `XXXXXXX` in the badge above with the DOI provided by Zenodo.

---

## Overview

MASTHING is an individual-tree, process-based simulation model of **mast seeding** that couples phenology, carbon gain, resource storage, temperature cues and environmental vetoes on plant reproduction. It is developed within the *General Model of Masting* framework (Bogdziewicz et al., 2024) and parameterized for European beech using 43 years (1980–2022) of seed-production records from 100 trees across 11 UK sites, with prospective evaluation on 2023–2025.

The model supports:

- **Hypothesis testing** on the proximate mechanisms driving masting, by comparing three formulations of reproductive allocation.
- **Climate-change scenarios**: diagnosing the long-term breakdown of masting intensity under rising summer temperatures.
- **Near-term seed forecasting** for forest management and seed-collection planning.

A full description of the model, parameters, calibration procedure and results is given in the companion paper:

> Bregaglio, S., Bajocco, S., Ferrara, C., Bogdziewicz, M., Hacket-Pain, A., Chianucci, F. (2026). *MASTHING: a process-based model of mast seeding in European beech.* [Journal TBD].

## Model formulations

Three alternative hypotheses about resource–climate coupling are implemented and can be selected at runtime via the `modelVersion` configuration setting:

| Version  | Description                                                                                       |
|----------|---------------------------------------------------------------------------------------------------|
| `RB`     | Resource-budget only. Allocation depends solely on internal reserves.                             |
| `RB+WC`  | Resource budget with **additive** post-solstice temperature cues, independent of resource status. |
| `RBxWC`  | Resource budget with **interactive** temperature cues, modulated by internal resource status.     |

In the paper, the interactive formulation (`RBxWC`) provides the best agreement with observed seed-production dynamics (R² = 0.76 at the site-year scale).

## Repository structure

```
MASTHING/
├── MASTHING.sln                  # Visual Studio solution
├── source/                       # Core model library (net8.0 classlib)
│   ├── data/                     # Input / output / parameter data classes
│   ├── functions/
│   │   ├── masting/              # allometry, carbon balance (resources), reproduction
│   │   ├── phenology/            # SWELL phenology (dormancy / growing season)
│   │   └── NDVIdynamics.cs       # NDVI → LAI scaling
│   └── utils.cs                  # shared helpers (astronomy, stress, cues)
├── runner/                       # Console application (simulation + calibration)
│   ├── Program.cs                # entry point
│   ├── optimizer.cs              # objective function for multi-start simplex
│   ├── readers/                  # CSV / JSON I/O
│   │   ├── paramReader.cs        # parameter CSV reader
│   │   ├── referenceReader.cs    # reference NDVI / seed CSV reader
│   │   └── weatherReader.cs      # ERA5 daily weather reader
│   ├── data/                     # Runner-side data containers (Site, parameter)
│   └── MASTHINGconfig.json       # example runtime configuration
├── optimizer/                    # UNIMI multi-start downhill simplex library
├── files/                        # Runtime inputs (create / populate as needed)
│   ├── parametersData/
│   │   ├── SWELLparameters.csv   # phenology parameter table
│   │   └── MASTHINGparameters.csv# carbon balance + reproduction parameter table
│   ├── referenceData/
│   │   ├── referencePhenology.csv
│   │   └── referenceSeeds.csv
│   └── weatherData/              # one CSV per site with daily ERA5 drivers
├── CITATION.cff                  # software citation metadata (used by GitHub + Zenodo)
├── LICENSE                       # MIT licence
├── USER_GUIDE.md                 # step-by-step end-user guide (non-C# developers)
├── PUBLISHING_GUIDE.md           # how this repository was published and archived on Zenodo
└── README.md
```

## Requirements

- **.NET 8.0 SDK** or later — <https://dotnet.microsoft.com/download/dotnet/8.0>
- Optional: **Visual Studio 2022** / **JetBrains Rider** / **VS Code with C# Dev Kit** for IDE support.

### NuGet dependencies

Pulled automatically on `dotnet restore`:

- `MathNet.Numerics` — statistical distributions used in the runner.

## Quick start

### 1. Clone and build

```bash
git clone https://github.com/GeoModelLab/MASTHING.git
cd MASTHING
dotnet restore
dotnet build -c Release
```

### 2. Configure a run

Edit `runner/MASTHINGconfig.json`:

```json
{
  "settings": {
    "calibrationVariable": "seeds",
    "species":             "beech",
    "weatherDirectory":    "..\\..\\..\\files\\weatherData",
    "numberSimplexes":     "5",
    "numberIterations":    "11865678",
    "modelVersion":        "RBxWC",
    "calibrationType":     "single",
    "sitesToRun": [
      "Buckholt", "Benwell", "FishHill", "HimleyHall", "Killerton",
      "Nettlebed", "Painswick", "Patcham", "Ripon", "Spennymoor", "Woodbury"
    ]
  }
}
```

Main options:

- `calibrationVariable`: `phenology` (MODIS NDVI as target) or `seeds` (annual normalised seed production).
- `species`: species tag used to filter the parameter CSVs (e.g. `beech`).
- `modelVersion`: `RB` (resource budget only), `RB+WC` (additive weather cue) or `RBxWC` (interactive weather cue).
- `calibrationType`: `single` (one parameter set per site, loops over `sitesToRun`) or anything else for a single domain-wide calibration across all sites that match the reference data.
- `weatherDirectory`: path (relative to the runner binary working directory) to the folder containing one daily weather CSV per site.
- `numberSimplexes` / `numberIterations`: multi-start Simplex settings (restarts and maximum iterations per restart).
- `sitesToRun`: list of site identifiers to simulate in `single` mode.

Site-level forward simulations are run with the calibrated parameters once the Simplex converges.

### 3. Run

```bash
cd runner
dotnet run -c Release -- MASTHINGconfig.json
```

Calibrated parameter sets are written to `runner/calibratedPixels/<calibrationVariable>/calibParam_<site>_<calibrationType>_<modelVersion>.csv` and daily tree-level trajectories are produced as part of the `oneShot` forward run with the calibrated parameters.

## Input data

MASTHING uses four kinds of input, all CSV-based:

1. **Species parameter tables** — `files/parametersData/SWELLparameters.csv` (phenology) and `files/parametersData/MASTHINGparameters.csv` (carbon balance + reproduction). Each row carries `species, class, name, minimum, maximum, value, calibration`; the `calibration` column is non-empty when the parameter is part of the Simplex calibration subset.
2. **Daily meteorology (ERA5)** — one CSV per site under `files/weatherData/`, named after the site ID used in the reference files. Columns (comma-separated, in this order): `lat, long, date, tmin (°C), tmax (°C), prec (mm)`. Global solar radiation is **not** required: it is estimated internally from Tmax/Tmin via the Hargreaves-Samani relation. Tree-level records share the site-level weather; the reader replicates the first available record backward for 8 years (1970–1978) to provide a spin-up for the state variables.
3. **Reference observations** — `files/referenceData/referencePhenology.csv` carries MODIS NDVI per site/day-of-year (used when `calibrationVariable = phenology`); `files/referenceData/referenceSeeds.csv` carries tree-level annual seed counts and DBH (used when `calibrationVariable = seeds`).
4. **Runtime configuration** — `runner/MASTHINGconfig.json` (see §Configure a run above).

Due to size and licensing considerations, observational data are not included in this repository. They can be obtained from the sources referenced in the paper (MODIS via `MODISTools`; ERA5 via Google Earth Engine / Copernicus; UK beech masting survey via Packham et al., 2008; Bogdziewicz et al., 2020c; 2023; Hacket-Pain et al., 2025). A deposit of the curated dataset will be made available on Zenodo and linked from this page once the paper is published. A minimal `SampleSite` example (one site with synthetic weather and observations) is provided for testing the build.

## Reproducing the paper results

1. Fetch the observational dataset from the Zenodo data deposit (DOI to be added).
2. Place the files under `files/` following the paths referenced in `MASTHINGconfig.json`.
3. Run a **domain-wide calibration** for the phenology module (`calibrationVariable: phenology`, `calibrationType: domain`). The resulting parameters reproduce Supplementary Table S1.6 (phenology section).
4. Using the calibrated phenology, run the **reproduction calibration** under each of the three formulations (`modelVersion: RB`, `RB+WC`, `RBxWC`). This reproduces Figure 5, Figure 6 and Figure 7 of the paper.
5. Run the 2023–2025 **validation** forward (use the `domain` calibrated parameters with `modelVersion: RBxWC` and extend the weather series to 2025) to reproduce Figure 9.

See [`USER_GUIDE.md`](./USER_GUIDE.md) for a longer, step-by-step walkthrough aimed at forest ecologists and non-C# developers.

## How to use MASTHING (for third parties)

If you are not reproducing the paper and simply want to apply MASTHING to **your own sites, species or climate scenarios**, you will almost never need to touch the C# source. Everything the model consumes is driven by CSV / JSON files under `files/` and by `runner/MASTHINGconfig.json`. The typical adaptation workflow is:

### 1. Prepare your weather inputs

Drop one CSV per site into `files/weatherData/`. The expected format is described in §Input data above (`lat, long, date, tmin, tmax, prec`). **Filenames must follow the convention `<lat>_<long>.csv`** (decimal coordinates, underscore as separator) because the runner maps each site to its nearest weather grid via the filename. Global radiation is estimated internally (Hargreaves–Samani) so you do **not** need to supply it.

### 2. Prepare your reference observations

Populate the two CSVs under `files/referenceData/`:

- `referencePhenology.csv` — one row per (site, year, DOY) with `site, year, doy, lat, long, ndvi`. Required only if you plan to calibrate phenology (`calibrationVariable = "phenology"`).
- `referenceSeeds.csv` — one row per (site, tree, year) with `site, year, treeId, lat, long, seeds, dbh`. Required only if you plan to calibrate or validate the reproductive module (`calibrationVariable = "seeds"`). The `dbh` (diameter at 1.3 m, cm) column may be `NA`; a default of 60 cm is applied in that case.

The `site` identifier you put in these CSVs becomes the key MASTHING uses to loop over sites (it is what goes into `sitesToRun` below).

### 3. (Optional) Add or adjust parameters for your species

The two CSVs under `files/parametersData/` already contain parameter ranges calibrated on European beech. To apply MASTHING to a new species:

- Duplicate the existing `beech` rows, change the `species` column to your new species tag (e.g. `oak`, `birch`), and edit the `value`, `minimum`, `maximum` columns to reflect species-specific priors.
- Set `calibration` to any non-empty value (e.g. `x`) for the parameters you want the Simplex to tune; leave it empty to keep them fixed at `value`.
- Set `species` in `MASTHINGconfig.json` to match the tag you used.

The parameter schema (seven columns: `species, class, name, minimum, maximum, value, calibration`) must be preserved — the reader in `runner/readers/paramReader.cs` assumes this layout.

### 4. Edit the configuration

All runtime choices live in `runner/MASTHINGconfig.json`. The most common adjustments are:

- `species` → your species tag (must match the parameter CSVs).
- `calibrationVariable` → `"phenology"` or `"seeds"` depending on which stage you are running.
- `modelVersion` → `"RB"`, `"RB+WC"` or `"RBxWC"` (see the model-formulations table above).
- `calibrationType` → `"single"` (per-site) or anything else for a pooled domain-wide calibration.
- `sitesToRun` → the list of site IDs (as they appear in the reference CSVs) to loop over in `"single"` mode.
- `weatherDirectory` → relative path to `files/weatherData/` from the runner binary folder.
- `numberSimplexes` / `numberIterations` → multi-start Simplex budget (more restarts = more robust, slower).

### 5. Run and read the outputs

After `dotnet run -c Release -- MASTHINGconfig.json` the runner produces two families of files:

- `runner/calibratedPixels/<calibrationVariable>/calibParam_<site>_<calibrationType>_<modelVersion>.csv` — the calibrated parameter vector for that run. These files are consumed as **inputs** by any downstream reproduction calibration (phenology first, seeds second — this is how the coupled calibration chain works).
- `runner/outputsCalibration/calib_<calibrationType>_model_<modelVersion>_<site>_<treeId>.csv` — the full daily trajectory of the simulation: weather inputs, simulated and observed NDVI, LAI, phenology code, stress factors, carbon balance, resource budget, reproductive state and seed production.

Both file families are flat CSVs with headers, so `pandas`, R `readr`, Excel or any other tool works out of the box.

### Files you will typically NOT need to modify

The C# source is stable and parameter-driven. You should not need to touch:

- `source/**` — the core model (phenology, resources, reproduction, NDVI dynamics). Equations and cardinal functions are fully parameterised via the CSVs in §3.
- `runner/readers/**` — the CSV/JSON readers. They assume the schemas documented above; if you follow the conventions you do not need to change them.
- `runner/optimizer.cs` — the objective function and multi-start driver. It is agnostic to site count, tree count and calibration variable.

### When you DO need to touch source

Realistically, you only need to modify source files if you are changing the science:

- Adding a new functional response (e.g. a different cardinal-temperature shape) → edit `source/utils.cs` and the relevant module in `source/functions/`.
- Adding a new parameter → add a row to the relevant CSV and a matching property in the corresponding class under `source/data/` (the reflection-based parameter assignment picks it up automatically).
- Adding a new output column → extend the `output` class in `source/data/output.cs` and the header + row construction in `optimizer.writeOutputsCalibration`.

For deeper modifications, see [`USER_GUIDE.md`](./USER_GUIDE.md) and the per-class XML docstrings embedded in the source.

## Citing MASTHING

If you use MASTHING in your research please cite **both** the software and the companion paper. The [`CITATION.cff`](./CITATION.cff) file in this repository is automatically parsed by GitHub (via the "Cite this repository" button) and Zenodo.

**Software**
```bibtex
@software{bregaglio_masthing_2026,
  author       = {Bregaglio, Simone and Bajocco, Sofia and Ferrara, Carlotta and
                  Bogdziewicz, Michał and Hacket-Pain, Andrew and Chianucci, Francesco},
  title        = {MASTHING: a process-based model of mast seeding in European beech},
  version      = {1.0.0},
  year         = {2026},
  publisher    = {Zenodo},
  doi          = {10.5281/zenodo.XXXXXXX},
  url          = {https://github.com/GeoModelLab/MASTHING}
}
```

**Paper**
```bibtex
@article{bregaglio_masthing_paper_2026,
  author  = {Bregaglio, Simone and Bajocco, Sofia and Ferrara, Carlotta and
             Bogdziewicz, Michał and Hacket-Pain, Andrew and Chianucci, Francesco},
  title   = {MASTHING: a process-based model of mast seeding in European beech},
  year    = {2026},
  journal = {TBD},
  doi     = {10.xxxx/xxxxxx}
}
```

## Contributing

Bug reports, feature requests and pull requests are welcome. Please:

1. Open an issue describing the problem or proposed change before starting major work.
2. Use a feature branch (`feature/<short-name>`) and open a PR against `main`.
3. Keep changes focused and include a short description of the ecological / numerical rationale.

## Authors

- **Simone Bregaglio** (corresponding) — CREA, GeoModel Lab — [simoneugomaria.bregaglio@crea.gov.it](mailto:simoneugomaria.bregaglio@crea.gov.it)
- Sofia Bajocco — CREA, GeoModel Lab
- Carlotta Ferrara — CREA, Research Centre for Forestry and Wood
- Michał Bogdziewicz — Adam Mickiewicz University, Poznań
- Andrew Hacket-Pain — University of Liverpool
- Francesco Chianucci — CREA, Research Centre for Forestry and Wood

## Funding

This work was funded by:

- Italian Ministry of Agriculture, Food Sovereignty and Forestry (MASAF) — SOFIA project (DM 0656382);
- Ferrero Trading Lux — HAZIMUT project;
- European Research Council — ForestFuture (101039066).

Views and opinions expressed are those of the authors only and do not necessarily reflect those of the European Union or the European Research Council.

## License

MASTHING is released under the [MIT License](./LICENSE). You are free to use, modify and redistribute the code, including for commercial purposes, with attribution.
