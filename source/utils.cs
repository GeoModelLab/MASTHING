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
            // Solar constant at the top of the atmosphere (MJ m-2 h-1).
            float solarConstant = 4.921F;
            // Degrees → radians conversion factor (cached as a float for speed).
            float DtoR = (float)Math.PI / 180;
            // Earth–Sun distance correction factor (dimensionless).
            float dd;
            // sin(δ)·sin(φ) term used in the daily radiation integral.
            float ss;
            // cos(δ)·cos(φ) term used in the daily radiation integral.
            float cc;
            // Sunset hour angle ω_s (radians).
            float ws;
            // Fallback day length at polar latitudes (hours).
            float dayHours = 0;

            // Earth–Sun distance correction (Spencer / Duffie & Beckman style).
            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            // Solar declination δ (radians) — Cooper (1969) approximation.
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            // sin(δ) · sin(φ): vertical projection term at local latitude φ.
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            // cos(δ) · cos(φ): horizontal projection term at local latitude φ.
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            // Sunset hour angle ω_s = acos(−tan(δ)·tan(φ)) (radians).
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                // Day length in hours = (24/π) · ω_s; 0.13333 ≈ 24/(180) absorbs the °→rad conversion.
                input.radData.dayLength = 0.13333F / DtoR * ws;
                // Daily extraterrestrial radiation Ra (MJ m-2 day-1): standard integral
                // Ra = (24·Gsc/π) · dr · (ω_s·sin(δ)·sin(φ) + cos(δ)·cos(φ)·sin(ω_s)).
                input.radData.etr = solarConstant * dd * 24 / (float)Math.PI
                    * (ws * ss + cc * (float)Math.Sin(ws));
            }
            else
            {
                // Polar region: the closed-form integral breaks down — default to 0 h.
                input.radData.dayLength = dayHours;
            }
            // Sunrise / sunset expressed in local solar time (hours), centred on solar noon.
            input.radData.hourSunrise = 12 - input.radData.dayLength / 2;
            input.radData.hourSunset = 12 + input.radData.dayLength / 2;


            // Estimate global solar radiation at ground level (Hargreaves–Samani, °C basis).
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
            // Degrees → radians conversion factor (precomputed once).
            float DtoR = (float)Math.PI / 180;
            // Earth–Sun distance correction factor (unused but kept for symmetry with astronomy()).
            float dd;
            // sin(δ)·sin(φ) term.
            float ss;
            // cos(δ)·cos(φ) term.
            float cc;
            // Sunset hour angle for the current day (radians).
            float ws;
            // Fallback day length at polar latitudes (hours).
            float dayHours = 0;

            // Earth–Sun distance correction (kept consistent with astronomy()).
            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            // Solar declination δ for the current day (radians).
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            // Solar declination for the previous day — used by callers that need day-length derivatives.
            float SolarDeclinationYesterday = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.AddDays(-1).DayOfYear));
            // sin(δ)·sin(φ) at the current latitude.
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            // cos(δ)·cos(φ) at the current latitude.
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            // Sunset hour angle ω_s = acos(−tan(δ)·tan(φ)).
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));
            // Same formula applied to yesterday's declination (diagnostic).
            float wsYesterday = (float)Math.Acos(-Math.Tan(SolarDeclinationYesterday) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                // Day length = (24/π) · ω_s (hours).
                dayHours = 0.13333F / DtoR * ws;
            }
            else
            {
                // At extreme latitudes, polar-day / polar-night handling is deferred to 0.
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
            // Accumulator for the 24 hourly values returned to the caller.
            List<float> hourlyTemperatures = new List<float>();
            // Loop index (0 = 00:00, 23 = 23:00 local solar time).
            int h = 0;

            // Daily mean temperature (°C) — centre of the cosine.
            double Tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            // Diurnal amplitude Tmax − Tmin (°C) — amplitude of the cosine.
            double DT = input.airTemperatureMaximum - input.airTemperatureMinimum;
            for (h = 0; h < 24; h++)
            {
                //todo: change with hour of the day with maximum temperature
                // Cosine interpolation centred on hour 14 (peak temperature); 0.2618 rad ≈ 2π/24.
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
            // Output: global solar radiation at ground level (MJ m-2 day-1).
            float gsr = 0;
            // kRs  : Hargreaves adjustment coefficient (°C^-0.5),
            // Ra   : top-of-atmosphere radiation (MJ m-2 day-1),
            // Rs   : surface solar radiation (MJ m-2 day-1).
            float kRs, Ra, Rs;
            // 0.17 is the FAO-recommended interior value; use 0.19 near coasts.
            kRs = 0.17F;
            Ra = ExtraterrestrialRadiation;

            // Hargreaves–Samani: Rs = kRs · sqrt((Tmax − Tmin)·Ra) — note (ΔT·Ra) sits inside the sqrt.
            Rs = kRs * (float)Math.Sqrt((Tmax - Tmin) * Ra);

            // Round to two decimals to keep the CSV output compact.
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
            // Output: latent heat of vaporisation λ (MJ kg-1).
            float latentHeat = 0;
            float a = 2.501F;    // intercept of the linear relation (MJ kg-1)
            float b = 0.002361F; // slope (MJ kg-1 °C-1)

            // Daily mean air temperature used as the argument of the linear relation.
            float avgT = 0.5F * (Tmax + Tmin);
            // λ = 2.501 − 0.002361 · Tavg.
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
            // Output: daily reference evapotranspiration (mm day-1).
            float ET0 = 0;
            // Short-hand aliases for the daily temperature extremes.
            float Tmax = input.airTemperatureMaximum;
            float Tmin = input.airTemperatureMinimum;
            // Latent heat of vaporisation λ (MJ kg-1) converts radiation to mm of evaporated water.
            float l = latentHeatVaporization(Tmax, Tmin);    // latent heat of vaporisation
            // Top-of-atmosphere radiation Ra (MJ m-2 day-1), populated by astronomy().
            float Gr = input.radData.etr;
            // Calibration offset (0 mm day-1) and slope (1) — left configurable for regional tuning.
            float a = 0F;
            float b = 1F;

            // Hargreaves–Samani ET0 = (1/λ) · 0.0023 · (Tavg + 17.8) · sqrt(Tmax − Tmin) · Ra.
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
            //local output variable (dimensionless forcing in [0,1]).
            float forcingRate = 0;

            //average air temperature used as the driver of the Yan & Hunt (1999) response.
            float averageAirTemperature = (input.airTemperatureMaximum +
                input.airTemperatureMinimum) / 2;

            //if average temperature is below minimum or above maximum — no thermal forcing.
            if (averageAirTemperature < tmin || averageAirTemperature > tmax)
            {
                forcingRate = 0;
            }
            else
            {
                //first term: decreasing linear arm between Topt and Tmax.
                float firstTerm = (tmax - averageAirTemperature) / (tmax - topt);
                //second term: rising linear arm between Tmin and Topt.
                float secondTerm = (averageAirTemperature - tmin) / (topt - tmin);
                //exponent controlling asymmetry between rising and falling arms.
                float Exponential = (topt - tmin) / (tmax - topt);

                //Yan & Hunt (1999) closed-form: forcingRate = firstTerm · secondTerm^Exponential.
                forcingRate = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            }
            //return the dimensionless forcing rate.
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
            //local variable to store the dimensionless photoperiod response in [0,1].
            float photoperiodFunction = 0;

            //below the non-limiting photoperiod the day is short enough to fully allow dormancy induction.
            if (input.radData.dayLength < parameters.parDormancyInduction.notLimitingPhotoperiod)
            {
                photoperiodFunction = 1;
            }
            //above the limiting photoperiod the day is too long — induction is fully inhibited.
            else if (input.radData.dayLength > parameters.parDormancyInduction.limitingPhotoperiod)
            {
                photoperiodFunction = 0;
            }
            else
            {
                //sigmoid midpoint: halfway between the non-limiting and limiting photoperiod thresholds.
                float midpoint = (parameters.parDormancyInduction.limitingPhotoperiod + parameters.parDormancyInduction.notLimitingPhotoperiod) * 0.5F;
                //sigmoid width: distance between the two thresholds — controls the slope.
                float width = parameters.parDormancyInduction.limitingPhotoperiod - parameters.parDormancyInduction.notLimitingPhotoperiod;

                //descending logistic: f(L) = 1 / (1 + exp((10/width)·(L − midpoint))).
                photoperiodFunction = 1 / (1 + (float)Math.Exp(10 / width *
                    ((input.radData.dayLength - midpoint))));

            }
            //return the photoperiod modifier.
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
            //daily mean temperature used as the argument of the sigmoidal response.
            float tAverage = (float)(input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;

            //local variable to store the dimensionless temperature response in [0,1].
            float temperatureFunction = 0;

            //cold enough to fully allow dormancy induction.
            if (tAverage <= parameters.parDormancyInduction.notLimitingTemperature)
            {
                temperatureFunction = 1;
            }
            //too warm — induction fully inhibited.
            else if (tAverage >= parameters.parDormancyInduction.limitingTemperature)
            {
                temperatureFunction = 0;
            }
            else
            {
                //sigmoid midpoint: average of the limiting and non-limiting temperature thresholds.
                float midpoint = (parameters.parDormancyInduction.limitingTemperature + parameters.parDormancyInduction.notLimitingTemperature) * .5F;
                //sigmoid width: distance between thresholds (controls the slope).
                float width = (parameters.parDormancyInduction.limitingTemperature - parameters.parDormancyInduction.notLimitingTemperature);
                //descending logistic: f(T) = 1 / (1 + exp((10/width)·(T − midpoint))).
                temperatureFunction = 1 / (1 + (float)Math.Exp(10 / width * (tAverage - midpoint)));

            }
            //return the temperature modifier.
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

            //per-hour chilling contributions returned for diagnostics.
            chillingUnitsList = new List<float>();
            //internal variable holding the chilling contribution of the current hour.
            float chillingUnits = 0;

            #region chilling units accumulation
            //loop over the 24 hourly temperatures produced by hourlyTemperature().
            foreach (var temperature in hourlyTemperatures)
            {
                //when hourly temperature is below the limiting lower temperature or above the limiting upper temperature
                if (temperature < parameters.parEndodormancy.limitingLowerTemperature ||
                    temperature > parameters.parEndodormancy.limitingUpperTemperature)
                {
                    //outside the [Tlow_lim, Tup_lim] envelope — no chilling accumulated.
                    chillingUnits = 0; //not needed, just to be clear
                }
                //when hourly temperature is between the limiting lower temperature
                //and the non limiting lower temperature
                else if (temperature >= parameters.parEndodormancy.limitingLowerTemperature &&
                    temperature < parameters.parEndodormancy.notLimitingLowerTemperature)
                {
                    //midpoint on the lower flank of the bell response.
                    double midpoint = (parameters.parEndodormancy.limitingLowerTemperature +
                        parameters.parEndodormancy.notLimitingLowerTemperature) / 2;
                    //width of the lower transition (always positive).
                    double width = Math.Abs(parameters.parEndodormancy.limitingLowerTemperature -
                        parameters.parEndodormancy.notLimitingLowerTemperature);

                    //ascending logistic on the lower flank (note negative width flips the sigmoid).
                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / -width * ((temperature - midpoint))));
                }
                //when hourly temperature is between the non limiting lower temperature and the
                //non limiting upper temperature
                else if (temperature >= parameters.parEndodormancy.notLimitingLowerTemperature &&
                    temperature <= parameters.parEndodormancy.notLimitingUpperTemperature)
                {
                    //optimum plateau: full chilling accumulation.
                    chillingUnits = 1;
                }
                //when hourly temperature is between the non limiting upper temperature and the
                //limiting upper temperature
                else
                {
                    //midpoint on the upper flank of the bell response.
                    double midpoint = (parameters.parEndodormancy.limitingUpperTemperature +
                       parameters.parEndodormancy.notLimitingUpperTemperature) / 2;
                    //width of the upper transition.
                    double width = Math.Abs(parameters.parEndodormancy.limitingUpperTemperature -
                        parameters.parEndodormancy.notLimitingUpperTemperature);

                    //descending logistic on the upper flank.
                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / width * ((temperature - midpoint))));
                }

                //store the hourly contribution for diagnostics.
                chillingUnitsList.Add(chillingUnits);
            }
            #endregion

            //daily chilling rate = mean of the 24 hourly contributions.
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
            //local variable holding the daily ecodormancy forcing rate.
            float ecodormancyRate = 0;


            //photoperiod ratio (day length / non-limiting photoperiod), clamped to 1.
            float ratioPhotoperiod = input.radData.dayLength / parameters.parEcodormancy.notLimitingPhotoperiod;
            if (ratioPhotoperiod > 1)
            {
                ratioPhotoperiod = 1;
            }

            //boost the asymptote proportionally to photoperiod × endodormancy-completion fraction.
            float asymptoteModifier = ratioPhotoperiod * asymptote;
            //effective upper asymptote: interpolates between the raw asymptote (no photoperiod) and 1 (full photoperiod).
            float newAsymptote = asymptote + (1 - asymptote) * asymptoteModifier;

            //sigmoid midpoint: shifts to higher temperatures when days are short.
            float midpoint = parameters.parEcodormancy.notLimitingTemperature * 0.5F +
                (1 - ratioPhotoperiod) * parameters.parEcodormancy.notLimitingTemperature;
            //daily mean air temperature — the driver of the ecodormancy response.
            float tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
            //sigmoid width: narrows as photoperiod decreases (steeper response in spring).
            float width = parameters.parEcodormancy.notLimitingTemperature * ratioPhotoperiod;

                //ascending logistic: rate = asymptote / (1 + exp(−10/width · (T − midpoint))).
                ecodormancyRate = newAsymptote /
              (1 + (float)Math.Exp(-10 / width * ((tavg - midpoint)))); ;

            //compute ecodormancy rate




            //return the daily forcing rate.
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
            //output: heat-stress multiplier (1 = no stress, 0 = full stress).
            float heatStress = 0;

            //below the growth maximum temperature: no heat stress.
            if (input.airTemperatureMaximum < parameters.parGrowth.maximumTemperature)
            {
                heatStress = 1;
            }
            //above the critical heat threshold: full stress.
            else if (input.airTemperatureMaximum >= parameters.parResources.criticalHeatTemperature)
            {
                heatStress = 0;
            }
            else
            {
                //sigmoid midpoint: halfway between the growth maximum and the critical heat threshold.
                double midpoint = (parameters.parGrowth.maximumTemperature +
                    parameters.parResources.criticalHeatTemperature) / 2;
                //sigmoid width: distance between the two cardinal values (always positive).
                double width = Math.Abs(parameters.parGrowth.maximumTemperature -
                   parameters.parResources.criticalHeatTemperature);

                //descending logistic: stress starts at 1 just above Tmax_growth and drops to 0 near Tcrit.
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
            //output: cold-stress multiplier (1 = no stress, 0 = full stress).
            float coldStress = 0;

            //below the critical cold threshold: full stress.
            if (input.airTemperatureMinimum <= parameters.parResources.criticalColdTemperature)
            {
                coldStress = 0;
            }
            //above the growth minimum temperature: no cold stress.
            else if (input.airTemperatureMinimum > parameters.parGrowth.minimumTemperature)
            {
                coldStress = 1;
            }
            else
            {

                //sigmoid midpoint: halfway between the critical cold threshold and the growth minimum.
                double midpoint = (parameters.parGrowth.minimumTemperature +
                    parameters.parResources.criticalColdTemperature) / 2;
                //sigmoid width: distance between the two cardinal values (positive).
                double width = Math.Abs(parameters.parGrowth.minimumTemperature -
                   parameters.parResources.criticalColdTemperature);

                //ascending logistic: stress rises from 0 (at Tcrit_cold) to 1 (at Tmin_growth).
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
            //water availability score in [0,1] (1 = wet, 0 = dry).
            float waterAvailability = 0;
            //water-stress modifier applied to GPP (1 = no stress, 0 = full stress).
            float waterStressGPP = 0;

            //legacy variable kept for signature symmetry.
            float waterStress = 0;
            //daily reference evapotranspiration (mm day-1) via Hargreaves–Samani.
            float et0 = referenceEvapotranspiration(input);

            //push today's precipitation and ET0 into the rolling-window memories.
            outputT1.resources.PrecipitationMemory.Add(input.precipitation);
            outputT1.resources.ET0memory.Add(et0);

            // ============================================================
            // SPIN-UP
            // ============================================================
            //during the rolling-window warm-up (memory not yet full), return a neutral modifier.
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

                //cumulative ET0 and precipitation over the rolling window.
                float et0Sum = outputT1.resources.ET0memory.Sum();
                float prec = outputT1.resources.PrecipitationMemory.Sum();

                //Numerical safety: add a small epsilon to avoid division by zero.
                float denom = et0Sum + prec + 1e-6f;

                //Normalized evaporative demand index I ∈ [−1, +1]; positive = dry.
                float I = (et0Sum - prec) / denom;
                I = Math.Clamp(I, -1f, 1f);

                //Rescale I to a water-availability score in [0,1]:
                // I = −1 → waterAvailability = 1 (no stress)
                // I =  0 → waterAvailability = 0.5
                // I = +1 → waterAvailability = 0 (max stress)
                waterAvailability = 1f - (I + 1f) * 0.5f;

                //Final safety clamp to guard against floating-point drift.
                waterAvailability = Math.Clamp(waterAvailability, 0f, 1f);


                //above the threshold → no stress; below → linearly decreasing GPP multiplier.
                if (waterAvailability >= parameters.parResources.waterStressThreshold)
                {
                    waterStressGPP = 1;
                }
                else
                {
                    //linear decline: slope 0.5 per unit of water-availability deficit, intercept 1.
                    waterStressGPP = .5f *
                        (waterAvailability - parameters.parResources.waterStressThreshold) + 1;
                }

                //drop the oldest record to keep the rolling window at waterStressDays entries.
                if (outputT1.resources.ET0memory.Count > (int)parameters.parResources.waterStressDays)
                {
                    outputT1.resources.ET0memory.RemoveAt(0);
                    outputT1.resources.PrecipitationMemory.RemoveAt(0);
                }

            }

            //clamp the modifier to [0,1] as a final safety net.
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
            //cardinal minimum / maximum temperatures defining the weather-cue active range.
            float tminFlowering = parameters.parReproduction.temperatureCueMinimum;
            float tmaxFlowering = parameters.parReproduction.temperatureCueMaximum;

            //half-width of the active range — shifts the sigmoid midpoint to (Tmin + Trange).
            float Trange = (tmaxFlowering - tminFlowering) * .5F;

            //logistic: asymptote = budgetLevel, sign flips monotonicity, sensitivity controls slope.
            float responseFunction = budgetLevel / (1 + (float)Math.Exp(sign * parameters.parReproduction.temperatureCueSensitivity *
                (solsticeTemperature - (tminFlowering+Trange))));

            //clip negative values to 0 (safety in case of numerical drift).
            if(responseFunction < 0)
            {
                responseFunction = 0;
            }
            //cap the response at the budget-level asymptote.
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
            //pollination-efficiency modifier in [0,1] (1 = full efficiency, 0 = washed-out).
            float responseFunction = 0;

            //daily rainfall (mm) above which pollination is severely impaired.
            //TODO: CHANGE 10
            float pollinationPrecipitationLimiting =  parameters.parReproduction.limitingPollinationPrecipitation;

            //sensitivity: decreases with the limiting rainfall (drier species react more sharply).
            float sensitivity = 1F - pollinationPrecipitationLimiting / 50F;
            //sigmoid midpoint: half of the limiting rainfall value.
            float midpoint = pollinationPrecipitationLimiting * 0.5F;
            //combined slope factor used inside the logistic.
            float growth = sensitivity * pollinationPrecipitationLimiting;

            //descending logistic: efficiency drops as precipitation increases past the midpoint.
            responseFunction = 1 / (1 + (float)Math.Exp(growth * (input.precipitation - midpoint)));

            //safety clamps to guarantee an output in [0,1].
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
            //ripening-dynamics modifier in [0,1] (close to 0 early in greendown, approaching 1 past 50%).
            float responseFunction = 0;


            //sensitivity: fixed at 0.9 — gives a smooth but decisive transition across the midpoint.
            float sensitivity = .9F;
            //midpoint: 50% greendown — taken as the canopy half-senescence threshold.
            float midpoint = 50;
            //logistic slope factor (sensitivity / 10) matching the [0,100] range of greendown percentage.
            float growth = sensitivity / 10;

            //ascending logistic: 1 / (1 + exp(−growth · (greendownPercentage − 50))).
            responseFunction = 1 / (1 + (float)Math.Exp(-growth * (outputT1.greenDownPercentage - midpoint)));

            //safety clamps to guarantee an output in [0,1].
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