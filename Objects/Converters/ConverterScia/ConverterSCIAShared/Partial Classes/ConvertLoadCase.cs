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

        public StructuralLoadCase LoadCaseToNative(Structural.Loading.LoadCase speckleLoadCase)
        {
            if (ExistsInContext(speckleLoadCase, out IEnumerable<StructuralLoadCase> contextObjects))
                return contextObjects.FirstOrDefault();

            ActionType actionType;
            LoadGroupType groupType;
            switch (speckleLoadCase.actionType)
            {
                case Structural.Loading.ActionType.Permanent:
                    actionType = ActionType.Permanent;
                    groupType = LoadGroupType.Permanent;
                    break;
                case Structural.Loading.ActionType.Variable:
                    actionType = ActionType.Variable;
                    groupType = LoadGroupType.Variable; // Could also be Fire, Moving, Seismic, Tensioning
                    break;
                case Structural.Loading.ActionType.Accidental:
                    actionType = ActionType.Accidental;
                    groupType = LoadGroupType.Accidental;
                    break;
                case Structural.Loading.ActionType.None:
                default:
                    throw new NotImplementedException($"LoadCase ActionType {speckleLoadCase.actionType} not supported");
            }

            LoadCaseType loadType = LoadCaseTypeToNative(speckleLoadCase.loadType, actionType);

            // Load cases are assigned to LoadGroups in SCIA
            // Get the one with the matching name, or if no name given, take the first one with a matching groupType.
            // Note: it is important that LoadGroups are converted before LoadCases!
            var groupName = speckleLoadCase.group;  
            StructuralLoadGroup admLoadGroup;

            if (string.IsNullOrWhiteSpace(groupName))
            {
                admLoadGroup = AdmModel.OfType<StructuralLoadGroup>().FirstOrDefault(g => g.LoadGroupType == groupType);
                groupName = "LG1";
            }
            else
            {
                admLoadGroup = GetAdmObjectByName<StructuralLoadGroup>(groupName);
            }

            if (admLoadGroup == null)
            {
                admLoadGroup = new StructuralLoadGroup(GetSCIAId(), groupName, groupType);
                AddToAnalysisModel(admLoadGroup, speckleLoadCase);
            }

            StructuralLoadCase admLoadCase = new StructuralLoadCase(GetSCIAId(speckleLoadCase), speckleLoadCase.name, actionType, admLoadGroup, loadType)
            {
                Description = speckleLoadCase.description
                //Duration = Duration.Long,
                //Specification = Specification.Standard
            };
            // 'Duration' is required when 'ActionType' is 'Variable'
            if (actionType == ActionType.Variable)
            {
                string duration = GetSpeckleDynamicStringProperty(speckleLoadCase, "duration");
                if (string.IsNullOrWhiteSpace(duration))
                { 
                    admLoadCase.Duration = Duration.Medium;
                }
                else
                { 
                    admLoadCase.Duration = GetSimilarEnum<Duration>(duration);
                }
            }

            AddToAnalysisModel(admLoadCase, speckleLoadCase);

            return admLoadCase;
        }

        public LoadCaseType LoadCaseTypeToNative(Structural.Loading.LoadType speckleType, ActionType actionType)
        {
            // Todo: improve this mapping!
            // In SCIA possible load case type depends on Action Type => see https://www.saf.guide/en/stable/loads/structuralloadcase.html
            return speckleType switch
            {
                Structural.Loading.LoadType.Other => LoadCaseType.Standard,
                // Permanent actions
                Structural.Loading.LoadType.Dead => actionType == ActionType.Permanent ? LoadCaseType.SelfWeight : throw new NotImplementedException($"Dead load must be permanent"),
                Structural.Loading.LoadType.SuperDead => actionType == ActionType.Permanent ? LoadCaseType.Standard : throw new NotImplementedException($"SuperDead load must be permanent"),
                Structural.Loading.LoadType.Prestress => actionType == ActionType.Permanent ? LoadCaseType.Prestress : throw new NotImplementedException($"Prestress must be permanent"),
                // Non-permanent actions
                Structural.Loading.LoadType.Live => actionType != ActionType.Permanent ? LoadCaseType.Static : throw new NotImplementedException($"Live load cannot be permanent"),
                Structural.Loading.LoadType.LiveRoof => actionType != ActionType.Permanent ? LoadCaseType.Maintenance : throw new NotImplementedException($"LiveRoof load cannot be permanent"),
                Structural.Loading.LoadType.Wind => actionType != ActionType.Permanent ? LoadCaseType.Wind : throw new NotImplementedException($"Wind load cannot be permanent"),
                Structural.Loading.LoadType.Snow => actionType != ActionType.Permanent ? LoadCaseType.Snow : throw new NotImplementedException($"Snow load cannot be permanent"),
                Structural.Loading.LoadType.Thermal => actionType != ActionType.Permanent ? LoadCaseType.Temperature : throw new NotImplementedException($"Temperature load cannot be permanent"),
                Structural.Loading.LoadType.SeismicRSA => actionType != ActionType.Permanent ? LoadCaseType.Seismic : throw new NotImplementedException($"Seismic load cannot be permanent"),
                Structural.Loading.LoadType.SeismicAccTorsion => actionType != ActionType.Permanent ? LoadCaseType.Seismic : throw new NotImplementedException($"Seismic load cannot be permanent"),
                Structural.Loading.LoadType.SeismicStatic => actionType != ActionType.Permanent ? LoadCaseType.Dynamic : throw new NotImplementedException($"Seismic load cannot be permanent"),
                // Structural.Loading.LoadType.None => LoadCaseType.Static,
                // Structural.Loading.LoadType.Soil => LoadCaseType.Standard,
                _ => throw new NotImplementedException($"LoadCase Type {speckleType} not supported"),
            };
        }

        #endregion


        #region --- TO SPECKLE ---

        public Structural.Loading.LoadCase LoadCaseToSpeckle(StructuralLoadCase admLoadCase)
        {
            if (ExistsInContext(admLoadCase, out Structural.Loading.LoadCase contextObject))
                return contextObject;

            var speckleLoadCase = new Structural.Loading.LoadCase()
            {
                name = admLoadCase.Name,
                group = admLoadCase.LoadGroup.Name,
                actionType = ActionTypeToSpeckle(admLoadCase.ActionType),
                loadType = LoadCaseTypeToSpeckle(admLoadCase.LoadType, admLoadCase.ActionType),
                description = admLoadCase.Description,
            };

            speckleLoadCase["duration"] = admLoadCase.Duration?.ToString();
            
            AddToSpeckleModel(speckleLoadCase, admLoadCase);
            return speckleLoadCase;
        }

        public Structural.Loading.LoadType LoadCaseTypeToSpeckle(LoadCaseType admType, ActionType admActionType)
        {
            return admType switch
            {
                // Todo: improve this mapping!
                LoadCaseType.SelfWeight => Structural.Loading.LoadType.Dead,
                LoadCaseType.Standard => admActionType == ActionType.Permanent ? Structural.Loading.LoadType.SuperDead : Structural.Loading.LoadType.Other,
                LoadCaseType.Prestress => Structural.Loading.LoadType.Prestress,
                LoadCaseType.Dynamic => Structural.Loading.LoadType.SeismicStatic,
                LoadCaseType.Static => Structural.Loading.LoadType.Live,
                LoadCaseType.Wind => Structural.Loading.LoadType.Wind,
                LoadCaseType.Snow => Structural.Loading.LoadType.Snow,
                LoadCaseType.Temperature => Structural.Loading.LoadType.Thermal,
                LoadCaseType.Seismic => Structural.Loading.LoadType.SeismicRSA,
                LoadCaseType.Maintenance => Structural.Loading.LoadType.LiveRoof,
                _ => throw new NotImplementedException($"ADM LoadCaseType {admType} not supported"),
            };
        }

        public Structural.Loading.ActionType ActionTypeToSpeckle(ActionType admType)
        {
            return admType switch
            {
                ActionType.Permanent => Structural.Loading.ActionType.Permanent,
                ActionType.Variable => Structural.Loading.ActionType.Variable,
                ActionType.Accidental => Structural.Loading.ActionType.Accidental,
                _ => throw new NotImplementedException($"ADM ActionType {admType} not supported"),
            };
        }

        #endregion
    }
}
