using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Linq;

using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Enums;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Subtypes;

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
        public StructuralMaterial MaterialToNative(Structural.Materials.StructuralMaterial speckleMaterial)
        {
            // Check whether the element already exists in the current context, and if so, return it
            if (ExistsInContext(speckleMaterial, out IEnumerable<StructuralMaterial> contextObjects))
                return contextObjects.FirstOrDefault();

            var materialTypeNative = speckleMaterial.materialType switch
            {
                Structural.MaterialType.Concrete => MaterialType.Concrete,
                Structural.MaterialType.Steel => MaterialType.Steel,
                Structural.MaterialType.Aluminium => MaterialType.Aluminium,
                Structural.MaterialType.Masonry => MaterialType.Masonry,
                Structural.MaterialType.Timber => MaterialType.Timber,
                _ => MaterialType.Other,
            };

            string grade = speckleMaterial.grade;
            if (string.IsNullOrWhiteSpace(grade)) grade = speckleMaterial.name;

            // TODO: this fails if grade is null, throwing an ArgumentNullException. Better handling needed?
            var admMaterial = new StructuralMaterial(
                GetSCIAId(speckleMaterial),
                speckleMaterial.name,
                materialTypeNative,
                grade);

            // TODO: read and use user-defined units from Model.ModelInfo.ModelSettings.ModelUnits (if available)
            if (speckleMaterial.density > 0)
            {
                admMaterial.UnitMass = UnitsNet.Density.FromKilogramsPerCubicMeter(speckleMaterial.density);
            }
            if (speckleMaterial.elasticModulus > 0)
            {
                admMaterial.EModulus = UnitsNet.Pressure.FromMegapascals(speckleMaterial.elasticModulus);
            }
            if (speckleMaterial.shearModulus > 0)
            {
                admMaterial.GModulus = UnitsNet.Pressure.FromMegapascals(speckleMaterial.shearModulus);
            }
            if (speckleMaterial.poissonsRatio > 0)
            {
                admMaterial.PoissonCoefficient = speckleMaterial.poissonsRatio;
            }
            if (speckleMaterial.thermalExpansivity > 0)
            {
                admMaterial.ThermalExpansion = UnitsNet.CoefficientOfThermalExpansion.FromInverseKelvin(speckleMaterial.thermalExpansivity);
            }

            AddToAnalysisModel(admMaterial, speckleMaterial);

            return admMaterial;
        }
        #endregion


        #region --- TO SPECKLE ---

        public Structural.Materials.StructuralMaterial MaterialToSpeckle(StructuralMaterial admMaterial)
        {
            // Check whether the element already exists in the current context, and if so, return it
            if (ExistsInContext(admMaterial, out Structural.Materials.StructuralMaterial contextObject))
                return contextObject;

            var speckleMaterialType = admMaterial.Type switch
            {
                MaterialType.Concrete => Structural.MaterialType.Concrete,
                MaterialType.Steel => Structural.MaterialType.Steel,
                MaterialType.Aluminium => Structural.MaterialType.Aluminium,
                MaterialType.Masonry => Structural.MaterialType.Masonry,
                MaterialType.Timber => Structural.MaterialType.Timber,
                _ => Structural.MaterialType.Other,
            };

            var speckleMaterial = new Structural.Materials.StructuralMaterial
            {
                name = admMaterial.Name,
                materialType = speckleMaterialType,
                grade = admMaterial.Quality,
                density = admMaterial.UnitMass.KilogramsPerCubicMeter,
                elasticModulus = admMaterial.EModulus.Megapascals,
                shearModulus = admMaterial.GModulus.Megapascals,
                poissonsRatio = admMaterial.PoissonCoefficient,
                thermalExpansivity = admMaterial.ThermalExpansion.InverseKelvin
            };

            AddToSpeckleModel(speckleMaterial, admMaterial);

            return speckleMaterial;
        }
        #endregion
    }
}
