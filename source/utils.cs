using source.data;
using source.functions;
using System.Collections.Generic;
using System.Reflection;

namespace source.functions
{
    /// <summary>
    /// Static collection of utility functions shared across the SWELL
    /// phenology and MASTHING masting modules. The class is organised in
    /// three groups:
    /// <list type="bullet">
    ///   <item><description>Additional weather variables: astronomy, day length, hourly temperature reconstruction, global solar radiation (Hargreaves), latent heat of vaporisation and reference evapotranspiration (Hargreaves).</description></item>
    ///   <item><description>SWELL phenophase-specific functions: thermal forcing (Yan &amp; Hunt, 1999), photothermal dormancy-induction components, hourly chilling response for endodormancy and temperature/photoperiod modulated ecodormancy rate.</description></item>
    ///   <item><description>MASTHING-specific functions: heat/cold/water abiotic stress modifiers of GPP, solstice-temperature cue for flowering investment, precipitation-driven pollination efficiency and greendown-driven ripening dynamics.</description></item>
    /// </list>
    /// The class is stateless and all methods are pure functions of their
    /// input arguments.
    /// </summary>
    public static class utils
    {
        #region additional weather variables
        /// <summary>
        /// Computes astronomical quantities for the current day: day length,
        /// extraterrestrial (top-of-atmosphere) solar radiation, sunrise and
        /// sunset hours, and Hargreaves-derived global solar radiation at
        /// ground level. Results are written into <c>input.radData</c> and
        /// returned for convenience.
        /// </summary>
        /// <param name="input">Daily input record; uses <c>date</c>, <c>latitude</c>, <c>airTemperatureMaximum</c>, <c>airTemperatureMinimum</c>.</param>
        /// <returns>The updated <see cref="radData"/> record.</returns>
        public static radData astronomy(input input)
        {
            float solarConstant = 4.921F;
            float DtoR = (float)Math.PI / 180;
            float dd;
            float ss;
            float cc;
            float ws;
            float dayHours = 0;

            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                input.radData.dayLength = 0.13333F / DtoR * ws;
                input.radData.etr = solarConstant * dd * 24 / (float)Math.PI
                    * (ws * ss + cc * (float)Math.Sin(ws));
            }
            else
            {
                input.radData.dayLength = dayHours;
            }
            input.radData.hourSunrise = 12 - input.radData.dayLength / 2;
            input.radData.hourSunset = 12 + input.radData.dayLength / 2;


