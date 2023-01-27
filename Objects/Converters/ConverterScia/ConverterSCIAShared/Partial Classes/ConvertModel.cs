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
using ModelExchanger.AnalysisDataModel.StructuralElements;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Curves;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Points;
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

        public AnalysisModel ModelToNative(Structural.Analysis.Model speckleModel)
        {
            // AdmModel = new AnalysisModel();
            // TODO: empty current ContextObjects?
            if (speckleModel.specs != null)
            {
                ModelInfoToNative(speckleModel.specs);
            }

            if (speckleModel.nodes != null)
            {
                foreach (var obj in speckleModel.nodes)
                {
                    if (obj is Structural.Geometry.Node node)
                    {
                        NodeToNative(node);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid Node input; must be of type '{typeof(Structural.Geometry.Node)}'");
                    }
                }
            }

            if (speckleModel.restraints != null)
            {
                foreach (var obj in speckleModel.restraints)
                {
                    if (obj is Structural.Geometry.Restraint restraint)
                    {
                        ConvertToNative(restraint);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid Restraint input; must be of type '{typeof(Structural.Geometry.Restraint)}'");
                    }
                }
            }

            if (speckleModel.materials != null)
            {
                foreach (var obj in speckleModel.materials)
                {
                    if (obj is Structural.Materials.StructuralMaterial m)
                    {
                        MaterialToNative(m);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid Material input; must be of type '{typeof(Structural.Materials.StructuralMaterial)}'");
                    }
                }
            }

            if (speckleModel.properties != null)
            {
                foreach (var obj in speckleModel.properties)
                {
                    if (obj is Structural.Properties.Property p)
                    {
                        ConvertToNative(p);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid Property input; must be of type '{typeof(Structural.Properties.Property)}'");
                    }
                }
            }

            if (speckleModel.elements != null)
            {
                if (speckleModel.elements.Any(el => el is Structural.Analysis.Model))
                {
                    throw new ArgumentException("Model is not an element");
                }
                
                ConvertToNative(speckleModel.elements);
            }

            if (speckleModel.loads != null)
            {
                foreach (var obj in speckleModel.loads.OrderBy(GetConversionOrder))
                {
                    if (obj is Structural.Loading.Load || obj is Structural.Loading.LoadCase || obj is Structural.Loading.LoadCombination || obj is Structural.SCIA.Loading.SCIALoadGroup)
                    {
                        ConvertToNative(obj);
                    }
                    else
                    {
                        throw new ArgumentException($"Invalid Load input; must be of type Load, LoadCase, LoadCombination or LoadGroup");
                    }
                }
            }

            // Add internal nodes to StructuralCurveMembers that have nodes on their geometry that do not belong their geometry
            //  (the InternalNodes property of ADM StructuralCurveMember), depending on the user settings
            // Todo: Add this as separate node in Grasshopper, where node merging is performed at GH SpeckleAnalysisModel level already?
            //  Will reduce the amount of IAnalysisModelService requests, which seem to be slow...
            AddMemberCoincidingNodes();
            
            // Before you pass your AnalysisModel instance to any of the other modules (e.g. Bimplus, Excel, JSON)
            // you should always run the ValidateModel(AnalysisModel) method from IAnalysisModelService.
            // All of our modules have been configured to not allow processing of invalid / partially validated models.
            // > From https://docs.calatrava.scia.net/html/1eb177c6-019c-48c0-9613-da89e5e43c7a.htm
            // > Note: this only seems to be needed when changing elements of the AnalysisModel without updating them using the IAnalysisModelService.
            // ValidateModel();

            return AdmModel;
        }
        #endregion

        #region --- TO SPECKLE ---

        public Structural.Analysis.Model ModelToSpeckle(AnalysisModel admModel)
        {
            // TODO: empty current ContextObjects?
            SpeckleContext = new Dictionary<Guid, Base>();

            foreach (IAnalysisObject admObj in admModel)
            {
                // System.Diagnostics.Debug.WriteLine($"- {admObj} {admObj.Name}");
                ConvertToSpeckle(admObj);
            }
            
            return CompileSpeckleModel();
        }

        /// <summary>
        /// Compile all SpeckleContext objects in a single Speckle Model object.
        /// </summary>
        /// <returns></returns>
        public Structural.Analysis.Model CompileSpeckleModel()
        {
            Structural.Analysis.Model speckleModel = new Structural.Analysis.Model()
            {
                specs = new Structural.Analysis.ModelInfo(),
                nodes = new List<Base>(),
                materials = new List<Base>(),
                elements = new List<Base>(),
                properties = new List<Base>(),
                restraints = new List<Base>(),
                loads = new List<Base>()
            };

            foreach (var speckleObject in SpeckleContext.Values)
            { 
                switch (speckleObject)
                {
                    case Structural.Analysis.ModelInfo mInfo:
                        speckleModel.specs = mInfo;
                        break;
                    case Structural.Geometry.Node _:
                        speckleModel.nodes.Add(speckleObject);
                        break;
                    case Structural.Geometry.Restraint _:
                        // Restraints are added as nested properties of nodes. It isn't useful to return them separately, as the inverse conversion isn't useful.
                        // speckleModel.restraints.Add(speckleObject);
                        break;
                    case Structural.Materials.StructuralMaterial _:
                        speckleModel.materials.Add(speckleObject);
                        break;
                    case Structural.Properties.Property _:
                        speckleModel.properties.Add(speckleObject);
                        break;
                    case Structural.Loading.Load _:
                    case Structural.Loading.LoadCase _:
                    case Structural.SCIA.Loading.SCIALoadGroup _:
                    case Structural.Loading.LoadCombination _:
                        speckleModel.loads.Add(speckleObject);
                        break;
                    case Structural.Geometry.Element1D _:
                    case Structural.Geometry.Element2D _:
                    case Structural.Geometry.Element3D _:
                    case Structural.Geometry.Storey _:
                        speckleModel.elements.Add(speckleObject);
                        break;
                    default:
                        throw new NotImplementedException($"Don't know how to add Base of type '{speckleObject.GetType()}' to Speckle analysis model");
                }
            }

            return speckleModel;
        }
        #endregion
    }
}
