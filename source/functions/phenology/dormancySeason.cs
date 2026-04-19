using source.data;
using System.Collections.Generic;

namespace source.functions
{
    /// <summary>
    /// Dormant-season module of the SWELL phenology model. Contains the
    /// three sequential routines that simulate the transitions from active
    /// growth to dormancy and back to bud break:
    /// <list type="bullet">
    ///   <item><description><see cref="induction"/> — photothermal dormancy induction.</description></item>
    ///   <item><description><see cref="endodormancy"/> — hourly chilling-unit accumulation during true dormancy.</description></item>
    ///   <item><description><see cref="ecodormancy"/> — post-chilling photothermal forcing until bud break.</description></item>
    /// </list>
    /// Each method reads "yesterday" state (<c>output</c>) and writes
    /// "today" state (<c>outputT1</c>) in place, using the parameter set
    /// provided in <c>parameters</c> and the daily forcings in <c>input</c>.
    /// </summary>
    public class dormancySeason
    {
        #region potential SWELL
        #region dormancy induction

        /// <summary>
        /// Simulates dormancy induction as a photothermal integral.
        /// While <c>isDormancyInduced</c> is false, the daily induction rate
        /// is the product of a photoperiod and a temperature response
        /// function (both [0,1]); the state is accumulated until the
        /// <c>photoThermalThreshold</c> is reached, at which point the tree
        /// is considered dormant and the phenological code is bumped.
        /// </summary>
        /// <param name="input">Daily meteorological drivers for the current day.</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parDormancyInduction</c>).</param>
        /// <param name="output">State at the previous day ("yesterday"); read-only.</param>
        /// <param name="outputT1">State at the current day ("today"); updated in place.</param>
        public void induction(input input, parameters parameters, output output, output outputT1)
        {

            //check if dormancy induction started
            if (!outputT1.isDormancyInduced)
            {
                //estimate photoperiod 
                input.radData = utils.astronomy(input);

                #region photothermal units

                #region photothermal rate
                //call photoperiod function
                outputT1.dormancyInduction.photoperiodDormancyInductionRate =
                    utils.photoperiodFunctionInduction(input, parameters, outputT1);
                //call temperature function
                outputT1.dormancyInduction.temperatureDormancyInductionRate =
                    utils.temperatureFunctionInduction(input, parameters, outputT1);

                //compute dormancy induction rate
                outputT1.dormancyInduction.photoThermalDormancyInductionRate =
                    outputT1.dormancyInduction.photoperiodDormancyInductionRate *
                    outputT1.dormancyInduction.temperatureDormancyInductionRate;
                #endregion

                #region photothermal state and completion percentage
                //integrate the rate variable to compute the state variable
                outputT1.dormancyInduction.photoThermalDormancyInductionState =
                    output.dormancyInduction.photoThermalDormancyInductionState +
                    outputT1.dormancyInduction.photoThermalDormancyInductionRate;

                //derive the percentage of phase completion
                outputT1.dormancyInductionPercentage = outputT1.dormancyInduction.photoThermalDormancyInductionState /
                    parameters.parDormancyInduction.photoThermalThreshold * 100;

                //check if dormancy induction is completed
                if (outputT1.dormancyInductionPercentage >= 100)
                {
                    //reset to 100% in case it exceeds (last day integration could be higher than threshold
                    outputT1.dormancyInductionPercentage = 100;
                    //boolean to state that dormancy is induced
                    outputT1.isDormancyInduced = true;
                    //reset to 0 the ecodormancy state
                    outputT1.ecodormancy.ecodormancyState = 0;
                }

                #endregion

                #endregion

                #region update phenological code
                if (outputT1.dormancyInduction.photoThermalDormancyInductionState > 0)
                {
                    outputT1.phenoCode = 1;
                }
                #endregion
            }

        }

        #endregion

