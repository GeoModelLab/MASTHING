using source.functions;
using System;

/// <summary>
/// Data namespace. Contains every container class that carries state and
/// forcings through the SWELL phenology and MASTHING masting models:
/// daily weather inputs, tree and radiation data, parameter records and the
/// aggregated per-day <c>output</c> object used both for intermediate state
/// and for simulation results.
/// </summary>
namespace source.data
{
    /// <summary>
    /// Aggregated per-day model state and diagnostic container. An
    /// <c>output</c> instance is passed (both as "yesterday" and "today") to
    /// every computing method of the SWELL phenology and MASTHING masting
    /// modules so that each routine can read the previous state and update
    /// the current state in place. Each phenophase is represented by a
    /// dedicated sub-class holding its own rate and state variables.
    /// </summary>
    public class output
    {
        /// <summary>Snapshot of the daily weather input (temperature, precipitation, radiation) used to build the output record.</summary>
        public input weather = new input();

        #region phenophase classes
        /// <summary>Sub-container for the dormancy-induction phenophase (photoperiod- and temperature-driven rate and state variables).</summary>
        public dormancyInduction dormancyInduction = new dormancyInduction();

        /// <summary>Sub-container for the endodormancy phenophase (hourly chilling-unit accumulation).</summary>
        public endodormancy endodormancy = new endodormancy();

        /// <summary>Sub-container for the ecodormancy phenophase (photothermal forcing toward bud break).</summary>
        public ecodormancy ecodormancy = new ecodormancy();

        /// <summary>Sub-container for the greendown phenophase (post-bud-break leaf expansion).</summary>
        public greenDown greenDown = new greenDown();

        /// <summary>Sub-container for the growth phenophase (thermal-forcing accumulation from bud break).</summary>
        public growth growth = new growth();

        /// <summary>Sub-container for the decline / senescence phenophase (photothermal-driven leaf senescence).</summary>
        public decline decline = new decline();

        #endregion

        #region masting
        /// <summary>Sub-container for the MASTHING carbon-balance / resource-budget module (GPP, respiration, abiotic stresses, resource budget).</summary>
        public resources resources = new resources();

        /// <summary>Sub-container for the MASTHING reproductive module (flowering investment, pollination, ripening).</summary>
        public reproduction reproduction = new reproduction();
        #endregion

        #region common variables

        #region boolean variables referring to the phenophase state        
        // Get or set whether dormancy is induced.
        public bool isDormancyInduced { get; set; }
        // Get or set whether ecodormancy is completed.
        public bool isEcodormancyCompleted { get; set; }
        // Get or set whether decline is completed.
        public bool isDeclineCompleted { get; set; }
        // Get or set whether growth is completed.
        public bool isGrowthCompleted { get; set; }
        // Get or set whether greendown is completed.        
        public bool isGreendownCompleted { get; set; }
        #endregion

        #region variables storing the percentage of the phenophase completion

        // Get or set the percentage of the dormancy induction completion.
        public float dormancyInductionPercentage { get; set; }
        // Get or set the percentage of the endodormancy completion.
        public float endodormancyPercentage { get; set; }
        // Get or set the percentage of the ecodormancy completion.
        public float ecodormancyPercentage { get; set; }
        // Get or set the percentage of the growth completion.
        public float growthPercentage { get; set; }
        // Get or set the percentage of the greendown completion.
        public float greenDownPercentage { get; set; }
        // Get or set the percentage of the decline completion.
        public float declinePercentage { get; set; }

        #endregion

        #region cycle completion variables
        public float dormancyCompletion { get; set; }
        public float growingSeasonCompletion { get; set; }
        public float cycleCompletion { get; set; }
        #endregion

        #region variables related to NDVI dynamics        
        // Get or set the simulated NDVI.
        public float vi { get; set; }

        // Get or set the simulated daily rate of change of the NDVI.
        public float viRate { get; set; }

        // Get or set the reference NDVI used for model calibration/evaluation.
        public float viReference { get; set; }

        public float viAtGrowth { get; set; }
        public float viAtSenescence { get; set; }

        #endregion

        public bool isGrowingSeasonStarted { get; set; }
        public bool isInvestmentDecided { get; set; }
        public bool isFloweringCompleted { get; set; }
        public bool isMaximumLAIreached { get; set; }
        public bool isMinimumLAIreached { get; set; }
        public float ndvi_LAImax { get; set; }
        public float viBudBreak { get; set; }
        public float growingSeason { get; set; }
        public float growingSeasonDiseased { get; set; }
        public float dormancySeason { get; set; }
        public float phenoCode { get; set; }
        public string phenoString { get; set; }
        public float ndviReference { get; set; }
        public float seedReference { get; set; }
        public float seedReferenceSD { get; set; }
        public float LAI { get; set; }
        #endregion
    }

