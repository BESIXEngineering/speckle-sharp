using Speckle.Core.Kits;
using Speckle.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;
using Objects.Structural.Loading;


namespace Objects.Structural.SCIA.Loading
{
    public class SCIALoadCombination : LoadCombination
    {
        // see https://www.saf.guide/en/stable/loads/structuralloadcombination.html

        //public string name { get; set; } 
        //public List<Base> loadCases { get; set; }
        // public List<double> loadFactors { get; set; }
        // public CombinationType combinationType { get; set; }

        public string description { get; set; }
        public SCIALoadCombinationCategory category { get; set; }
        public SCIANationalStandard nationalStandard { get; set; }
        public List<double> loadMultipliers { get; set; }

        public SCIALoadCombination() { }

        [SchemaInfo("SCIALoadCombination", "Creates a Speckle load combination for SCIA", "SCIA", "Loading")]
        public SCIALoadCombination(string name, 
            [SchemaParamInfo("A list of load cases")] List<Base> loadCases,
            [SchemaParamInfo("A list of load factors (to be mapped to provided load cases)")] List<double> loadFactors = null,
            [SchemaParamInfo("A list of load multipliers (to be mapped to provided load cases)")] List<double> loadMultipliers = null,
            [SchemaParamInfo("Optional description of the load combination")] string description = null,
            [SchemaParamInfo("The category of the load combination")] SCIALoadCombinationCategory category = SCIALoadCombinationCategory.UltimateLimitState,
            [SchemaParamInfo("The type of the load combination (if Category = ULS or SLS; can be either LinearAdd or Envelope)")] CombinationType combinationType = CombinationType.LinearAdd,
            [SchemaParamInfo("The national standard of the load combination (if Category = AccordingNationalStandard)")] SCIANationalStandard nationalStandard = SCIANationalStandard.NotDefined)
        {
            this.name = name;
            this.description = description;
            this.category = category;
            this.combinationType = combinationType;

            if (combinationType != CombinationType.LinearAdd || combinationType != CombinationType.Envelope)
                throw new ArgumentException("SCIA only supports Linear and Envelope type load combinations");

            this.nationalStandard = category == SCIALoadCombinationCategory.AccordingNationalStandard ? nationalStandard : SCIANationalStandard.NotDefined;

            if (loadFactors == null && category != SCIALoadCombinationCategory.AccordingNationalStandard)
                throw new ArgumentNullException("Load factors are required if category is not AccordingNationalStandard");

            if (loadFactors != null && loadCases.Count != loadFactors.Count)
                throw new ArgumentException("Number of load factors provided does not match number of load cases provided");

            if (loadMultipliers != null && loadCases.Count != loadMultipliers.Count)
                throw new ArgumentException("Number of load multipliers provided does not match number of load cases provided");

            this.loadCases = loadCases;
            this.loadFactors = loadFactors;
            this.loadMultipliers = loadMultipliers;
        }
	}
}
