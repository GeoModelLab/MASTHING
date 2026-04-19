// ===========================================================================
//  MASTHING — runner / entry point
// ---------------------------------------------------------------------------
//  Top-level console application that:
//    1. Reads the JSON configuration file (default: MasThingConfig.json).
//    2. Loads the reference dataset (NDVI for phenology calibration, or
//       seed-production records for reproduction calibration / validation).
//    3. Loads daily meteorological inputs (temperature, precipitation,
//       radiation) from the ERA5-derived CSVs specified in the config.
//    4. Runs either:
//         - calibration mode (multi-start Simplex optimization of the
//           phenological or reproductive parameters), or
//         - validation mode (forward simulation with fixed parameters).
//    5. Writes simulated outputs per tree and per site to disk.
//
//  Model version is selected in the config (modelVersion: "RB", "RB+WC" or
//  "RBxWC") and reproduces the three formulations compared in
//  Bregaglio et al. (2026).
// ===========================================================================

using UNIMI.optimizer;
using source.data;
using runner;
using runner.data;
using System.Text.Json;

#region read the configuration file (pixelConfig.config)
//first CLI argument overrides the default config file name.
string fileName = args.Length > 0 ? args[0] : "MASTHINGconfig.json";

//echo the configuration file path so the log captures exactly which run was launched.
Console.WriteLine("CONFIG FILE: {0}", fileName);

//load the whole JSON into a string and deserialise into the typed root record (see bottom of this file).
string jsonString = File.ReadAllText(fileName);
var config = JsonSerializer.Deserialize<root>(jsonString);

//guard against a malformed / empty config — we need the settings bag to proceed.
if (config?.settings == null)
{
    throw new Exception("Invalid configuration file: missing settings");
}


//calibration target ("phenology" → MODIS NDVI; "seeds" → tree-level seed counts).
string calibrationVariable = config.settings.calibrationVariable;
//calibration mode ("single" → per-site loop; anything else → one pooled calibration).
string calibrationType = config.settings.calibrationType;
Console.WriteLine("CALIBRATION TYPE: {0}", calibrationType);

//model formulation ("RB" / "RB+WC" / "RBxWC").
string modelVersion = config.settings.modelVersion;
Console.WriteLine("MODEL VERSION: {0}", modelVersion);

//folder containing the per-grid daily weather CSVs (named "<lat>_<lon>.csv").
var weatherDir = config.settings.weatherDirectory;
Console.WriteLine("WEATHER DIRECTORY: {0}", weatherDir);

//multi-start simplex hyperparameters — parsed from string to int here once.
int numberSimplexes = int.Parse(config.settings.numberSimplexes);
int numberIterations = int.Parse(config.settings.numberIterations);

//species selector — used to pick the correct rows out of the parameter CSVs.
string species = config.settings.species;
Console.WriteLine("PLANT SPECIES: {0}", species);

#endregion

#region read reference data

//reader responsible for parsing the reference-data CSVs into Site/tree dictionaries.
referenceReader referenceReader = new referenceReader();

//dispatch on calibrationVariable: MODIS NDVI for phenology, beech masting records for seeds.
var allSites = new Dictionary<string, Site>();
if (calibrationVariable == "phenology")
{
    //six-column NDVI CSV: site, year, doy, lat, long, ndvi.
    allSites = referenceReader.readReferenceDataPheno(@"files//referenceData//referencePhenology.csv");
}
else
{
    //seven-column seeds CSV: site, year, treeId, lat, long, seeds, dbh.
    allSites = referenceReader.readReferenceDataSeeds(@"files//referenceData//referenceSeeds.csv");
}


#endregion

#region get all weather files
//progress trace so the user sees the weather-discovery step.
Console.WriteLine("reading weather files....");
//the optimizer is the long-lived object that will hold all the per-run state.
optimizer optimizer = new optimizer();
//push the model formulation onto the optimizer up front.
optimizer.modelVersion = config.settings.modelVersion;

