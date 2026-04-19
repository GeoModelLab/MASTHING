using source.data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ---------------------------------------------------------------------------
// MASTHING — carbon balance module (resources).
//
// Implements the daily carbon dynamics described in Section 2.1 and
// Supplementary Section S1.2 of Bregaglio et al. (2026):
//   * Gross Primary Production (GPP) via light-use efficiency
//     (Monteith, 1972; Medlyn, 1998)
//   * Temperature response of photosynthesis (Yan & Hunt, 1999)
//   * Heat / cold / water stress multipliers
//   * Maintenance respiration of wood and foliage (Q10 formulation;
//     Lin et al., 2012)
//   * Resource budget update and normalization to the dimensionless
//     budget level (BL_t) used by the reproduction module.
// ---------------------------------------------------------------------------

namespace source.functions
{
    /// <summary>
    /// Carbon balance component of MASTHING. Computes daily GPP, autotrophic
    /// respiration and the net change in the internal resource budget (B_t),
    /// which is the central state variable governing reproduction.
    /// </summary>
    public class resources
    {
        /// <summary>
        /// Entry point called once per day. Orchestrates:
        ///  (1) normalization of the growing-season fraction,
        ///  (2) computation of maintenance respiration,
        ///  (3) GPP estimation with LUE and stress downregulation,
        ///  (4) update of the resource budget.
        /// </summary>
        public void photosyntheticRate(input input, parameters parameters, output output, output outputT1)
        {

            //normalize growing season
            normalizeGrowingSeason(input, parameters, output, outputT1);

            //calculate respiration rate
            respirationRate(input, parameters, output, outputT1);

            //consider the effect of the tree respiration
            outputT1.resources.resourcesRate = - outputT1.resources.respirationTreeRate;


            //compute water stress
            outputT1.resources.waterStressRate = utils.waterStressFunction(input, outputT1, parameters);
            outputT1.resources.waterStressState += (1 - outputT1.resources.waterStressRate);

            if (outputT1.phenoCode >= 3 && outputT1.phenoCode <= 5)
            {
                //compute LAI
                LAI(input, output, outputT1, parameters);
                float treeInterceptionFraction = 1 - (float)Math.Exp(-0.5*outputT1.LAI);
               
                //reset investment decision
                outputT1.isInvestmentDecided = false;
                
                input.radData = utils.astronomy(input);

                //compute heat stress
                outputT1.resources.heatStressRate = utils.heatStressFunction(input, parameters);
                outputT1.resources.heatStressState = output.resources.heatStressState + 
                    (1-outputT1.resources.heatStressRate);

                //compute cold stress
                outputT1.resources.coldStressRate = utils.coldStressFunction(input, parameters);
                outputT1.resources.coldStressState = output.resources.coldStressState + 
                    (1 - outputT1.resources.coldStressRate);

               

                //unitless
                float temperatureFunction = utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                            parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);
                outputT1.weather.radData.gsr = input.radData.gsr;

                float photoLimitingFactor = (float)Math.Min(outputT1.resources.heatStressRate, outputT1.resources.coldStressRate);
                photoLimitingFactor = (float)Math.Min(photoLimitingFactor, outputT1.resources.waterStressRate);
                //photosynthetic rate (after respiration)
                outputT1.resources.resourcesRate = outputT1.resources.resourcesRate +
                    parameters.parResources.lightUseEfficiency *
                    outputT1.weather.radData.gsr * 0.5F * treeInterceptionFraction * temperatureFunction * photoLimitingFactor; //g MJ m-2 d-1 (0.5 for PAR)
                
                //update state variable
                outputT1.resources.resourcesState = output.resources.resourcesState +  outputT1.resources.resourcesRate;
                //update resource budget
                outputT1.resources.resourceBudget = output.resources.resourceBudget +  outputT1.resources.resourcesRate;

              
                if(outputT1.resources.resourceBudget < 0)
                {
                    outputT1.resources.resourceBudget = 0;
                }

               

            }
            else
            {
                //reinitialize variables
                outputT1.resources.resourceBudget = output.resources.resourceBudget + outputT1.resources.resourcesRate;
                outputT1.resources.heatStressRate = 1;
                outputT1.resources.heatStressState = 0;
                outputT1.resources.coldStressRate = 1;
                outputT1.resources.coldStressState = 0;
                outputT1.resources.waterStressRate = 1;
                outputT1.resources.waterStressState = 0;
                outputT1.resources.resourcesState = 0;
                //flowering restarted
                outputT1.isFloweringCompleted = false;
                
            }
            float minimumBudget = parameters.parReproduction.reproductionThreshold * outputT1.resources.maximumResourceBudget;
            float budgetLevel = (outputT1.resources.resourceBudget - minimumBudget) /
                (outputT1.resources.maximumResourceBudget - minimumBudget); ;

