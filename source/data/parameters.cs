using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace source.data
{
    /// <summary>
    /// Top-level parameter container for a species/site combination. Bundles
    /// the parameter sub-classes of each SWELL phenophase (induction,
    /// endodormancy, ecodormancy, growth, greendown, senescence, vegetation
    /// index) and of the MASTHING carbon-balance and reproduction modules,
    /// together with the model-version selector that switches between the
    /// RB, RB+WC and RBxWC formulations compared in Bregaglio et al. (2026).
    /// </summary>
    public class parameters
    {
        #region swell
        /// <summary>Parameters controlling the dormancy-induction photothermal response.</summary>
        public parDormancyInduction parDormancyInduction = new parDormancyInduction();
        /// <summary>Parameters controlling the hourly chilling-unit accumulation during endodormancy.</summary>
        public parEndodormancy parEndodormancy = new parEndodormancy();
        /// <summary>Parameters controlling the photothermal forcing accumulation during ecodormancy.</summary>
        public parEcodormancy parEcodormancy = new parEcodormancy();
        /// <summary>Parameters controlling the post-bud-break growth phenophase.</summary>
        public parGrowth parGrowth = new parGrowth();
        /// <summary>Parameters controlling the greendown phenophase.</summary>
        public parGreendown parGreendown = new parGreendown();
        /// <summary>Parameters controlling the decline / senescence phenophase.</summary>
        public parSenescence parSenescence = new parSenescence();
        /// <summary>Parameters controlling the NDVI / vegetation-index dynamics used to benchmark phenology against MODIS.</summary>
        public parVegetationIndex parVegetationIndex = new parVegetationIndex();
        #endregion

        #region masting
        /// <summary>Parameters of the MASTHING carbon-balance / resource-budget module (LUE, Q10, stress thresholds).</summary>
        public parResources parResources = new parResources();
        /// <summary>Parameters of the MASTHING reproductive module (weather cues, flowering, pollination, ripening).</summary>
        public parReproduction parReproduction = new parReproduction();

        /// <summary>
        /// Model-version flag that selects the reproductive-allocation scheme:
        /// "RB" (resource-budget only), "RB+WC" (additive weather cue) or
        /// "RBxWC" (interactive weather cue), as compared in
        /// Bregaglio et al. (2026).
        /// </summary>
        public string modelVersion { get; set; }
        #endregion
    }

    #region swell
    /// <summary>Parameters of the photothermal dormancy-induction response function.</summary>
    public class parDormancyInduction
    {
        /// <summary>Day length below which the photoperiod function is fully permissive for dormancy induction (hours).</summary>
        public float limitingPhotoperiod { get; set; }

        /// <summary>Day length above which the photoperiod function fully inhibits dormancy induction (hours).</summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>Threshold of accumulated photothermal units that triggers completion of dormancy induction.</summary>
        public float photoThermalThreshold { get; set; }

        /// <summary>Temperature below which the temperature function is fully permissive for dormancy induction (°C).</summary>
        public float limitingTemperature { get; set; }

        /// <summary>Temperature above which the temperature function fully inhibits dormancy induction (°C).</summary>
        public float notLimitingTemperature { get; set; }
    }

    /// <summary>Parameters of the four-knot hourly chilling-unit response during endodormancy.</summary>
    public class parEndodormancy
    {
        /// <summary>Lower limiting temperature: below this no chilling is accumulated (°C).</summary>
        public float limitingLowerTemperature { get; set; }

        /// <summary>Lower non-limiting temperature: optimum chilling starts (°C).</summary>
        public float notLimitingLowerTemperature { get; set; }

        /// <summary>Upper non-limiting temperature: optimum chilling ends (°C).</summary>
        public float notLimitingUpperTemperature { get; set; }

        /// <summary>Upper limiting temperature: above this no chilling is accumulated (°C).</summary>
        public float limitingUpperTemperature { get; set; }

        /// <summary>Critical chilling accumulation that marks endodormancy completion.</summary>
        public float chillingThreshold { get; set; }
    }

    /// <summary>Parameters of the ecodormancy photothermal forcing response.</summary>
    public class parEcodormancy
    {
        /// <summary>Day length above which photoperiod is fully permissive for ecodormancy release (hours).</summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>Temperature level controlling the midpoint of the sigmoidal forcing response (°C).</summary>
        public float notLimitingTemperature { get; set; }

        /// <summary>Critical photothermal accumulation that marks ecodormancy completion.</summary>
        public float photoThermalThreshold { get; set; }
    }

    /// <summary>Parameters of the growth phenophase (Yan &amp; Hunt 1999 forcing response).</summary>
    public class parGrowth
    {
        /// <summary>Minimum temperature below which forcing is zero (°C).</summary>
        public float minimumTemperature { get; set; }

        /// <summary>Optimum temperature at which forcing peaks (°C).</summary>
        public float optimumTemperature { get; set; }

        /// <summary>Maximum temperature above which forcing is zero (°C).</summary>
        public float maximumTemperature { get; set; }

        /// <summary>Critical accumulation of forcing units that completes the growth phenophase.</summary>
        public float thermalThreshold { get; set; }
    }

    /// <summary>Parameters of the decline / senescence phenophase.</summary>
    public class parSenescence
    {
        /// <summary>Day length below which the photoperiod function inhibits senescence (hours).</summary>
        public float limitingPhotoperiod { get; set; }

        /// <summary>Day length above which the photoperiod function is fully permissive for senescence (hours).</summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>Critical accumulation of photothermal units that ends the decline phase.</summary>
        public float photoThermalThreshold { get; set; }

        /// <summary>Temperature below which senescence is fully permissive (°C).</summary>
        public float limitingTemperature { get; set; }

        /// <summary>Temperature above which senescence is fully inhibited (°C).</summary>
        public float notLimitingTemperature { get; set; }
    }

    /// <summary>Parameters of the greendown phenophase.</summary>
    public class parGreendown
    {
        /// <summary>Critical accumulation of thermal units during greendown.</summary>
        public float thermalThreshold { get; set; }
    }

    /// <summary>Per-phenophase NDVI rates and limits used by the vegetation-index model.</summary>
    public class parVegetationIndex
    {
        /// <summary>Maximum NDVI rate during growth (unitless).</summary>
        public float nVIGrowth { get; set; }
        /// <summary>Maximum NDVI rate during endodormancy (unitless).</summary>
        public float nVIEndodormancy { get; set; }
        /// <summary>Maximum NDVI rate during senescence (unitless).</summary>
        public float nVISenescence { get; set; }
        /// <summary>Maximum NDVI rate during greendown (unitless).</summary>
        public float nVIGreendown { get; set; }
        /// <summary>Maximum NDVI rate during ecodormancy (unitless).</summary>
        public float nVIEcodormancy { get; set; }
        /// <summary>Site-specific minimum (understory) NDVI floor.</summary>
        public float minimumVI { get; set; }
        /// <summary>Site-specific maximum NDVI cap.</summary>
        public float maximumVI { get; set; }
    }
    #endregion

    #region masting
    /// <summary>Parameters of the MASTHING carbon-balance / resource-budget module.</summary>
    public class parResources
    {
        /// <summary>Light-use efficiency for GPP (g C MJ-1).</summary>
        public float lightUseEfficiency { get; set; }

        /// <summary>Critical daily maximum air temperature above which heat stress fully suppresses GPP (°C).</summary>
        public float criticalHeatTemperature { get; set; }

        /// <summary>Critical daily minimum air temperature below which cold stress fully suppresses GPP (°C).</summary>
        public float criticalColdTemperature { get; set; }

        /// <summary>Fraction of total tree biomass available as maximum non-structural storage budget (kg C kg-1).</summary>
        public float resourceBudgetFraction { get; set; }

        /// <summary>Relative daily maintenance respiration rate for woody biomass ([0,1]).</summary>
        public float relativeRespirationWood { get; set; }

        /// <summary>Relative daily maintenance respiration rate for foliage ([0,1]).</summary>
        public float relativeRespirationLeaves { get; set; }

        /// <summary>Q10 coefficient for the temperature scaling of maintenance respiration.</summary>
        public float Q10 { get; set; }

        /// <summary>Length (days) of the rolling window used to compute the water-stress index from precipitation and ET0.</summary>
        public float waterStressDays { get; set; }

        /// <summary>Threshold of the rescaled water-availability index below which GPP is linearly reduced.</summary>
        public float waterStressThreshold { get; set; }

        /// <summary>Specific leaf area used for leaf biomass allocation (m2 kg-1).</summary>
        public float specificLeafArea { get; set; }
    }

    /// <summary>Parameters of the MASTHING reproductive module.</summary>
    public class parReproduction
    {
        /// <summary>Lower limit of the solstice-temperature cue response (°C).</summary>
        public float temperatureCueMinimum { get; set; }
        /// <summary>Upper limit of the solstice-temperature cue response (°C).</summary>
        public float temperatureCueMaximum { get; set; }

        /// <summary>Sensitivity (slope) of the sigmoidal temperature-cue response (unitless).</summary>
        public float temperatureCueSensitivity { get; set; }

        /// <summary>Flowering peak time expressed as a percentage of the growth phase (%).</summary>
        public float floweringTime { get; set; }

        /// <summary>Flowering-period length, expressed as fraction of the growth phase.</summary>
        public float floweringDuration { get; set; }

        /// <summary>Daily precipitation at which pollination efficiency is halved (mm).</summary>
        public float limitingPollinationPrecipitation { get; set; }

        /// <summary>Minimum resource-budget level required to open a reproductive season ([0,1]).</summary>
        public float reproductionThreshold { get; set; }

        /// <summary>Carbon cost to convert a unit of flower biomass into fruit biomass ([0,1]).</summary>
        public float flowersToFruitCost { get; set; }

        /// <summary>Weight given to the current-year temperature cue relative to the previous year's cue in the interannual weather-cue response (unitless).</summary>
        public float temperatureYearTweight { get; set; }
    }
    #endregion
}