//enumerate all CSVs in the weather directory — the optimizer matches pixels to grids via filename.
var weatherFiles = new DirectoryInfo(weatherDir).GetFiles();
optimizer.allWeatherDataFiles = weatherFiles.Select(file => Path.GetFileName(file.FullName)).ToList();
//and the directory itself so the optimizer can build full paths on demand.
optimizer.weatherDir = weatherDir;
#endregion

#region read MASTHING parameter files

//inventory of already-calibrated output files — used to skip sites that have already been processed.
var calibratedFilesInfo = new DirectoryInfo(@"calibratedPixels").GetFiles();

//trim the file extensions so the list holds plain site identifiers.
List<string> calibratedFiles = calibratedFilesInfo.Select(file => Path.GetFileNameWithoutExtension(file.FullName)).ToList();

//parameter CSV reader — loads the species-specific search envelopes.
paramReader paramReader = new paramReader();
if (calibrationVariable == "phenology")
{
    //phenology calibration only needs the SWELL parameter set.
    optimizer.species_nameParam = paramReader.read(@"files//parametersData//SWELLparameters.csv", species);
}
else
{
    //seeds calibration needs the full MASTHING set …
    optimizer.species_nameParam = paramReader.read(@"files//parametersData//MASTHINGparameters.csv", species);
    //… plus the SWELL (phenology) parameters merged in, so the full model can be exercised in a single pass.
    var swellParams = paramReader.read(@"files//parametersData//SWELLparameters.csv", species);
    foreach (var param in swellParams[species])
    {
        optimizer.species_nameParam[species][param.Key] = param.Value;
    }
}
//container for the per-parameter calibrated values extracted from the simplex results matrix.
Dictionary<string, float> paramCalibValue = new Dictionary<string, float>();
#endregion

//optional list of site ids to calibrate individually (used only when calibrationType == "single").
List<string> sitesToRun = config?.settings?.sitesToRun ?? new List<string>();

//propagate the run-level flags to the optimizer so its branches see them.
optimizer.calibrationVariable = calibrationVariable;
optimizer.calibrationType = calibrationType;

#region define optimizer settings
//multi-start downhill-simplex driver from UNIMI.optimizer.
MultiStartSimplex msx = new MultiStartSimplex();

//number of simplex restarts (controls global exploration).
msx.NofSimplexes = numberSimplexes;
//convergence tolerance on the objective function.
msx.Ftol = 0.001;
//hard cap on iterations per simplex (safety net against stalled runs).
msx.Itmax = numberIterations;
#endregion

