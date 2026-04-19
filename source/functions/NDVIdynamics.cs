using source.data;
using System;

// ---------------------------------------------------------------------------
// MASTHING — vegetation index (NDVI) dynamics.
//
// Implements the remotely-sensed phenology descriptor used by the SWELL model
// (Bajocco et al., 2025) and coupled to the MASTHING canopy LAI scaling
// (Bregaglio et al., 2023). The normalized NDVI is used to derive daily LAI
// and to define the timing of growth, greendown and senescence phases.
// ---------------------------------------------------------------------------

namespace source.functions
{
    /// <summary>
    /// Simulates the annual NDVI trajectory across the phenological phases
    /// (dormancy, growth, greendown, decline) following the SWELL model.
    /// The normalized NDVI is rescaled to produce daily LAI via
    /// <c>LAI = NDVI_norm × LAImax</c>.
    /// </summary>
    public class VIdynamics
    {
        float startDormancy = 0;

        /// <summary>
        /// Updates the normalized NDVI for the current day based on the active
        /// phenological phase (<c>phenoCode</c>). Behaviour per phase:
        ///  - 2 (dormancy): baseline NDVI value,
        ///  - 3 (growth):   NDVI increases linearly with growth completion,
        ///  - 4 (greendown): NDVI held at its seasonal maximum,
        ///  - 5 (senescence): NDVI declines following the decline percentage.
        /// </summary>
        public void ndviNormalized(input input, parameters parameters, output output, output outputT1)
        {
            //carry over the two VI anchors (start-of-growth and end-of-senescence) from the previous day.
            outputT1.viAtGrowth = output.viAtGrowth;
            outputT1.viAtSenescence = output.viAtSenescence;
            //today's change in normalised VI — accumulated by each branch below and integrated at the end.
            float rateNDVInormalized = 0;
            if (outputT1.phenoCode == 2)
            {
                //first day we enter dormancy: freeze the current VI as the seasonal senescence anchor.
                if (startDormancy == 0)
                {
                    startDormancy = 1;
                    outputT1.viAtSenescence = output.vi / 100;
                    output.viAtSenescence = outputT1.viAtSenescence;

                    //avoid a flat minimum: bump the anchor slightly above minVI to keep the rescaling well-defined.
                    if (output.viAtSenescence <= parameters.parVegetationIndex.minimumVI)
                    {
                        outputT1.viAtSenescence = parameters.parVegetationIndex.minimumVI + .01F;
                        output.viAtSenescence = outputT1.viAtSenescence;
                    }
                }

                //temperature offset placeholder (set to 0 for the canopy; used for understorey experiments).
                float tshift = 0;

                //decompose the dormant-season VI change into an endodormancy (cold-driven) and an ecodormancy (warming-driven) contribution.
                float endodormancyContribution = 0;
                float ecodormancyContribution = 0;
                //daily mean air temperature.
                float aveTemp = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
                //normalised temperature ratio relative to the growth minimum temperature.
                float tratio = 0;

                //cold branch: temperatures below Tmin push VI down toward the minimum.
                if (aveTemp < (parameters.parGrowth.minimumTemperature - tshift))
                {
                    //magnitude of the departure below Tmin (always positive).
                    float tbelow0 = Math.Abs((parameters.parGrowth.minimumTemperature - tshift) - aveTemp);
                    //scale by 10 °C and cap at -1 so that the rate cannot exceed the nominal endodormancy coefficient.
                    tratio = -tbelow0 / 10;
                    if (tratio < -1)
                    {
                        tratio = -1;
                    }
                    //raw endodormancy contribution: negative coefficient × temperature ratio.
                    endodormancyContribution = parameters.parVegetationIndex.nVIEndodormancy * tratio;

                    //distance of today's VI to the senescence floor (clipped to [0,1]).
                    float VItomin = (output.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                       (output.viAtSenescence - parameters.parVegetationIndex.minimumVI);
                    //ceiling at 1 otherwise unrealistic vi decreases
                    if (VItomin > 1) { VItomin = 1; }

                    //taper the endodormancy contribution as VI approaches the floor.
                    endodormancyContribution *= VItomin;
                    //safety: keep the contribution non-positive (cold should never raise VI here).
                    if (endodormancyContribution > 0)
                    {
                        endodormancyContribution = 0;
                    }

                    //diagnostic debug breakpoint — intentionally empty, preserved for reviewers.
                    if (endodormancyContribution < -1000)
                    {

                    }
                }
                else
                {
                    //warm branch: ecodormancy contribution, only active once days start lengthening.
                    input yesterday = new input();
                    yesterday.latitude = input.latitude;
                    yesterday.date = input.date.AddDays(-1);
                    //day length on the previous day — used to detect the winter solstice.
                    float dayLengthYesterday = utils.dayLength(yesterday);

                    //before the solstice (days still shortening) ecodormancy is inactive.
                    if (dayLengthYesterday > input.radData.dayLength)
                    {
                        ecodormancyContribution = 0;
                    }
                    else
                    {
                        tratio = 0;
                        //thermal forcing unit (Yan & Hunt 1999) driving early VI recovery.
                        float gddEco = utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature - tshift,
                         parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                        //distance of today's VI to the seasonal maximum (clipped to [0,1]).
                        float VItoMax = (output.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                  (parameters.parVegetationIndex.maximumVI - parameters.parVegetationIndex.minimumVI);
                        if (VItoMax > 1) VItoMax = 1;


                        //ecodormancy contribution scales with thermal forcing and how far VI is from the maximum.
                        ecodormancyContribution = gddEco * parameters.parVegetationIndex.nVIEcodormancy * (1 - VItoMax);
                    }
                }

                //dormant-season VI rate = cold (endodormancy) + warm (ecodormancy) contributions.
                rateNDVInormalized = (ecodormancyContribution + endodormancyContribution);


            }
            //growth (phenoCode 3): VI rises toward the seasonal maximum driven by canopy growth.
            else if (outputT1.phenoCode == 3)
            {
                //reset the dormancy-entry flag so the next dormant season will re-anchor viAtSenescence.
                startDormancy = 0;
                //nominal VI growth coefficient from the parameter set.
                float growthNDVInormalized = parameters.parVegetationIndex.nVIGrowth;
                //first-pass VI rate proportional to the phenology growth rate (overwritten below).
                rateNDVInormalized = growthNDVInormalized * 100 * outputT1.growth.growthRate;

                //on the first growth day, anchor viAtGrowth to the current VI as the lower bound of the rescaling.
                if (outputT1.viAtGrowth == 0)
                {
                    outputT1.viAtGrowth = output.vi / 100;
                    output.viAtGrowth = outputT1.viAtGrowth;
                }

                //cap the anchor just below maxVI to keep the rescaling well-defined.
                if (outputT1.viAtGrowth >= parameters.parVegetationIndex.maximumVI)
                {
                    outputT1.viAtGrowth = parameters.parVegetationIndex.maximumVI - 0.01F;
                }
                //distance from the current VI to the seasonal maximum (clipped to [0,1]).
                float VItoMax = (output.vi / 100 - outputT1.viAtGrowth) /
                    (parameters.parVegetationIndex.maximumVI - outputT1.viAtGrowth);
                if (VItoMax > 1) VItoMax = 1;
                //final VI rate: slows down as greendown starts (1 − greendown%) and as VI approaches its ceiling.
                rateNDVInormalized = growthNDVInormalized * (1 - outputT1.greenDownPercentage / 100) * (1 - VItoMax);

            }
            //greendown (phenoCode 4): VI starts to decline from its plateau.
            else if (outputT1.phenoCode == 4)
            {
                //reset the growth anchor at the start of greendown.
                outputT1.viAtGrowth = 0;
                //nominal VI greendown coefficient from the parameter set.
                float greenDownNDVInormalized = parameters.parVegetationIndex.nVIGreendown;

                //EVI dynamics follow an exponential weighting scheme.
                if (input.vegetationIndex == "EVI")
                {
                    //exponential weight growing with greendown percentage (0 → 1).
                    float weight = 1 - (float)Math.Exp(-.25 * outputT1.greenDownPercentage);
                    //VI rate is negative (declining) and proportional to the canopy greendown rate.
                    rateNDVInormalized = -greenDownNDVInormalized *
                        (weight * outputT1.greenDown.greenDownRate);
                }
                else if (input.vegetationIndex == "NDVI")
                {
                    //NDVI uses a linear weight in greendownPercentage/100.
                    rateNDVInormalized = -greenDownNDVInormalized *
                       (outputT1.greenDownPercentage) / 100 *
                       outputT1.greenDown.greenDownRate;
                }
            }
            //decline (phenoCode 5) or late dormancy induction (phenoCode 1): VI falls toward its seasonal minimum.
            else if (outputT1.phenoCode == 5 || outputT1.phenoCode == 1)
            {
                //symmetric bell centred at 50% decline — accelerates the drop in mid-decline and tails off at the edges.
                float weight = SymmetricBellFunction(outputT1.declinePercentage);
                //combined coefficient: greendown baseline + bell-weighted senescence contribution.
                float declineNDVInormalized = -parameters.parVegetationIndex.nVIGreendown -
                    parameters.parVegetationIndex.nVISenescence * weight;


                //VI decline rate (negative).
                rateNDVInormalized = declineNDVInormalized;
            }

            //cache today's VI rate on the previous-day object — it drives the state update on the next line.
            output.viRate = rateNDVInormalized;

            //integrate VI: today's VI = yesterday's VI + today's rate.
            outputT1.vi = output.vi + output.viRate;


            //clip below the parameter minimum to avoid unphysical negative VI.
            if (outputT1.vi / 100 < parameters.parVegetationIndex.minimumVI)
            {
                outputT1.vi = parameters.parVegetationIndex.minimumVI * 100;
            }
            //clip above 1 on the /100 scale — VI never exceeds its theoretical maximum.
            if (outputT1.vi / 100 > 1)
            {
                outputT1.vi = 1;
            }



        }

        /// <summary>
        /// Symmetric Gaussian bell centred at x = 50 with variance 10³/2.
        /// Used to weight the senescence contribution to VI, giving maximum
        /// weight around the mid-point of the decline phenophase.
        /// </summary>
        /// <param name="x">Decline percentage (0..100).</param>
        /// <returns>Bell-shaped weight in (0, 1].</returns>
        static float SymmetricBellFunction(float x)
        {
            //Gaussian kernel: f(x) = exp(-(x - 50)² / 1000) — peaks at x = 50.
            float scaledX = (float)Math.Exp(-Math.Pow((x - 50), 2) / Math.Pow(10, 3));

            return scaledX;
        }
    }
}