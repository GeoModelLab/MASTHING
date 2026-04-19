using runner.data;
using source.data;

// ---------------------------------------------------------------------------
// MASTHING — reference data reader.
// Parses observational CSVs providing:
//   * MODIS NDVI time series per site/pixel (used for phenology calibration),
//   * tree-level seed production records (UK beech masting survey, 1980–2025)
//     used for reproductive-module calibration and prospective validation.
// ---------------------------------------------------------------------------

namespace runner
{
    /// <summary>
    /// Reads observational reference data (NDVI time series and seed-production
    /// records) into the per-pixel data structures used by the runner.
    /// </summary>
    internal class referenceReader
    {
        /// <summary>
        /// Reads the MODIS NDVI reference CSV (one row per site-year-DOY) into
        /// a dictionary of <see cref="Site"/> objects keyed by site identifier.
        /// Expected column layout: site, year, doy, lat, long, ndvi.
        /// </summary>
        /// <param name="file">Path to the phenology reference CSV.</param>
        /// <returns>Dictionary mapping site IDs to populated <see cref="Site"/> records.</returns>
        public Dictionary<string, Site> readReferenceDataPheno(string file)
        {
            //output dictionary keyed by site identifier.
            var idPixel = new Dictionary<string, Site>();

            //open the CSV and skip the header row.
            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            //stream the CSV one row at a time.
            while (!sr.EndOfStream)
            {

                //split on both commas and quotes to tolerate quoted values.
                string[] line = sr.ReadLine().Split(',', '"');
                //first column carries the site identifier.
                string site = line[0];

                //only full six-column rows are processed; malformed lines are silently skipped.
                if (line.Length == 6)
                {
                    //first time we see this site: instantiate its container and latch the coordinates.
                    if (!idPixel.ContainsKey(site))
                    {

                        idPixel.Add(site, new Site());
                        idPixel[site].latitude = float.Parse(line[3]);
                        idPixel[site].longitude = float.Parse(line[4]);
                    }
                    //parse the calendar year from the CSV.
                    int year = int.Parse(line[1]);


                    //skip rows where NDVI is missing.
                    if (line[5] != "NA")
                    {
                        //reconstruct the calendar date from (year, DOY).
                        DateTime date = new DateTime(year, 1, 1).AddDays(int.Parse(line[2]));
                        //guard against duplicate (site, date) rows.
                        if (!idPixel[site].dateNDVInorm.ContainsKey(date))
                        {
                            //TODO: check for NDVI
                            idPixel[site].dateNDVInorm.Add(date, float.Parse(line[5]));
                        }
                    }


                }
            }
            //close the file
            sr.Close();

            return idPixel;
        }

        /// <summary>
        /// Reads the tree-level seed production CSV (one row per site-tree-year)
        /// into a dictionary of <see cref="Site"/> objects keyed by site
        /// identifier. Expected column layout: site, year, treeId, lat, long,
        /// seeds, dbh.
        /// </summary>
        /// <param name="file">Path to the seeds reference CSV.</param>
        /// <returns>Dictionary mapping site IDs to populated <see cref="Site"/> records.</returns>
        public Dictionary<string, Site> readReferenceDataSeeds(string file)
        {
            //output dictionary keyed by site identifier.
            var idSite = new Dictionary<string, Site>();

            //open the CSV and skip the header row.
            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            //stream the CSV one row at a time.
            while (!sr.EndOfStream)
            {
                //split on commas and quotes; [0]=site, [1]=year, [2]=treeId, [3]=lat, [4]=long, [5]=seeds, [6]=dbh.
                string[] line = sr.ReadLine().Split(',', '"');
                string site = line[0];

                //first time we see this site: instantiate the Site and latch its coordinates.
                if (!idSite.ContainsKey(site))
                {
                    idSite.Add(site, new Site());
                    idSite[site].longitude = float.Parse(line[4]);
                    idSite[site].latitude = float.Parse(line[3]);
                }

                //first time we see this tree at this site: create a tree record indexed by treeId.
                if (!idSite[site].id_YearSeeds.ContainsKey(line[2]))
                {
                    idSite[site].id_YearSeeds.Add(line[2], new tree());
                }
                //keep the tree's id in sync with the dictionary key.
                idSite[site].id_YearSeeds[line[2]].id = line[2];
                //diameter at breast height: parse when available, otherwise fall back to a canonical 60 cm.
                if (line[6] != "NA")
                {
                    idSite[site].id_YearSeeds[line[2]].diameter130 = float.Parse(line[6]);
                }
                else
                {
                    idSite[site].id_YearSeeds[line[2]].diameter130 = 60F;
                }

                //store the year → seed-count mapping when the observation is not missing.
                if (line[5] != "NA")
                    idSite[site].id_YearSeeds[line[2]].YearSeeds.Add(int.Parse(line[1]), float.Parse(line[5]));
            }
            //close the file
            sr.Close();

            return idSite;
        }
    }
}