    /// <summary>
    /// Lightweight, serialisation-friendly snapshot of the daily meteorological
    /// drivers used to reconstruct or export the forcings associated with a
    /// given <see cref="output"/> record.
    /// </summary>
    public class weather
    {
        /// <summary>Daily maximum air temperature (°C).</summary>
        public float airTemperatureMaximum { get; set; }
        /// <summary>Daily minimum air temperature (°C).</summary>
        public float airTemperatureMinimum { get; set; }
        /// <summary>Daily precipitation (mm day-1).</summary>
        public float precipitation { get; set; }
        /// <summary>Astronomical day length (hours).</summary>
        public float dayLength { get; set; }

        /// <summary>Daily global solar radiation at ground level (MJ m-2 day-1).</summary>
        public float solarRadiation { get; set; }

        /// <summary>Daily extraterrestrial (top-of-atmosphere) solar radiation (MJ m-2 day-1).</summary>
        public float etr { get; set; }

        /// <summary>Returns a deep copy of this weather snapshot.</summary>
        public weather Clone()
        {
            weather clonedObject = new weather
            {
                // Copy other value types and reference types
                airTemperatureMaximum = this.airTemperatureMaximum,
                airTemperatureMinimum = this.airTemperatureMinimum,
                precipitation = this.precipitation,
                dayLength = this.dayLength,
                solarRadiation = this.solarRadiation,
                etr = this.etr
            };
            return clonedObject;
        }
    }

    #region SWELL outputs
    /// <summary>
    /// State and rate variables of the dormancy-induction phenophase. The
    /// induction rate is the multiplicative combination of a photoperiod
    /// limitation function and a temperature limitation function (both [0,1]);
    /// the photothermal state is its time integral and is compared against
    /// <c>parDormancyInduction.photoThermalThreshold</c> to trigger dormancy.
    /// </summary>
    public class dormancyInduction
    {
        /// <summary>Daily photoperiod contribution to dormancy induction ([0,1]).</summary>
        public float photoperiodDormancyInductionRate { get; set; }
        /// <summary>Daily temperature contribution to dormancy induction ([0,1]).</summary>
        public float temperatureDormancyInductionRate { get; set; }
        /// <summary>Daily combined photothermal rate (product of the two factors above).</summary>
        public float photoThermalDormancyInductionRate { get; set; }
        /// <summary>Accumulated photothermal induction units since the start of the induction phase.</summary>
        public float photoThermalDormancyInductionState { get; set; }
    }

    /// <summary>
    /// State and rate variables of the endodormancy phenophase. Chilling
    /// units are accumulated from hourly temperature using a four-knot
    /// response function and the daily rate is the mean of the 24 hourly
    /// contributions.
    /// </summary>
    public class endodormancy
    {
        /// <summary>Daily mean chilling rate (chilling units day-1).</summary>
        public float endodormancyRate { get; set; }
        /// <summary>Cumulative chilling units since the beginning of the dormant season.</summary>
        public float endodormancyState { get; set; }
    }

    /// <summary>
    /// State and rate variables of the ecodormancy phenophase. The daily
    /// rate is driven by a photoperiod-modulated sigmoidal response to mean
    /// air temperature and the asymptote depends on the completion fraction
    /// of endodormancy; the state accumulates these rates until the
    /// photothermal threshold is reached.
    /// </summary>
    public class ecodormancy
    {
        /// <summary>Daily ecodormancy (forcing) rate.</summary>
        public float ecodormancyRate { get; set; }
        /// <summary>Accumulated ecodormancy forcing units.</summary>
        public float ecodormancyState { get; set; }
    }

    /// <summary>
    /// State and rate variables of the growth (post-bud-break) phenophase.
    /// Both variables are expressed in forcing (thermal) units.
    /// </summary>
    public class growth
    {
        /// <summary>Daily forcing rate (Yan &amp; Hunt, 1999).</summary>
        public float growthRate { get; set; }
        /// <summary>Cumulative forcing units since bud break.</summary>
        public float growthState { get; set; }
    }

    /// <summary>
    /// State and rate variables of the decline / senescence phenophase. The
    /// daily rate is a weighted blend of forcing and photothermal induction
    /// driven by the decline-completion fraction.
    /// </summary>
    public class decline
    {
        /// <summary>Daily decline rate.</summary>
        public float declineRate { get; set; }
        /// <summary>Accumulated decline photothermal units.</summary>
        public float declineState { get; set; }
    }

    /// <summary>
    /// State and rate variables of the greendown phenophase (between growth
    /// completion and the onset of senescence).
    /// </summary>
    public class greenDown
    {
        /// <summary>Daily greendown rate (forcing units day-1).</summary>
        public float greenDownRate { get; set; }
        /// <summary>Cumulative greendown forcing units.</summary>
        public float greenDownState { get; set; }
    }
    #endregion

