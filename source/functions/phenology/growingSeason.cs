using source.data;
using System;

namespace source.functions
{
    /// <summary>
    /// Growing-season module of the SWELL phenology model. Contains the
    /// three sequential phenophases that follow bud break and carry the
    /// canopy through to leaf shedding:
    /// <list type="bullet">
    ///   <item><description><see cref="growthRate"/> — thermal forcing from bud break to mature canopy.</description></item>
    ///   <item><description><see cref="greendownRate"/> — stable late-growth plateau.</description></item>
    ///   <item><description><see cref="declineRate"/> — photothermal senescence toward leaf shedding.</description></item>
    /// </list>
    /// Each routine updates the "today" state (<c>outputT1</c>) in place
    /// using the species parameters and the daily meteorological drivers.
    /// </summary>
    public class growingSeason
    {
        /// <summary>
        /// Simulates post-bud-break growth as an accumulation of thermal
        /// forcing units (Yan &amp; Hunt, 1999). Once the growth state reaches
        /// <c>parGrowth.thermalThreshold</c> the phenophase is closed
        /// (<c>isGrowthCompleted = true</c>) and the dormancy-induction
        /// accumulator is reset to zero so that next season's photothermal
        /// integral starts from scratch.
        /// </summary>
        /// <param name="input">Daily meteorological drivers for the current day.</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parGrowth</c>).</param>
        /// <param name="output">State at the previous day; read-only.</param>
        /// <param name="outputT1">State at the current day; updated in place.</param>
        public void growthRate(input input, parameters parameters, output output, output outputT1)
        {
            //check if the growth phenophase is not completed and ecodormancy is completed
            if (!outputT1.isGrowthCompleted && outputT1.isEcodormancyCompleted)
            {
                //check if the growth state is below the critical threshold
                if (output.growth.growthState < parameters.parGrowth.thermalThreshold)
                {
                    //compute growth rate
                    outputT1.growth.growthRate =
                            utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                            parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);
                }
                else
                {
                    outputT1.growth.growthRate = 0;
                }

                //update the growth state
                outputT1.growth.growthState = output.growth.growthState + outputT1.growth.growthRate;

                //update phenological code
                if (outputT1.growth.growthState > 0 && outputT1.ecodormancyPercentage == 100)
                {
                    outputT1.ecodormancy.ecodormancyRate = 0;
                    outputT1.endodormancy.endodormancyRate = 0;
                    outputT1.endodormancy.endodormancyState = 0;
                    outputT1.endodormancyPercentage = 0;
                    outputT1.phenoCode = 3;
                }

                //if growth state is above the threshold, set it to the critical threshold
                if (outputT1.growth.growthState > parameters.parGrowth.thermalThreshold &&
                    !outputT1.isGrowthCompleted)
                {
                    outputT1.growth.growthState = parameters.parGrowth.thermalThreshold;
                    outputT1.dormancyInduction.photoThermalDormancyInductionState = 0;
                    outputT1.isGrowthCompleted = true;
                }

                //compute the completion percentage of the growth state
                outputT1.growthPercentage = outputT1.growth.growthState /
                parameters.parGrowth.thermalThreshold * 100;
            }
            else //otherwise growth percentage is kept to the previous value
            {
                outputT1.growthPercentage = output.growthPercentage;
            }
        }

        /// <summary>
        /// Simulates the greendown phenophase (mature canopy plateau).
        /// Starts once growth is complete and accumulates thermal units
        /// until <c>parGreendown.thermalThreshold</c> is reached, at which
        /// point the canopy is considered "mature" and the decline phase
        /// becomes active.
        /// </summary>
        /// <param name="input">Daily meteorological drivers.</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parGreendown</c>, <c>parGrowth</c>).</param>
        /// <param name="output">State at the previous day; read-only.</param>
        /// <param name="outputT1">State at the current day; updated in place.</param>
        public void greendownRate(input input, parameters parameters,
          output output, output outputT1)
        {
            //check if the growth phenophase is  completed and greendown is not completed
            if (outputT1.growthPercentage == 100 && !outputT1.isGreendownCompleted)
            {
                outputT1.isDormancyInduced = true;
                //compute thermal unit (call to an external function in the utils static class)
                outputT1.greenDown.greenDownRate =
                        utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                        parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                //update greendown state variable
                outputT1.greenDown.greenDownState = output.greenDown.greenDownState +
                    outputT1.greenDown.greenDownRate;

                //update greendown percentage
                outputT1.greenDownPercentage = outputT1.greenDown.greenDownState /
                    parameters.parGreendown.thermalThreshold * 100;

                //limit the greendown percentage to 100
                if (outputT1.greenDownPercentage >= 100)
                {
                    outputT1.greenDownPercentage = 100;
                    outputT1.isGreendownCompleted = true;
                    outputT1.isDormancyInduced = false;
                    outputT1.greenDown.greenDownRate = 0;
                }

                //update phenological code
                if (!outputT1.isGreendownCompleted)
                {
                    outputT1.phenoCode = 4;
                }
            }
        }

        /// <summary>
        /// Simulates the decline / senescence phenophase. The daily rate is
        /// a weighted blend of a thermal-forcing term and a photothermal
        /// induction term, the weights of which shift toward the induction
        /// term as senescence progresses. When
        /// <c>parSenescence.photoThermalThreshold</c> is reached the
        /// phenophase is closed (<c>isDeclineCompleted = true</c>).
        /// </summary>
        /// <param name="input">Daily meteorological drivers.</param>
        /// <param name="parameters">Calibrated species parameter set (uses <c>parSenescence</c>, <c>parGrowth</c>, <c>parDormancyInduction</c>).</param>
        /// <param name="output">State at the previous day; read-only.</param>
        /// <param name="outputT1">State at the current day; updated in place.</param>
        public void declineRate(input input, parameters parameters,
           output output, output outputT1)
        {
            //check if the greendown phase is completed and the decline phase is not completed
            if (outputT1.greenDownPercentage == 100 && !outputT1.isDeclineCompleted)
            {
                //compute thermal unit
                float thermalUnit =
                        utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                        parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                //compute rad data
                input.radData = utils.astronomy(input);
                //call photoperiod function
                float photoFunction = utils.photoperiodFunctionInduction(input, parameters, outputT1);
                float tempFunction = utils.temperatureFunctionInduction(input, parameters, outputT1);
                float induPhotoThermal = photoFunction * tempFunction;

                //compute the percentage completion of the decline phase before updating, to compute the weighted average
                float declinePercentageYesterday = output.decline.declineState /
                    parameters.parSenescence.photoThermalThreshold;

                //compute the weighted average of the decline rate
                outputT1.decline.declineRate = thermalUnit * (1 - declinePercentageYesterday) +
                     induPhotoThermal * declinePercentageYesterday;

                //state variable
                outputT1.decline.declineState = output.decline.declineState +
                    outputT1.decline.declineRate;

                //update decline percentage
                outputT1.declinePercentage = outputT1.decline.declineState /
                    parameters.parSenescence.photoThermalThreshold * 100;

                //limit the decline percentage to 100
                if (outputT1.declinePercentage >= 100)
                {
                    outputT1.declinePercentage = 100;
                    outputT1.isDeclineCompleted = true;
                    outputT1.isDormancyInduced = false;
                    outputT1.greenDown.greenDownRate = 0;
                    outputT1.decline.declineRate = 0;
                }

                //update the phenological code
                if (!outputT1.isDeclineCompleted)
                {
                    outputT1.phenoCode = 5;
                }
            }
        }
    }
}