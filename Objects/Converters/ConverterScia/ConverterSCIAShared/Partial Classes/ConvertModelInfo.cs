using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;

using SciaTools.AdmToAdm.AdmSignalR.Models.ModelModification;
using SciaTools.Kernel.ModelExchangerExtension.Models.Exchange;
using CSInfrastructure.Extensions;

using Objects;
using Speckle.Core.Models;
using Speckle.Core.Kits;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA
    {
        #region --- TO SCIA ---
        public List<IAnalysisObject> ModelInfoToNative(Structural.Analysis.ModelInfo speckleModelInfo)
        {
            var result = new List<IAnalysisObject>();
            
            // TODO: Find a way to pass on the original SCIA guid, knowing that a single speckle object matches two SCIA objects with a different GUID
            ModelInformation admModelInfo = new ModelInformation(Guid.NewGuid(), speckleModelInfo.name ?? "B6ModelInfo")
            {
                SystemOfUnits = SystemOfUnits.Metric,
                SourceCompany = "BESIX",
                SourceApplication = "Speckle SCIA converter",
                // Owner = "KRO",
                LastUpdate = DateTime.Today,
                // todo: set objects that shouldn't be deleted on update if none given: IgnoredObjects = ... IgnoredGroups = ...
                //  see https://docs.calatrava.scia.net/html/42bf142f-f222-d5a6-5d7f-c66d7dcef678.htm
                // todo (available starting from SCIA 21.1): NationalCode = NationalCode.EC_NBN_EN
            };

            if (speckleModelInfo.description != null) admModelInfo.Description = speckleModelInfo.description;
            if (speckleModelInfo.initials != null) admModelInfo.Owner = speckleModelInfo.initials;
            if (speckleModelInfo.settings != null)
            {
                if (speckleModelInfo.settings.modelUnits != null)
                {
                    // todo: set converter units here using ModelUnits object!
                    var lengthUnits = Units.GetUnitsFromString(speckleModelInfo.settings.modelUnits.length);
                    if (lengthUnits == Units.Feet || lengthUnits == Units.Inches
                        || lengthUnits == Units.Yards || lengthUnits == Units.Miles)
                    {
                        admModelInfo.SystemOfUnits = SystemOfUnits.Imperial;
                    }
                }
                CoincidenceTolerance = Math.Pow(10, -speckleModelInfo.settings.coincidenceTolerance);
            }
            AddToAnalysisModel(admModelInfo, speckleModelInfo);
            result.Add(admModelInfo);

            if (!string.IsNullOrEmpty(speckleModelInfo.projectName))
            {
                var sciaProjectInfo = new ProjectInformation(Guid.NewGuid(), speckleModelInfo.projectName);
                if (speckleModelInfo.projectNumber != null) sciaProjectInfo.ProjectNumber = speckleModelInfo.projectNumber;
                AddToAnalysisModel(sciaProjectInfo, speckleModelInfo);
                result.Add(sciaProjectInfo);
            }

            return result;
        }
        #endregion


        #region --- TO SPECKLE ---
        public Structural.Analysis.ModelInfo ModelInfoToSpeckle(ModelInformation admModelInfo)
        {
            // Get from context, if existing, or create an empty object
            Structural.Analysis.ModelInfo speckleModelInfo =
                SpeckleContext.Select(kvp => kvp.Value).OfType<Structural.Analysis.ModelInfo>().FirstOrDefault();

            if (speckleModelInfo == null)
            {
                speckleModelInfo = new Structural.Analysis.ModelInfo();
                AddToSpeckleModel(speckleModelInfo, admModelInfo);
            }

            speckleModelInfo.name = admModelInfo.Name;
            speckleModelInfo.description = admModelInfo.Description;
            speckleModelInfo.initials = admModelInfo.Owner;
            // speckleModelInfo.settings = new Structural.Analysis.ModelSettings()

            return speckleModelInfo;
        }

        public Structural.Analysis.ModelInfo ProjectInfoToSpeckle(ProjectInformation admProjectInfo)
        {
            // Get from context, if existing, or create an empty object
            Structural.Analysis.ModelInfo speckleModelInfo =
                SpeckleContext.Select(kvp => kvp.Value).OfType<Structural.Analysis.ModelInfo>().FirstOrDefault();

            if (speckleModelInfo == null)
            {
                speckleModelInfo = new Structural.Analysis.ModelInfo();
                AddToSpeckleModel(speckleModelInfo, admProjectInfo);
            }

            speckleModelInfo.projectName = admProjectInfo.Name;
            speckleModelInfo.projectNumber = admProjectInfo.ProjectNumber;

            return speckleModelInfo;
        }
        #endregion
    }
}
