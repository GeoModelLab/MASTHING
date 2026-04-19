using UNIMI.optimizer;
using source.data;
using source.functions;
using runner.data;
using System.Reflection;
using System;

// ---------------------------------------------------------------------------
// MASTHING — calibration driver (optimizer).
//
// Implements the objective function and parameter-evaluation loop used by
// the multi-start downhill-simplex algorithm described in Section 2.2.3 of
// Bregaglio et al. (2026). Supports two calibration stages:
//   (1) phenology: minimises RMSE between simulated and observed MODIS NDVI;
//   (2) reproduction: maximises R² between simulated and observed annual
//       normalised seed production (per site or pooled across sites).
// Each evaluation runs a full daily simulation for every tree/year in the
// calibration set under the requested model formulation (RB / RB+WC / RBxWC).
// ---------------------------------------------------------------------------

namespace runner
{
    /// <summary>
    /// Calibration driver for MASTHING. Implements <see cref="IOBJfunc"/>
    /// so it can be plugged into the multi-start simplex (UNIMI.optimizer).
    /// The objective returned by <c>compute</c> depends on the
    /// <c>calibrationVariable</c> setting (NDVI → RMSE, seeds → 1 − R²).
    /// </summary>
    internal class optimizer : IOBJfunc
    {
        #region optimizer methods
        int _neval = 0;
        int _ncompute = 0;

        public Dictionary<int, string> _Phenology = new Dictionary<int, string>();
        
        // the number of times that this function is called        
        public int neval
        {
            get
            {
                return _neval;
            }

            set
            {
                _neval = value;
            }
        }

        
        // the number of times where the function is evaluated 
        // (when an evaluation is requested outside the parameters domain this counter is not incremented        
        public int ncompute
        {
            get
            {
                return _ncompute;
            }

            set
            {
                _ncompute = value;
            }
        }
        #endregion

        #region instances of SWELL data types and functions
        //SWELL data types
        output output = new output();
        output outputT1 = new output();
        //instance of the SWELL functions
        VIdynamics NDVIdynamics = new VIdynamics();
        dormancySeason dormancy = new dormancySeason();
        growingSeason growing = new growingSeason();
        source.functions.resources resources = new source.functions.resources();
        source.functions.reproduction reproduction = new source.functions.reproduction();
        source.functions.allometry allometry = new source.functions.allometry();
        #endregion

        #region instance of the weather reader class
        weatherReader weatherReader = new weatherReader();
        #endregion

        #region local variables to perform the optimization
        public Dictionary<string, Site> idSite = new Dictionary<string, Site>();
        public Dictionary<string, Dictionary<string, parameter>> species_nameParam = new Dictionary<string, Dictionary<string, parameter>>();
        public Dictionary<string, float> param_outCalibration = new Dictionary<string, float>();
        public Dictionary<DateTime, output> date_outputs = new Dictionary<DateTime, output>();
        public string weatherDir;
        public List<string> allWeatherDataFiles;
        public string species;
        public string weatherDataFile;
        public string calibrationVariable;
        public string calibrationType;
        public string modelVersion;
        #endregion

