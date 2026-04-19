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

        /// <summary>
        /// Multi-start simplex objective function. Takes a candidate parameter
        /// vector (<paramref name="Coefficient"/>) and returns the scalar cost
        /// to be minimised: RMSE on NDVI for phenology calibration, or a
        /// weighted blend of RMSE and (1 − Spearman) on normalised seeds for
        /// reproduction calibration. Out-of-bounds candidates are penalised
        /// with 1E+300 so the simplex rejects them.
        /// </summary>
        /// <param name="Coefficient">Candidate parameter values in the order they appear in <c>species_nameParam</c>.</param>
        /// <param name="limits">Per-parameter (min, max) envelope enforced by the simplex.</param>
        /// <returns>Scalar objective value; lower is better.</returns>
        public double ObjfuncVal(double[] Coefficient, double[,] limits)
        {
            #region Calibration methods
            //guard: reject any candidate that steps outside the parameter box.
            for (int j = 0; j < Coefficient.Length; j++)
            {
                //zero coefficient marks the end of the active parameters (defensive, rarely triggered).
                if (Coefficient[j] == 0)
                {
                    break;
                }
                //out-of-bounds → return a very large penalty so the simplex rejects the vertex.
                if (Coefficient[j] <= limits[j, 0] | Coefficient[j] > limits[j, 1])
                {
                    return 1E+300;
                }

            }
            //bump the evaluation counters (both the simplex-level and the accepted-point counters).
            _neval++;
            _ncompute++;
            #endregion

            #region assign parameters
            //fresh parameter container — will be populated from the simplex vertex via reflection.
            source.data.parameters parameters = new parameters();
            //cache the runtime type so we can look up its public fields (one per functional group).
            var _parametersType = parameters.GetType();

            //running index into the simplex Coefficient vector (only parameters with calibration != "" consume a slot).
            int coef = 0;
            //iterate over every parameter known to this species (key format: "classParam_propertyName").
            foreach (var param in species_nameParam[species_nameParam.Keys.First()].Keys)
            {
                //decode the compound key: [0] = functional-group field (parGrowth/parReproduction/...), [1] = property name.
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                //grab the functional-group field (e.g. parameters.parGrowth) via reflection.
                var classProperty = _parametersType.GetField(paramClass);
                //and the live instance so we can write onto its properties.
                var classInstance = classProperty.GetValue(parameters);

                //resolve the actual property descriptor on the functional-group instance.
                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                //calibration flag non-empty → this parameter is part of the simplex search.
                if (species_nameParam[species_nameParam.Keys.First()][param].calibration != "")
                {
                    //coerce the candidate double to the property's declared type (float/int/...).
                    object convertedValue = Convert.ChangeType(Coefficient[coef],
                        propertyInfo.PropertyType);
                    //assign the candidate value onto the parameters container.
                    propertyInfo.SetValue(classInstance, convertedValue);
                    //consume one slot of the simplex vector.
                    coef++;
                }
                else
                {
                    //non-calibrated parameter: fall back to the fixed value latched in param_outCalibration.
                    propertyInfo.SetValue(classInstance, param_outCalibration[param]);
                }
            }
            #endregion

            //flat accumulator of squared errors (NDVI branch) — RMSE is computed from this at the end.
            List<double> errors = new List<double>();

            //per-tree normalised seed series — one List<float> per tree kept in these outer lists.
            List<List<float>> referenceSeed = new List<List<float>>();
            List<List<float>> simulatedSeed = new List<List<float>>();

            //temporary per-tree buffers — filled during the per-day loop and then normalised to [0,1].
            List<float> referenceSeedList = new List<float>();
            List<float> simulatedSeedList = new List<float>();

            //final scalar objective to return to the simplex.
            double objFun = 0;
            //per-tree Pearson correlations — later averaged to form the Spearman/ranks component of the cost.
            var SpearmanCorrelationList = new List<float>();

            //push the current model formulation (RB / RB+WC / RBxWC) onto the parameters container.
            parameters.modelVersion = modelVersion;
            //paired NDVI series (observed vs simulated) — filled only in the phenology branch.
            List<float> referenceNDVI= new List<float>();
            List<float> simulatedNDVI = new List<float>();

            //outer loop: one iteration per calibration site.
            foreach (var id in idSite.Keys)
            {
                //reset the per-site NDVI buffers at the top of each site.
                referenceNDVI = new List<float>();
                simulatedNDVI = new List<float>();

                //fresh state objects so dormancy/phenology latches from another site don't leak in.
                output = new output();
                outputT1 = new output();

                //spatially nearest weather grid to the site's MODIS pixel (Haversine distance).
                string closestFile = FindClosestPoint(idSite[id].latitude, idSite[id].longitude, allWeatherDataFiles);

                //read the full daily weather record for that grid (key = date).
                Dictionary<DateTime, input> weatherData =
                    weatherReader.readWeather(weatherDir + "//" + closestFile);
                //build the summer-solstice temperature cue dictionary from the just-loaded weather.
                Year_TempAfterSolstice = new Dictionary<int, float>();
                setCuesParameters(weatherData, idSite[id].latitude, parameters);

                //latch per-site min/max solstice temperatures — used to normalise the reproductive weather cue.
                parameters.parReproduction.temperatureCueMinimum = Year_TempAfterSolstice.Min(pair => pair.Value);
                parameters.parReproduction.temperatureCueMaximum = Year_TempAfterSolstice.Max(pair => pair.Value);

                //phenology-only sites carry no tree records — inject a placeholder so the per-tree loop runs once.
                if(idSite[id].id_YearSeeds.Keys.Count == 0)
                {
                    idSite[id].id_YearSeeds.Add("pheno", new tree());
                }

                //inner loop: one iteration per tree at this site (or the single "pheno" placeholder).
                foreach (var idTree in idSite[id].id_YearSeeds.Keys)
                {
                    //fresh per-tree observation/simulation buffers.
                    referenceSeedList = new List<float>();
                    simulatedSeedList = new List<float>();
                    //fresh model state at the start of each tree's simulation window.
                    output = new output();
                    outputT1 = new output();

                    //pull the tree record (with its DBH) and run the allometric initialisation.
                    tree thisTree = idSite[id].id_YearSeeds[idTree];
                    thisTree = allometry.allometryInitialization(thisTree, parameters, output, outputT1);

                    //derive the per-tree resource-budget envelope from biomass × the budget-fraction parameter.
                    outputT1.resources.maximumResourceBudget = parameters.parResources.resourceBudgetFraction * (thisTree.totalBiomass);
                    //minimum budget that still allows reproduction — set by the reproduction-threshold fraction.
                    outputT1.resources.minimumResourceBudgetReproduction = outputT1.resources.maximumResourceBudget * parameters.parReproduction.reproductionThreshold;
                    //start the simulation at the mid-point between the reproduction-threshold and the max budget.
                    outputT1.resources.resourceBudget = (outputT1.resources.minimumResourceBudgetReproduction + outputT1.resources.maximumResourceBudget) * .5f;

                    //when calibrating reproduction we override the (possibly naive) phenology parameters with
                    //the already-calibrated values for this site, so the NDVI trajectory is held fixed.
                    if (calibrationVariable != "phenology")
                    {
                        //pre-calibrated phenology CSVs are stored in this site-specific folder.
                        string dir = "calibratedPixels//phenology";

                        //use the site id as a file-name substring match.
                        string mustContain = id;

                        //pick the first CSV in the directory whose name contains the site id.
                        string filePath = Directory.GetFiles(dir, "*.csv").FirstOrDefault(f => Path.GetFileName(f).Contains(mustContain));

                        //fail fast if no matching file exists — calibration would silently use defaults otherwise.
                        if (filePath == null)
                        {
                            throw new FileNotFoundException(
                                $"No CSV file containing '{mustContain}' found in {dir}"
                            );
                        }

                        //open the phenology-parameter CSV for sequential reading.
                        StreamReader streamReader = new StreamReader(filePath);

                        //skip the header row.
                        streamReader.ReadLine();
                        //stream each "class_name,value" line and push it into the parameters container via reflection.
                        while (!streamReader.EndOfStream)
                        {
                            string line = streamReader.ReadLine();
                            var values = line.Split(',');

                            //decode "class_name" and the string value.
                            string propertyClass = values[0].Split('_')[0].Trim();
                            string propertyName = values[0].Split('_')[1].Trim();
                            string propertyValue = values[1].Trim();

                            //reflect on the parameters container to locate the right field/property.
                            var parametersType = parameters.GetType();

                            //functional-group field (parGrowth, parVegetationIndex, ...).
                            var classProperty = parametersType.GetField(propertyClass);

                            if (classProperty != null)
                            {
                                //resolve the live instance of the functional group.
                                var classInstance = classProperty.GetValue(parameters);
                                if (classInstance != null)
                                {
                                    //resolve the actual property and its setter.
                                    var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                    if (propertyInfo != null && propertyInfo.CanWrite)
                                    {
                                        //unused — kept as a defensive trace of the target instance.
                                        string x = classInstance.ToString();

                                        //coerce the CSV string to the declared property type (float/int/...).
                                        object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                        //and write it onto the live parameters container.
                                        propertyInfo.SetValue(classInstance, convertedValue);

                                    }
                                    else
                                    {
                                        //diagnostic: property missing on this class — log and continue.
                                        Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                    }
                                }
                                else
                                {
                                    //diagnostic: reflected class instance is null — log and continue.
                                    Console.WriteLine($"Class instance '{propertyClass}' is null.");
                                }
                            }
                            else
                            {
                                //diagnostic: unknown functional-group field — log and continue.
                                Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                            }
                        }
                    }


                    //innermost loop: one iteration per simulation day for this tree at this site.
                    foreach (var day in weatherData.Keys)
                    {
                        //attach the current tree record so allometric state is accessible in the functions.
                        weatherData[day].tree = thisTree;
                        //this study uses MODIS NDVI — flag the VI type for the NDVI dynamics module.
                        weatherData[day].vegetationIndex = "NDVI";
                        //if an annual seed count exists for this year, expose it to the model (for diagnostics only).
                        if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                        {
                            outputT1.seedReference = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                        }

                        //run one daily time step of MASTHING for this tree.
                        modelCall(weatherData[day], parameters);

                        if (calibrationVariable == "phenology")
                        {
                            //NDVI calibration: only accumulate errors when an observation exists AND we are in the MODIS era.
                            if (idSite[id].dateNDVInorm.ContainsKey(day) && day.Year > 2003)
                            {
                                //squared error on the /100 scale — fed to the RMSE accumulator.
                                errors.Add(Math.Pow(idSite[id].dateNDVInorm[day] - outputT1.vi / 100, 2));

                                //keep paired series so we can also compute a Spearman correlation at the end.
                                referenceNDVI.Add(idSite[id].dateNDVInorm[day]);
                                simulatedNDVI.Add(outputT1.vi / 100);
                            }
                        }
                        else
                        {
                            //seeds calibration branch.
                            if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                            {
                                //only sample the reproductive state once greendown has fully completed —
                                //at that point the seed cohort of the year is fixed and comparable to the observation.
                                if (outputT1.greenDownPercentage == 100 && output.phenoCode == 4)
                                {
                                    //keep paired (observed, simulated) annual seed values for later normalisation.
                                    var thisReferenceSeed = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                                    referenceSeedList.Add(thisReferenceSeed);
                                    simulatedSeedList.Add(output.reproduction.ripeningActualState);
                                }
                            }
                        }
                    }
                    //at the end of this tree's simulation window: min–max normalise the two series so they live in [0,1].
                    //(only do this if we actually collected at least one year — otherwise the min/max call would throw.)
                    if (referenceSeedList.Count > 0)
                    {
                        //min-max rescale the observed series.
                        float min = referenceSeedList.Min();
                        float max = referenceSeedList.Max();
                        List<float> normalizedReferenceSeed = referenceSeedList.Select(x => (x - min) / (max - min)).ToList();
                        //min-max rescale the simulated series (degenerate min==max → set all to 0 to avoid /0).
                        min = simulatedSeedList.Min();
                        max = simulatedSeedList.Max();
                        List<float> normalizedSimulatedSeed = simulatedSeedList.Select(x => (max != min) ? (x - min) / (max - min) : 0).ToList(); ;

                        //push both normalised series onto the tree-level accumulators.
                        referenceSeed.Add(normalizedReferenceSeed);
                        simulatedSeed.Add(normalizedSimulatedSeed);
                        //per-tree Pearson r on the normalised series — rounded to 2 decimals for stability.
                        SpearmanCorrelationList.Add((float)Math.Round(PearsonCorrelation(normalizedReferenceSeed,
                            normalizedSimulatedSeed), 2));
                    }
                }

            }
            //assemble the final scalar objective from the accumulators above.
            float PearsonCorrel = 0;
            float spearmanCorrelation = 0;
            double rmse = 0;
            double rSquared = 0;
            if (calibrationVariable == "seeds")
            {
                //flatten the per-tree normalised series into a single vector for the RMSE computation.
                List<float> flattenReference = referenceSeed.SelectMany(list => list).ToList();
                List<float> flattenSimulated = simulatedSeed.SelectMany(list => list).ToList();

                //pooled RMSE across all trees × years on the [0,1] scale.
                rmse = Math.Round(ComputeRMSE(flattenReference, flattenSimulated),2);
                //Spearman component is approximated by the mean of the per-tree Pearson r's (computed on normalised data).
                spearmanCorrelation = (float)Math.Round(SpearmanCorrelationList.Average(),2);
                //weighted blend — rank agreement dominates (0.9 weight), RMSE contributes 0.1.
                objFun = Math.Round(rmse*.1F+(1 - spearmanCorrelation)*.9F, 2);
            }
            else
            {
                //phenology branch: classical RMSE from the squared-error accumulator.
                rmse = Math.Round(Math.Sqrt(errors.Sum() / errors.Count), 2);
                //Spearman on the paired NDVI series across all sites.
                spearmanCorrelation = (float)Math.Round(SpearmanCorrelation(referenceNDVI, simulatedNDVI), 2);
                //equal-weight blend of RMSE and (1 − Spearman).
                objFun = Math.Round(((1 - spearmanCorrelation) + rmse) * .5F, 2);
            }

            //live progress trace for the simplex run.
            Console.WriteLine("RMSE = {0}, Spearman = {1}, Objective function = {2}",  rmse, spearmanCorrelation, objFun);

            //hand the cost back to the simplex driver.
            return objFun;
        }

        Dictionary<int, float> Year_TempAfterSolstice = new Dictionary<int, float>();

        #region objective functions
        /// <summary>
        /// Pearson linear correlation coefficient between two equal-length
        /// numeric series. Returns <c>NaN</c> if either series has zero
        /// variance (division-by-zero guard).
        /// </summary>
        static float PearsonCorrelation(List<float> x, List<float> y)
        {
            //pairwise correlation requires identical cardinalities — fail fast otherwise.
            if (x.Count != y.Count)
                throw new ArgumentException("Lists must have the same length");

            //cache the common length so the loop bound is explicit.
            int n = x.Count;

            //sample means of the two series.
            float meanX = x.Average();
            float meanY = y.Average();

            //running sums of cross-product and squared deviations (for the covariance numerator and variance denominators).
            float sumXY = 0;
            float sumXX = 0;
            float sumYY = 0;

            //accumulate the deviation products in a single pass.
            for (int i = 0; i < n; i++)
            {
                //centred residuals for each series at index i.
                float dx = x[i] - meanX;
                float dy = y[i] - meanY;

                //cross-product (numerator of Pearson r).
                sumXY += dx * dy;
                //sum of squared x deviations (part of the denominator).
                sumXX += dx * dx;
                //sum of squared y deviations (part of the denominator).
                sumYY += dy * dy;
            }

            //geometric-mean denominator √(Σdx² · Σdy²).
            float denominator = (float)Math.Sqrt(sumXX * sumYY);

            //zero variance on either side → correlation is undefined.
            if (denominator == 0)
                return float.NaN;

            //Pearson r = covariance / (σ_x · σ_y) (no division by n because factors cancel).
            return sumXY / denominator;
        }
        /// <summary>
        /// Root-mean-square error between two equal-length series.
        /// </summary>
        static double ComputeRMSE(List<float> actual, List<float> predicted)
        {
            //equal-length precondition for paired error metrics.
            if (actual.Count != predicted.Count)
                throw new ArgumentException("Lists must have the same length.");

            //running sum of squared residuals.
            double sumSquaredError = 0;

            //single pass: squared difference per index.
            for (int i = 0; i < actual.Count; i++)
            {
                double error = actual[i] - predicted[i];
                sumSquaredError += error * error;
            }

            //divide by n to get the MSE.
            double meanSquaredError = sumSquaredError / actual.Count;
            //take the square root to bring back to the original units.
            double rmse = Math.Sqrt(meanSquaredError);

            return rmse;
        }

        /// <summary>
        /// Spearman rank correlation coefficient between two equal-length
        /// numeric series. Ties are broken by stable sort order (no tie
        /// correction is applied — suitable for the mostly-continuous NDVI
        /// series used here).
        /// </summary>
        static float SpearmanCorrelation(List<float> list1, List<float> list2)
        {
            //equal-length precondition for paired rank metrics.
            if (list1.Count != list2.Count)
                throw new ArgumentException("Lists must have the same length");

            //cache the common length for the rank-difference formula.
            int n = list1.Count;

            // Create tuples of (value, index) for each list
            //project each value with its original index, sort by value, then assign 1-based ranks → index → rank map.
            var rankedList1 = list1.Select((value, index) => new { Value = value, Index = index })
                                    .OrderBy(item => item.Value)
                                    .Select((item, rank) => new { item.Index, Rank = rank + 1 })
                                    .ToDictionary(item => item.Index, item => item.Rank);

            //same projection for the second series.
            var rankedList2 = list2.Select((value, index) => new { Value = value, Index = index })
                                    .OrderBy(item => item.Value)
                                    .Select((item, rank) => new { item.Index, Rank = rank + 1 })
                                    .ToDictionary(item => item.Index, item => item.Rank);

            // Calculate Spearman correlation coefficient
            //accumulator for Σd² where d = rank1 − rank2 at the same original index.
            float dSquared = 0;
            for (int i = 0; i < n; i++)
            {
                //paired rank difference at original index i.
                float d = rankedList1[i] - rankedList2[i];
                //square and accumulate.
                dSquared += d * d;
            }

            //classic Spearman ρ = 1 − 6·Σd² / (n·(n²−1)).
            return 1 - (6 * dSquared) / (n * (n * n - 1));
        }
        #endregion

        #region compute weights for the cues
        /// <summary>
        /// Computes a day-length-based weight vector across the post-solstice
        /// window (21 Jun → 31 Jul). The weight is 1 on the solstice and
        /// declines linearly with shortening day length, giving more emphasis
        /// to the days closest to the astronomical maximum insolation.
        /// </summary>
        static List<float> CalculateWeights(int startDay, int endDay, float latitude)
        {

            //anchor 1: summer solstice — maximum day length (weight = 1).
            input inputSolstice = new input();
            inputSolstice.date = new DateTime(inputSolstice.date.Year, 6, 21);
            inputSolstice.latitude = latitude;
            //run the astronomy module to populate radData.dayLength.
            utils.astronomy(inputSolstice);
            float dayLengthSolstice = inputSolstice.radData.dayLength;

            //anchor 2: end of July — bottom of the weighting window (weight = 0).
            input inputEndJuly = new input();
            inputEndJuly.date = new DateTime(inputSolstice.date.Year, 7, 31);
            inputEndJuly.latitude = latitude;
            utils.astronomy(inputEndJuly);
            float dayLengthEndJuly = inputEndJuly.radData.dayLength;

            //compute a per-day weight by linear interpolation on day length.
            List<float> weights = new List<float>();
            for (int day = inputSolstice.date.DayOfYear;
                day <= inputEndJuly.date.DayOfYear; day++)
            {
                //reconstruct a date from the DOY and get its day length.
                input input = new input();
                input.date = new DateTime(input.date.Year, 1, 1).AddDays(day);
                input.latitude = latitude;
                utils.astronomy(input);
                float dayLength = input.radData.dayLength;

                //linear mapping: day length = solstice → 1, day length = endJuly → 0.
                float weight = 1 - (dayLengthSolstice - dayLength) / (dayLengthSolstice - dayLengthEndJuly);
                weights.Add(weight);
            }
            return weights;
        }
        /// <summary>
        /// Weighted arithmetic mean: Σ(value × weight) / Σweight. Both
        /// input lists must have identical length.
        /// </summary>
        static float CalculateWeightedAverage(List<float> values, List<float> weights)
        {
            //paired operation → enforce equal lengths.
            if (values.Count != weights.Count)
                throw new ArgumentException("Lists must have the same length.");

            //running numerator Σ(value_i · weight_i).
            float weightedSum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                weightedSum += values[i] * weights[i];
            }

            //divide by Σweight to normalise.
            float weightedAverage = weightedSum / weights.Sum();
            return weightedAverage;
        }
        #endregion

        /// <summary>
        /// Populates the <c>Year_TempAfterSolstice</c> cache with the
        /// mean daily Tmax over the 21 Jun → 31 Jul window of each year
        /// in the weather record. This post-solstice mean is the reproductive
        /// temperature cue used by <c>reproduction.floweringInvestment</c> and
        /// is subsequently rescaled by the site's min/max envelope.
        /// </summary>
        public void setCuesParameters(Dictionary<DateTime, input> weatherData, float latitude, parameters parameters)
        {
            //iterate over the distinct calendar years present in the weather record.
            foreach (var year in weatherData.Keys.Select(x => x.Year).Distinct())
            {
                var tempAfterSolstice = new List<float>();

                //pull the daily Tmax for days 172..212 (21 Jun → 31 Jul) of this year.
                tempAfterSolstice = weatherData.
                    Where(x => x.Key.Year == year && (x.Key.DayOfYear >= 172 && x.Key.DayOfYear <= 212)).
                    Select(x => (x.Value.airTemperatureMaximum)).ToList();

                //only store a value if the window has at least one day of data.
                if (tempAfterSolstice.Count > 0)
                {
                    //cache the arithmetic mean of the post-solstice Tmax as this year's reproductive temperature cue.
                    Year_TempAfterSolstice.Add(year, tempAfterSolstice.Average());
                }
            }
        }

        /// <summary>
        /// Runs a single ("one-shot") simulation with the supplied parameter
        /// set across all calibration sites/trees. Used for validation runs
        /// after calibration: it assembles the parameters, reads the weather,
        /// executes the daily model loop, and writes the per-day outputs to
        /// the <c>outputsCalibration</c> folder via
        /// <see cref="writeOutputsCalibration"/>.
        /// </summary>
        /// <param name="paramValue">Dictionary of calibrated parameter values keyed by "class_name".</param>
        /// <param name="date_outputs">(out) Per-day output record produced during the last run.</param>
        public void oneShot(Dictionary<string, float> paramValue, out Dictionary<DateTime, output> date_outputs)
        {
            //fresh per-day output dictionary for each run.
            date_outputs = new Dictionary<DateTime, output>();

            #region assign parameters
            //parameter container populated from the calibrated + non-calibrated dictionaries.
            source.data.parameters parameters = new parameters();
            //cache the reflected type so we can resolve functional-group fields.
            var _parametersType = parameters.GetType();

            //first pass: push every calibrated parameter value onto the container via reflection.
            foreach (var param in paramValue.Keys)
            {
                //decode the compound key "class_name".
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                //resolve functional-group field → live instance.
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                //and the target property descriptor.
                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                //coerce the stored float to the property's declared type and write it.
                object convertedValue = Convert.ChangeType(paramValue[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);
            }

            //second pass: push every non-calibrated (fixed) parameter value onto the container.
            foreach (var param in param_outCalibration.Keys)
            {
                //decode "class_name" and write via reflection (same pattern as the first pass).
                string paramClass = param.Split('_')[0].Trim();
                string propertyName = param.Split('_')[1].Trim();

                //resolve functional-group field → live instance.
                var classProperty = _parametersType.GetField(paramClass);
                var classInstance = classProperty.GetValue(parameters);

                //and the target property descriptor.
                var propertyInfo = classInstance.GetType().GetProperty(propertyName);

                //assign the fixed value with the right runtime type.
                object convertedValue = Convert.ChangeType(param_outCalibration[param],
                    propertyInfo.PropertyType);
                propertyInfo.SetValue(classInstance, convertedValue);
            }

            #endregion

            //latch the model formulation (RB / RB+WC / RBxWC) for this run.
            parameters.modelVersion = modelVersion;
            //outer loop: one iteration per calibration site.
            foreach (var id in idSite.Keys)
            {
                //fresh state for each site so phenology latches don't leak across sites.
                output = new output();
                outputT1 = new output();

                //spatially nearest weather grid to this site's MODIS pixel.
                string closestFile = FindClosestPoint(idSite[id].latitude, idSite[id].longitude, allWeatherDataFiles);

                //load that weather grid's daily record.
                Dictionary<DateTime, input> weatherData =
                   weatherReader.readWeather(weatherDir + "//" + closestFile);

                //recompute the reproductive temperature-cue cache for this site's weather.
                Year_TempAfterSolstice = new Dictionary<int, float>();
                setCuesParameters(weatherData, idSite[id].latitude, parameters);
                //update min/max envelope for the cue normalisation.
                parameters.parReproduction.temperatureCueMinimum = Year_TempAfterSolstice.Min(pair => pair.Value);
                parameters.parReproduction.temperatureCueMaximum = Year_TempAfterSolstice.Max(pair => pair.Value);

                //when running a reproduction validation, override the phenology parameters with the
                //already-calibrated site-specific values so the NDVI trajectory is held fixed.
                if (calibrationVariable != "phenology")
                {
                    //folder holding the per-site phenology parameter CSVs produced by Fase A.
                    string dir = "calibratedPixels//phenology";

                    //match CSVs whose filename contains the site id.
                    string mustContain = id;

                    string filePath = Directory.GetFiles(dir, "*.csv").FirstOrDefault(f => Path.GetFileName(f).Contains(mustContain));

                    //fail fast if the site has no calibrated phenology file — avoids silently using defaults.
                    if (filePath == null)
                    {
                        throw new FileNotFoundException(
                            $"No CSV file containing '{mustContain}' found in {dir}"
                        );
                    }

                    //open the per-site phenology parameter CSV.
                    StreamReader streamReader = new StreamReader(filePath);

                    //skip the header row.
                    streamReader.ReadLine();
                    //push each "class_name,value" row onto the parameters container via reflection.
                    while (!streamReader.EndOfStream)
                    {
                        string line = streamReader.ReadLine();
                        var values = line.Split(',');

                        //decode compound key + value.
                        string propertyClass = values[0].Split('_')[0].Trim();
                        string propertyName = values[0].Split('_')[1].Trim();
                        string propertyValue = values[1].Trim();

                        //reflection bookkeeping.
                        var parametersType = parameters.GetType();

                        //functional-group field.
                        var classProperty = parametersType.GetField(propertyClass);

                        if (classProperty != null)
                        {
                            //live instance of the functional group.
                            var classInstance = classProperty.GetValue(parameters);
                            if (classInstance != null)
                            {
                                //target property on the group.
                                var propertyInfo = classInstance.GetType().GetProperty(propertyName);
                                if (propertyInfo != null && propertyInfo.CanWrite)
                                {
                                    //defensive trace — kept as a breakpoint anchor, unused otherwise.
                                    string x = classInstance.ToString();

                                    //coerce the CSV string to the declared property type and write it.
                                    object convertedValue = Convert.ChangeType(propertyValue, propertyInfo.PropertyType);
                                    propertyInfo.SetValue(classInstance, convertedValue);

                                }
                                else
                                {
                                    //diagnostic: property missing on this class — log and continue.
                                    Console.WriteLine($"Property '{propertyName}' not found in class '{propertyClass}'.");
                                }
                            }
                            else
                            {
                                //diagnostic: class instance is null — log and continue.
                                Console.WriteLine($"Class instance '{propertyClass}' is null.");
                            }
                        }
                        else
                        {
                            //diagnostic: unknown functional-group field — log and continue.
                            Console.WriteLine($"Class '{propertyClass}' not found in parameters.");
                        }
                    }
                }

                //phenology branch: no tree-level loop required — run once per site and write NDVI outputs.
                if (calibrationVariable == "phenology")
                {
                    //fresh per-day output container for this site.
                    date_outputs = new Dictionary<DateTime, output>();

                    //daily loop over the weather record.
                    foreach (var day in weatherData.Keys)
                    {
                        //propagate site latitude into the input record (some modules need it for astronomy).
                        weatherData[day].latitude = idSite[id].latitude;
                        weatherData[day].vegetationIndex = "NDVI";
                        //run one day of the SWELL phenology model.
                        modelCall(weatherData[day], parameters);

                        //copy weather inputs onto the output record for later CSV dumping.
                        outputT1.weather.airTemperatureMinimum = weatherData[day].airTemperatureMinimum;
                        outputT1.weather.airTemperatureMaximum = weatherData[day].airTemperatureMaximum;
                        outputT1.weather.precipitation = weatherData[day].precipitation;
                        outputT1.weather.radData.dayLength = weatherData[day].radData.dayLength;
                        outputT1.weather.radData.etr = weatherData[day].radData.etr;

                        //if a MODIS NDVI observation exists for today, attach it to the output record.
                        if (idSite[id].dateNDVInorm.ContainsKey(day))
                        {
                            outputT1.ndviReference = idSite[id].dateNDVInorm[day];
                        }

                        //push today's output onto the per-site dictionary.
                        date_outputs.Add(day, outputT1);
                    }

                    //flush the per-site outputs to outputsCalibration/ for later post-processing.
                    writeOutputsCalibration(id, date_outputs, "pheno");
                }
                //seeds branch: inner per-tree loop required because each tree has its own allometric state.
                else if (calibrationVariable == "seeds")
                {
                    //iterate over every tree at this site.
                    foreach (var idTree in idSite[id].id_YearSeeds.Keys)
                    {
                        //fresh state for each tree.
                        output = new output();
                        outputT1 = new output();

                        //pull tree record + run allometric initialisation.
                        tree thisTree = idSite[id].id_YearSeeds[idTree];
                        thisTree = allometry.allometryInitialization(thisTree, parameters, output, outputT1);

                        //rebuild the resource-budget envelope for this tree.
                        outputT1.resources.maximumResourceBudget = parameters.parResources.resourceBudgetFraction * (thisTree.totalBiomass);
                        outputT1.resources.minimumResourceBudgetReproduction = outputT1.resources.maximumResourceBudget * parameters.parReproduction.reproductionThreshold;
                        //start the simulation at the mid-budget point.
                        outputT1.resources.resourceBudget = (outputT1.resources.minimumResourceBudgetReproduction + outputT1.resources.maximumResourceBudget) * .5f;

                        //fresh per-day output container for this tree.
                        date_outputs = new Dictionary<DateTime, output>();

                        //kept for clarity — the last day of the record (unused in the loop body).
                        var lastDay = weatherData.Keys.Last();
                        //daily loop: full MASTHING simulation for this tree.
                        foreach (var day in weatherData.Keys)
                        {
                            //attach tree record + mark MODIS NDVI as the VI source.
                            weatherData[day].tree = thisTree;
                            weatherData[day].vegetationIndex = "NDVI";
                            //expose the observed seed count (when available) for diagnostics.
                            if (idSite[id].id_YearSeeds[idTree].YearSeeds.ContainsKey(day.Year))
                            {
                                outputT1.seedReference = idSite[id].id_YearSeeds[idTree].YearSeeds[day.Year];
                            }

                            //run one day of MASTHING.
                            modelCall(weatherData[day], parameters);

                            //copy weather inputs onto the output record for later CSV dumping.
                            outputT1.weather.airTemperatureMinimum = weatherData[day].airTemperatureMinimum;
                            outputT1.weather.airTemperatureMaximum = weatherData[day].airTemperatureMaximum;
                            outputT1.weather.precipitation = weatherData[day].precipitation;
                            outputT1.weather.radData.dayLength = weatherData[day].radData.dayLength;
                            outputT1.weather.radData.etr = weatherData[day].radData.etr;

                            //attach today's MODIS NDVI when available.
                            if (idSite[id].dateNDVInorm.ContainsKey(day))
                            {
                                outputT1.ndviReference = idSite[id].dateNDVInorm[day];
                            }
                            //push today's output onto the per-tree dictionary.
                            date_outputs.Add(day, outputT1);
                        }


                        //flush this tree's outputs to outputsCalibration/ under its own tree id.
                        writeOutputsCalibration(id, date_outputs, idTree);

                    }
                }
            }
        }

        #region write output files from calibration and validation
        /// <summary>
        /// Writes the per-day simulation outputs to
        /// <c>outputsCalibration/calib_&lt;type&gt;_model_&lt;version&gt;_&lt;site&gt;_&lt;tree&gt;.csv</c>.
        /// The resulting CSV contains the full daily trajectory of weather
        /// inputs, VI/LAI, phenology code, stress rates, carbon budget and
        /// reproductive state, plus the observed NDVI/seed values when
        /// available. These files feed the R post-processing pipeline that
        /// produces Figs. 4–7 of the paper.
        /// </summary>
        public void writeOutputsCalibration(string id, Dictionary<DateTime, output> date_outputs, string idTree)
        {

            #region write outputs
            //list of CSV rows — flushed to disk in one WriteAllLines call.
            List<string> toWrite = new List<string>();

            //single flat header — mirrors the per-row schema assembled below.
            string header = "pixel,date,treeID,modelVersion,calibrationType," +
            "tmax,tmin,prec,dayLength," +
             "NDVI_swell,reference,LAI,phenoCode," +
             "coldStress,heatStress,waterStress," +
             "resourceRate,resourceState,respirationWood,respirationLeaves," +
             "resourceBudget,budgetLevel,savedResources,weatherCues," +
             "floweringInvestment,pollinationEfficiency,reproductionInvestment," +
             "ripeningActual,referenceSeed";

            //push the header as the first row.
            toWrite.Add(header);

            //emit one row per simulated day (filtered to 1978+ to match the beech masting reference window).
            foreach (var weather in date_outputs.Keys)
            {
                if (weather.Year >= 1978)
                {
                    //build the CSV row in a local string by progressive concatenation.
                    string line = "";

                    //identifier columns.
                    line += id + ",";
                    line += weather.ToString() + ",";
                    line += idTree + ",";
                    line += modelVersion + ",";
                    line += calibrationType + ",";
                    //weather inputs.
                    line += date_outputs[weather].weather.airTemperatureMaximum + ",";
                    line += date_outputs[weather].weather.airTemperatureMinimum + ",";
                    line += date_outputs[weather].weather.precipitation + ",";
                    line += date_outputs[weather].weather.radData.dayLength + ",";
                    //simulated NDVI (stored on the /100 scale internally → back to [0,1] for output).
                    line += date_outputs[weather].vi / 100 + ",";
                    //observed NDVI when available — empty cell otherwise.
                    if (idSite[id].dateNDVInorm.ContainsKey(weather))
                    {
                        line += idSite[id].dateNDVInorm[weather] + ",";
                    }
                    else
                    {
                        line += ",";
                    }
                    //canopy state.
                    line += date_outputs[weather].LAI + ",";
                    line += date_outputs[weather].phenoCode + ",";
                    //stress factors.
                    line += date_outputs[weather].resources.coldStressRate + ",";
                    line += date_outputs[weather].resources.heatStressRate + ",";
                    line += date_outputs[weather].resources.waterStressRate + ",";
                    //carbon balance — per-day rate and cumulative state.
                    line += date_outputs[weather].resources.resourcesRate + ",";
                    line += date_outputs[weather].resources.resourcesState + ",";
                    line += date_outputs[weather].resources.respirationWoodRate + ",";
                    line += date_outputs[weather].resources.respirationLeavesRate + ",";
                    //resource budget accounting.
                    line += date_outputs[weather].resources.resourceBudget + ",";
                    line += date_outputs[weather].resources.budgetLevel + ",";
                    line += date_outputs[weather].resources.savedResources + ",";
                    //reproductive state (weather cues, flowering, pollination, ripening).
                    line += date_outputs[weather].reproduction.floweringWeatherCues + ",";
                    line += date_outputs[weather].reproduction.floweringInvestment + ",";
                    line += date_outputs[weather].reproduction.pollinationEfficiency + ",";
                    line += date_outputs[weather].reproduction.reproductionInvestment + ",";
                    line += date_outputs[weather].reproduction.ripeningActualState + ",";
                    //observed seed count — only emitted once per year (on the day greendown has just completed, phenoCode == 4).
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

                    //push the assembled row onto the output buffer.
                    toWrite.Add(line);
                }
            }
            //single bulk write — filename encodes calibration type, model version, site and tree.
            System.IO.File.WriteAllLines(@"outputsCalibration//calib_" + calibrationType + "_model_" + modelVersion + "_" + id + "_" + idTree + ".csv", toWrite);
            #endregion

        }

        #endregion

        /// <summary>
        /// Single daily time step of MASTHING. Propagates the latches from
        /// the previous day onto the new output record and calls the
        /// dormancy/growing/NDVI/resources/reproduction modules in the
        /// documented order. This method owns the two-state
        /// (<c>output</c> = yesterday, <c>outputT1</c> = today) rolling buffer.
        /// </summary>
        public void modelCall(input weatherData, parameters parameters)
        {
            //roll the buffer: today's state becomes yesterday's, and we allocate a fresh "today" record.
            output = outputT1;
            outputT1 = new output();
            //carry forward the one-way latches (set once, never unset within a season).
            outputT1.isDormancyInduced = output.isDormancyInduced;
            outputT1.isInvestmentDecided = output.isInvestmentDecided;
            outputT1.isFloweringCompleted = output.isFloweringCompleted;
            outputT1.isMaximumLAIreached = output.isMaximumLAIreached;
            outputT1.isMinimumLAIreached = output.isMinimumLAIreached;
            //carry forward the NDVI-at-LAImax anchor and the VI at bud break.
            outputT1.ndvi_LAImax = output.ndvi_LAImax;
            outputT1.viBudBreak = output.viBudBreak;
            //carry forward the rolling-window water-balance memories (Geoderma 2021).
            outputT1.resources.PrecipitationMemory = output.resources.PrecipitationMemory;
            outputT1.resources.ET0memory = output.resources.ET0memory;
            //carry forward the reproductive temperature cue captured at the solstice.
            outputT1.reproduction.solsticeTemperatureCue = output.reproduction.solsticeTemperatureCue;
            //carry forward the tree-level resource-budget envelope (latched at allometric init).
            outputT1.resources.maximumResourceBudget = output.resources.maximumResourceBudget;
            outputT1.resources.minimumResourceBudgetReproduction = output.resources.minimumResourceBudgetReproduction;

            //cold-start guard: on day 1 the VI is 0 — seed it at the parameter minimum to avoid division-by-zero downstream.
            if (output.vi == 0)
            {
                output.vi = parameters.parVegetationIndex.minimumVI*100F;
            }

            //run the modules in their documented order.
            //1) dormancy season: induction / endodormancy / ecodormancy.
            dormancy.induction(weatherData, parameters, output, outputT1);
            dormancy.endodormancy(weatherData, parameters, output, outputT1);
            dormancy.ecodormancy(weatherData, parameters, output, outputT1);
            //2) growing season: growth / greendown / decline.
            growing.growthRate(weatherData, parameters, output, outputT1);
            growing.greendownRate(weatherData, parameters, output, outputT1);
            growing.declineRate(weatherData, parameters, output, outputT1);
            //3) NDVI dynamics: integrate the VI from the phase-specific rates.
            NDVIdynamics.ndviNormalized(weatherData, parameters, output, outputT1);
            //4) resources: photosynthesis + respiration + carbon-budget update.
            resources.photosyntheticRate(weatherData, parameters, output, outputT1);
            //5) reproduction: flowering → pollination → ripening.
            reproduction.floweringInvestment(weatherData, parameters, output, outputT1);
            reproduction.pollinationDynamics(weatherData, parameters, output, outputT1);
            reproduction.ripeningDynamics(weatherData, parameters, output, outputT1);
        }

        #region associate the correct grid weather to the corresponding remote sensing pixel
        /// <summary>
        /// Returns the name of the weather grid CSV (filename encoded as
        /// "<c>lat_lon.csv</c>") whose centroid is closest — by great-circle
        /// (Haversine) distance — to the given target coordinates. Used to
        /// map each MODIS pixel to its matching weather time series.
        /// </summary>
        private string FindClosestPoint(double targetLatitude, double targetLongitude, List<string> fileNames)
        {
            //initialise the min-distance search with +∞.
            double closestDistance = double.MaxValue;
            string closestFileName = null;

            //linear scan over all candidate grid files.
            foreach (string fileName in fileNames)
            {
                //filename schema: "<lat>_<lon>.csv" — strip the extension and split on underscore.
                string[] parts = fileName.Replace(".csv", "").Split('_');
                double latitude = double.Parse(parts[0]);
                double longitude = double.Parse(parts[1]);

                //great-circle distance between the target and the candidate grid centroid.
                double distance = CalculateDistance(targetLatitude, targetLongitude, latitude, longitude);

                //standard argmin update.
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestFileName = fileName;
                }
            }

            return closestFileName;
        }

        /// <summary>
        /// Great-circle distance (km) between two lat/lon points using the
        /// Haversine formula with a mean Earth radius of 6371 km.
        /// </summary>
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            //mean Earth radius (km).
            const double R = 6371;
            //differences in radians.
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);
            //Haversine intermediate: a = sin²(Δφ/2) + cos φ₁·cos φ₂·sin²(Δλ/2).
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            //central angle c = 2·atan2(√a, √(1−a)).
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            //distance = R · c.
            return R * c;
        }

        /// <summary>
        /// Converts an angle from degrees to radians.
        /// </summary>
        static double ToRadians(double angle)
        {
            //π · deg / 180 = rad.
            return Math.PI * angle / 180.0;
        }
        #endregion
    }
}