    #region MASTHING model outputs
    /// <summary>
    /// Carbon-balance / resource-budget state of the MASTHING module.
    /// Holds daily and cumulative values of photosynthesis, maintenance
    /// respiration, abiotic-stress limitations (heat, cold, water) and the
    /// non-structural carbon reservoir that fuels reproduction.
    /// </summary>
    public class resources
    {
        #region abiotic stresses
        /// <summary>Daily heat-stress multiplier (unitless, [0,1]; 1 = no stress).</summary>
        public float heatStressRate { get; set; }
        /// <summary>Cumulative heat-stress index (unitless, [0,1]).</summary>
        public float heatStressState { get; set; }
        /// <summary>Daily cold-stress multiplier (unitless, [0,1]; 1 = no stress).</summary>
        public float coldStressRate { get; set; }
        /// <summary>Cumulative cold-stress index (unitless, [0,1]).</summary>
        public float coldStressState { get; set; }
        /// <summary>Daily water-stress multiplier (unitless, [0,1]; 1 = no stress).</summary>
        public float waterStressRate { get; set; }
        /// <summary>Cumulative water-stress index (unitless, [0,1]).</summary>
        public float waterStressState { get; set; }

        /// <summary>Rolling window of daily reference evapotranspiration (mm day-1) used to compute the water-stress index.</summary>
        public List<float> ET0memory = new List<float>();
        /// <summary>Rolling window of daily precipitation (mm day-1) used to compute the water-stress index.</summary>
        public List<float> PrecipitationMemory = new List<float>();
        #endregion

        #region resource variables
        /// <summary>Daily net assimilation rate contributing to the non-structural resource pool (g C m-2 day-1).</summary>
        public float resourcesRate { get; set; }
        /// <summary>Cumulative seasonal resource acquisition (g C m-2).</summary>
        public float resourcesState { get; set; }
        /// <summary>Current non-structural resource budget available for allocation (g C m-2).</summary>
        public float resourceBudget { get; set; }
        /// <summary>Maximum storable resource budget, derived from total tree biomass and <c>resourceBudgetFraction</c>.</summary>
        public float maximumResourceBudget { get; set; }
        /// <summary>Resource-budget threshold below which reproductive investment is vetoed.</summary>
        public float minimumResourceBudgetReproduction { get; set; }
        /// <summary>Relative budget level (resourceBudget / maximumResourceBudget), used by the weather-cue response in the RB+WC and RBxWC model versions.</summary>
        public float budgetLevel { get; set; }

        /// <summary>Resources conserved when a weather-driven veto suppresses flowering (g C m-2).</summary>
        public float savedResources { get; set; }
        #endregion

        #region maintenance respiration
        /// <summary>Daily wood maintenance respiration (g C m-2 day-1).</summary>
        public float respirationWoodRate { get; set; }
        /// <summary>Daily leaf maintenance respiration (g C m-2 day-1).</summary>
        public float respirationLeavesRate { get; set; }
        /// <summary>Cumulative wood maintenance respiration (g C m-2).</summary>
        public float respirationWood { get; set; }
        /// <summary>Cumulative leaf maintenance respiration (g C m-2).</summary>
        public float respirationLeaves { get; set; }
        /// <summary>Daily total tree maintenance respiration (g C m-2 day-1).</summary>
        public float respirationTreeRate { get; set; }
        /// <summary>Cumulative total tree maintenance respiration (g C m-2).</summary>
        public float respirationTree { get; set; }
        #endregion
    }

    /// <summary>
    /// Reproductive state of the MASTHING module. Tracks flowering investment,
    /// pollination dynamics, ripening progression and the solstice-centred
    /// temperature cue used by the RB+WC and RBxWC model versions.
    /// </summary>
    public class reproduction
    {
        /// <summary>Daily weather-cue multiplier for flowering ([0,1]).</summary>
        public float floweringWeatherCues { get; set; }
        /// <summary>Budget actually invested in flowering at a given day (g C).</summary>
        public float floweringInvestment { get; set; }
        /// <summary>Total reproductive budget allocated to the current season (g C).</summary>
        public float reproductionInvestment { get; set; }
        /// <summary>Budget allocated to fruit ripening (g C).</summary>
        public float ripeningInvestment { get; set; }
        /// <summary>Potential pollination state before precipitation-driven efficiency loss.</summary>
        public float pollinationPotentialState { get; set; }
        /// <summary>Actual pollination state after precipitation-driven efficiency loss.</summary>
        public float pollinationActualState { get; set; }
        /// <summary>Day-of-flowering pollination efficiency ([0,1]), driven by precipitation.</summary>
        public float pollinationEfficiency { get; set; }

        #region weather cues
        /// <summary>Archive of solstice-centred mean temperatures keyed by year, used to drive the interannual temperature cue for flowering.</summary>
        public Dictionary<DateTime, float> solsticeTemperatureCue = new Dictionary<DateTime, float>();
        #endregion

        /// <summary>Potential ripening state (g C), before senescence-driven veto.</summary>
        public float ripeningPotentialState { get; set; }
        /// <summary>Daily increment of the potential ripening state (g C day-1).</summary>
        public float ripeningPotentialIncrease { get; set; }
        /// <summary>Actual ripening state (g C), after veto by phenology-driven ripening dynamics.</summary>
        public float ripeningActualState { get; set; }
    }

    #endregion
}
