using CSInfrastructure.IOC;
using ModelExchanger.AnalysisDataModel;
using ModelExchanger.AnalysisDataModel.Contracts;
using ModelExchanger.AnalysisDataModel.Integration.Bootstrapper;
using ModelExchanger.AnalysisDataModel.Libraries;
using ModelExchanger.AnalysisDataModel.Loads;
using ModelExchanger.AnalysisDataModel.Models;
using ModelExchanger.AnalysisDataModel.StructuralElements;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Curves;
using ModelExchanger.AnalysisDataModel.StructuralReferences.Points;
using Objects;
using Speckle.Core.Kits;
using Speckle.Core.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Objects.Converter.SCIA
{
    public partial class ConverterSCIA : ISpeckleConverter
    {
    #region -- General properties --
#if SCIAV210
    public static string SCIAAppName = "SCIA_21-0";
#elif SCIAV211
    public static string SCIAAppName = "SCIA_21-1";
#elif SCIAV220
    public static string SCIAAppName = "SCIA_22-0";
#else
    public static string SCIAAppName = "SCIA";
#endif

        public string Description => "SCIA ADM Converter";

        public string Name => nameof(ConverterSCIA);

        public string Author => "BESIX";

        public string WebsiteOrEmail => "https://www.besix.com";

        public IEnumerable<string> GetServicedApplications() => new string[] { SCIAAppName };  // !!! Must be identical to the app name in the assembly name

        // public HashSet<Exception> ConversionErrors { get; private set; } = new HashSet<Exception>();  --> Moved to ProgressReport
        public ProgressReport Report { get; private set; } = new ProgressReport();
        #endregion


        #region -- Context --
        /* The ContextDocument is an AnalysisModel to which we can directly add any SCIA IAnalysisObjects created by the xxxToNative functions. 
         * To find an object in an AnalysisModel object, see https://docs.calatrava.scia.net/html/f7f7f755-b592-4645-a497-02494df62e9b.htm
         * Additional model queries are available through the IAnalysisModelQuery https://docs.calatrava.scia.net/html/9371df8a-ec99-59b9-1cf6-c23ce465c71a.htm
         * or IStructuralAnalysisModelQuery https://docs.calatrava.scia.net/html/a2e7e9e5-237a-aeaa-1dc2-602c6aba6ada.htm
         * Use the IAnalysisModelService to apply CRUD operations on a model https://docs.calatrava.scia.net/html/83366ba4-cbde-4325-baee-4150c4989d03.htm
         * To compare analysis models, use the IAnalysisModelComparisonService https://docs.calatrava.scia.net/html/0d9095d6-110a-4961-8fb9-925b4c928974.htm
         * To sort objects depending on dependencies, use IAnalysisModelReferenceSorter https://docs.calatrava.scia.net/html/01246263-38a7-4ff4-8efb-a0bddd33a7f3.htm
         */

        /// <summary>
        /// Distance in meters within which two nodes are considered to coincide.
        /// </summary>
        private double CoincidenceTolerance { get; set; } = 1e-9; 

        /// <summary>
        /// The SCIA application document that the converter is targeting.
        /// </summary>
        public AnalysisModel AdmModel { get; private set; }
        public Dictionary<Guid, Base> SpeckleContext { get; private set; } = null;
        public void SetContextDocument(object doc) => AdmModel = (doc as AnalysisModel) ?? throw new ArgumentException("Invalid AnalysisModel");

        /// <summary>
        /// To know which other objects are being converted in the current conversion run, in order to sort relationships between them.
        /// For example, elements that have children use this to determine whether they should send their children out or not.
        /// </summary>
        public List<ApplicationObject> ContextObjects { get; set; } = new List<ApplicationObject>();
        public void SetContextObjects(List<ApplicationObject> objects) => throw new NotImplementedException();  // ContextObjects = objects; 
        // TODO: if we wish to use this Current and Previous context that we can set from the Connector, then we should return ApplicationPlaceholderObjects
        //  from the ToNative functions to easily assign them to the context

        /// <summary>
        /// To keep track of previously converted objects. If possible, conversions routines
        /// will edit an existing object, otherwise they will delete the old one and create the new one.
        /// </summary>
        public List<ApplicationObject> PreviousContextObjects { get; set; } = new List<ApplicationObject>();
        

        public void SetPreviousContextObjects(List<ApplicationObject> objects) => throw new NotImplementedException(); //  => PreviousContextObjects = objects; See comment above

        public void SetConverterSettings(object settings) => throw new NotImplementedException("This converter does not have any settings.");
        
        public ReceiveMode ReceiveMode { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        #endregion


        #region -- ToNative --
        /// <summary>
        /// Persistent storage of the scope, as it is computationally intensive to request.
        /// </summary>
        private IScopedServiceProvider admScope = null;
        private IAnalysisModelService admModelService = null;

        private bool PrepareADMService()
        {
            if (admScope == null)
            {
                IBootstrapper bootstrapper = new AnalysisDataModelBootstrapper();
                admScope = bootstrapper.CreateThreadedScope();
                admModelService = admScope.GetService<IAnalysisModelService>();
                return true;
            }
            return false;
        }

        private void FinalizeADMService()
        {
            if (admScope == null)
            {
                throw new InvalidOperationException("ADM model service not initialized");
            }
            // todo: is it needed to go through the fuzz of disposing this object?
            admScope.Dispose();
        }

        public bool CanConvertToNative(Base @object)
        {
            switch (@object)
            {
                //analysis
                case Structural.Analysis.Model _:
                case Structural.Analysis.ModelInfo _:
                //properties
                case Structural.Materials.StructuralMaterial _:
                case Structural.Properties.Property1D _:
                //geometry
                case Structural.Geometry.Storey _:
                case Structural.Geometry.Node _:
                case Structural.Geometry.Element1D _:
                case Structural.Geometry.Element2D _:
                //loads
                case Structural.Loading.LoadCase _:
                case Structural.SCIA.Loading.SCIALoadGroup _:
                case Structural.Loading.LoadCombination _:
                case Structural.Loading.LoadNode _:
                case Structural.Loading.LoadBeam _:
                    return true;
                default:
                    return false;
            };
        }

        /// <summary>
        /// Convert a Speckle Base object to SCIA and add the converted objects to the ContextObjects for further reference.
        /// </summary>
        /// <param name="object"></param>
        /// <returns></returns>
        public object ConvertToNative(Base @object)
        {
            if (@object == null) return null;

            bool finalizeADMServiceAtEnd = PrepareADMService();
            object result = null;

            try
            {
                switch (@object)
                {
                    case Structural.Analysis.Model o:
                        result = ModelToNative(o);
                        break;

                    case Structural.Analysis.ModelInfo o:
                        result = ModelInfoToNative(o);
                        break;

                    case Structural.Materials.StructuralMaterial o:
                        result = MaterialToNative(o);
                        break;

                    case Structural.Properties.Profiles.SectionProfile o:
                        Report.LogConversionError(new NotImplementedException("Skipped SectionProfile: Can't directly convert a SectionProfile to SCIA. Use a Profile1D instead to provide Material info."));
                        break;

                    case Structural.Properties.Property1D o:
                        result = Property1DToNative(o);
                        break;

                    case Structural.Geometry.Storey o:
                        result = StoreyToNative(o);
                        break;

                    case Structural.Geometry.Node o:
                        result = NodeToNative(o);
                        break;

                    case Structural.Geometry.Element1D o:
                        result = Element1DToNative(o);
                        break;

                    case Structural.Geometry.Element2D o:
                        result = Element2DToNative(o);
                        break;

                    case Structural.Loading.LoadCase o:
                        result = LoadCaseToNative(o);
                        break;

                    case Structural.SCIA.Loading.SCIALoadGroup o:
                        result = LoadGroupToNative(o);
                        break;

                    case Structural.Loading.LoadCombination o:
                        result = LoadCombinationToNative(o);
                        break;

                    case Structural.Loading.LoadNode o:
                        result = NodeLoadToNative(o);
                        break;

                    case Structural.Loading.LoadBeam o:
                        result = BeamLoadToNative(o);
                        break;
                    
                    default:
                        Report.LogConversionError(new Exception($"Skipped unsupported Speckle type: { @object.GetType() }"));
                        break;
                }
            }
            catch (NotImplementedException e)
            {
                Report.LogConversionError(new NotImplementedException($"Skipped Speckle object of type '{ @object.GetType() }': {e.Message}"));
            }
            catch (ArgumentNullException e)
            {
                Report.LogConversionError(new ArgumentNullException(e.ParamName, $"Failed for Speckle object of type '{ @object.GetType() }': {e.Message}"));
            }
            catch (ArgumentException e)
            {
                Report.LogConversionError(new ArgumentException($"Failed for Speckle object of type '{ @object.GetType() }': {e.Message}"));
            }
            catch (FormatException e)
            {
                Report.LogConversionError(new ArgumentException($"Failed for Speckle object of type '{ @object.GetType() }': {e.Message}"));
            }

            if (finalizeADMServiceAtEnd)
            {
                FinalizeADMService();
            }

            return result;
        }

        public List<object> ConvertToNative(List<Base> objects)
        {
            bool finalizeADMServiceAtEnd = PrepareADMService();
            
            var result = new List<object>();
            foreach (Base @base in objects.OrderBy(GetConversionOrder))
            {
                var converted = ConvertToNative(@base);
                if (converted is List<IAnalysisObject> list)
                {
                    result.AddRange(list);
                }
                else
                {
                    result.Add(converted);
                }
            }

            if (finalizeADMServiceAtEnd)
            {
                FinalizeADMService();
            }

            return result;
        }

        public int GetConversionOrder(Base item)
        {
            return item switch
            {
                Structural.Analysis.Model _ => 1,
                Structural.Analysis.ModelInfo _ => 2,
                Structural.Materials.StructuralMaterial _ => 10,
                Structural.Properties.Property1D _ => 11,
                Structural.Geometry.Storey _ => 12,
                Structural.Geometry.Node _ => 20,
                Structural.Geometry.Element1D _ => 21,
                Structural.Geometry.Element2D _ => 22,
                Structural.SCIA.Loading.SCIALoadGroup _ => 40,
                Structural.Loading.LoadCase _ => 41,
                Structural.Loading.LoadCombination _ => 42,
                Structural.Loading.Load _ => 43,
                _ => 100,
            };
            ;
        }
        #endregion


        #region -- ToSpeckle --

        public bool CanConvertToSpeckle(object @object)
        {
            switch (@object)
            {
                //analysis
                case AnalysisModel _:
                case ModelInformation _:
                case ProjectInformation _:
                //properties
                case StructuralMaterial _:
                case StructuralParametricCrossSection _:
                case StructuralManufacturedCrossSection _:
                //geometry
                case StructuralStorey _:
                case StructuralPointConnection _:
                case StructuralPointSupport _:
                case StructuralCurveMember _:
                case RelConnectsRigidLink _:
                case RelConnectsStructuralMember _:
                case StructuralSurfaceMember _:
                //loads
                case StructuralLoadCase _:
                case StructuralLoadGroup _:
                case StructuralLoadCombination _:
                case StructuralPointAction<PointStructuralReferenceOnPoint> _:
                case StructuralPointAction<PointStructuralReferenceOnBeam> _:
                case StructuralCurveAction<CurveStructuralReferenceOnBeam> _:
                    return true;
                default:
                    return false;
            };
        }

        public Base ConvertToSpeckle(object @object)
        {
            if (@object == null) return null;

            try
            {
                switch (@object)
                {
                    case AnalysisModel o:
                        return ModelToSpeckle(o);
                    case ModelInformation o:
                        return ModelInfoToSpeckle(o);
                    case ProjectInformation o:
                        return ProjectInfoToSpeckle(o);

                    case StructuralMaterial o:
                        return MaterialToSpeckle(o);
                    case StructuralCrossSection o:
                        return CrossSectionToSpeckle(o);
                    case StructuralStorey o:
                        return StoreyToSpeckle(o);

                    case StructuralPointConnection o:
                        return NodeToSpeckle(o);
                    case StructuralPointSupport o:
                        return PointSupportToSpeckle(o);

                    case StructuralCurveMember o:
                        return CurveMemberToSpeckle(o);
                    case RelConnectsRigidLink o:
                        return RigidLinkToSpeckle(o);
                    case RelConnectsStructuralMember o:
                        return HingeOnBeamToSpeckle(o);

                    case StructuralSurfaceMember o:
                        return SurfaceMemberToSpeckle(o);

                    case StructuralLoadCase o:
                        return LoadCaseToSpeckle(o);
                    case StructuralLoadGroup o:
                        return LoadGroupToSpeckle(o);
                    case StructuralLoadCombination o:
                        return LoadCombinationToSpeckle(o);
                    case StructuralPointAction<PointStructuralReferenceOnPoint> o:
                        return NodeLoadToSpeckle(o);
                    case StructuralPointAction<PointStructuralReferenceOnBeam> o:
                        return BeamLoadToSpeckle(o);
                    case StructuralCurveAction<CurveStructuralReferenceOnBeam> o:
                        return BeamLoadToSpeckle(o);

                    default:
                        Report.LogConversionError(new Exception($"Skipped unsupported ADM type: { @object.GetType() }"));
                        break;
                }
            }
            catch (NotImplementedException e)
            {
                Report.LogConversionError(new NotImplementedException($"Skipped ADM object of type '{ @object.GetType() }': {e.Message}"));
            }
            catch (ArgumentNullException e)
            {
                Report.LogConversionError(new ArgumentNullException(e.ParamName, $"Failed for ADM object of type '{ @object.GetType() }': {e.Message}"));
            }
            catch (ArgumentException e)
            {
                Report.LogConversionError(new ArgumentException($"Failed for ADM object of type '{ @object.GetType() }': {e.Message}"));
            }

            return null;
        }

        public List<Base> ConvertToSpeckle(List<object> objects)
        {
            return objects.Select(x => ConvertToSpeckle(x)).ToList();
        }

        #endregion
    }
}
