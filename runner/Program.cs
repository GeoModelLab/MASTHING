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
string fileName = args.Length > 0 ? args[0] : "MASTHINGconfig.json";

Console.WriteLine("CONFIG FILE: {0}", fileName);

string jsonString = File.ReadAllText(fileName);
var config = JsonSerializer.Deserialize<root>(jsonString);

if (config?.settings == null)
{
    throw new Exception("Invalid configuration file: missing settings");
}


//set calibration variable
string calibrationVariable = config.settings.calibrationVariable;
string calibrationType = config.settings.calibrationType;
Console.WriteLine("CALIBRATION TYPE: {0}", calibrationType);

//set model version
string modelVersion = config.settings.modelVersion;
Console.WriteLine("MODEL VERSION: {0}", modelVersion);

//set weather directory
var weatherDir = config.settings.weatherDirectory;
Console.WriteLine("WEATHER DIRECTORY: {0}", weatherDir);

//set simplex parameters
int numberSimplexes = int.Parse(config.settings.numberSimplexes);
int numberIterations = int.Parse(config.settings.numberIterations);

//set species
string species = config.settings.species;
Console.WriteLine("PLANT SPECIES: {0}", species);

#endregion

#region read reference data

//instance of reference reader class
referenceReader referenceReader = new referenceReader();

//read reference data
var allSites = new Dictionary<string, Site>();
if (calibrationVariable == "phenology")
{
    allSites = referenceReader.readReferenceDataPheno(@"files//referenceData//referencePhenology.csv");
}
else
{
    allSites = referenceReader.readReferenceDataSeeds(@"files//referenceData//referenceSeeds.csv");
}


#endregion

#region get all weather files
//message to console
Console.WriteLine("reading weather files....");
//optimizer class
optimizer optimizer = new optimizer();
optimizer.modelVersion = config.settings.modelVersion;

//read weather files
var weatherFiles = new DirectoryInfo(weatherDir).GetFiles();
optimizer.allWeatherDataFiles = weatherFiles.Select(file => Path.GetFileName(file.FullName)).ToList();
optimizer.weatherDir = weatherDir;            
#endregion

#region read MASTHING parameter files

//list of already calibrated files
var calibratedFilesInfo = new DirectoryInfo(@"calibratedPixels").GetFiles();

// Convert FileInfo[] to List<string>
List<string> calibratedFiles = calibratedFilesInfo.Select(file => Path.GetFileNameWithoutExtension(file.FullName)).ToList();

//read parameter file with limits
paramReader paramReader = new paramReader();
if (calibrationVariable == "phenology")
{
    optimizer.species_nameParam = paramReader.read(@"files//parametersData//SWELLparameters.csv", species);
}
else
{
    optimizer.species_nameParam = paramReader.read(@"files//parametersData//MASTHINGparameters.csv", species);
    // Merge SWELL (phenology) parameters into the main species_nameParam dictionary
    var swellParams = paramReader.read(@"files//parametersData//SWELLparameters.csv", species);
    foreach (var param in swellParams[species])
    {
        optimizer.species_nameParam[species][param.Key] = param.Value;
    }
}
//data structure to store calibrated parameters
Dictionary<string, float> paramCalibValue = new Dictionary<string, float>();
#endregion

//set start pixel and number of pixels (for calibration)
List<string> sitesToRun = config?.settings?.sitesToRun ?? new List<string>();

//set calibration variable
optimizer.calibrationVariable = calibrationVariable;
optimizer.calibrationType = calibrationType;

#region define optimizer settings
//optimizer instance
MultiStartSimplex msx = new MultiStartSimplex();

msx.NofSimplexes = numberSimplexes;
msx.Ftol = 0.001;
msx.Itmax = numberIterations;
#endregion

