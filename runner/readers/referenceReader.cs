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
        public Dictionary<string, Site> readReferenceDataPheno(string file)
        {
            var idPixel = new Dictionary<string, Site>();

            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            while (!sr.EndOfStream)
            {

                string[] line = sr.ReadLine().Split(',', '"');
                string site = line[0];

                if (line.Length == 6)
                {
                    if (!idPixel.ContainsKey(site))
                    {

                        idPixel.Add(site, new Site());
                        idPixel[site].latitude = float.Parse(line[3]);
                        idPixel[site].longitude = float.Parse(line[4]);
                    }
                    int year = int.Parse(line[1]);


                    if (line[5] != "NA")
                    {
                        DateTime date = new DateTime(year, 1, 1).AddDays(int.Parse(line[2]));
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

        public Dictionary<string, Site> readReferenceDataSeeds(string file)
        {
            var idSite = new Dictionary<string, Site>();

            StreamReader sr = new StreamReader(file);
            sr.ReadLine();

            while (!sr.EndOfStream)
            {
                string[] line = sr.ReadLine().Split(',', '"');
                string site = line[0];

                //if (idPixel.Keys.Count < 3)
                //{
                if (!idSite.ContainsKey(site))
                {
                    idSite.Add(site, new Site());
                    idSite[site].longitude = float.Parse(line[4]);
                    idSite[site].latitude = float.Parse(line[3]);
                }

                if (!idSite[site].id_YearSeeds.ContainsKey(line[2]))
                {
                    idSite[site].id_YearSeeds.Add(line[2], new tree());
                }
                idSite[site].id_YearSeeds[line[2]].id = line[2];
                if (line[6] != "NA")
                {
                    idSite[site].id_YearSeeds[line[2]].diameter130 = float.Parse(line[6]);
                }
                else
                {
                    idSite[site].id_YearSeeds[line[2]].diameter130 = 60F;
                }

                if (line[5] != "NA")
                    idSite[site].id_YearSeeds[line[2]].YearSeeds.Add(int.Parse(line[1]), float.Parse(line[5]));
            }
            //close the file
            sr.Close();

            return idSite;
        }
    }
}
