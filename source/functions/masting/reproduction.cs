using source.data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ---------------------------------------------------------------------------
// MASTHING — reproductive allocation module.
//
// Implements the reproduction processes described in Section 2.1.1 and
// Supplementary Section S1.4 of Bregaglio et al. (2026):
//   * flowering investment (resource-budget + temperature cue coupling)
//   * pollination dynamics (Gaussian-like flowering curve + precipitation veto)
//   * ripening dynamics (water stress during seed development).
//
// Three model formulations are supported:
//   - RB     : resource budget only (no explicit weather cue)
//   - RB+WC  : additive weather cue, independent of resource status
//   - RBxWC  : interactive weather cue, modulated by internal resource status
// ---------------------------------------------------------------------------

namespace source.functions
{
    /// <summary>
    /// Reproductive allocation component of MASTHING. Determines how much carbon
    /// is invested in flowers and seeds each year, based on internal reserves,
    /// post-solstice temperature cues and environmental vetoes (rainfall during
    /// flowering, drought during seed ripening).
    /// </summary>
    public class reproduction
    {
        /// <summary>
        /// Computes the annual flowering investment (eq. SE1.24–SE1.25 in the paper).
        /// Temperature cues are accumulated in the post-solstice window (DOY 172–212).
        /// On 31 December the composite cue (TC_t) is combined with the normalised
        /// budget level (BL_t) to produce the reproductive potential, which is then
        /// capped by available reserves and split between flowering and ripening costs.
        /// </summary>
        /// <param name="input">Daily input data (meteo, date, tree structure).</param>
        /// <param name="parameters">Model parameters, including the formulation version (RB / RB+WC / RBxWC).</param>
        /// <param name="output">Model state from the previous day (carry-over).</param>
        /// <param name="outputT1">Model state of the current day, updated in place.</param>
        public void floweringInvestment(input input, parameters parameters, output output, output outputT1)
        {
            //consider weather cues after solstice (temperature) and during summer time 
            if (input.date.DayOfYear >= 172 && input.date.DayOfYear <= 212)
            {
                outputT1.reproduction.solsticeTemperatureCue.Add(input.date, input.airTemperatureMaximum);
            }
           
            //compute weather cues
            if (input.date.DayOfYear==365 && outputT1.reproduction.solsticeTemperatureCue.Count > 0)
            {                
                //set resource budget at 0 to avoid negative values
                if(outputT1.resources.resourceBudget<0){outputT1.resources.resourceBudget = 0;}

                //estimate the budget level as the ratio between the resource budget and the maximum
                float minimumBudget = outputT1.resources.minimumResourceBudgetReproduction;
                float budgetLevel = (outputT1.resources.resourceBudget - minimumBudget) / (outputT1.resources.maximumResourceBudget - minimumBudget); ;
               
                //limit the budget level to 1
                if (budgetLevel >= 1) budgetLevel = 1; if (budgetLevel < 0) budgetLevel = 0;
                outputT1.resources.budgetLevel = budgetLevel;

                #region flowering investment               

                #region cues
                //extract solstice temperature cues for each year
                var temperatureCues = outputT1.reproduction.solsticeTemperatureCue.
                    GroupBy(kv => kv.Key.Year).
                    Select(group => new { Year = group.Key, Values = group.Select(kv => kv.Value).ToList() }).
                    OrderBy(item => item.Year).
                    ToDictionary(item => item.Year, item => item.Values);

                //remove temperature cues from previous year

                // Identify keys to be removed (those older than two years before the current year)
                var keysToRemove = outputT1.reproduction.solsticeTemperatureCue.Keys
                    .Where(date => date.Year < input.date.Year - 2)
                    .ToList();

                // Remove the identified keys from the original dictionary
                foreach (var key in keysToRemove)
                {
                    outputT1.reproduction.solsticeTemperatureCue.Remove(key);
                }

                //calculate the weighted average of the cues for the current year
                float T1Cues = temperatureCues[input.date.Year].Average();

                //declare a variable for the temperature cues function in the current year
                float T1CuesFunction = 0;
                //MODEL RB+WC = temperature cues not interacting with budget level
                if (parameters.modelVersion == "RB+WC")
                {
                    T1CuesFunction = utils.temperatureCueFunction(input, parameters, T1Cues, 1F, -1F);
                }
                //MODEL RBxWC = temperature cues interacting with budget level
                else if (parameters.modelVersion == "RBxWC")
                {
                    T1CuesFunction = utils.temperatureCueFunction(input, parameters, T1Cues, budgetLevel, -1F);
                }
                else //MODEL RB = only resource budget, no effect of temperature cues
                {
                    T1CuesFunction = 1;
                }

                //empty list for the cues of the previous year
                List<float> T2temp = new List<float>();
                //if there are cues for the previous year
                if (temperatureCues.ContainsKey(input.date.Year - 1))
                {
                    T2temp = temperatureCues[input.date.Year - 1];
                }
                else //first year of simulation, use the cues of the current year
                {
                    T2temp = temperatureCues[input.date.Year];
                }
                //compute the weights of the cues after solstice until the end of july (172-212) T2
                float T2Cues = T2temp.Average();
                float T2CuesFunction = 0;
                //MODEL RB+WC = temperature cues not interacting with budget level
                if (parameters.modelVersion == "RB+WC")
                {
                    T2CuesFunction = (utils.temperatureCueFunction(input, parameters, T2Cues, 1F,1F));
                }
                //MODEL RBxWC = temperature cues interacting with budget level
                else if (parameters.modelVersion == "RBxWC") 
                {
                    T2CuesFunction = utils.temperatureCueFunction(input, parameters, T2Cues, budgetLevel, 1F);
                }
                else //MODEL RB = only resource budget, no effect of temperature cues
                {
                    T2CuesFunction = 1;
                }

                 //estimate the temperature cue for the current year by weighting the cues of the current and previous year with a parameter under calibration (temperatureYearTweight)
                 float temperatureCue = (T1CuesFunction*parameters.parReproduction.temperatureYearTweight) + 
                    (T2CuesFunction*(1-parameters.parReproduction.temperatureYearTweight));

                //truncate temperature cues if negative
                if (temperatureCue < 0) temperatureCue = 0;
                if (temperatureCue > 1) temperatureCue = 1;

                //set the temperature cue for the current year
                outputT1.reproduction.floweringWeatherCues = temperatureCue;

                #endregion

                #endregion

                #region reproduction and flowering investment
                float reproductionPotential = 0;

                if (parameters.modelVersion == "RBxWC" || parameters.modelVersion == "RB+WC")
                {
                    reproductionPotential = (budgetLevel + outputT1.reproduction.floweringWeatherCues)*.5f;
                }
                else //MODEL RB = only resource budget, no effect of temperature cues
                {
                    reproductionPotential = budgetLevel;
                }
                
                //modulate flowering investment to the effect size
                if (reproductionPotential>1) { reproductionPotential = 1;}
                
                //set reproduction investment
                outputT1.reproduction.reproductionInvestment = Math.Min(outputT1.resources.resourceBudget,
                    reproductionPotential * outputT1.resources.maximumResourceBudget);
             
                //compute additional cost for flowers
                float additionalCostFlowers = 1 + parameters.parReproduction.flowersToFruitCost;

                //compute ripening investment
                outputT1.reproduction.ripeningInvestment = outputT1.reproduction.reproductionInvestment / additionalCostFlowers;

                //compute flowering investment
                outputT1.reproduction.floweringInvestment = outputT1.reproduction.reproductionInvestment - outputT1.reproduction.ripeningInvestment;
               
                if(outputT1.reproduction.floweringInvestment<0){ outputT1.reproduction.floweringInvestment = 0;}   
    
                if(outputT1.resources.resourceBudget < 0){outputT1.resources.resourceBudget = 0;}
                
                #endregion

                //set the flowering decision to true
                outputT1.isInvestmentDecided = true;
            }
            else
            {
                outputT1.reproduction.floweringInvestment = output.reproduction.floweringInvestment;
                outputT1.reproduction.reproductionInvestment = output.reproduction.reproductionInvestment;
                outputT1.reproduction.ripeningInvestment = output.reproduction.ripeningInvestment;
            }
        }
        /// <summary>
        /// Simulates pollination dynamics over the flowering window (defined by
        /// <c>floweringTime</c> ± <c>floweringDuration</c>/2 of growth completion).
        /// Potential pollination follows a Gaussian-like curve centred on the flowering
        /// peak; actual pollination is reduced by the precipitation-based efficiency
        /// factor (eq. SE1.26). The resource budget is debited in proportion to the
        /// realised pollination rate. At the end of the flowering window the flowering,
        /// ripening and total reproduction investments are scaled by the final
        /// pollination efficiency.
        /// </summary>
        public void pollinationDynamics(input input, parameters parameters, output output, output outputT1)
        {
            //flowering window expressed as a percentage of growth completion (symmetric around floweringTime).
            float startFlowering =  parameters.parReproduction.floweringTime - parameters.parReproduction.floweringDuration / 2F;
            float endFlowering =  parameters.parReproduction.floweringTime + parameters.parReproduction.floweringDuration / 2F;
            //guard against a negative start that could occur for very short flowering durations.
            if (startFlowering < 0) startFlowering = 0;

            //we are inside the flowering window: accumulate potential and actual pollination.
            if (outputT1.growthPercentage >= startFlowering && outputT1.growthPercentage <=endFlowering)
            {
                //Gaussian-like potential pollination centred on floweringTime; 50F sets the peak so that daily increments sum to ≈100%.
                float pollinationToday = 50F *
                    (float)Math.Exp(-1 / parameters.parReproduction.floweringDuration *
                    (float)Math.Pow(10, 4) * (float)Math.Pow((outputT1.growthPercentage /
                    100F - parameters.parReproduction.floweringTime/100F), 2));

                //today's potential pollination increment (finite-difference derivative of the Gaussian).
                float pollinationPotentialRate = pollinationToday-output.reproduction.pollinationPotentialState;

                //precipitation veto: wet days suppress pollen transfer.
                float pollinationEfficiencyPrecipitation = utils.pollinationEfficiencyPrecipitation(input, parameters);
                float weatherPollinationEffect = pollinationEfficiencyPrecipitation;

                //apply the precipitation veto asymmetrically across the rising and falling arms of the Gaussian.
                float pollinationActualRate = 0;
                if (pollinationPotentialRate > 0)
                {
                    //rising limb: actual = potential · efficiency.
                    pollinationActualRate = pollinationPotentialRate * weatherPollinationEffect;
                }
                else
                {
                    //falling limb: already-pollinated flowers are lost at the weather-modulated rate.
                    pollinationActualRate = pollinationPotentialRate + (pollinationPotentialRate * (1 - weatherPollinationEffect));
                }

                //store today's potential pollination state (percentage scale).
                outputT1.reproduction.pollinationPotentialState = pollinationToday;

                //integrate actual pollination state and clip negatives from the falling limb.
                outputT1.reproduction.pollinationActualState = output.reproduction.pollinationActualState + pollinationActualRate;

                if (outputT1.reproduction.pollinationActualState < 0) outputT1.reproduction.pollinationActualState = 0;

                //debit today's share of the flowering investment from the resource budget.
                outputT1.resources.resourceBudget = outputT1.resources.resourceBudget - (outputT1.reproduction.floweringInvestment* Math.Abs(pollinationPotentialRate / 100F));

                if(outputT1.resources.resourceBudget < 0) { outputT1.resources.resourceBudget = 0; }

                //pollination efficiency bookkeeping — separated from pollinationActualState so that weather losses are tracked explicitly.
                if (pollinationPotentialRate > 0)
                {
                    outputT1.reproduction.pollinationEfficiency = output.reproduction.pollinationEfficiency + pollinationActualRate;
                }
                else
                {
                    outputT1.reproduction.pollinationEfficiency = output.reproduction.pollinationEfficiency +
                        Math.Abs(pollinationPotentialRate * weatherPollinationEffect);
                }
            }
            else if (outputT1.growthPercentage >= endFlowering)
            {

                //end of flowering: scale all investments by the realised pollination efficiency.
                if(!outputT1.isFloweringCompleted && outputT1.phenoCode == 3)
                {
                    outputT1.reproduction.floweringInvestment *= output.reproduction.pollinationEfficiency/100;
                    outputT1.reproduction.ripeningInvestment *= output.reproduction.pollinationEfficiency/100;
                    outputT1.reproduction.reproductionInvestment *= output.reproduction.pollinationEfficiency / 100;
                    outputT1.isFloweringCompleted = true;
                }
            }
            else
            {
                //before flowering: zero-initialise the pollination trackers for the current year.
                outputT1.reproduction.pollinationActualState = 0;
                outputT1.reproduction.pollinationPotentialState = 0;
                outputT1.reproduction.pollinationEfficiency = 0;
            }
        }