#region pixel-level simulations
if (calibrationType == "single")
{
    #region loop over pixels
    foreach (var site in sitesToRun)
    {
        //get pixel ID
        string siteID = site;

        //message to console
        Console.WriteLine("site {0} start", siteID);

        //set pixel to calibrate
        optimizer.idSite = allSites.Where(x => x.Key == siteID).ToDictionary(p => p.Key, p => p.Value); ;

        #region define parameters settings for calibration
        //count parameters under calibration
        int paramCalibrated = 0;
        //loop over parameters
        foreach (var name in optimizer.species_nameParam[species].Keys)
        {
            //add parameter to calibration if calibration field is not empty (x)
            if (optimizer.species_nameParam[species][name].calibration != "") { paramCalibrated++; }

        }
        //set number of dimension in the matrix
        double[,] Limits = new double[paramCalibrated, 2];

        //parameters out of calibration
        Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
        //populate limits 
        //count parameters under calibration
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
                param_outCalibration.Add(name, optimizer.species_nameParam[species][name].value);
            }
        }
        double[,] results = new double[1, 1];
        #endregion

        //set optimizer calibration properties
        optimizer.species = species;
        optimizer.param_outCalibration = param_outCalibration;

        //run optimizer
        msx.Multistart(optimizer, paramCalibrated, Limits, out results);

        //get calibrated parameters
        paramCalibValue = new Dictionary<string, float>();
        int count = 0;

        #region write calibrated parameters
        string header = "param,value";
        List<string> writeParam = new List<string>();
        writeParam.Add(header);
        foreach (var param in optimizer.species_nameParam[species].Keys)
        {
            if (optimizer.species_nameParam[species][param].calibration != "")
            {
                //write a line for each parameter
                string line = "";
                line += param + ",";
                line += results[0, count];
                writeParam.Add(line);
                paramCalibValue.Add(param, (float)results[0, count]);
                count++;
            }
        }

        //write calibrated parameters to file
        // directory
        string dir = Path.Combine("calibratedPixels", calibrationVariable);

        // create the directory if it does not exist
        Directory.CreateDirectory(dir);

        // file path
        string filePath = Path.Combine(dir,
            $"calibParam_{siteID.Trim('\"')}_{calibrationType}_{modelVersion}.csv");

        // write the parameter file
        File.WriteAllLines(filePath, writeParam);
        #endregion

        //empty dictionary of dates and outputs objects
        var dateOutputs = new Dictionary<DateTime, output>();
        //execute model with calibrated parameters
        optimizer.oneShot(paramCalibValue, out dateOutputs);

        //message to console
        Console.WriteLine("site {0} calibrated", siteID);

    }
    #endregion
}
#endregion

#region all pixel simulations
else
{

    #region loop over pixels

    //set pixel to calibrate
    optimizer.idSite = allSites;

    #region define parameters settings for calibration
    //count parameters under calibration
    int paramCalibrated = 0;
    //loop over parameters
    foreach (var name in optimizer.species_nameParam[species].Keys)
    {
        //add parameter to calibration if calibration field is not empty (x)
        if (optimizer.species_nameParam[species][name].calibration != "") { paramCalibrated++; }

    }
    //set number of dimension in the matrix
    double[,] Limits = new double[paramCalibrated, 2];

    //parameters out of calibration
    Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
    //populate limits 
    //count parameters under calibration
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
            param_outCalibration.Add(name, optimizer.species_nameParam[species][name].value);
        }
    }
    double[,] results = new double[1, 1];
    #endregion

    //set optimizer calibration properties
    optimizer.species = species;
  
    optimizer.param_outCalibration = param_outCalibration;

    //run optimizer
    msx.Multistart(optimizer, paramCalibrated, Limits, out results);

    //get calibrated parameters
    paramCalibValue = new Dictionary<string, float>();
    int count = 0;

    #region write calibrated parameters
    string header = "param, value";
    List<string> writeParam = new List<string>();
    writeParam.Add(header);
    foreach (var param in optimizer.species_nameParam[species].Keys)
    {
        if (optimizer.species_nameParam[species][param].calibration != "")
        {
            //write a line for each parameter
            string line = "";
            line += param + ",";
            line += results[0, count];
            writeParam.Add(line);
            paramCalibValue.Add(param, (float)results[0, count]);
            count++;
        }
    }

    //write calibrated parameters to file
    // directory
    string dir = Path.Combine("calibratedPixels", calibrationVariable);

    // create the directory if it does not exist
    Directory.CreateDirectory(dir);

    // file path
    string filePath = Path.Combine(
        dir,
        $"calibParam_{calibrationType}_{modelVersion}.csv"
    );

    // write the parameter file
    File.WriteAllLines(filePath, writeParam);
    #endregion

    //empty dictionary of dates and outputs objects
    var dateOutputs = new Dictionary<DateTime, output>();
    //execute model with calibrated parameters
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

