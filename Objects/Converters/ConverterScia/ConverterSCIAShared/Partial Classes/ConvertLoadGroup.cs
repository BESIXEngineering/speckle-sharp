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
        public StructuralLoadGroup LoadGroupToNative(SCIALoadGroup speckleLoadGroup)
        {
            if (ExistsInContext(speckleLoadGroup, out IEnumerable<StructuralLoadGroup> contextObjects))
                return contextObjects.FirstOrDefault();

            string name = speckleLoadGroup.name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GetUniqueADMName<StructuralLoadGroup>("LG");
            }

            LoadGroupType admLoadGroupType = GetSimilarEnum<LoadGroupType>(speckleLoadGroup.loadGroupType);

            var admLoadGroup = new StructuralLoadGroup(GetSCIAId(speckleLoadGroup), name, admLoadGroupType);

            if (speckleLoadGroup.relation != SCIALoadGroupRelation.NotDefined)
            {
                admLoadGroup.Relation = GetSimilarEnum<Relation>(speckleLoadGroup.relation);
            }
            if (admLoadGroupType == LoadGroupType.Variable)
            {
                admLoadGroup.Load = GetSimilarEnum<Load>(speckleLoadGroup.variableLoadType);
            }

            AddToAnalysisModel(admLoadGroup, speckleLoadGroup);
            return admLoadGroup;
        }

        #endregion


        #region --- TO SPECKLE ---

        public SCIALoadGroup LoadGroupToSpeckle(StructuralLoadGroup admLoadGroup)
        {
            if (ExistsInContext(admLoadGroup, out SCIALoadGroup contextObject))
                return contextObject;

            SCIALoadGroupType groupType = GetSimilarEnum<SCIALoadGroupType>(admLoadGroup.LoadGroupType);
            SCIALoadGroupRelation relation = SCIALoadGroupRelation.NotDefined;
            if (admLoadGroup.Relation != null)
            {
                relation = GetSimilarEnum<SCIALoadGroupRelation>(admLoadGroup.Relation);
            }
            SCIAVariableLoadType load = SCIAVariableLoadType.NotDefined;
            if (admLoadGroup.Load?.Value != null)
            {
                load = GetSimilarEnum<SCIAVariableLoadType>(admLoadGroup.Load.Value);
            }

            var speckleLoadGroup = new SCIALoadGroup(admLoadGroup.Name, groupType, relation, load);

            AddToSpeckleModel(speckleLoadGroup, admLoadGroup);
            return speckleLoadGroup;
        }

        #endregion
    }
}
