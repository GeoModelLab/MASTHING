# MASTHING — User Guide

This guide walks you from a fresh laptop to a full MASTHING calibration +
forward run on your own data. It assumes no prior experience with C# or
.NET and is aimed at forest ecologists and quantitative biologists who
want to use the model rather than modify it.

If something doesn't work, jump straight to the [Troubleshooting](#troubleshooting)
section at the end, or open an issue on GitHub.

---

## Contents

1. [What MASTHING does](#1-what-masthing-does)
2. [System requirements](#2-system-requirements)
3. [Install the .NET 6 SDK](#3-install-the-net-6-sdk)
4. [Get the code](#4-get-the-code)
5. [Build MASTHING](#5-build-masthing)
6. [Organise the input files](#6-organise-the-input-files)
7. [Configure a run](#7-configure-a-run)
8. [Run a calibration](#8-run-a-calibration)
9. [Run a forward / validation simulation](#9-run-a-forward--validation-simulation)
10. [Understand the output files](#10-understand-the-output-files)
11. [Switch between model versions (RB / RB+WC / RBxWC)](#11-switch-between-model-versions)
12. [Plotting the results](#12-plotting-the-results)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. What MASTHING does

MASTHING simulates, day by day and tree by tree, the seasonal cycle of a
beech tree (*Fagus sylvatica*) and predicts, at the end of each year, how
many seeds the tree sets. The model is process-based: it couples

- a phenology module (SWELL) that tracks the dormancy → bud break →
  growth → senescence cycle from ERA5 daily weather;
- a carbon-balance module that accumulates GPP minus maintenance
  respiration into a non-structural reservoir, modulated by heat, cold
  and water stress;
- a reproductive module that decides how much of the reservoir to invest
  in flowering at the summer solstice, applies a precipitation-driven
  veto on pollination, and ripens fruits along the greendown phenophase.

Three alternative hypotheses about how trees "decide" to invest are
implemented; you select which one to run via one JSON setting
(`modelVersion`). See [§11](#11-switch-between-model-versions).

---

## 2. System requirements

MASTHING is a small, cross-platform console application. You need:

- A laptop or workstation running **Windows 10/11**, **macOS 12+** or a
  recent **Linux** distribution (Ubuntu 20.04+ tested).
- Roughly **2 GB of free disk space** (mostly for the .NET SDK).
- An internet connection the first time you build (to download the .NET
  SDK and the NuGet packages referenced by the project).
- A text editor. Any plain editor works for the JSON configuration; for
  editing C# code comfortably we suggest **Visual Studio Code** with the
  official *C# Dev Kit* extension, or the free edition of
  **Visual Studio 2022** (Windows) or **JetBrains Rider**.

Runtime is usually a few minutes per site for a forward simulation and a
few tens of minutes for a single-site multi-start calibration on a
modern laptop.

---

## 3. Install the .NET 6 SDK

MASTHING targets .NET 6.0 (LTS).

- **Windows / macOS**: download the SDK installer from
  <https://dotnet.microsoft.com/download/dotnet/6.0> and follow the
  wizard.
- **Linux (Ubuntu)**: follow the apt instructions at the same URL, e.g.
  ```bash
  sudo apt-get install -y dotnet-sdk-6.0
  ```

Verify the installation by opening a terminal / PowerShell and running:

```bash
dotnet --list-sdks
```

You should see a `6.0.x` line. Anything newer (`7.0`, `8.0`) also works
because .NET is backward compatible, but 6.0 is what we test against.

---

## 4. Get the code

Two options.

**Option A — with git (recommended):**

```bash
git clone https://github.com/GeoModelLab/MASTHING.git
cd MASTHING
```

**Option B — as a zip:** click the green *Code* button on the GitHub
page → *Download ZIP*, then unzip somewhere convenient and open a
terminal in the extracted `MASTHING-main/` folder.

Throughout the rest of this guide we assume your current working
directory is the repository root (the folder that contains
`MASTHING.sln`).

---

## 5. Build MASTHING

From the repository root:

```bash
dotnet restore
dotnet build -c Release
```

`dotnet restore` downloads the NuGet dependencies (mostly
`MathNet.Numerics` for statistical utilities). `dotnet build -c Release`
compiles the `source` class library and the `runner` console
application in release mode.

If the build completes with `Build succeeded.` you're ready to go. The
runner binary is produced under
`runner/bin/Release/net6.0/runner.exe` (or `runner` on macOS/Linux).

---

## 6. Organise the input files

MASTHING expects a specific folder layout under the repository root.
The runner reads inputs from paths **relative to its working
directory**, so the examples below assume you launch the runner from
the `runner/` folder (which is what `dotnet run` does by default).

```
MASTHING/
├── files/
│   ├── parametersData/
│   │   ├── SWELLparameters.csv
│   │   └── MASTHINGparameters.csv
│   ├── referenceData/
│   │   ├── referencePhenology.csv   # needed for calibrationVariable = "phenology"
│   │   └── referenceSeeds.csv       # needed for calibrationVariable = "seeds"
│   └── weatherData/
│       ├── Benwell.csv
│       ├── Buckholt.csv
│       └── ...                     # one CSV per site
└── runner/
    └── MASTHINGconfig.json
```

### 6.1 Parameter tables

`SWELLparameters.csv` (phenology) and `MASTHINGparameters.csv`
(carbon balance + reproduction) share the same column layout:

| column        | meaning                                                                                |
|---------------|----------------------------------------------------------------------------------------|
| `species`     | species tag; the runner filters rows by this value (must match your config)           |
| `class`       | parameter group (e.g. `parGrowth`, `parEndodormancy`, `parResources`, `parReproduction`)|
| `name`        | parameter name (must match a property name in the corresponding C# class)             |
| `minimum`     | lower bound used by the Simplex optimizer                                              |
| `maximum`     | upper bound used by the Simplex optimizer                                              |
| `value`       | nominal / starting value                                                               |
| `calibration` | non-empty (conventionally `x`) to include the parameter in the calibration subset      |

Leave `calibration` empty (or set it to any empty string) to freeze a
parameter at `value` during calibration.

### 6.2 Reference data

`referenceSeeds.csv` columns (one row per tree-year observation):

```
siteID, year, treeID, latitude, longitude, seeds, DBH
```

`seeds` is the annual normalised seed count used as calibration target;
`DBH` is the trunk diameter at 1.30 m (cm); use `NA` where the tree has
not been measured.

`referencePhenology.csv` columns (one row per MODIS observation):

```
siteID, year, dayOfYear, latitude, longitude, NDVInormalized
```

### 6.3 Weather data

One CSV per site under `files/weatherData/`. The file **must** be named
exactly like the `siteID` used in the reference CSVs (plus the `.csv`
extension). The columns expected by `weatherReader` are (see
`runner/readers/weatherReader.cs` for details):

```
date, airTemperatureMaximum, airTemperatureMinimum, precipitation, solarRadiation
```

Date format is ISO-like (`yyyy-MM-dd`), temperatures are in °C,
precipitation in mm day-1, global solar radiation in MJ m-2 day-1.
Daily ERA5 reanalysis downloads from Copernicus or Google Earth Engine
are a convenient source.

---

## 7. Configure a run

The runner is driven by a single JSON file,
`runner/MASTHINGconfig.json`. The default file shipped with the
repository looks like this:

```json
{
  "settings": {
    "calibrationVariable": "seeds",
    "species":             "beech",
    "weatherDirectory":    "..\\..\\..\\files\\weatherData",
    "numberSimplexes":     "5",
    "numberIterations":    "11865678",
    "modelVersion":        "RB",
    "calibrationType":     "single",
    "sitesToRun":          ["SampleSite"]
  }
}
```

Meaning of each field:

- **`calibrationVariable`** — `"phenology"` (calibrate against MODIS
  NDVI) or `"seeds"` (calibrate against tree-level seed counts). Also
  determines which reference CSV the runner tries to read.
- **`species`** — species tag. Must match the value in the `species`
  column of the parameter CSVs.
- **`weatherDirectory`** — path to the weather folder, relative to the
  runner binary working directory. The default
  `..\..\..\files\weatherData` walks from
  `runner/bin/Release/net6.0/` back up to the repository root and then
  into `files/weatherData/`.
- **`numberSimplexes`** — number of multi-start restarts of the
  downhill Simplex. 5–10 is a reasonable trade-off.
- **`numberIterations`** — maximum number of Simplex iterations per
  restart. Convergence usually stops earlier thanks to the internal
  `Ftol = 0.001` tolerance; keep the default high value as a safety cap.
- **`modelVersion`** — `"RB"`, `"RB+WC"` or `"RBxWC"` (see §11).
- **`calibrationType`** — `"single"` to loop over `sitesToRun` and
  produce one parameter set per site, anything else (e.g. `"domain"`)
  to calibrate a single parameter set across all sites present in the
  reference CSV.
- **`sitesToRun`** — list of site IDs to simulate in `"single"` mode.
  The runner ignores sites that do not appear in the reference CSV.

Tips:
- Forward-slashes also work on Windows
  (`"files/weatherData"`) — the runner uses `DirectoryInfo` to resolve
  the path.
- You can keep several config files side by side (e.g.
  `config_phenology.json`, `config_seeds_RBxWC.json`) and pass the one
  you want as a command-line argument (see §8).

---

## 8. Run a calibration

From the `runner/` folder:

```bash
cd runner
dotnet run -c Release -- MASTHINGconfig.json
```

The `--` separates the arguments to `dotnet run` from the arguments
passed to the runner itself. `MASTHINGconfig.json` is the path to your
configuration file, relative to the runner working directory.

While the run is in progress the runner prints to stdout:

```
CONFIG FILE: MASTHINGconfig.json
CALIBRATION TYPE: single
MODEL VERSION: RB
WEATHER DIRECTORY: ..\..\..\files\weatherData
PLANT SPECIES: beech
reading weather files....
site SampleSite start
...
site SampleSite calibrated
```

Each `site X calibrated` message marks the end of the Simplex
optimization for that site; the runner then triggers a forward
simulation with the calibrated parameters via `optimizer.oneShot`.

---

## 9. Run a forward / validation simulation

The refactored runner always performs a forward simulation
(`oneShot`) after calibration, using the best parameters found by the
Simplex. To run a *pure* forward simulation with a pre-existing
parameter set, two options are available:

1. **Lower the Simplex work**: set `numberSimplexes = 1` and
   `numberIterations = 1`. The Simplex returns almost immediately and
   the subsequent `oneShot` uses the nominal values from the parameter
   CSVs (or the near-initial Simplex vertex).
2. **Freeze parameters**: clear the `calibration` column for all rows
   in `SWELLparameters.csv` and `MASTHINGparameters.csv` so that no
   parameter is optimised. The Simplex has nothing to vary, the
   `oneShot` falls back on the tabulated `value`, and the run is
   effectively a forward simulation.

Option 2 is the recommended way to reproduce the 2023–2025 prospective
validation described in the paper, once you have frozen the
domain-calibrated parameter set into the `value` column.

---

## 10. Understand the output files

The runner writes a calibrated-parameter CSV and a set of per-day
model outputs.

### Calibrated parameters

For `calibrationType = "single"`, one CSV per site under:

```
runner/calibratedPixels/<calibrationVariable>/calibParam_<siteID>_single_<modelVersion>.csv
```

For any other `calibrationType`, a single CSV:

```
runner/calibratedPixels/<calibrationVariable>/calibParam_<calibrationType>_<modelVersion>.csv
```

Each file has a simple `param,value` header: one row per calibrated
parameter, identified by its `class_name` key (e.g.
`parGrowth_optimumTemperature`). You can paste these values back into
the `value` column of the parameter CSV to freeze them for subsequent
forward runs.

### Daily / annual simulation outputs

The `optimizer.oneShot` method writes daily tree-level trajectories
(phenology, carbon balance, reproductive state) and annual aggregations
to the working directory. The exact file names and columns are defined
in `runner/optimizer.cs`; for casual use the most interesting files are
the annual seed predictions per tree, which can be joined directly with
the reference seed CSV for scoring.

---

## 11. Switch between model versions

MASTHING implements three increasingly rich formulations of the
reproductive allocation rule:

| `modelVersion` | Interpretation                                                                        | When to use                                                  |
|----------------|---------------------------------------------------------------------------------------|--------------------------------------------------------------|
| `RB`           | Resource-budget only: investment is a monotonic function of the stored carbon reservoir. | Null hypothesis; classical mast-as-overflow story.           |
| `RB+WC`        | Adds an **additive** post-solstice temperature cue to the investment decision.        | Tests whether weather cues add signal on top of reserves.    |
| `RBxWC`        | Makes the temperature cue **interact** with the resource status.                      | Best agreement with observed masting in Bregaglio et al. 2026.|

To switch, change the `modelVersion` field in the JSON config file and
rerun the calibration. The output file names embed the model version,
so each calibration run lives in its own CSV and you can compare the
three fits side by side.

---

## 12. Plotting the results

MASTHING does not include a plotting backend. The suggested workflow is
to load the calibrated-parameter CSVs and the daily tree-level output
files into **Python** (with `pandas`, `matplotlib`, `scikit-learn`) or
**R** (with `tidyverse`, `ggplot2`). Two plots to start with:

1. Observed vs. simulated annual seeds per tree, with a 1:1 reference
   line (e.g. Figure 5 in the paper).
2. Simulated non-structural resource budget vs. time for a focal tree,
   overlaid with flowering years (Figure 6), to inspect the budget →
   investment mechanism.

A minimal Python snippet:

```python
import pandas as pd
import matplotlib.pyplot as plt

obs = pd.read_csv("files/referenceData/referenceSeeds.csv")
sim = pd.read_csv("runner/annualSeedsSimulated.csv")  # name produced by oneShot

merged = obs.merge(sim, on=["siteID", "treeID", "year"])
plt.scatter(merged["seeds"], merged["seedsSim"])
plt.plot([0, 1], [0, 1], "k--")
plt.xlabel("Observed seeds (normalised)")
plt.ylabel("Simulated seeds")
plt.tight_layout()
plt.show()
```

---

## 13. Troubleshooting

**`dotnet: command not found`** — the SDK is not on PATH. Restart your
terminal, or on macOS/Linux add `$HOME/.dotnet` to your shell PATH.

**`FileNotFoundException: SWELLparameters.csv`** — the runner expects
the `files/` folder next to the repository root, and it resolves paths
relative to the binary working directory
(`runner/bin/Release/net6.0/`). The default JSON uses
`..\..\..\files\weatherData` for that reason. Run the program via
`dotnet run` from the `runner/` folder to keep the working directory
consistent.

**`FormatException` when parsing CSVs** — likely a locale-driven decimal
separator issue. MASTHING expects `.` as decimal separator. On Windows
you may need to export the CSVs with `CultureInfo.InvariantCulture` or
change your regional settings temporarily.

**Unrealistic seed predictions, or flat trajectories** — check that
your weather CSVs cover the entire period of the reference observations
(including a full year of spin-up before the first observation) and that
the `siteID` in the reference CSV exactly matches the weather-file
basename.

**Calibration is too slow** — lower `numberSimplexes` (3 is often
enough for exploratory runs), increase `Ftol` in `runner/Program.cs`
(e.g. from `0.001` to `0.01`), or restrict `calibration` flags in the
parameter CSV to the parameters you genuinely want to tune.

**The runner runs but writes no output** — make sure the
`calibratedPixels/` folder is writable and that your disk has free
space. The runner creates the output directory if it does not exist,
but it will fail silently on permission errors on some systems.

---

For anything else, please open an issue on
<https://github.com/GeoModelLab/MASTHING/issues> with a minimal
reproducer (config + a small slice of the input CSVs) and the console
output you see.