            //limit the budget level to 1
            if (budgetLevel >= 1) budgetLevel = 1;
            if (budgetLevel < 0) budgetLevel = 0;
            outputT1.resources.budgetLevel = budgetLevel;

        }

        //compute LAI
        public void LAI(input input, output output, output outputT1, parameters parameters)
        {
            if (outputT1.phenoCode == 3)
            {
                if (!outputT1.isMinimumLAIreached)
                {
                    outputT1.isMinimumLAIreached = true;
                    outputT1.viBudBreak = outputT1.vi;
                }

                outputT1.LAI = input.tree.LAImax * outputT1.growthPercentage / 100;
                outputT1.isMaximumLAIreached = false;
                output.isMaximumLAIreached = false;
            }
            else if (outputT1.phenoCode >= 4 && outputT1.phenoCode <= 5)
            {
                if (outputT1.growthPercentage == 100 && !outputT1.isMaximumLAIreached)
                {
                    outputT1.isMaximumLAIreached = true;
                    outputT1.ndvi_LAImax = outputT1.vi;
                }

               
                if(outputT1.vi >= outputT1.viBudBreak)
                {
                    outputT1.LAI = input.tree.LAImax * (outputT1.vi / outputT1.ndvi_LAImax);
                }
                else
                {
                    float ratio = outputT1.vi / outputT1.viBudBreak;
                    outputT1.LAI = output.LAI - output.LAI * (1-ratio);
                }
              
            }
            else
            {
                outputT1.LAI = 0;
            }
           
        }
       
        //compute respiration rate
        public void respirationRate(input input, parameters parameters, output output, output output1)
        {
            //Temperature effect on respiration
            float tave = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            float temperatureEffect = (float)Math.Pow(parameters.parResources.Q10, (tave  - 25) / 10);
            
            //compute rate of respiration for wood
            output1.resources.respirationWoodRate = parameters.parResources.relativeRespirationWood * temperatureEffect * (input.tree.branchesBiomass + input.tree.stemBiomass) * .5F * 
                1000F * 0.1F / input.tree.crownProjectionArea; //to scale to g m-2 d-1; 
                                                    //carbon content of wood is 50%

            //compute rate of respiration for leaves
            if (output.LAI > 0)
            { 
                output1.resources.respirationLeavesRate = parameters.parResources.relativeRespirationLeaves * 
                    temperatureEffect * input.tree.foliageBiomass * (output.LAI/input.tree.LAImax) * 1000 / 
                    input.tree.crownProjectionArea; //to scale to g m-2 d-1
            }
            else
            {
                output1.resources.respirationLeavesRate = 0;
            }
           
            //update state variable
            output1.resources.respirationWood = output.resources.respirationWood + output1.resources.respirationWoodRate;
            output1.resources.respirationLeaves = output.resources.respirationLeaves + output1.resources.respirationLeavesRate;
            output1.resources.respirationTreeRate = output1.resources.respirationWoodRate + output1.resources.respirationLeavesRate;
            output1.resources.respirationTree = output.resources.respirationTree + (output1.resources.respirationWoodRate + 
                output1.resources.respirationLeavesRate);
        }


        #region private functions
        float ndviBudBreak;

        //normalize growing season from growth to decline
        private void normalizeGrowingSeason(input input, parameters parameters, output output, output outputT1)
        {

            if (outputT1.phenoCode < 3)
            {
                outputT1.growingSeason = 0;
                outputT1.isGrowingSeasonStarted = false;
            }
            else
            {
                if(!output.isGrowingSeasonStarted)
                {
                    ndviBudBreak = outputT1.vi;
                    output.isGrowingSeasonStarted = true;
                }

                float realAmplitude = (parameters.parVegetationIndex.maximumVI-parameters.parVegetationIndex.minimumVI) - ndviBudBreak / 100;
                               
                outputT1.growingSeason = (outputT1.vi/100 - ndviBudBreak / 100) /(realAmplitude);
                
                outputT1.isGrowingSeasonStarted = output.isGrowingSeasonStarted;
                
              
            }

            //truncate unfeasible results (when NDVI is below budbreak)
            if(outputT1.growingSeason<0)
            {
                outputT1.growingSeason = 0;
            }
        }
        #endregion

    }

   

}