            input.radData.gsr = globalSolarRadiation(input.radData.etr,
                input.airTemperatureMaximum, input.airTemperatureMinimum);
            return input.radData;
        }
        /// <summary>
        /// Stand-alone day-length calculation. Used where only astronomical
        /// day length (hours) is needed, without the full
        /// <see cref="astronomy"/> bundle. Returns 0 for polar latitudes
        /// (|lat| ≥ 65°) where the linear approximation breaks down.
        /// </summary>
        /// <param name="input">Daily input record; uses <c>date</c> and <c>latitude</c>.</param>
        /// <returns>Day length in hours.</returns>
        public static float dayLength(input input)
        {
            float DtoR = (float)Math.PI / 180;
            float dd;
            float ss;
            float cc;
            float ws;
            float dayHours = 0;

            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            float SolarDeclinationYesterday = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.AddDays(-1).DayOfYear));
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));
            float wsYesterday = (float)Math.Acos(-Math.Tan(SolarDeclinationYesterday) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                dayHours = 0.13333F / DtoR * ws;
            }
            else
            {
                dayHours = 0;
            }

            return dayHours;
        }

        /// <summary>
        /// Reconstructs the 24 hourly air temperatures from the daily
        /// minimum and maximum using a cosine interpolation centred on
        /// 14:00 local solar time (assumed hour of maximum temperature).
        /// </summary>
        /// <param name="input">Daily input record; uses <c>airTemperatureMaximum</c> and <c>airTemperatureMinimum</c>.</param>
        /// <returns>List of 24 hourly temperatures (°C), index 0 = 00:00.</returns>
        public static List<float> hourlyTemperature(input input)
        {
            List<float> hourlyTemperatures = new List<float>();
            int h = 0;

            double Tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            double DT = input.airTemperatureMaximum - input.airTemperatureMinimum;
            for (h = 0; h < 24; h++)
            {
                //todo: change with hour of the day with maximum temperature
                hourlyTemperatures.Add((float)(Tavg + DT / 2 * Math.Cos(0.2618F * (h - 14))));
            }

            return hourlyTemperatures;
        }

        /// <summary>
        /// Estimates daily global solar radiation at ground level using the
        /// Hargreaves-Samani temperature-based method:
        /// <c>Rs = k_Rs · sqrt(Tmax − Tmin) · Ra</c>, with a nominal
        /// interior coefficient <c>k_Rs = 0.17</c> °C^-0.5.
        /// </summary>
        /// <param name="ExtraterrestrialRadiation">Top-of-atmosphere radiation Ra (MJ m-2 day-1).</param>
        /// <param name="Tmax">Daily maximum air temperature (°C).</param>
        /// <param name="Tmin">Daily minimum air temperature (°C).</param>
        /// <returns>Global solar radiation at ground level (MJ m-2 day-1), rounded to two decimals.</returns>
        public static float globalSolarRadiation(float ExtraterrestrialRadiation, float Tmax, float Tmin)
        {
            float gsr = 0;
            float kRs, Ra, Rs;
            kRs = 0.17F;
            Ra = ExtraterrestrialRadiation;

            Rs = kRs * (float)Math.Sqrt((Tmax - Tmin) * Ra);

            gsr = (float)Math.Round(Rs, 2);

            return gsr;
        }

        /// <summary>
        /// Latent heat of vaporisation as a linear function of mean daily
        /// air temperature: <c>λ = 2.501 − 0.002361 · Tavg</c> (MJ kg-1).
        /// </summary>
        /// <param name="Tmax">Daily maximum air temperature (°C).</param>
        /// <param name="Tmin">Daily minimum air temperature (°C).</param>
        /// <returns>Latent heat of vaporisation (MJ kg-1).</returns>
        public static float latentHeatVaporization(float Tmax, float Tmin)
        {
            float latentHeat = 0;
            float a = 2.501F;    // intercept
            float b = 0.002361F; // slope

            float avgT = 0.5F * (Tmax + Tmin);
            latentHeat = a - b * avgT;

            return latentHeat;
        }
        /// <summary>
        /// Hargreaves-Samani reference evapotranspiration (ET0):
        /// <c>ET0 = (1/λ) · 0.0023 · (Tavg + 17.8) · sqrt(Tmax − Tmin) · Ra</c>.
        /// Expects <c>input.radData.etr</c> to have been populated by
        /// <see cref="astronomy"/>.
        /// </summary>
        /// <param name="input">Daily input record.</param>
        /// <returns>Reference evapotranspiration (mm day-1).</returns>
        public static float referenceEvapotranspiration(input input)
        {
            float ET0 = 0;
            float Tmax = input.airTemperatureMaximum;
            float Tmin = input.airTemperatureMinimum;
            float l = latentHeatVaporization(Tmax, Tmin);    // latent heat of vaporisation
            float Gr = input.radData.etr;
            float a = 0F;
            float b = 1F;

            ET0 = a + b * (1F / l)
                * 0.0023F * (((Tmax + Tmin) / 2F) + 17.8F) * (float)Math.Sqrt(Tmax - Tmin) * Gr;

            return ET0;
        }

        #endregion

        #region SWELL phenophase specific functions

        #region growth, greendown, decline thermal units
        /// <summary>
        /// Computes a daily thermal-forcing unit using the asymmetric
        /// cardinal-temperature response of Yan &amp; Hunt (1999). Zero below
        /// <paramref name="tmin"/> or above <paramref name="tmax"/>, one at
        /// <paramref name="topt"/>, interpolated by a power function
        /// elsewhere.
        /// </summary>
        /// <param name="input">Daily input record (uses min/max air temperature).</param>
        /// <param name="tmin">Cardinal minimum temperature (°C).</param>
        /// <param name="topt">Cardinal optimum temperature (°C).</param>
        /// <param name="tmax">Cardinal maximum temperature (°C).</param>
        /// <returns>Forcing rate in [0,1].</returns>
        public static float forcingUnitFunction(input input, float tmin, float topt, float tmax)
        {
            //local output variable
            float forcingRate = 0;

            //average air temperature
            float averageAirTemperature = (input.airTemperatureMaximum +
                input.airTemperatureMinimum) / 2;

            //if average temperature is below minimum or above maximum
            if (averageAirTemperature < tmin || averageAirTemperature > tmax)
            {
                forcingRate = 0;
            }
            else
            {
                //intermediate computations
                float firstTerm = (tmax - averageAirTemperature) / (tmax - topt);
                float secondTerm = (averageAirTemperature - tmin) / (topt - tmin);
                float Exponential = (topt - tmin) / (tmax - topt);

                //compute forcing rate
                forcingRate = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            }
            //assign to output variable
            return forcingRate;
        }
        #endregion

        #region dormancy induction
        /// <summary>
        /// Photoperiod limitation function for the dormancy-induction and
        /// decline phenophases. Returns 1 when day length is below
        /// <c>notLimitingPhotoperiod</c>, 0 above <c>limitingPhotoperiod</c>
        /// and a smooth sigmoidal transition in between.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>radData.dayLength</c>).</param>
        /// <param name="parameters">Species parameter set (uses <c>parDormancyInduction</c>).</param>
        /// <param name="outputT1">Current-day state (unused).</param>
        /// <returns>Dimensionless photoperiod modifier in [0,1].</returns>
        public static float photoperiodFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //local variable to store the output
            float photoperiodFunction = 0;

            //day length is non limiting PT
            if (input.radData.dayLength < parameters.parDormancyInduction.notLimitingPhotoperiod)
            {
                photoperiodFunction = 1;
            }
            else if (input.radData.dayLength > parameters.parDormancyInduction.limitingPhotoperiod)
            {
                photoperiodFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingPhotoperiod + parameters.parDormancyInduction.notLimitingPhotoperiod) * 0.5F;
                float width = parameters.parDormancyInduction.limitingPhotoperiod - parameters.parDormancyInduction.notLimitingPhotoperiod;

                //compute function
                photoperiodFunction = 1 / (1 + (float)Math.Exp(10 / width *
                    ((input.radData.dayLength - midpoint))));

            }
            //return the photoperiod function
            return photoperiodFunction;
        }

        /// <summary>
        /// Temperature limitation function for dormancy induction / decline.
        /// Returns 1 when daily mean temperature is below
        /// <c>notLimitingTemperature</c>, 0 above <c>limitingTemperature</c>
        /// and a smooth sigmoidal transition in between.
        /// </summary>
        /// <param name="input">Daily input record (uses min/max air temperature).</param>
        /// <param name="parameters">Species parameter set (uses <c>parDormancyInduction</c>).</param>
        /// <param name="outputT1">Current-day state (unused).</param>
        /// <returns>Dimensionless temperature modifier in [0,1].</returns>
        public static float temperatureFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //average temperature
            float tAverage = (float)(input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;

            //local variable to store the output
            float temperatureFunction = 0;

            if (tAverage <= parameters.parDormancyInduction.notLimitingTemperature)
            {
                temperatureFunction = 1;
            }
            else if (tAverage >= parameters.parDormancyInduction.limitingTemperature)
            {
                temperatureFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingTemperature + parameters.parDormancyInduction.notLimitingTemperature) * .5F;
                float width = (parameters.parDormancyInduction.limitingTemperature - parameters.parDormancyInduction.notLimitingTemperature);
                //compute function
                temperatureFunction = 1 / (1 + (float)Math.Exp(10 / width * (tAverage - midpoint)));

            }
            //return the output
            return temperatureFunction;
        }
        #endregion

        #region endodormancy
        /// <summary>
        /// Computes the daily chilling rate from 24 hourly temperatures.
        /// Each hourly value is mapped to [0,1] via a four-knot response
        /// defined by the <c>parEndodormancy</c> temperatures (limiting
        /// lower, non-limiting lower, non-limiting upper, limiting upper):
        /// zero outside the [limitingLower, limitingUpper] envelope, one
        /// in the optimum plateau, sigmoidal transitions on both flanks.
        /// The daily rate is the arithmetic mean of the 24 hourly values.
        /// </summary>
        /// <param name="input">Daily input record (unused, kept for signature symmetry).</param>
        /// <param name="parameters">Species parameter set (uses <c>parEndodormancy</c>).</param>
        /// <param name="hourlyTemperatures">24 hourly temperatures (°C) for the current day.</param>
        /// <param name="chillingUnitsList">(out) Per-hour chilling contributions in [0,1], returned for diagnostics.</param>
        /// <returns>Daily mean chilling rate (chilling units day-1).</returns>
        public static float endodormancyRate(input input, parameters parameters,
            List<float> hourlyTemperatures, out List<float> chillingUnitsList)
        {

            chillingUnitsList = new List<float>();
            //internal variable to store chilling units
            float chillingUnits = 0;

            #region chilling units accumulation
            foreach (var temperature in hourlyTemperatures)
            {
                //when hourly temperature is below the limiting lower temperature or above the limiting upper temperature
                if (temperature < parameters.parEndodormancy.limitingLowerTemperature ||
                    temperature > parameters.parEndodormancy.limitingUpperTemperature)
                {
                    //no chilling units are accumulated 
                    chillingUnits = 0; //not needed, just to be clear
                }
                //when hourly temperature is between the limiting lower temperature
                //and the non limiting lower temperature
                else if (temperature >= parameters.parEndodormancy.limitingLowerTemperature &&
                    temperature < parameters.parEndodormancy.notLimitingLowerTemperature)
                {
                    //compute lag and slope
                    double midpoint = (parameters.parEndodormancy.limitingLowerTemperature +
                        parameters.parEndodormancy.notLimitingLowerTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingLowerTemperature -
                        parameters.parEndodormancy.notLimitingLowerTemperature);

                    //update chilling units
                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / -width * ((temperature - midpoint))));
                }
                //when hourly temperature is between the non limiting lower temperature and the 
                //non limiting upper temperature
                else if (temperature >= parameters.parEndodormancy.notLimitingLowerTemperature &&
                    temperature <= parameters.parEndodormancy.notLimitingUpperTemperature)
                {
                    chillingUnits = 1;
                }
                //when hourly temperature is between the non limiting upper temperature and the
                //limiting upper temperature
                else
                {
                    double midpoint = (parameters.parEndodormancy.limitingUpperTemperature +
                       parameters.parEndodormancy.notLimitingUpperTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingUpperTemperature -
                        parameters.parEndodormancy.notLimitingUpperTemperature);

                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / width * ((temperature - midpoint))));
                }

                chillingUnitsList.Add(chillingUnits);
            }
            #endregion

            //return the output
            return chillingUnitsList.Sum() / 24;
        }
        #endregion

        #region ecodormancy
        /// <summary>
        /// Ecodormancy forcing rate combining temperature and photoperiod.
        /// The photoperiod ratio (dayLength / notLimitingPhotoperiod,
        /// clamped to 1) modulates both the sigmoid width and the asymptote
        /// of the temperature response; the asymptote is further lowered by
        /// the <paramref name="asymptote"/> argument, which carries the
        /// endodormancy-completion fraction from the caller.
        /// </summary>
        /// <param name="input">Daily input record (uses day length and min/max air temperature).</param>
        /// <param name="asymptote">Endodormancy-completion fraction ([0,1]) used to cap the daily forcing.</param>
        /// <param name="parameters">Species parameter set (uses <c>parEcodormancy</c>).</param>
        /// <returns>Daily ecodormancy forcing rate.</returns>
        public static float ecodormancyRate(input input, float asymptote, parameters parameters)
        {
            //local variable to store the output
            float ecodormancyRate = 0;

           
            //the slope of the photothermal function depends on day length 
            float ratioPhotoperiod = input.radData.dayLength / parameters.parEcodormancy.notLimitingPhotoperiod;
            if (ratioPhotoperiod > 1)
            {
                ratioPhotoperiod = 1;
            }

            //modify asymptote depending on day length and endodormancy completion
            float asymptoteModifier = ratioPhotoperiod * asymptote;
            float newAsymptote = asymptote + (1 - asymptote) * asymptoteModifier;

            //lag depends on maximum temperature and day length
            float midpoint = parameters.parEcodormancy.notLimitingTemperature * 0.5F +
                (1 - ratioPhotoperiod) * parameters.parEcodormancy.notLimitingTemperature;
            float tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
            float width = parameters.parEcodormancy.notLimitingTemperature * ratioPhotoperiod;

            
                ecodormancyRate = newAsymptote /
              (1 + (float)Math.Exp(-10 / width * ((tavg - midpoint)))); ;
           
            //compute ecodormancy rate
          



            //return the output
            return ecodormancyRate;

        }
        #endregion

        #endregion

        #region masting

        #region resources
        #region abiotic stresses
        /// <summary>
        /// Heat-stress multiplier applied to GPP. Returns 1 below the
        /// growth <c>maximumTemperature</c>, 0 above
        /// <c>parResources.criticalHeatTemperature</c> and a sigmoidal
        /// transition in between, centred on the midpoint of the two
        /// cardinal values.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>airTemperatureMaximum</c>).</param>
        /// <param name="parameters">Species parameter set (uses <c>parGrowth</c> and <c>parResources</c>).</param>
        /// <returns>Dimensionless heat-stress modifier in [0,1] (1 = no stress).</returns>
        public static float heatStressFunction(input input, parameters parameters)
        {
            float heatStress = 0;

            if (input.airTemperatureMaximum < parameters.parGrowth.maximumTemperature)
            {
                heatStress = 1;
            }
            else if (input.airTemperatureMaximum >= parameters.parResources.criticalHeatTemperature)
            {
                heatStress = 0;
            }
            else
            {
                //compute lag and slope
                double midpoint = (parameters.parGrowth.maximumTemperature +
                    parameters.parResources.criticalHeatTemperature) / 2;
                double width = Math.Abs(parameters.parGrowth.maximumTemperature -
                   parameters.parResources.criticalHeatTemperature);

                //update chilling units
                heatStress = 1 / (1 + (float)Math.Exp(10 / width * ((input.airTemperatureMaximum - midpoint))));
            }
            return heatStress;
        }

        /// <summary>
        /// Cold-stress multiplier applied to GPP. Returns 0 below
        /// <c>parResources.criticalColdTemperature</c>, 1 above the growth
        /// <c>minimumTemperature</c> and a sigmoidal transition in between.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>airTemperatureMinimum</c>).</param>
        /// <param name="parameters">Species parameter set (uses <c>parGrowth</c> and <c>parResources</c>).</param>
        /// <returns>Dimensionless cold-stress modifier in [0,1] (1 = no stress).</returns>
        public static float coldStressFunction(input input, parameters parameters)
        {
            float coldStress = 0;

            if (input.airTemperatureMinimum <= parameters.parResources.criticalColdTemperature)
            {
                coldStress = 0;
            }
            else if (input.airTemperatureMinimum > parameters.parGrowth.minimumTemperature)
            {
                coldStress = 1;
            }
            else
            {

                //compute lag and slope
                double midpoint = (parameters.parGrowth.minimumTemperature +
                    parameters.parResources.criticalColdTemperature) / 2;
                double width = Math.Abs(parameters.parGrowth.minimumTemperature -
                   parameters.parResources.criticalColdTemperature);

                //update chilling units
                coldStress = 1 / (1 + (float)Math.Exp(10 / -width * ((input.airTemperatureMinimum - midpoint))));
            }

            return coldStress;
        }

        /// <summary>
        /// Water-stress modifier applied to GPP, based on a rolling-window
        /// normalised evaporative-demand index I = (ΣET0 − ΣP) / (ΣET0 + ΣP),
        /// rescaled to a water-availability score in [0,1]
        /// (see doi:10.1016/j.geoderma.2021.115003). During spin-up
        /// (window not yet filled) returns 1 (no stress). Above the
        /// <c>parResources.waterStressThreshold</c> GPP is not limited;
        /// below the threshold the multiplier declines linearly to zero.
        /// The routine also maintains the rolling window state on
        /// <c>outputT1.resources</c>.
        /// </summary>
        /// <param name="input">Daily input record.</param>
        /// <param name="outputT1">Current-day state; holds the rolling P/ET0 memory updated in place.</param>
        /// <param name="parameters">Species parameter set (uses <c>parResources</c>).</param>
        /// <returns>Dimensionless water-stress modifier in [0,1] (1 = no stress).</returns>
        public static float waterStressFunction(input input, output outputT1, parameters parameters)
        {
            float waterAvailability = 0;
            float waterStressGPP = 0;

            float waterStress = 0;
            float et0 = referenceEvapotranspiration(input);

            outputT1.resources.PrecipitationMemory.Add(input.precipitation);
            outputT1.resources.ET0memory.Add(et0);

            // ============================================================
            // SPIN-UP
            // ============================================================
            if (outputT1.resources.PrecipitationMemory.Count < (int)parameters.parResources.waterStressDays)
            {
                return (1f);
            }
            else
            {
                ////compute water stress: https://doi.org/10.1016/j.geoderma.2021.115003

                // ============================================================
                // Compute water availability using normalized evaporative demand
                // (ET0 - P) / (ET0 + P), rescaled to [0–1]
                // ============================================================

                float et0Sum = outputT1.resources.ET0memory.Sum();
                float prec = outputT1.resources.PrecipitationMemory.Sum();

                // Numerical safety
                float denom = et0Sum + prec + 1e-6f;

                // Normalized evaporative demand index [-1, +1]
                float I = (et0Sum - prec) / denom;
                I = Math.Clamp(I, -1f, 1f);

                // Rescale to water availability [0, 1]
                // I = -1 → waterAvailability = 1 (no stress)
                // I =  0 → waterAvailability = 0.5
                // I = +1 → waterAvailability = 0 (max stress)
                waterAvailability = 1f - (I + 1f) * 0.5f;

                // Final safety clamp
                waterAvailability = Math.Clamp(waterAvailability, 0f, 1f);


                //compute water stress GPP
                if (waterAvailability >= parameters.parResources.waterStressThreshold)
                {
                    waterStressGPP = 1;
                }
                else
                {
                    waterStressGPP = .5f *
                        (waterAvailability - parameters.parResources.waterStressThreshold) + 1;
                }

                //remove when the memory effect ends
                if (outputT1.resources.ET0memory.Count > (int)parameters.parResources.waterStressDays)
                {
                    outputT1.resources.ET0memory.RemoveAt(0);
                    outputT1.resources.PrecipitationMemory.RemoveAt(0);
                }

            }

            //set maximum water stress to 0
            waterStressGPP = Math.Clamp(waterStressGPP, 0f, 1f);


            return (waterStressGPP);
        }


        #endregion

        #endregion

        #region reproduction
        /// <summary>
        /// Sigmoidal weather-cue response used by the RB+WC and RBxWC
        /// MASTHING formulations to modulate flowering investment as a
        /// function of a solstice-centred temperature index. The function
        /// is monotonically increasing or decreasing depending on the
        /// <paramref name="sign"/> argument, saturates at
        /// <paramref name="budgetLevel"/> and is zero outside
        /// [temperatureCueMinimum, temperatureCueMaximum].
        /// </summary>
        /// <param name="input">Daily input record (unused, kept for signature symmetry).</param>
        /// <param name="parameters">Species parameter set (uses <c>parReproduction</c>).</param>
        /// <param name="solsticeTemperature">Mean air temperature around the previous summer solstice (°C).</param>
        /// <param name="budgetLevel">Upper asymptote of the response (typically the current <c>budgetLevel</c>).</param>
        /// <param name="sign">+1 for decreasing response, −1 for increasing response.</param>
        /// <returns>Weather-cue modifier in [0, <paramref name="budgetLevel"/>].</returns>
        public static float temperatureCueFunction(input input, parameters parameters,
            float solsticeTemperature, float budgetLevel, float sign)
        {            
            //set minimum and maximum temperature of the weather cues
            float tminFlowering = parameters.parReproduction.temperatureCueMinimum;
            float tmaxFlowering = parameters.parReproduction.temperatureCueMaximum;

            float Trange = (tmaxFlowering - tminFlowering) * .5F;

            float responseFunction = budgetLevel / (1 + (float)Math.Exp(sign * parameters.parReproduction.temperatureCueSensitivity *
                (solsticeTemperature - (tminFlowering+Trange))));

            //set limits
            if(responseFunction < 0)
            {
                responseFunction = 0;
            }     
            if(responseFunction > budgetLevel)
            {
                responseFunction = budgetLevel;
            }

            return responseFunction;
        }

    
        #region pollination efficiency
        /// <summary>
        /// Precipitation-driven pollination-efficiency response. Estimates
        /// a sigmoidal decrease of pollination efficiency with increasing
        /// daily rainfall, centred at half of
        /// <c>parReproduction.limitingPollinationPrecipitation</c> with a
        /// sensitivity that scales inversely with the limiting value.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>precipitation</c>).</param>
        /// <param name="parameters">Species parameter set (uses <c>parReproduction</c>).</param>
        /// <returns>Pollination-efficiency modifier in [0,1].</returns>
        public static float pollinationEfficiencyPrecipitation(input input, parameters parameters)
        {
            //instantiate local variable as output
            float responseFunction = 0;

            //set limits
            //TODO: CHANGE 10
            float pollinationPrecipitationLimiting =  parameters.parReproduction.limitingPollinationPrecipitation;

            //estimate parameters
            float sensitivity = 1F - pollinationPrecipitationLimiting / 50F;
            float midpoint = pollinationPrecipitationLimiting * 0.5F;
            float growth = sensitivity * pollinationPrecipitationLimiting;

            //assign value
            responseFunction = 1 / (1 + (float)Math.Exp(growth * (input.precipitation - midpoint)));

            if (responseFunction > 1) responseFunction = 1;
            if (responseFunction < 0) responseFunction = 0;

            return responseFunction;

        }
        #endregion

        /// <summary>
        /// Phenology-driven ripening-dynamics veto. Returns a sigmoidal
        /// modifier of the ripening rate that is close to zero early in
        /// greendown (when leaves are still expanding) and approaches one
        /// once the greendown percentage crosses about 50%, preventing
        /// premature completion of fruit ripening.
        /// </summary>
        /// <param name="input">Daily input record (unused, kept for signature symmetry).</param>
        /// <param name="outputT1">Current-day state (uses <c>greenDownPercentage</c>).</param>
        /// <param name="parameters">Species parameter set (unused, kept for signature symmetry).</param>
        /// <returns>Ripening-dynamics modifier in [0,1].</returns>
        public static float ripeningDynamicsFunction(input input, output outputT1, parameters parameters)
        {
            //instantiate local variable as output
            float responseFunction = 0;


            //estimate parameters
            float sensitivity = .9F;
            float midpoint = 50;
            float growth = sensitivity / 10;

            //assign value
            responseFunction = 1 / (1 + (float)Math.Exp(-growth * (outputT1.greenDownPercentage - midpoint)));

            if (responseFunction > 1) responseFunction = 1;
            if (responseFunction < 0) responseFunction = 0;

            return responseFunction;

        }
        #endregion

        #endregion

    }

    #region vvvv execution interface
    /// <summary>
    /// Thin stateful driver used to embed the SWELL + MASTHING daily step
    /// inside the vvvv visual-programming environment. It wires together
    /// the phenology, NDVI, carbon-balance, reproductive and allometry
    /// classes, carries the previous-day state (<c>outputT0</c>) and the
    /// current-day state (<c>outputT1</c>) across calls and reads the
    /// parameter CSV the first time it is invoked. Not used by the
    /// command-line runner.
    /// </summary>
    public class vvvvInterface
    {
        /// <summary>Dormant-season driver (induction, endodormancy, ecodormancy).</summary>
        dormancySeason dormancy = new dormancySeason();
        /// <summary>Growing-season driver (growth, greendown, decline).</summary>
        growingSeason growing = new growingSeason();
        /// <summary>Vegetation-index (NDVI) dynamics driver.</summary>
        VIdynamics NDVIdynamics = new VIdynamics();
        /// <summary>Carbon-balance / resource-budget driver.</summary>
        resources resources = new resources();
        /// <summary>Reproductive module driver.</summary>
        reproduction reproduction = new reproduction();
        /// <summary>Tree-allometry driver.</summary>
        allometry allometry = new allometry();
        /// <summary>State of the previous simulated day.</summary>
        output outputT0 = new output();
        /// <summary>State of the current simulated day.</summary>
        output outputT1 = new output();
        /// <summary>Species parameter set, populated on the first call.</summary>
        parameters parameters = new parameters();

        /// <summary>
        /// Advances the SWELL + MASTHING daily loop by one step for the
        /// current <paramref name="input"/> record. On the first call the
        /// parameter CSV at <paramref name="parametersFile"/> is loaded and
        /// the tree allometry is initialised; subsequent calls only run
        /// the daily modules (dormancy → growth → NDVI → resources →
        /// reproduction) and return the updated <see cref="output"/>
        /// record.
        /// </summary>
        /// <param name="input">Daily meteorological drivers for the current day.</param>
        /// <param name="parametersFile">Path to a species parameter CSV (same format as the command-line runner).</param>
        /// <returns>Updated current-day state (also retained as <c>outputT1</c> for the next call).</returns>
        public output vvvvExecution(input input, string parametersFile)
        {

            //pass values from the previous day
            outputT0 = outputT1;
            outputT1 = new output();
            outputT1.isDormancyInduced = outputT0.isDormancyInduced;
            outputT1.isInvestmentDecided = outputT0.isInvestmentDecided;
            outputT1.isFloweringCompleted = outputT0.isFloweringCompleted;
            outputT1.isMaximumLAIreached = outputT0.isMaximumLAIreached;
            outputT1.isMinimumLAIreached = outputT0.isMinimumLAIreached;
            outputT1.ndvi_LAImax = outputT0.ndvi_LAImax;
            outputT1.viBudBreak = outputT0.viBudBreak;
            outputT1.resources.PrecipitationMemory = outputT0.resources.PrecipitationMemory;
            outputT1.resources.ET0memory = outputT0.resources.ET0memory;
            outputT1.reproduction.solsticeTemperatureCue = outputT0.reproduction.solsticeTemperatureCue;
            outputT1.reproduction.ripeningActualState = outputT0.reproduction.ripeningActualState;

            //initialize the outputT0
            if (outputT0.vi == 0)
            {
                parameters=readParameters(parametersFile);
                outputT0.vi = parameters.parVegetationIndex.minimumVI * 100F;
                input.tree = allometry.allometryInitialization(input.tree, parameters, outputT0, outputT1);
                //set initial budget as half of the maximum budget
                outputT0.resources.resourceBudget = input.tree.totalBiomass * parameters.parResources.resourceBudgetFraction*.5F;
            }

            parameters.modelVersion = "RBxWC";
            parameters.parReproduction.temperatureCueMinimum = 14;
            parameters.parReproduction.temperatureCueMaximum = 22;
            //compute rad data
            if (input.airTemperatureMinimum > input.airTemperatureMaximum)
            {
                input.airTemperatureMaximum = input.airTemperatureMinimum;
                input.airTemperatureMinimum = input.airTemperatureMaximum;
            }
            //call the functions
            //dormancy season
            dormancy.induction(input, parameters, outputT0, outputT1);
            dormancy.endodormancy(input, parameters, outputT0, outputT1);
            dormancy.ecodormancy(input, parameters, outputT0, outputT1);
            //growing season
            growing.growthRate(input, parameters, outputT0, outputT1);
            growing.greendownRate(input, parameters, outputT0, outputT1);
            growing.declineRate(input, parameters, outputT0, outputT1);
            //NDVI dynamics
            NDVIdynamics.ndviNormalized(input, parameters, outputT0, outputT1);
            //resources
            resources.photosyntheticRate(input, parameters, outputT0, outputT1);
            reproduction.floweringInvestment(input, parameters, outputT0, outputT1);
            reproduction.pollinationDynamics(input, parameters, outputT0, outputT1);
            reproduction.ripeningDynamics(input, parameters, outputT0, outputT1);

            outputT1.weather = input;

          

            return outputT1;
        }


        /// <summary>
        /// Reflection-based reader for the species parameter CSV used by
        /// the vvvv driver. Each line of the input file is expected to
        /// carry (species, class, name, min, max, value, calibrationFlag)
        /// and the method dispatches each value to the appropriate
        /// sub-container (parDormancy, parEndodormancy, parEcodormancy,
        /// parGrowth, parGreendown, parDecline, parGrowingSeason,
        /// parResources, parReproduction) using the reflection-based
        /// property lookup.
        /// </summary>
        /// <param name="parameterFile">Path to the species parameter CSV.</param>
        /// <returns>Fully populated <see cref="parameters"/> object.</returns>
        public parameters readParameters(string parameterFile)
        {
            //instance of output parameters class
            parameters parameters = new parameters();

            //properties of the single parameter classes
            parDormancyInduction parDormancy = new parDormancyInduction();
            PropertyInfo[] propsDormancy = parDormancy.GetType().GetProperties();//get all properties
            parEndodormancy parEndodormancy = new parEndodormancy();
            PropertyInfo[] propsEndodormancy = parEndodormancy.GetType().GetProperties();//get all properties
            parEcodormancy parEcodormancy = new parEcodormancy();
            PropertyInfo[] propsEcodormancy = parEcodormancy.GetType().GetProperties();//get all properties
            parGrowth parGrowth = new parGrowth();
            PropertyInfo[] propsGrowth = parGrowth.GetType().GetProperties();//get all properties
            parGreendown parGreendown = new parGreendown();
            PropertyInfo[] propsGreendown = parGreendown.GetType().GetProperties();//get all properties
            parSenescence parDecline = new parSenescence();
            PropertyInfo[] propsDecline = parDecline.GetType().GetProperties();//get all properties
            parVegetationIndex parGrowingSeason = new parVegetationIndex();
            PropertyInfo[] propsGrowingSeason = parGrowingSeason.GetType().GetProperties();//get all properties
            parResources parResources = new parResources();
            PropertyInfo[] propsResources = parResources.GetType().GetProperties();//get all properties
            parReproduction parReproduction = new parReproduction();
            PropertyInfo[] propsReproduction = parReproduction.GetType().GetProperties();//get all properties

            //open the stream
            StreamReader streamReader = new StreamReader(parameterFile);
            streamReader.ReadLine();


            //read the data
            while (!streamReader.EndOfStream)
            {
                //read and split the first line
                string[] line = streamReader.ReadLine().Split(',');
                string paramClass = line[1];
                string paramName = line[2];
                float paramValue = float.Parse(line[5]);
                //split class from param name
                if (paramClass == "parDormancy")
                {
                    foreach (PropertyInfo prp in propsDormancy)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parDormancy, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parEndodormancy")
                {
                    foreach (PropertyInfo prp in propsEndodormancy)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parEndodormancy, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parEcodormancy")
                {
                    foreach (PropertyInfo prp in propsEcodormancy)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parEcodormancy, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parGrowth")
                {
                    foreach (PropertyInfo prp in propsGrowth)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parGrowth, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parDecline")
                {
                    foreach (PropertyInfo prp in propsDecline)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parDecline, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parGreendown")
                {
                    foreach (PropertyInfo prp in propsGreendown)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parGreendown, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parGrowingSeason")
                {
                    foreach (PropertyInfo prp in propsGrowingSeason)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parGrowingSeason, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parResources")
                {
                    foreach (PropertyInfo prp in propsResources)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parResources, paramValue); //set the values for this parameter
                        }

                    }
                }
                else if (paramClass == "parReproduction")
                {
                    foreach (PropertyInfo prp in propsReproduction)
                    {
                        if (paramName == prp.Name)
                        {
                            prp.SetValue(parReproduction, paramValue); //set the values for this parameter
                        }

                    }
                }
            }

            parameters.parDormancyInduction = parDormancy;
            parameters.parEndodormancy = parEndodormancy;
            parameters.parEcodormancy = parEcodormancy;
            parameters.parGrowth = parGrowth;
            parameters.parGreendown = parGreendown;
            parameters.parSenescence = parDecline;
            parameters.parVegetationIndex = parGrowingSeason;
            parameters.parResources = parResources;
            parameters.parReproduction = parReproduction;


            return parameters;
        }


    }



    #endregion

}