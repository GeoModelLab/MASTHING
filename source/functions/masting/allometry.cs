using source.data;
using System;

// ---------------------------------------------------------------------------
// MASTHING — tree allometry initialization.
//
// Implements the species-specific allometric relationships for Fagus sylvatica
// described in Supplementary Section S1.1 of Bregaglio et al. (2026).
// Given the diameter at breast height (DBH, 1.3 m) as the only tree-level
// input, it derives:
//   - crown projection area (CPA, m²)
//   - tree height (H, m)
//   - aboveground biomass components (total, stem, branch, foliage, kg DM)
//   - leaf area and maximum LAI (m² m⁻²)
// References: Bartelink (1997), Zianis & Mencuccini (2004).
// ---------------------------------------------------------------------------

namespace source.functions
{
    /// <summary>
    /// Initializes the allometric and structural properties of a beech tree from DBH.
    /// Must be called once at the beginning of each simulation before any process
    /// module (carbon balance, phenology, reproduction) is executed.
    /// </summary>
    public class allometry
    {
        /// <summary>
        /// Computes allometric attributes of a tree from its DBH and returns a fully
        /// initialized <see cref="tree"/> object. All equations follow the references
        /// cited in Supplementary Table S1.6 of the MASTHING paper.
        /// </summary>
        /// <param name="inputTree">Tree object with DBH (cm) and identifier.</param>
        /// <param name="parameters">Model parameters (uses species-specific SLA).</param>
        /// <param name="output">Previous-day model state (unused for initialization, kept for API consistency).</param>
        /// <param name="outputT1">Current-day model state (unused for initialization, kept for API consistency).</param>
        /// <returns>Tree object with allometric variables populated.</returns>
        public tree allometryInitialization(tree inputTree, parameters parameters, output output, output outputT1)
        {
            //instantiate the tree object
            tree tree = new tree();
            tree.id = inputTree.id;
            //this is the diameter at 130 cm height, the only variable needed to estimate tree allometry
            tree.diameter130 = inputTree.diameter130;
            tree.diameterBaseCrown = (float)Math.Exp(tree.diameter130*0.0504F)+1.1544F; //cm
            tree.crownProjectionArea = (float)0.4064F*(float)Math.Pow(tree.diameter130,1.2830F); //m2
            tree.treeHeight = (float)Math.Exp(1.4192F+0.5358F*Math.Log(tree.diameter130)); //m
            //Zianis and Mencuccini 2003 Aboveground biomass relationships for beech (Fagus moesiaca Cz.) trees in Vermio Mountain, Northern Greece, and generalised equations for Fagus sp.
            //Ann For Sci 60, 439-448
            tree.totalBiomass = (float)Math.Exp(-1.3816F+2.3485F*Math.Log(tree.diameter130));//kg
            tree.baseCrownHeight = (float)Math.Exp(1.2238F+0.4677*Math.Log(tree.diameter130));//m
            tree.foliageBiomass = 0.0001663F * (float)Math.Pow(tree.diameter130,2)* tree.treeHeight +  0.224F;//kg
            tree.branchesBiomass=(float)Math.Exp(-5.2898F+2.9353*Math.Log(tree.diameter130));//kg
            tree.stemBiomass = (float)Math.Exp(-1.6015+2.3427*Math.Log(tree.diameter130));//kg
            tree.branchSingleBiomass = (float)Math.Exp(3.415+2.818*Math.Log(tree.branchDiameter));//kg
            tree.leafArea = 3.38F* (float)Math.Pow(tree.crownProjectionArea,1.028);// m2
            //Bartelink 1997 Allometric relationships for biomass and leaf area of beech(Fagus sylvatica L). Ann Sci For 54, 39-50
            tree.SLA = parameters.parResources.specificLeafArea;// m2/kg
            tree.LAImax = tree.foliageBiomass * tree.SLA / tree.crownProjectionArea; //tree.leafArea * tree.SLA
           // tree.LAImax = tree.leafArea / tree.crownProjectionArea;
            //return the tree object
            return tree;
        }
    }
}