        /// <summary>
        /// Simulates seed ripening during the greendown phase (phenoCode == 4).
        /// Potential ripening follows a logistic function of greendown completion
        /// (eq. SE1.27) and is downregulated by water stress. Resources saved by
        /// incomplete ripening under drought are returned to the resource budget,
        /// implementing the environmental veto of Bogdziewicz et al. (2018).
        /// During dormancy (phenoCode == 2) ripened seeds are reset to zero.
        /// </summary>
        public void ripeningDynamics(input input, parameters parameters, output output, output outputT1)
        {

            //ripening is active only during the greendown phenophase (phenoCode 4).
            if (outputT1.phenoCode == 4)
            {
                //today's cumulative potential ripening fraction (sigmoidal in greendownPercentage).
                float ripeningToday = utils.ripeningDynamicsFunction(input, outputT1, parameters);

                //today's potential ripening increment (finite difference of the sigmoid).
                float ripeningPotRateIncrease = (ripeningToday - output.reproduction.ripeningPotentialIncrease);
                outputT1.reproduction.ripeningPotentialIncrease = ripeningToday;

                //actual ripening is reduced by drought stress: rate = potential · waterStressRate.
                float ripeningActRateIncrease = ripeningPotRateIncrease * outputT1.resources.waterStressRate;

                //integrate actual ripening state (seed mass accumulated so far).
                outputT1.reproduction.ripeningActualState = output.reproduction.ripeningActualState +
                    ripeningActRateIncrease * outputT1.reproduction.ripeningInvestment;

                //integrate potential ripening state (counterfactual without water stress).
                outputT1.reproduction.ripeningPotentialState = output.reproduction.ripeningActualState +
                    ripeningPotRateIncrease * outputT1.reproduction.ripeningInvestment;

                //derive today's actual and potential rates from the state integrals.
                float ripeningActualRate = (outputT1.reproduction.ripeningActualState - output.reproduction.ripeningActualState);
                float ripeningPotentialRate = (outputT1.reproduction.ripeningPotentialState - output.reproduction.ripeningActualState);

                //resources that would have been spent on aborted seeds are returned to the budget (Bogdziewicz et al. 2018 veto).
                outputT1.resources.savedResources = (ripeningPotentialRate - ripeningActualRate);


                //budget update: debit actual ripening, credit back the saved resources.
                outputT1.resources.resourceBudget = outputT1.resources.resourceBudget - ripeningActualRate + outputT1.resources.savedResources;


                //safety: never let the budget go below zero.
                if (outputT1.resources.resourceBudget < 0)
                {
                    outputT1.resources.resourceBudget = 0;
                }
            }
            else if (outputT1.declinePercentage > 0)
            {
                //during dormancy (phenoCode 2) any un-dispersed seed carried over is reset to zero for the new cycle.
                if(outputT1.phenoCode==2 && outputT1.reproduction.ripeningActualState>0)
                {
                    outputT1.reproduction.ripeningActualState = 0;
                    output.reproduction.ripeningActualState = 0;
                }
            }
        }
    }
}
