using source.data;
using System;
using System.Collections.Generic;

namespace runner.data
{
    /// <summary>
    /// Reference site / MODIS pixel container used by the MASTHING runner.
    /// A <c>Site</c> groups the geographic location with the observational
    /// reference data needed to calibrate or evaluate the model at that
    /// location:
    ///   * a MODIS NDVI time series (daily, normalised) for phenology
    ///     calibration, and
    ///   * per-tree annual seed-production records (UK beech masting survey,
    ///     1980–2025) used for reproductive-module calibration and validation.
    /// </summary>
    public class Site
    {
        /// <summary>Site identifier as it appears in the reference CSV files.</summary>
        public string id { get; set; }

        /// <summary>Site latitude (decimal degrees, WGS84).</summary>
        public float latitude { get; set; }

        /// <summary>Site longitude (decimal degrees, WGS84).</summary>
        public float longitude { get; set; }

        /// <summary>
        /// Normalised MODIS NDVI time series indexed by calendar date.
        /// Used as target variable when <c>calibrationVariable = "phenology"</c>.
        /// </summary>
        public Dictionary<DateTime, float> dateNDVInorm = new Dictionary<DateTime, float>();

        /// <summary>
        /// Per-tree seed-production records indexed by tree ID. Each entry
        /// carries the tree's DBH (diameter at 1.30 m) and the year-indexed
        /// observed seed counts used as target variable when
        /// <c>calibrationVariable = "seeds"</c>.
        /// </summary>
        public Dictionary<string, tree> id_YearSeeds = new Dictionary<string, tree>();
    }
}