        #region endodormancy
        /// <summary>
        /// Simulates hourly chilling-unit accumulation during true dormancy.
        /// Once dormancy has been induced and until ecodormancy is complete,
        /// the daily mean chilling rate is derived from 24 hourly
        /// temperatures via the four-knot response function implemented in
        /// <c>utils.endodormancyRate</c>. The state integrates these rates
        /// and the percentage of completion is evaluated against
        /// <c>parEndodormancy.chillingThreshold</c>.
        /// </summary>
        /// <param name="input">Daily meteorological drivers (used to derive hourly temperatures).</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parEndodormancy</c>).</param>
        /// <param name="output">State at the previous day; read-only.</param>
        /// <param name="outputT1">State at the current day; updated in place.</param>
        public void endodormancy(input input, parameters parameters,
            output output, output outputT1)
        {

            //check if dormancy is induced and ecodormancy is not completed
            if (outputT1.isDormancyInduced && !outputT1.isEcodormancyCompleted)
            {
                //initialize hourly temperature lists (call to the external function in utils static class)
                List<float> hourlyTemperatures = utils.hourlyTemperature(input);

                //internal variable to store chilling units
                float chillingUnits = utils.endodormancyRate(input, parameters, hourlyTemperatures, out List<float> chillingUnitsList);

                //compute daily chilling rate in a 0-1 scale
                outputT1.endodormancy.endodormancyRate = chillingUnits;

                //compute endodormancy progress
                outputT1.endodormancy.endodormancyState = output.endodormancy.endodormancyState +
                    outputT1.endodormancy.endodormancyRate;

                //compute endodormancy percentage
                outputT1.endodormancyPercentage = outputT1.endodormancy.endodormancyState /
                    parameters.parEndodormancy.chillingThreshold * 100;

                //if endodormancy is completed, set the variable to 100
                if (outputT1.endodormancyPercentage >= 100)
                {
                    outputT1.endodormancyPercentage = 100;
                }

            }

        }
        #endregion

        #region ecodormancy
        /// <summary>
        /// Simulates ecodormancy progression (photothermal forcing toward
        /// bud break). The daily rate is computed by
        /// <c>utils.ecodormancyRate</c> with an asymptote that depends on
        /// the endodormancy-completion fraction, so that trees that have
        /// not fully chilled reach a lower plateau. When cumulative
        /// forcing exceeds <c>parEcodormancy.photoThermalThreshold</c>,
        /// bud break is declared (<c>isEcodormancyCompleted = true</c>).
        /// </summary>
        /// <param name="input">Daily meteorological drivers.</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parEcodormancy</c>).</param>
        /// <param name="output">State at the previous day; read-only.</param>
        /// <param name="outputT1">State at the current day; updated in place.</param>
        public void ecodormancy(input input, parameters parameters,
           output output, output outputT1)
        {
            //estimate photoperiod 
            input.radData = utils.astronomy(input);

            //check if dormancy is induced and ecodormancy is not completed
            if (outputT1.isDormancyInduced && !outputT1.isEcodormancyCompleted)
            {
                //the asymptote of photothermal units for ecodormancy depends on endodormancy percentage
                float asymptote = outputT1.endodormancyPercentage / 100;

                //compute ecodormancy rate (call to the external function in utils static class)
                outputT1.ecodormancy.ecodormancyRate = utils.ecodormancyRate(input, asymptote, parameters);

                //compute ecodormancy progress
                outputT1.ecodormancy.ecodormancyState = output.ecodormancy.ecodormancyState + outputT1.ecodormancy.ecodormancyRate;

                //ecodormancy completion percentage
                outputT1.ecodormancyPercentage = outputT1.ecodormancy.ecodormancyState /
                    parameters.parEcodormancy.photoThermalThreshold * 100;

                //if ecodormancy is completed, set the variable to 100 and set the boolean variable
                if (outputT1.ecodormancyPercentage >= 100)
                {
                    outputT1.ecodormancyPercentage = 100;
                    outputT1.isEcodormancyCompleted = true;
                }

                #region update phenological code
                if (outputT1.ecodormancy.ecodormancyState > 0)
                {
                    outputT1.phenoCode = 2;
                    outputT1.isGrowthCompleted = false;
                    outputT1.isDeclineCompleted = false;
                }
                #endregion
            }
            else
            {
                outputT1.ecodormancyPercentage = output.ecodormancyPercentage;
            }
        }
        #endregion
        #endregion

    }
}