#region pixel-level simulations
if (calibrationType == "single")
{
    #region loop over pixels
    //per-site calibration: each iteration independently calibrates one site end-to-end.
    foreach (var site in sitesToRun)
    {
        //alias for clarity.
        string siteID = site;

        //progress trace at the start of each site.
        Console.WriteLine("site {0} start", siteID);

        //filter the full site dictionary down to just this one site for the optimizer.
        optimizer.idSite = allSites.Where(x => x.Key == siteID).ToDictionary(p => p.Key, p => p.Value); ;

        #region define parameters settings for calibration
        //count of active (calibrated) parameters — sets the simplex dimensionality.
        int paramCalibrated = 0;
        //first pass: count parameters whose calibration flag is non-empty.
        foreach (var name in optimizer.species_nameParam[species].Keys)
        {
            //non-empty calibration flag → this parameter joins the simplex.
            if (optimizer.species_nameParam[species][name].calibration != "") { paramCalibrated++; }

        }
        //allocate the (min, max) envelope matrix used by the simplex bounds check.
        double[,] Limits = new double[paramCalibrated, 2];

        //dictionary for parameters that are NOT being calibrated — kept at their fixed CSV value.
        Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
        //second pass: populate Limits[] for active parameters, param_outCalibration for the rest.
        int i = 0;
        foreach (var name in optimizer.species_nameParam[species].Keys)
        {
            if (optimizer.species_nameParam[species][name].calibration != "")
            {
                //[i,1] is the upper bound, [i,0] is the lower bound.
                Limits[i, 1] = optimizer.species_nameParam[species][name].maximum;
                Limits[i, 0] = optimizer.species_nameParam[species][name].minimum;
                i++;
            }
            else
            {
                //latch the nominal value as the fixed non-calibrated value.
                param_outCalibration.Add(name, optimizer.species_nameParam[species][name].value);
            }
        }
        //placeholder out-parameter — the simplex resizes it to (1, paramCalibrated) inside Multistart.
        double[,] results = new double[1, 1];
        #endregion

        //push the species tag and the fixed-parameter dictionary onto the optimizer.
        optimizer.species = species;
        optimizer.param_outCalibration = param_outCalibration;

        //launch the multi-start simplex — this is the heavy lifting.
        msx.Multistart(optimizer, paramCalibrated, Limits, out results);

        //harvest the best parameter vector from the simplex result matrix.
        paramCalibValue = new Dictionary<string, float>();
        int count = 0;

        #region write calibrated parameters
        //CSV schema for the output file.
        string header = "param,value";
        List<string> writeParam = new List<string>();
        writeParam.Add(header);
        //walk the parameters in the same order the optimizer did so indices align with `results`.
        foreach (var param in optimizer.species_nameParam[species].Keys)
        {
            if (optimizer.species_nameParam[species][param].calibration != "")
            {
                //emit a "name,value" row and mirror the value into paramCalibValue for the one-shot run.
                string line = "";
                line += param + ",";
                line += results[0, count];
                writeParam.Add(line);
                paramCalibValue.Add(param, (float)results[0, count]);
                count++;
            }
        }

        //persist the per-site calibrated parameters to calibratedPixels/<variable>/.
        string dir = Path.Combine("calibratedPixels", calibrationVariable);

        //create the destination folder on first run.
        Directory.CreateDirectory(dir);

        //filename encodes site, calibration type and model version so overlapping runs don't collide.
        string filePath = Path.Combine(dir,
            $"calibParam_{siteID.Trim('\"')}_{calibrationType}_{modelVersion}.csv");

        //bulk write the parameter CSV.
        File.WriteAllLines(filePath, writeParam);
        #endregion

        //buffer for the per-day output records produced by the one-shot run below.
        var dateOutputs = new Dictionary<DateTime, output>();
        //replay the simulation with the just-calibrated parameters and dump the daily trajectories.
        optimizer.oneShot(paramCalibValue, out dateOutputs);

        //progress trace at the end of each site.
        Console.WriteLine("site {0} calibrated", siteID);

    }
    #endregion
}
#endregion

