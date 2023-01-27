using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Loads;
using ModelExchanger.AnalysisDataModel.Subtypes;
using ModelExchanger.AnalysisDataModel.StructuralElements;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects.Structural.Geometry;
using Objects.Structural.SCIA;
using Objects.Structural.SCIA.Loading;
using Speckle.Core.Models;
using Speckle.Core.Kits;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---
        public StructuralLoadCombination LoadCombinationToNative(Structural.Loading.LoadCombination speckleLoadCombination)
        {
            if (ExistsInContext(speckleLoadCombination, out IEnumerable<StructuralLoadCombination> contextObjects))
                return contextObjects.FirstOrDefault();


            string name = speckleLoadCombination.name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetUniqueADMName<StructuralLoadCombination>("LC");
            }

            string description;
            LoadCaseCombinationCategory category = LoadCaseCombinationCategory.UltimateLimitState;

            // ToDo no support yet for LoadCombinationType Enum in SCIA 21.0????
            // LoadCaseCombinationType? type = null;
            LoadCaseCombinationStandard? standard = null;
            List<double> multipliers = null;

            if (speckleLoadCombination is SCIALoadCombination speckleSciaLC)
            {
                description = speckleSciaLC.description;
                category = GetSimilarEnum<LoadCaseCombinationCategory>(speckleSciaLC.category);
                if (category == LoadCaseCombinationCategory.AccordingNationalStandard && speckleSciaLC.nationalStandard != SCIANationalStandard.NotDefined)
                {
                    standard = GetSimilarEnum<LoadCaseCombinationStandard>(speckleSciaLC.nationalStandard);
                }
                multipliers = speckleSciaLC.loadMultipliers;
            }
            else
            {
                description = GetSpeckleDynamicStringProperty(speckleLoadCombination, "description");
            }

            // Gather load cases with factors and multipliers
            if (speckleLoadCombination.loadCases == null || !speckleLoadCombination.loadCases.Any())
                throw new ArgumentException("Invalid amount of load cases for load combination");

            List<StructuralLoadCombinationData> admLoadCaseData = new List<StructuralLoadCombinationData>(speckleLoadCombination.loadCases.Count);
            for (int i = 0; i < speckleLoadCombination.loadCases.Count; i++)
            {
                var admLoadCase = LoadCaseToNative(speckleLoadCombination.loadCases[i] as Structural.Loading.LoadCase 
                    ?? throw new ArgumentNullException("loadCases", "Load case of load combination cannot be null"));
                
                double factor = 1;
                if (speckleLoadCombination.loadFactors != null && speckleLoadCombination.loadFactors.Any())
                {
                    factor = (i < speckleLoadCombination.loadFactors.Count) ? speckleLoadCombination.loadFactors[i] : speckleLoadCombination.loadFactors.Last();
                }

                double multiplier = 1;
                if (multipliers != null && multipliers.Any())
                {
                    factor = (i < multipliers.Count) ? multipliers[i] : multipliers.Last();
                }

                var loadCaseData = new StructuralLoadCombinationData(admLoadCase, factor, multiplier);
                admLoadCaseData.Add(loadCaseData);
            }

            // todo: Specify combination type (only for SCIA 21.1 and later)
            // speckleLoadCombination.combinationType

            // Create ADM load combination object
            StructuralLoadCombination admLoadCombination = new StructuralLoadCombination(GetSCIAId(speckleLoadCombination), name, category, admLoadCaseData);
            if (string.IsNullOrEmpty(description))
            {
                admLoadCombination.Description = description;
            }
            if (standard != null && category == LoadCaseCombinationCategory.AccordingNationalStandard)
            {
                admLoadCombination.NationalStandard = standard;
            }

            AddToAnalysisModel(admLoadCombination, speckleLoadCombination);
            return admLoadCombination;
        }

        #endregion


        #region --- TO SPECKLE ---
        public SCIALoadCombination LoadCombinationToSpeckle(StructuralLoadCombination admLoadCombination)
        {
            if (ExistsInContext(admLoadCombination, out SCIALoadCombination contextObject))
                return contextObject;
            
            // todo: Specify combination type (only for SCIA 21.1 and later)
            // admLoadCombination.Type = 

            SCIALoadCombinationCategory category = GetSimilarEnum<SCIALoadCombinationCategory>(admLoadCombination.Category);
            SCIANationalStandard standard = SCIANationalStandard.NotDefined;
            if (admLoadCombination.NationalStandard != null)
            {
                standard = GetSimilarEnum<SCIANationalStandard>(admLoadCombination.NationalStandard);
            }

            // TODO add combination type support

            List<Base> loadCases = new List<Base>();
            List<double> loadFactors = new List<double>();
            List<double> loadMultipliers = new List<double>();

            foreach (var data in admLoadCombination)
            {
                loadCases.Add(LoadCaseToSpeckle(data.LoadCase));
                loadFactors.Add(data.Factor);
                loadMultipliers.Add(data.Multiplier);
            }

            SCIALoadCombination speckleLoadCombination = new SCIALoadCombination()
            {
                name = admLoadCombination.Name,
                description = admLoadCombination.Description,
                category = category,
                nationalStandard = standard,
                loadCases = loadCases,
                loadFactors = loadFactors,
                loadMultipliers = loadMultipliers,
            };

            AddToSpeckleModel(speckleLoadCombination, admLoadCombination);
            return speckleLoadCombination;
        }
        #endregion

    }
}