        //this method perform the multi-start simplex calibration
        public double ObjfuncVal(double[] Coefficient, double[,] limits)
        {
            #region Calibration methods
            for (int j = 0; j < Coefficient.Length; j++)
            {
                if (Coefficient[j] == 0)
                {
                    break;
                }
                if (Coefficient[j] <= limits[j, 0] | Coefficient[j] > limits[j, 1])
                {
                    return 1E+300;
                }
                
            }
            _neval++;
            _ncompute++;
            #endregion

            #region assign parameters
            // Get the type of the parameters object
            source.data.parameters parameters = new parameters();
            var _parametersType = parameters.GetType();

            int coef = 0;
            foreach (var param in species_nameParam[species_nameParam.Keys.First()].Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                if (species_nameParam[species_nameParam.Keys.First()][param].calibration != "")
                {
                    object convertedValue = Convert.ChangeType(Coefficient[coef],
                        propertyInfo.PropertyType);
                    propertyInfo.SetValue(classInstance, convertedValue);
                    coef++;
                }
                else
                {
                    propertyInfo.SetValue(classInstance, param_outCalibration[param]);
                }
            }
            #endregion

            //list of errors
            List<double> errors = new List<double>();

            //list of list to store the normalized data
            List<List<float>> referenceSeed = new List<List<float>>();
            List<List<float>> simulatedSeed = new List<List<float>>();

            List<float> referenceSeedList = new List<float>();
            List<float> simulatedSeedList = new List<float>();

            double objFun = 0;
            //reinitialize Spearman correlation for each tree
            var SpearmanCorrelationList = new List<float>();

            //assign model version
            parameters.modelVersion = modelVersion;
            List<float> referenceNDVI= new List<float>();
            List<float> simulatedNDVI = new List<float>();

            //loop over ids
            foreach (var id in idSite.Keys)
            {
                referenceNDVI = new List<float>();
                simulatedNDVI = new List<float>();

                //reinitialize variables for each site
                output = new output();
                outputT1 = new output();

                // Find the closest point
                string closestFile = FindClosestPoint(idSite[id].latitude, idSite[id].longitude, allWeatherDataFiles);

                //read weather data
                Dictionary<DateTime, input> weatherData = 
                    weatherReader.readWeather(weatherDir + "//" + closestFile);
                //set cues parameters                
                Year_TempAfterSolstice = new Dictionary<int, float>();
                setCuesParameters(weatherData, idSite[id].latitude, parameters);

                parameters.parReproduction.temperatureCueMinimum = Year_TempAfterSolstice.Min(pair => pair.Value);
                parameters.parReproduction.temperatureCueMaximum = Year_TempAfterSolstice.Max(pair => pair.Value);
               
                if(idSite[id].id_YearSeeds.Keys.Count == 0)
                {
                    idSite[id].id_YearSeeds.Add("pheno", new tree());
                }

                foreach (var idTree in idSite[id].id_YearSeeds.Keys)
                {
                    //reinitialize the lists of seeds
                    referenceSeedList = new List<float>();
                    simulatedSeedList = new List<float>();
                    //reinitialize output objects
                    output = new output();
                    outputT1 = new output();

                    //initialize allometry
                    tree thisTree = idSite[id].id_YearSeeds[idTree];
                    thisTree = allometry.allometryInitialization(thisTree, parameters, output, outputT1);

                    //set initial budget as half of the maximum budget for reproduction
                    outputT1.resources.maximumResourceBudget = parameters.parResources.resourceBudgetFraction * (thisTree.totalBiomass);
                    outputT1.resources.minimumResourceBudgetReproduction = outputT1.resources.maximumResourceBudget * parameters.parReproduction.reproductionThreshold;
                    outputT1.resources.resourceBudget = (outputT1.resources.minimumResourceBudgetReproduction + outputT1.resources.maximumResourceBudget) * .5f;

                    //read and assign phenology parameters if the calibration variable is not phenology
                    //(in this case we keep the same phenology parameters as in the previous step of the calibration)
                    if (calibrationVariable != "phenology")
                    {
                        string dir = "calibratedPixels//phenology";

                        string mustContain = id;

                        string filePath = Directory.GetFiles(dir, "*.csv").FirstOrDefault(f => Path.GetFileName(f).Contains(mustContain));

                        if (filePath == null)
                        {
                            throw new FileNotFoundException(
                                $"No CSV file containing '{mustContain}' found in {dir}"
                            );
                        }

                        StreamReader streamReader = new StreamReader(filePath);

                        streamReader.ReadLine();
                        while (!streamReader.EndOfStream)
                        {
                            string line = streamReader.ReadLine();
                            var values = line.Split(',');

                            string propertyClass = values[0].Split('_')[0].Trim();
                            string propertyName = values[0].Split('_')[1].Trim();
                            string propertyValue = values[1].Trim();

                            // Get the type of the parameters object
                            var parametersType = parameters.GetType();

                            // Find the class inside the parameters instance
                            var classProperty = parametersType.GetField(propertyClass);

                            if (classProperty != null)
                            {
                                var classInstance = classProperty.GetValue(parameters);
                                if (classInstance != null)
                                {
                                    var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                    if (propertyInfo != null && propertyInfo.CanWrite)
                                    {
                                        string x = classInstance.ToString();

                                        object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                        propertyInfo.SetValue(classInstance, convertedValue);

                                    }
                                    else
                                    {
                                        Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Class instance '{propertyClass}' is null.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                            }
                        }
                    }


                    //loop over dates
                    foreach (var day in weatherData.Keys)
                    {
                        weatherData[day].tree = thisTree;
                        weatherData[day].vegetationIndex = "NDVI";
                        if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                        {
                            outputT1.seedReference = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                        }

                        //call the model
                        modelCall(weatherData[day], parameters);

                        if (calibrationVariable == "phenology")
                        {
                            if (idSite[id].dateNDVInorm.ContainsKey(day) && day.Year > 2003)
                            {
                                errors.Add(Math.Pow(idSite[id].dateNDVInorm[day] - outputT1.vi / 100, 2));

                                referenceNDVI.Add(idSite[id].dateNDVInorm[day]);
                                simulatedNDVI.Add(outputT1.vi / 100);
                            }
                        }
                        else
                        {

                            if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                            {
                                //this is used to weight less the errors when the NDVI is very low
                                if (outputT1.greenDownPercentage == 100 && output.phenoCode == 4)
                                {
                                    var thisReferenceSeed = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                                    referenceSeedList.Add(thisReferenceSeed);
                                    simulatedSeedList.Add(output.reproduction.ripeningActualState);
                                }
                            }
                        }
                    }
                    //normalize data
                    //reference seed
                    if (referenceSeedList.Count > 0)
                    {
                        float min = referenceSeedList.Min();
                        float max = referenceSeedList.Max();
                        List<float> normalizedReferenceSeed = referenceSeedList.Select(x => (x - min) / (max - min)).ToList();
                        //simulated seed
                        min = simulatedSeedList.Min();
                        max = simulatedSeedList.Max();
                        List<float> normalizedSimulatedSeed = simulatedSeedList.Select(x => (max != min) ? (x - min) / (max - min) : 0).ToList(); ;

                        //populate the list of list
                        referenceSeed.Add(normalizedReferenceSeed);
                        simulatedSeed.Add(normalizedSimulatedSeed);
                        SpearmanCorrelationList.Add((float)Math.Round(PearsonCorrelation(normalizedReferenceSeed,
                            normalizedSimulatedSeed), 2));
                    }
                }
                               
            }
            //compute objective function
            float PearsonCorrel = 0;
            float spearmanCorrelation = 0;
            double rmse = 0;
            double rSquared = 0;
            if (calibrationVariable == "seeds")
            {
                List<float> flattenReference = referenceSeed.SelectMany(list => list).ToList();
                List<float> flattenSimulated = simulatedSeed.SelectMany(list => list).ToList();

                rmse = Math.Round(ComputeRMSE(flattenReference, flattenSimulated),2);
                spearmanCorrelation = (float)Math.Round(SpearmanCorrelationList.Average(),2);
                objFun = Math.Round(rmse*.1F+(1 - spearmanCorrelation)*.9F, 2);
            }
            else
            {
                rmse = Math.Round(Math.Sqrt(errors.Sum() / errors.Count), 2);
                spearmanCorrelation = (float)Math.Round(SpearmanCorrelation(referenceNDVI, simulatedNDVI), 2);
                objFun = Math.Round(((1 - spearmanCorrelation) + rmse) * .5F, 2);
            }
            
            //write it in the console
            Console.WriteLine("RMSE = {0}, Spearman = {1}, Objective function = {2}",  rmse, spearmanCorrelation, objFun);

            //return the objective function
            return objFun;
        }

        Dictionary<int, float> Year_TempAfterSolstice = new Dictionary<int, float>();

        #region objective functions
        static float PearsonCorrelation(List<float> x, List<float> y)
        {
            if (x.Count != y.Count)
                throw new ArgumentException("Lists must have the same length");

            int n = x.Count;

            float meanX = x.Average();
            float meanY = y.Average();

            float sumXY = 0;
            float sumXX = 0;
            float sumYY = 0;

            for (int i = 0; i < n; i++)
            {
                float dx = x[i] - meanX;
                float dy = y[i] - meanY;

                sumXY += dx * dy;
                sumXX += dx * dx;
                sumYY += dy * dy;
            }

            float denominator = (float)Math.Sqrt(sumXX * sumYY);

            if (denominator == 0)
                return float.NaN;

            return sumXY / denominator;
        }
        static double ComputeRMSE(List<float> actual, List<float> predicted)
        {
            if (actual.Count != predicted.Count)
                throw new ArgumentException("Lists must have the same length.");

            double sumSquaredError = 0;

            for (int i = 0; i < actual.Count; i++)
            {
                double error = actual[i] - predicted[i];
                sumSquaredError += error * error;
            }

            double meanSquaredError = sumSquaredError / actual.Count;
            double rmse = Math.Sqrt(meanSquaredError);

            return rmse;
        }
        
        static float SpearmanCorrelation(List<float> list1, List<float> list2)
        {
            if (list1.Count != list2.Count)
                throw new ArgumentException("Lists must have the same length");

            int n = list1.Count;

            // Create tuples of (value, index) for each list
            var rankedList1 = list1.Select((value, index) => new { Value = value, Index = index })
                                    .OrderBy(item => item.Value)
                                    .Select((item, rank) => new { item.Index, Rank = rank + 1 })
                                    .ToDictionary(item => item.Index, item => item.Rank);

            var rankedList2 = list2.Select((value, index) => new { Value = value, Index = index })
                                    .OrderBy(item => item.Value)
                                    .Select((item, rank) => new { item.Index, Rank = rank + 1 })
                                    .ToDictionary(item => item.Index, item => item.Rank);

            // Calculate Spearman correlation coefficient
            float dSquared = 0;
            for (int i = 0; i < n; i++)
            {
                float d = rankedList1[i] - rankedList2[i];
                dSquared += d * d;
            }

            return 1 - (6 * dSquared) / (n * (n * n - 1));
        }
        #endregion

        #region compute weights for the cues
        static List<float> CalculateWeights(int startDay, int endDay, float latitude)
        {

            //day length solstice
            input inputSolstice = new input();
            inputSolstice.date = new DateTime(inputSolstice.date.Year, 6, 21);
            inputSolstice.latitude = latitude;
            utils.astronomy(inputSolstice);
            float dayLengthSolstice = inputSolstice.radData.dayLength;

            //day length end of july
            input inputEndJuly = new input();
            inputEndJuly.date = new DateTime(inputSolstice.date.Year, 7, 31);
            inputEndJuly.latitude = latitude;
            utils.astronomy(inputEndJuly);
            float dayLengthEndJuly = inputEndJuly.radData.dayLength;

            // Calculate weights for each day
            List<float> weights = new List<float>();
            for (int day = inputSolstice.date.DayOfYear;
                day <= inputEndJuly.date.DayOfYear; day++)
            {
                input input = new input();
                input.date = new DateTime(input.date.Year, 1, 1).AddDays(day);
                input.latitude = latitude;
                utils.astronomy(input);
                float dayLength = input.radData.dayLength;

                // Calculate weight using linear relationship
                float weight = 1 - (dayLengthSolstice - dayLength) / (dayLengthSolstice - dayLengthEndJuly);
                weights.Add(weight);
            }
            return weights;
        }
        static float CalculateWeightedAverage(List<float> values, List<float> weights)
        {
            // Ensure lists have the same length
            if (values.Count != weights.Count)
                throw new ArgumentException("Lists must have the same length.");

            // Compute weighted sum
            float weightedSum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                weightedSum += values[i] * weights[i];
            }

            // Compute weighted average
            float weightedAverage = weightedSum / weights.Sum();
            return weightedAverage;
        }
        #endregion

        public void setCuesParameters(Dictionary<DateTime, input> weatherData, float latitude, parameters parameters)
        {
            //loop over years
            foreach (var year in weatherData.Keys.Select(x => x.Year).Distinct())
            {
                var tempAfterSolstice = new List<float>();

                //get the temperature after the solstice
                tempAfterSolstice = weatherData.
                    Where(x => x.Key.Year == year && (x.Key.DayOfYear >= 172 && x.Key.DayOfYear <= 212)).
                    Select(x => (x.Value.airTemperatureMaximum)).ToList();

                if (tempAfterSolstice.Count > 0)
                {
                    Year_TempAfterSolstice.Add(year, tempAfterSolstice.Average());
                }
            }
        }

        //this method is called in the validation run
        public void oneShot(Dictionary<string, float> paramValue, out Dictionary<DateTime, output> date_outputs)
        {
            //reinitialize the date_outputs object
            date_outputs = new Dictionary<DateTime, output>();

            #region assign parameters
            // Get the type of the parameters object
            source.data.parameters parameters = new parameters();
            var _parametersType = parameters.GetType();

            foreach (var param in paramValue.Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                object convertedValue = Convert.ChangeType(paramValue[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);
            }

            foreach (var param in param_outCalibration.Keys)
            {
                //split class from param name
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                // Find the class inside the parameters instance
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                object convertedValue = Convert.ChangeType(param_outCalibration[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);
            }

            #endregion

            //assign model version
            parameters.modelVersion = modelVersion;
            //loop over pixels
            foreach (var id in idSite.Keys)
            {
                //reinitialize variables for each site
                output = new output();
                outputT1 = new output();

                // Find the closest point
                string closestFile = FindClosestPoint(idSite[id].latitude, idSite[id].longitude, allWeatherDataFiles);

                //read weather
                Dictionary<DateTime, input> weatherData =
                   weatherReader.readWeather(weatherDir + "//" + closestFile);

                //set cues parameters                
                Year_TempAfterSolstice = new Dictionary<int, float>();
                setCuesParameters(weatherData, idSite[id].latitude, parameters);
                parameters.parReproduction.temperatureCueMinimum = Year_TempAfterSolstice.Min(pair => pair.Value);
                parameters.parReproduction.temperatureCueMaximum = Year_TempAfterSolstice.Max(pair => pair.Value);

                //read and assign phenology parameters if the calibration variable is not phenology
                //(in this case we keep the same phenology parameters as in the previous step of the calibration)
                if (calibrationVariable != "phenology")
                {
                    string dir = "calibratedPixels//phenology";

                    string mustContain = id;

                    string filePath = Directory.GetFiles(dir, "*.csv").FirstOrDefault(f => Path.GetFileName(f).Contains(mustContain));

                    if (filePath == null)
                    {
                        throw new FileNotFoundException(
                            $"No CSV file containing '{mustContain}' found in {dir}"
                        );
                    }

                    StreamReader streamReader = new StreamReader(filePath);

                    streamReader.ReadLine();
                    while (!streamReader.EndOfStream)
                    {
                        string line = streamReader.ReadLine();
                        var values = line.Split(',');

                        string propertyClass = values[0].Split('_')[0].Trim();
                        string propertyName = values[0].Split('_')[1].Trim();
                        string propertyValue = values[1].Trim();

                        // Get the type of the parameters object
                        var parametersType = parameters.GetType();

                        // Find the class inside the parameters instance
                        var classProperty = parametersType.GetField(propertyClass);

                        if (classProperty != null)
                        {
                            var classInstance = classProperty.GetValue(parameters);
                            if (classInstance != null)
                            {
                                var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                if (propertyInfo != null && propertyInfo.CanWrite)
                                {
                                    string x = classInstance.ToString();

                                    object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                    propertyInfo.SetValue(classInstance, convertedValue);

                                }
                                else
                                {
                                    Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Class instance '{propertyClass}' is null.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                        }
                    }
                }

                if (calibrationVariable == "phenology")
                {
                    //reinitialize the date_outputs object
                    date_outputs = new Dictionary<DateTime, output>();

                    //loop over dates
                    foreach (var day in weatherData.Keys)
                    {
                        //assing latitude
                        weatherData[day].latitude = idSite[id].latitude;
                        weatherData[day].vegetationIndex = "NDVI";
                        //call the SWELL model
                        modelCall(weatherData[day], parameters);

                        //add weather data to output object
                        outputT1.weather.airTemperatureMinimum = weatherData[day].airTemperatureMinimum;
                        outputT1.weather.airTemperatureMaximum = weatherData[day].airTemperatureMaximum;
                        outputT1.weather.precipitation = weatherData[day].precipitation;
                        outputT1.weather.radData.dayLength = weatherData[day].radData.dayLength;
                        outputT1.weather.radData.etr = weatherData[day].radData.etr;

                        //add the NDVI data
                        if (idSite[id].dateNDVInorm.ContainsKey(day))
                        {
                            outputT1.ndviReference = idSite[id].dateNDVInorm[day];
                        }

                        //add the object to the output dictionary
                        date_outputs.Add(day, outputT1);
                    }

                    //write the outputs from the calibration run
                    writeOutputsCalibration(id, date_outputs, "pheno");
                }
                else if (calibrationVariable == "seeds")
                {
                    foreach (var idTree in idSite[id].id_YearSeeds.Keys)
                    {
                        output = new output();
                        outputT1 = new output();

                        //initialize allometry
                        tree thisTree = idSite[id].id_YearSeeds[idTree];
                        thisTree = allometry.allometryInitialization(thisTree, parameters, output, outputT1);

                        //set initial budget as half of the maximum budget for reproduction
                        outputT1.resources.maximumResourceBudget = parameters.parResources.resourceBudgetFraction * (thisTree.totalBiomass);
                        outputT1.resources.minimumResourceBudgetReproduction = outputT1.resources.maximumResourceBudget * parameters.parReproduction.reproductionThreshold;
                        outputT1.resources.resourceBudget = (outputT1.resources.minimumResourceBudgetReproduction + outputT1.resources.maximumResourceBudget) * .5f;

                        date_outputs = new Dictionary<DateTime, output>();

                        var lastDay = weatherData.Keys.Last();
                        //loop over dates
                        foreach (var day in weatherData.Keys)
                        {
                            //set dbh
                            weatherData[day].tree = thisTree;
                            weatherData[day].vegetationIndex = "NDVI";
                            if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                            {
                                outputT1.seedReference = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                            }

                            //call the SWELL model
                            modelCall(weatherData[day], parameters);

                            //add weather data to output object
                            outputT1.weather.airTemperatureMinimum = weatherData[day].airTemperatureMinimum;
                            outputT1.weather.airTemperatureMaximum = weatherData[day].airTemperatureMaximum;
                            outputT1.weather.precipitation = weatherData[day].precipitation;
                            outputT1.weather.radData.dayLength = weatherData[day].radData.dayLength;
                            outputT1.weather.radData.etr = weatherData[day].radData.etr;

                            //add the NDVI data
                            if (idSite[id].dateNDVInorm.ContainsKey(day))
                            {
                                outputT1.ndviReference = idSite[id].dateNDVInorm[day];
                            }
                            //add the object to the output dictionary
                            date_outputs.Add(day, outputT1);
                        }


                        //write the outputs from the calibration run
                        writeOutputsCalibration(id, date_outputs, idTree);

                    }
                }
            }
        }

        #region write output files from calibration and validation
        //write outputs from the calibration run
        public void writeOutputsCalibration(string id, Dictionary<DateTime, output> date_outputs, string idTree)
        {

            #region write outputs
            //empty list to store outputs
            List<string> toWrite = new List<string>();

            //define the file header
            string header = "pixel,date,treeID,modelVersion,calibrationType," +
            "tmax,tmin,prec,dayLength," +
             "NDVI_swell,reference,LAI,phenoCode," +
             "coldStress,heatStress,waterStress," +
             "resourceRate,resourceState,respirationWood,respirationLeaves," +
             "resourceBudget,budgetLevel,savedResources,weatherCues," +
             "floweringInvestment,pollinationEfficiency,reproductionInvestment," +
             "ripeningActual,referenceSeed";

            //add the header to the list
            toWrite.Add(header);

            //loop over days
            foreach (var weather in date_outputs.Keys)
            {
                if (weather.Year >= 1978)
                {
                    //empty string to store outputs
                    string line = "";

                    //populate this line
                    line += id + ",";
                    line += weather.ToString() + ",";
                    line += idTree + ",";
                    line += modelVersion + ",";
                    line += calibrationType + ",";
                    line += date_outputs[weather].weather.airTemperatureMaximum + ",";
                    line += date_outputs[weather].weather.airTemperatureMinimum + ",";
                    line += date_outputs[weather].weather.precipitation + ",";
                    line += date_outputs[weather].weather.radData.dayLength + ",";
                    line += date_outputs[weather].vi / 100 + ",";
                    if (idSite[id].dateNDVInorm.ContainsKey(weather))
                    {
                        line += idSite[id].dateNDVInorm[weather] + ",";
                    }
                    else
                    {
                        line += ",";
                    }
                    line += date_outputs[weather].LAI + ",";
                    line += date_outputs[weather].phenoCode + ",";
                    line += date_outputs[weather].resources.coldStressRate + ",";
                    line += date_outputs[weather].resources.heatStressRate + ",";
                    line += date_outputs[weather].resources.waterStressRate + ",";
                    line += date_outputs[weather].resources.resourcesRate + ",";
                    line += date_outputs[weather].resources.resourcesState + ",";
                    line += date_outputs[weather].resources.respirationWoodRate + ",";
                    line += date_outputs[weather].resources.respirationLeavesRate + ",";
                    line += date_outputs[weather].resources.resourceBudget + ",";
                    line += date_outputs[weather].resources.budgetLevel + ",";
                    line += date_outputs[weather].resources.savedResources + ",";
                    line += date_outputs[weather].reproduction.floweringWeatherCues + ",";
                    line += date_outputs[weather].reproduction.floweringInvestment + ",";
                    line += date_outputs[weather].reproduction.pollinationEfficiency + ",";
                    line += date_outputs[weather].reproduction.reproductionInvestment + ",";
                    line += date_outputs[weather].reproduction.ripeningActualState + ",";
                    if (idSite[id].id_YearSeeds.ContainsKey(idTree))
                    {
                        if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(weather.Year) &&
                            date_outputs[weather].phenoCode == 4)
                        {
                            line += idSite[id].id_YearSeeds[idTree].YearSeeds[weather.Year];
                        }
                        else
                        {
                            line += "";
                        }
                    }
                    else
                    {
                        line += "";
                    }

                    //add the line to the list
                    toWrite.Add(line);
                }
            }
            //save the file
            System.IO.File.WriteAllLines(@"outputsCalibration//calib_" + calibrationType + "_model_" + modelVersion + "_" + id + "_" + idTree + ".csv", toWrite);
            #endregion

        }

        #endregion

        //call the MASTHING model
        public void modelCall(input weatherData, parameters parameters)
        {
            //pass values from the previous day
            output = outputT1;            
            outputT1 = new output();
            outputT1.isDormancyInduced = output.isDormancyInduced;
            outputT1.isInvestmentDecided = output.isInvestmentDecided;
            outputT1.isFloweringCompleted = output.isFloweringCompleted;
            outputT1.isMaximumLAIreached = output.isMaximumLAIreached;
            outputT1.isMinimumLAIreached = output.isMinimumLAIreached;
            outputT1.ndvi_LAImax = output.ndvi_LAImax;
            outputT1.viBudBreak = output.viBudBreak;
            outputT1.resources.PrecipitationMemory = output.resources.PrecipitationMemory;
            outputT1.resources.ET0memory = output.resources.ET0memory;
            outputT1.reproduction.solsticeTemperatureCue = output.reproduction.solsticeTemperatureCue;
            outputT1.resources.maximumResourceBudget = output.resources.maximumResourceBudget;
            outputT1.resources.minimumResourceBudgetReproduction = output.resources.minimumResourceBudgetReproduction;

            if (output.vi == 0)
            {
                output.vi = parameters.parVegetationIndex.minimumVI*100F;
            }
          
            //call the functions
            //dormancy season
            dormancy.induction(weatherData, parameters, output, outputT1);
            dormancy.endodormancy(weatherData, parameters, output, outputT1);
            dormancy.ecodormancy(weatherData, parameters, output, outputT1);
            //growing season
            growing.growthRate(weatherData, parameters, output, outputT1);
            growing.greendownRate(weatherData, parameters, output, outputT1);
            growing.declineRate(weatherData, parameters, output, outputT1);
            //NDVI dynamics
            NDVIdynamics.ndviNormalized(weatherData, parameters, output, outputT1);
            //resources
            resources.photosyntheticRate(weatherData, parameters, output, outputT1);
            reproduction.floweringInvestment(weatherData, parameters, output, outputT1);
            reproduction.pollinationDynamics(weatherData, parameters, output, outputT1);
            reproduction.ripeningDynamics(weatherData, parameters, output, outputT1);
        }

        #region associate the correct grid weather to the corresponding remote sensing pixel
        //find the nearest weather grid with respect to pixel latitude and longitude
        private string FindClosestPoint(double targetLatitude, double targetLongitude, List<string> fileNames)
        {
            double closestDistance = double.MaxValue;
            string closestFileName = null;

            foreach (string fileName in fileNames)
            {
                // Extract latitude and longitude from the file name
                string[] parts = fileName.Replace(".csv", "").Split('_');
                double latitude = double.Parse(parts[0]);
                double longitude = double.Parse(parts[1]);

                // Calculate distance using Haversine formula
                double distance = CalculateDistance(targetLatitude, targetLongitude, latitude, longitude);

                // Update closest point if the current distance is smaller
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestFileName = fileName;
                }
            }

            return closestFileName;
        }

        //calculate distance between pixel and weather grid centroids
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Radius of the Earth in kilometers
            const double R = 6371;
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        //conversion to radians
        static double ToRadians(double angle)
        {
            return Math.PI * angle / 180.0;
        }
        #endregion
    }
}