#region all pixel simulations
else
{

    #region loop over pixels

    //pooled calibration: all sites feed a single joint objective function.
    optimizer.idSite = allSites;

    #region define parameters settings for calibration
    //count of active parameters — same pattern as the per-site branch above.
    int paramCalibrated = 0;
    foreach (var name in optimizer.species_nameParam[species].Keys)
    {
        //non-empty calibration flag → active parameter.
        if (optimizer.species_nameParam[species][name].calibration != "") { paramCalibrated++; }

    }
    //allocate the simplex bounds matrix.
    double[,] Limits = new double[paramCalibrated, 2];

    //fixed-parameter dictionary (non-calibrated).
    Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
    //populate Limits[] + param_outCalibration in one pass over the parameter set.
    int i = 0;
    foreach (var name in optimizer.species_nameParam[species].Keys)
    {
        if (optimizer.species_nameParam[species][name].calibration != "")
        {
            Limits[i, 1] = optimizer.species_nameParam[species][name].maximum;
            Limits[i, 0] = optimizer.species_nameParam[species][name].minimum;
            i++;
        }
        else
        {
            //fixed value for parameters that should NOT be calibrated.
            param_outCalibration.Add(name, optimizer.species_nameParam[species][name].value);
        }
    }
    //placeholder for the simplex result matrix.
    double[,] results = new double[1, 1];
    #endregion

    //push species tag and fixed-parameter dictionary onto the optimizer.
    optimizer.species = species;

    optimizer.param_outCalibration = param_outCalibration;

    //run the pooled multi-start simplex.
    msx.Multistart(optimizer, paramCalibrated, Limits, out results);

    //harvest the best parameter vector.
    paramCalibValue = new Dictionary<string, float>();
    int count = 0;

    #region write calibrated parameters
    //CSV schema — note the (unused) leading space which the post-processing R code strips.
    string header = "param, value";
    List<string> writeParam = new List<string>();
    writeParam.Add(header);
    //walk parameters in optimizer order so indices align with `results`.
    foreach (var param in optimizer.species_nameParam[species].Keys)
    {
        if (optimizer.species_nameParam[species][param].calibration != "")
        {
            //emit "name,value" + mirror into paramCalibValue for the downstream one-shot run.
            string line = "";
            line += param + ",";
            line += results[0, count];
            writeParam.Add(line);
            paramCalibValue.Add(param, (float)results[0, count]);
            count++;
        }
    }

    //pooled-calibration output folder (one file per variable × model version).
    string dir = Path.Combine("calibratedPixels", calibrationVariable);

    //create the destination folder on first run.
    Directory.CreateDirectory(dir);

    //filename: no per-site suffix because the calibration is pooled.
    string filePath = Path.Combine(
        dir,
        $"calibParam_{calibrationType}_{modelVersion}.csv"
    );

    //bulk write the parameter CSV.
    File.WriteAllLines(filePath, writeParam);
    #endregion

    //buffer for the per-day output records produced by the one-shot run below.
    var dateOutputs = new Dictionary<DateTime, output>();
    //replay the simulation across all sites with the calibrated parameters and dump the daily trajectories.
    optimizer.oneShot(paramCalibValue, out dateOutputs);

    #endregion

}
#endregion

    
#region settings from json

/// <summary>
/// Root element of the <c>MASTHINGconfig.json</c> configuration file.
/// Wraps a single <see cref="settings"/> object that carries every option
/// needed to launch a calibration or validation run.
/// </summary>
public class root
{
    /// <summary>Top-level settings bag parsed from <c>MASTHINGconfig.json</c>.</summary>
    public settings? settings { get; set; }
}

/// <summary>
/// Parameters controlling a single MASTHING run, as deserialised from the
/// JSON configuration file.
/// </summary>
public class settings
{
    /// <summary>Target variable for calibration/validation: "phenology" (MODIS NDVI) or "seeds" (tree-level seed counts).</summary>
    public string? calibrationVariable { get; set; }

    /// <summary>Species tag (e.g. "beech") used to select parameter rows in the CSV files.</summary>
    public string? species { get; set; }

    /// <summary>Path to the folder containing per-site daily weather CSVs (relative to the runner working directory).</summary>
    public string? weatherDirectory { get; set; }

    /// <summary>Number of multi-start Simplex restarts.</summary>
    public string? numberSimplexes { get; set; }

    /// <summary>Maximum number of Simplex iterations per restart.</summary>
    public string? numberIterations { get; set; }

    /// <summary>"single" for a per-site calibration loop; any other value triggers a single domain-wide calibration.</summary>
    public string? calibrationType { get; set; }

    /// <summary>Model version selector: "RB" (resource-budget only), "RB+WC" (additive weather cue) or "RBxWC" (interactive weather cue).</summary>
    public string? modelVersion { get; set; }

    /// <summary>List of site identifiers to simulate in "single" calibration mode.</summary>
    public List<string> sitesToRun { get; set; }
}
#endregion

