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
        /// <param name="input">Daily meteorological drivers (temperature, precipitation, latitude).</param>
        /// <param name="parameters">Species parameter set (uses <c>parResources</c>, <c>parGrowth</c>, <c>parReproduction</c>).</param>
        /// <param name="output">State at the previous day (read-only).</param>
        /// <param name="outputT1">State at the current day (updated in place).</param>
        public void photosyntheticRate(input input, parameters parameters, output output, output outputT1)
        {

            //normalize the current-day position within the growing season (0..1).
            normalizeGrowingSeason(input, parameters, output, outputT1);

            //compute today's wood + leaf maintenance respiration (Q10 formulation).
            respirationRate(input, parameters, output, outputT1);

            //net resource change starts as the negative of tree respiration; GPP is added below if the tree is in an active phenophase.
            outputT1.resources.resourcesRate = - outputT1.resources.respirationTreeRate;


            //water-stress modifier (rolling-window ET0/P index — see utils.waterStressFunction).
            outputT1.resources.waterStressRate = utils.waterStressFunction(input, outputT1, parameters);
            //accumulate the water-stress deficit across the growing season.
            outputT1.resources.waterStressState += (1 - outputT1.resources.waterStressRate);

            //phenoCode 3..5 = growth, greendown, decline (canopy is active → GPP runs).
            if (outputT1.phenoCode >= 3 && outputT1.phenoCode <= 5)
            {
                //update daily LAI as a function of growth / decline progress.
                LAI(input, output, outputT1, parameters);
                //Beer's law: fraction of incoming PAR intercepted by the canopy; k = 0.5.
                float treeInterceptionFraction = 1 - (float)Math.Exp(-0.5*outputT1.LAI);

                //every growing-season day re-opens the window for the next flowering-investment decision.
                outputT1.isInvestmentDecided = false;

                //refresh astronomical variables (day length, etr, gsr) for the current day.
                input.radData = utils.astronomy(input);

                //heat-stress modifier and cumulative heat-stress state.
                outputT1.resources.heatStressRate = utils.heatStressFunction(input, parameters);
                outputT1.resources.heatStressState = output.resources.heatStressState +
                    (1-outputT1.resources.heatStressRate);

                //cold-stress modifier and cumulative cold-stress state.
                outputT1.resources.coldStressRate = utils.coldStressFunction(input, parameters);
                outputT1.resources.coldStressState = output.resources.coldStressState +
                    (1 - outputT1.resources.coldStressRate);



                //Yan & Hunt (1999) dimensionless thermal response of photosynthesis.
                float temperatureFunction = utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                            parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);
                //cache today's global solar radiation on the output weather sub-record (for logging).
                outputT1.weather.radData.gsr = input.radData.gsr;

                //Liebig-style limiting factor: min of heat, cold and water stress multipliers.
                float photoLimitingFactor = (float)Math.Min(outputT1.resources.heatStressRate, outputT1.resources.coldStressRate);
                photoLimitingFactor = (float)Math.Min(photoLimitingFactor, outputT1.resources.waterStressRate);
                //net resource gain = −respiration + LUE·PAR·fAPAR·fT·fStress  (PAR ≈ 0.5·global radiation).
                outputT1.resources.resourcesRate = outputT1.resources.resourcesRate +
                    parameters.parResources.lightUseEfficiency *
                    outputT1.weather.radData.gsr * 0.5F * treeInterceptionFraction * temperatureFunction * photoLimitingFactor; //g MJ m-2 d-1 (0.5 for PAR)

                //cumulative net resources for the season.
                outputT1.resources.resourcesState = output.resources.resourcesState +  outputT1.resources.resourcesRate;
                //update the running resource budget (carbon pool available to reproduction).
                outputT1.resources.resourceBudget = output.resources.resourceBudget +  outputT1.resources.resourcesRate;


                //prevent the budget from going negative (treated as a hard floor).
                if(outputT1.resources.resourceBudget < 0)
                {
                    outputT1.resources.resourceBudget = 0;
                }



            }
            else
            {
                //dormant / pre-growth branch: only respiration costs affect the budget.
                outputT1.resources.resourceBudget = output.resources.resourceBudget + outputT1.resources.resourcesRate;
                //reset the season-level stress state variables ready for the next growing season.
                outputT1.resources.heatStressRate = 1;
                outputT1.resources.heatStressState = 0;
                outputT1.resources.coldStressRate = 1;
                outputT1.resources.coldStressState = 0;
                outputT1.resources.waterStressRate = 1;
                outputT1.resources.waterStressState = 0;
                outputT1.resources.resourcesState = 0;
                //flowering flag is reset so next season can trigger it again.
                outputT1.isFloweringCompleted = false;

            }
            //minimum budget below which reproduction is not possible (reproductionThreshold·maxBudget).
            float minimumBudget = parameters.parReproduction.reproductionThreshold * outputT1.resources.maximumResourceBudget;
            //dimensionless budget level BL_t ∈ [0,1] used by the reproduction module.
            float budgetLevel = (outputT1.resources.resourceBudget - minimumBudget) /
                (outputT1.resources.maximumResourceBudget - minimumBudget); ;

            //safety clamp so BL_t stays in the unit interval.
            if (budgetLevel >= 1) budgetLevel = 1;
            if (budgetLevel < 0) budgetLevel = 0;
            outputT1.resources.budgetLevel = budgetLevel;

        }

        /// <summary>
        /// Updates the tree's Leaf Area Index (LAI) each day from its maximum
        /// value (<c>LAImax</c> from allometry) and the current phenological
        /// state, using growth percentage during canopy expansion and the
        /// normalised VI trajectory during greendown/decline.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>tree.LAImax</c>).</param>
        /// <param name="output">State at the previous day (read-only).</param>
        /// <param name="outputT1">State at the current day (updated in place).</param>
        /// <param name="parameters">Species parameter set.</param>
        public void LAI(input input, output output, output outputT1, parameters parameters)
        {
            //growth phase: LAI rises linearly with growthPercentage up to LAImax.
            if (outputT1.phenoCode == 3)
            {
                //on the first growth day, latch the VI at budbreak and flag the minimum-LAI event.
                if (!outputT1.isMinimumLAIreached)
                {
                    outputT1.isMinimumLAIreached = true;
                    outputT1.viBudBreak = outputT1.vi;
                }

                //linear build-up of LAI proportional to growth completion.
                outputT1.LAI = input.tree.LAImax * outputT1.growthPercentage / 100;
                //reset maximum-LAI flags so greendown can latch them at peak canopy.
                outputT1.isMaximumLAIreached = false;
                output.isMaximumLAIreached = false;
            }
            //greendown (4) or decline (5): LAI scales with current VI normalised by the VI-at-LAImax anchor.
            else if (outputT1.phenoCode >= 4 && outputT1.phenoCode <= 5)
            {
                //latch the VI anchor at the first day growth completes and the canopy is at full LAI.
                if (outputT1.growthPercentage == 100 && !outputT1.isMaximumLAIreached)
                {
                    outputT1.isMaximumLAIreached = true;
                    outputT1.ndvi_LAImax = outputT1.vi;
                }


                //VI still above budbreak → LAI scales with VI relative to its peak (LAImax anchor).
                if(outputT1.vi >= outputT1.viBudBreak)
                {
                    outputT1.LAI = input.tree.LAImax * (outputT1.vi / outputT1.ndvi_LAImax);
                }
                else
                {
                    //VI dropped below budbreak: fade LAI from yesterday's value using a linear ratio.
                    float ratio = outputT1.vi / outputT1.viBudBreak;
                    outputT1.LAI = output.LAI - output.LAI * (1-ratio);
                }

            }
            else
            {
                //dormancy / pre-growth: no canopy, LAI = 0.
                outputT1.LAI = 0;
            }

        }

        /// <summary>
        /// Computes maintenance respiration of wood and foliage using a Q10
        /// formulation (Lin et al. 2012). Wood respiration scales with stem +
        /// branch biomass; leaf respiration scales with foliage biomass and
        /// the current LAI fraction. Both are normalised by crown projection
        /// area to produce fluxes in g C m-2 d-1.
        /// </summary>
        /// <param name="input">Daily input record (uses <c>tree</c> and daily temperature).</param>
        /// <param name="parameters">Species parameter set (uses <c>parResources</c>).</param>
        /// <param name="output">State at the previous day (read-only).</param>
        /// <param name="output1">State at the current day (updated in place).</param>
        public void respirationRate(input input, parameters parameters, output output, output output1)
        {
            //daily mean temperature used as the Q10 driver.
            float tave = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            //Q10 temperature scalar referenced to 25 °C.
            float temperatureEffect = (float)Math.Pow(parameters.parResources.Q10, (tave  - 25) / 10);

            //wood respiration: relative rate × Q10 × (stem + branch biomass) × 0.5 (C fraction) × 1000 · 0.1 → g m-2 d-1 / CPA.
            output1.resources.respirationWoodRate = parameters.parResources.relativeRespirationWood * temperatureEffect * (input.tree.branchesBiomass + input.tree.stemBiomass) * .5F *
                1000F * 0.1F / input.tree.crownProjectionArea; //to scale to g m-2 d-1;
                                                    //carbon content of wood is 50%

            //leaf respiration: active only when the canopy is on (LAI > 0).
            if (output.LAI > 0)
            {
                //relative rate × Q10 × foliage biomass × (LAI fraction) × 1000 / CPA → g m-2 d-1.
                output1.resources.respirationLeavesRate = parameters.parResources.relativeRespirationLeaves *
                    temperatureEffect * input.tree.foliageBiomass * (output.LAI/input.tree.LAImax) * 1000 /
                    input.tree.crownProjectionArea; //to scale to g m-2 d-1
            }
            else
            {
                //no canopy → no foliar respiration cost.
                output1.resources.respirationLeavesRate = 0;
            }

            //accumulate wood and leaf respiration state variables (cumulative losses).
            output1.resources.respirationWood = output.resources.respirationWood + output1.resources.respirationWoodRate;
            output1.resources.respirationLeaves = output.resources.respirationLeaves + output1.resources.respirationLeavesRate;
            //total tree-level respiration = wood + leaves (rate and running state).
            output1.resources.respirationTreeRate = output1.resources.respirationWoodRate + output1.resources.respirationLeavesRate;
            output1.resources.respirationTree = output.resources.respirationTree + (output1.resources.respirationWoodRate +
                output1.resources.respirationLeavesRate);
        }


        #region private functions
        //seasonal anchor: VI at budbreak used to rescale the growing-season [0,1] progress metric.
        float ndviBudBreak;

        /// <summary>
        /// Rescales the current VI to a dimensionless growing-season progress
        /// value in [0,1]: 0 at budbreak, 1 at peak canopy. Used as a driver
        /// for stress accumulation diagnostics.
        /// </summary>
        private void normalizeGrowingSeason(input input, parameters parameters, output output, output outputT1)
        {

            //outside the growth–greendown–decline window: no meaningful progress, reset both state and flag.
            if (outputT1.phenoCode < 3)
            {
                outputT1.growingSeason = 0;
                outputT1.isGrowingSeasonStarted = false;
            }
            else
            {
                //latch the VI at budbreak on the first growing-season day.
                if(!output.isGrowingSeasonStarted)
                {
                    ndviBudBreak = outputT1.vi;
                    output.isGrowingSeasonStarted = true;
                }

                //the amplitude of the seasonal VI cycle (maxVI − minVI), shifted down by the budbreak VI.
                float realAmplitude = (parameters.parVegetationIndex.maximumVI-parameters.parVegetationIndex.minimumVI) - ndviBudBreak / 100;

                //normalised progress: (VI − VI_budbreak) / amplitude; close to 0 at budbreak, 1 at peak.
                outputT1.growingSeason = (outputT1.vi/100 - ndviBudBreak / 100) /(realAmplitude);

                //propagate the started flag forward.
                outputT1.isGrowingSeasonStarted = output.isGrowingSeasonStarted;


            }

            //guard against small transient negatives right after budbreak when VI dips under the latch.
            if(outputT1.growingSeason<0)
            {
                outputT1.growingSeason = 0;
            }
        }
        #endregion

    }

   

}
